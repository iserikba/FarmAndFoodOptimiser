using Iserik.FaFOptimiser.Catalog;
using Iserik.FaFOptimiser.Solver;
using Mafi;
using Mafi.Base;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Population;
using Mafi.Core.Products;
using Mafi.Core.PropertiesDb;
using Mafi.Core.Prototypes;
using Mafi.Unity.UiToolkit.Library;
using System;
using System.Collections.Generic;

namespace Iserik.FaFOptimiser.Services
{
    public class DemandStateManager
    {
        private readonly ProductionChainService m_chainService;
        public readonly RecipeCatalog m_catalog;
        private readonly SettlementsManager m_settlementsManager;
        private readonly IPropertiesDb m_propertiesDb;
        private readonly ProtosDb m_protosDb;

        public OptimizationResult LatestResult { get; set; }

        private readonly Dictionary<string, (string Category, float BaseDemand)> m_dynamicFoodStats
            = new Dictionary<string, (string, float)>();

        // User's direct requests
        public Dictionary<ProductProto, Fix32> DirectCropDemands { get; } = new Dictionary<ProductProto, Fix32>();
        public Dictionary<ProductProto, Fix32> DirectChickFarmDemands { get; } = new Dictionary<ProductProto, Fix32>(); // <-- NEW
        public Dictionary<ProductProto, Fix32> ManufacturedDemands { get; } = new Dictionary<ProductProto, Fix32>();

        // UI Pending Selections
        public ProductProto PendingNewCrop { get; set; }
        public ProductProto PendingNewProduct { get; set; }

        public Dictionary<ProductProto, ResolvedChain> SelectedChains { get; } = new Dictionary<ProductProto, ResolvedChain>();
        public Dictionary<ProductProto, ResolvedChain> PinnedChains { get; } = new Dictionary<ProductProto, ResolvedChain>();

        // Aggregated totals for the UI
        public Dictionary<ProductProto, Fix32> AggregateCropDemands { get; private set; } = new Dictionary<ProductProto, Fix32>();
        public Dictionary<ProductProto, Fix32> AggregateChickFarmDemands { get; private set; } = new Dictionary<ProductProto, Fix32>(); // <-- NEW

        private readonly GreedyByproductOptimizer m_byproductOptimizer;
        public event Action OnDemandsUpdated;
        public Dictionary<ProductProto, Fix32> CurrentSurpluses { get; private set; } = new Dictionary<ProductProto, Fix32>();
        public bool IsManualOverrideEnabled { get; set; } = false;

        private readonly HashSet<ProductProto> m_excludedCrops = new HashSet<ProductProto>();

        // --- NEW: Unified Flock State ---
        public int ActualChickenCount { get; private set; } = 0;
        public int MinChickenCount { get; private set; } = 0;
        public ResolvedChain SelectedFlockChain { get; private set; }

        public DemandStateManager(
            ProductionChainService chainService,
            GreedyByproductOptimizer byproductOptimizer,
            RecipeCatalog catalog,
            SettlementTelemetryService telemetryService, // Fixed constructor matching your usage
            SettlementsManager settlementsManager,
            IPropertiesDb propertiesDb,
            ProtosDb protosDb)
        {
            this.m_chainService = chainService;
            this.m_byproductOptimizer = byproductOptimizer;
            this.m_catalog = catalog;
            this.m_settlementsManager = settlementsManager;
            this.m_propertiesDb = propertiesDb;
            this.m_protosDb = protosDb;

            BuildDynamicFoodStats();
        }

        public bool IsCropInScope(ProductProto product) => !this.m_excludedCrops.Contains(product);

        public void ToggleCropScope(ProductProto product)
        {
            if (this.m_excludedCrops.Contains(product))
            {
                this.m_excludedCrops.Remove(product);
                Log.Info($"[FaFOptimiser] Crop/Product '{product.Id}' included in solver scope.");
            }
            else
            {
                this.m_excludedCrops.Add(product);
                Log.Info($"[FaFOptimiser] Crop/Product '{product.Id}' excluded from solver scope.");
            }
            RecalculateTheoreticalDemand();
        }

        public void SetChickenCountOverride(int newCount)
        {
            // Enforce the 50-bird floor clamp
            if (newCount < this.MinChickenCount) newCount = this.MinChickenCount;

            this.ActualChickenCount = newCount;
            RecalculateAggregateCrops();
        }

        public void SetCropScope(ProductProto product, bool inScope, bool triggerUpdate = true)
        {
            if (inScope) this.m_excludedCrops.Remove(product);
            else this.m_excludedCrops.Add(product);
            if (triggerUpdate) RecalculateTheoreticalDemand();
        }

        /// <summary>
        /// NEW: Unified Picker Routing!
        /// Routes the picked item into the appropriate group and sets the pending variable.
        /// </summary>
        public void HandlePickedProduct(ProductProto product)
        {
            if (this.m_catalog.IsCrop(product))
            {
                this.PendingNewCrop = product;
            }
            else if (this.m_catalog.IsChickFarmProduct(product))
            {
                // We reuse PendingNewCrop for Chicken Farm items so the UI shares the same selector!
                this.PendingNewCrop = product;
            }
            else
            {
                this.PendingNewProduct = product;
            }
        }

        private void BuildDynamicFoodStats()
        {
            foreach (FoodProto foodProto in this.m_protosDb.All<FoodProto>())
            {
                string catName = foodProto.FoodCategory.Id.Value;
                float baseDemand = foodProto.GetConsumedQuantityFromPopDays(3000, Percent.Hundred).Value.ToFloat();
                this.m_dynamicFoodStats[foodProto.Product.Id.Value] = (catName, baseDemand);
            }
        }

        public void LoadFromSettlement(SettlementTelemetryService telemetry)
        {
            var unifiedDemand = telemetry.GetUnifiedFoodDemand();

            this.DirectCropDemands.Clear();
            this.DirectChickFarmDemands.Clear(); // <-- Clear livestock demands
            this.ManufacturedDemands.Clear();
            this.SelectedChains.Clear();

            foreach (var kvp in unifiedDemand)
            {
                ProductProto product = kvp.Key;
                FoodDemandMetrics metrics = kvp.Value;
                Fix32 amount = metrics.EstimatedMonthlyConsumption;

                if (amount > Fix32.Zero || metrics.IsConfiguredInMarket)
                {
                    SetDemandAmount(product, amount, triggerUpdate: false);
                }
            }

            RecalculateTheoreticalDemand();
        }

        public void SetDemandAmount(ProductProto product, Fix32 amount, bool triggerUpdate = true)
        {
            if (this.m_catalog.IsCrop(product))
            {
                if (amount > Fix32.Zero) this.DirectCropDemands[product] = amount;
                else this.DirectCropDemands.Remove(product);
            }
            // Route Animal Feed directly into the Livestock group!
            else if (this.m_catalog.IsChickFarmProduct(product) || product.Id == Ids.Products.AnimalFeed)
            {
                if (amount > Fix32.Zero) this.DirectChickFarmDemands[product] = amount;
                else this.DirectChickFarmDemands.Remove(product);
            }
            else
            {
                if (amount > Fix32.Zero)
                {
                    this.ManufacturedDemands[product] = amount;
                    var chains = this.m_chainService.GetAlternativeChains(product, amount);
                    if (chains.Count > 0)
                    {
                        this.SelectedChains[product] = chains[0];
                    }
                }
                else
                {
                    this.ManufacturedDemands.Remove(product);
                    this.SelectedChains.Remove(product);
                }
            }

            // --- THE FIX: Trigger aggregate calculations immediately ---
            if (triggerUpdate)
            {
                RecalculateTheoreticalDemand();
                RecalculateAggregateCrops(); // <-- Added this line! Now updates UI immediately without Auto-Fill!
            }
            // ---------------------------------------------------------
        }

        private void RecalculateAggregateCrops()
        {
            this.AggregateCropDemands.Clear();
            this.AggregateChickFarmDemands.Clear();

            // 1. Add Direct Crop Demands
            foreach (var kvp in this.DirectCropDemands)
                AddDemandToDictionary(this.AggregateCropDemands, kvp.Key, kvp.Value);

            // 2. Add Direct Livestock Demands (Eggs, Carcasses, AND direct Animal Feed!)
            foreach (var kvp in this.DirectChickFarmDemands)
                AddDemandToDictionary(this.AggregateChickFarmDemands, kvp.Key, kvp.Value);

            // 3. Extract Chain Requirements from Manufactured Foods (Cake, Meat, etc.)
            foreach (var chain in this.SelectedChains.Values)
            {
                // Safety guard: Skip Animal Feed if it was leftover in SelectedChains
                if (chain.TargetProduct != null && chain.TargetProduct.Id == Ids.Products.AnimalFeed) continue;

                var cropsNeeded = chain.GetRequiredCrops();
                foreach (var kvp in cropsNeeded)
                    AddDemandToDictionary(this.AggregateCropDemands, kvp.Key, kvp.Value);

                var chickNeeded = chain.GetRequiredChickFarmProducts();
                foreach (var kvp in chickNeeded)
                    AddDemandToDictionary(this.AggregateChickFarmDemands, kvp.Key, kvp.Value);
            }

            // ==========================================
            // 4. THE MAX() RULE & FLOCK FEEDING RESOLUTION
            // ==========================================
            double maxFarmsRequired = 0;

            if (this.m_protosDb.TryGetProto(Ids.Products.Eggs, out ProductProto eggsProto) &&
                this.AggregateChickFarmDemands.TryGetValue(eggsProto, out Fix32 eggsQty))
            {
                double farmsForEggs = eggsQty.ToFloat() / 7.0;
                if (farmsForEggs > maxFarmsRequired) maxFarmsRequired = farmsForEggs;
            }

            if (this.m_protosDb.TryGetProto(Ids.Products.ChickenCarcass, out ProductProto ccProto) &&
                this.AggregateChickFarmDemands.TryGetValue(ccProto, out Fix32 ccQty))
            {
                double farmsForCc = ccQty.ToFloat() / 10.0;
                if (farmsForCc > maxFarmsRequired) maxFarmsRequired = farmsForCc;
            }

            int rawBirds = (int)System.Math.Ceiling(maxFarmsRequired * 500.0);
            this.MinChickenCount = ((rawBirds + 49) / 50) * 50;

            if (this.ActualChickenCount < this.MinChickenCount)
            {
                this.ActualChickenCount = this.MinChickenCount;
            }

            // ==========================================
            // 5. RESOLVE TOTAL ANIMAL FEED (Flock Requirement + Direct Demand)
            // ==========================================
            if (this.m_protosDb.TryGetProto(Ids.Products.AnimalFeed, out ProductProto feedProto))
            {
                // Calculate flock requirement (15.1 feed per 500 birds)
                float flockFeedFloat = (this.ActualChickenCount / 500f) * 15.1f;
                Fix32 totalFeedNeeded = Fix32.FromFloat(flockFeedFloat);

                // Add any direct user demand typed into the Animal Feed input box
                if (this.AggregateChickFarmDemands.TryGetValue(feedProto, out Fix32 directFeed))
                {
                    totalFeedNeeded += directFeed;
                }

                if (totalFeedNeeded > Fix32.Zero)
                {
                    // Save the grand total so the UI row displays the full requirement!
                    this.AggregateChickFarmDemands[feedProto] = totalFeedNeeded;

                    var feedChains = this.m_chainService.GetAlternativeChains(feedProto, totalFeedNeeded);
                    if (feedChains.Count > 0)
                    {
                        ResolvedChain activeChain = null;
                        if (this.PinnedChains.TryGetValue(feedProto, out var pinned) && pinned != null)
                        {
                            foreach (var chain in feedChains)
                            {
                                if (chain.IsEquivalentTo(pinned))
                                {
                                    activeChain = chain;
                                    break;
                                }
                            }
                        }
                        if (activeChain == null) activeChain = feedChains[0];

                        this.SelectedFlockChain = activeChain;
                        this.SelectedChains[feedProto] = activeChain;

                        // Add required crops (Corn/Wheat) to our grand total exactly ONCE!
                        foreach (var kvp in activeChain.GetRequiredCrops())
                        {
                            AddDemandToDictionary(this.AggregateCropDemands, kvp.Key, kvp.Value);
                        }
                    }
                }
                else
                {
                    // --- THE FIX: Clean up all residual state when demand hits 0.0 ---
                    this.AggregateChickFarmDemands.Remove(feedProto);
                    this.SelectedFlockChain = null;
                    this.SelectedChains.Remove(feedProto);
                    // -----------------------------------------------------------------
                }
            }

            OnDemandsUpdated?.Invoke();
        }

        public void SetSelectedChain(ProductProto product, ResolvedChain chain)
        {
            // Removed ManufacturedDemands.ContainsKey check so Animal Feed can be selected!
            this.PinnedChains[product] = chain;
            this.SelectedChains[product] = chain;

            // Explicitly sync the flock chain if the player is selecting Animal Feed
            if (product.Id == Ids.Products.AnimalFeed)
            {
                this.SelectedFlockChain = chain;
            }

            RecalculateTheoreticalDemand();
        }

        private void AddDemandToDictionary(Dictionary<ProductProto, Fix32> dict, ProductProto product, Fix32 amount)
        {
            if (dict.ContainsKey(product)) dict[product] += amount;
            else dict[product] = amount;
        }

        public void RecalculateTheoreticalDemand()
        {
            if (IsManualOverrideEnabled)
            {
                RecalculateAggregateCrops();
                return;
            }

            int totalPopulation = this.m_settlementsManager.GetTotalPopulation();
            if (totalPopulation <= 0) return;

            Fix32 globalMultiplier = this.m_propertiesDb.GetProperty(IdsCore.PropertyIds.FoodConsumptionMultiplier).Value.ToFix32();

            var activeFoods = new List<ProductProto>();
            activeFoods.AddRange(this.ManufacturedDemands.Keys);
            activeFoods.AddRange(this.DirectCropDemands.Keys);
            activeFoods.AddRange(this.DirectChickFarmDemands.Keys); // <-- NEW: Include livestock items

            var activeCategories = new Dictionary<string, int>();

            foreach (var food in activeFoods)
            {
                if (this.m_dynamicFoodStats.TryGetValue(food.Id.Value, out var stats))
                {
                    if (activeCategories.ContainsKey(stats.Category))
                        activeCategories[stats.Category]++;
                    else
                        activeCategories[stats.Category] = 1;
                }
            }

            int Nc = activeCategories.Count;
            if (Nc == 0) return;

            Fix32 popMultiplier = Fix32.FromFloat(totalPopulation / 100f);
            var rawDemandsToOptimize = new Dictionary<ProductProto, Fix32>();

            foreach (var food in activeFoods)
            {
                if (this.m_dynamicFoodStats.TryGetValue(food.Id.Value, out var stats))
                {
                    int N = activeCategories[stats.Category];
                    float finalDemandFloat = stats.BaseDemand / (Nc * N);
                    Fix32 finalDemand = Fix32.FromFloat(finalDemandFloat) * popMultiplier * globalMultiplier;

                    if (this.m_catalog.IsCrop(food))
                    {
                        this.DirectCropDemands[food] = finalDemand;
                    }
                    else if (this.m_catalog.IsChickFarmProduct(food)) // <-- NEW: Route to livestock bucket
                    {
                        this.DirectChickFarmDemands[food] = finalDemand;
                    }
                    else
                    {
                        this.ManufacturedDemands[food] = finalDemand;
                        rawDemandsToOptimize[food] = finalDemand;
                    }
                }
                else
                {
                    if (!this.m_catalog.IsCrop(food) &&
                        !this.m_catalog.IsChickFarmProduct(food) &&
                        this.ManufacturedDemands.TryGetValue(food, out Fix32 manualDemand))
                    {
                        rawDemandsToOptimize[food] = manualDemand;
                    }
                }
            }

            GreedyOptimizationResult optimizedData = this.m_byproductOptimizer.RunTwoPassOptimization(rawDemandsToOptimize, this.PinnedChains);

            this.SelectedChains.Clear();
            foreach (var kvp in optimizedData.FinalChains)
            {
                this.SelectedChains[kvp.Key] = kvp.Value;
                kvp.Value.AddSatisfiedProduct(kvp.Key);
            }

            this.CurrentSurpluses = optimizedData.FinalSurpluses;
            RecalculateAggregateCrops();
        }
    }
}