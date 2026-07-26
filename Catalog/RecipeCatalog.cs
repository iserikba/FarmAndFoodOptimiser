using Mafi;
using Mafi.Base;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Products;
using Mafi.Core.PropertiesDb;
using Mafi.Core.Prototypes;
using Mafi.Localization;
using Iserik.FaFOptimiser.Solver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.Catalog
{
    /// <summary>
    /// This catalog handles BOTH live extraction for the Farm Solver 
    /// AND relational indexing (BOM Explosion) for the "Fetch Population" bridge.
    /// </summary>
    public sealed class RecipeCatalog
    {
        private readonly ProtosDb m_protosDb;
        private readonly IPropertiesDb m_propertiesDb;

        public HashSet<ProductProto> AllowedBomProducts { get; } = new HashSet<ProductProto>();

        // --- GLOBAL MULTIPLIERS FOR SOLVER ---
        public float GlobalCropMultiplier { get; private set; } = 1.0f;
        public float GlobalWaterMultiplier { get; private set; } = 1.0f;
        public float GlobalFertilizerMultiplier { get; private set; } = 1.0f;

        // --- LOOKUP TABLES FOR BOM EXPLOSION ---
        private ImmutableArray<RecipeProto> m_allRecipes = ImmutableArray<RecipeProto>.Empty;
        private Dictionary<ProductProto, ImmutableArray<RecipeProto>> m_recipesByOutput;
        private Dictionary<ProductProto, ImmutableArray<RecipeProto>> m_recipesByInput;
        private Dictionary<RecipeProto, IProtoWithIcon> m_machineByRecipe = new Dictionary<RecipeProto, IProtoWithIcon>();
        private Dictionary<RecipeProto, FarmProto> m_farmByRecipe = new Dictionary<RecipeProto, FarmProto>();

        // A temporary list to hold our generated virtual recipes before indexing
        private List<RecipeProto> m_virtualRecipes = new List<RecipeProto>();

        public RecipeCatalog(ProtosDb protosDb, IPropertiesDb propertiesDb)
        {
            this.m_protosDb = protosDb;
            this.m_propertiesDb = propertiesDb;
        }

        public ImmutableArray<RecipeProto> AllRecipes => this.m_allRecipes;

        // ==========================================
        // PHASE 1: SOLVER CONFIGURATION
        // ==========================================
        public void RefreshGlobalMultipliers()
        {
            IProperty<Percent> yieldMult = this.m_propertiesDb.GetProperty(IdsCore.PropertyIds.FarmYieldMultiplier);
            IProperty<Percent> waterMult = this.m_propertiesDb.GetProperty(IdsCore.PropertyIds.FarmWaterConsumptionMultiplier);

            this.GlobalCropMultiplier = yieldMult.Value.ToFloat();
            this.GlobalWaterMultiplier = waterMult.Value.ToFloat();
            this.GlobalFertilizerMultiplier = 1.0f;
        }

        // Checks if a product is a crop by seeing if any of its recipes use a Farm
        public bool IsCrop(ProductProto product)
        {
            var recipes = GetRecipesProducing(product);
            foreach (var recipe in recipes)
            {
                if (GetFarmForRecipe(recipe) != null) return true;
            }
            return false;
        }

        // --- NEW: Checks if a product originates from a Chicken Farm ---
        public bool IsChickFarmProduct(ProductProto product)
        {
            if (product == null) return false;

            // Fast explicit ID check
            if (product.Id == Ids.Products.Eggs || product.Id == Ids.Products.ChickenCarcass)
            {
                return true;
            }

            // Dynamic check against generated recipes
            var recipes = GetRecipesProducing(product);
            foreach (var recipe in recipes)
            {
                var machine = GetMachineForRecipe(recipe);
                if (machine != null && machine.Id == Ids.Buildings.ChickenFarm)
                {
                    return true;
                }
            }

            return false;
        }

        public List<CropRecipe> GetCropRecipes()
        {
            List<CropRecipe> dynamicRecipes = new List<CropRecipe>();

            var farmTierIds = new Dictionary<int, Proto.ID>
            {
                { 1, Ids.Buildings.FarmT1 },
                { 2, Ids.Buildings.FarmT2 },
                { 3, Ids.Buildings.FarmT3 },
                { 4, Ids.Buildings.FarmT4 }
            };

            RefreshGlobalMultipliers();

            foreach (var kvp in farmTierIds)
            {
                int tierLevel = kvp.Key;

                if (!this.m_protosDb.TryGetProto(kvp.Value, out FarmProto farmProto)) continue;

                foreach (CropProto crop in this.m_protosDb.All<CropProto>())
                {
                    if (crop.RequiresGreenhouse && !farmProto.IsGreenhouse) continue;

                    bool isPhantom = crop.ProductProduced.IsEmpty;
                    string internalId = isPhantom ? crop.Id.Value : crop.ProductProduced.Product.Id.Value;

                    // Cycle duration is exactly DaysToGrow in the raw data
                    int totalCycleDays = crop.DaysToGrow;
                    int months = totalCycleDays / 30;

                    // Trust the engine's built-in multiplier calculations (FarmProto.DemandsMultiplier)
                    float water = this.GlobalWaterMultiplier * crop.GetConsumedWaterPerDay(farmProto).Value.ToFloat() * totalCycleDays;

                    // Use the method that applies the Farm's specific modifiers!
                    float dailyDrain = crop.GetConsumedFertilityPerDay(farmProto).ToFloat();

                    // Calculate total cycle cost and convert percentage ( * 100f)
                    float fertility = dailyDrain * totalCycleDays * 100f;

                    // Round to 1 decimal place for clean UI presentation (e.g., 46.9)
                    //float fertility = (float)System.Math.Round(exactFertilityCost, 1);

                    //float fertility = crop.ConsumedFertilityPerDay.ToFloat()* 100f * totalCycleDays; 
                    // Trust the engine's built-in yield calculations (FarmProto.YieldMultiplier)
                    float production = isPhantom ? 0 : this.GlobalCropMultiplier * crop.GetProductProduced(farmProto).Quantity.Value;

                    dynamicRecipes.Add(new CropRecipe
                    {
                        Name = internalId,
                        Months = months,
                        Water = water,
                        Fertility = fertility,
                        Production = production,
                        FarmTier = tierLevel
                    });
                }
            }
            return dynamicRecipes;
        }

        
        // ==========================================
        // PHASE 2: BOM EXPLOSION INDEXING
        // ==========================================
        public void RefreshBOMIndexing()
        {
            var recipeList = new List<RecipeProto>();
            var byOutput = new Dictionary<ProductProto, List<RecipeProto>>();
            var byInput = new Dictionary<ProductProto, List<RecipeProto>>();

            this.m_machineByRecipe.Clear();
            this.m_farmByRecipe.Clear();
            this.m_virtualRecipes.Clear();

            // Build the whitelist BEFORE indexing!
            GenerateAllowList();

            // 1. Index Native Machines
            foreach (MachineProto machine in this.m_protosDb.All<MachineProto>())
            {
                foreach (RecipeProto recipe in machine.Recipes)
                {
                    if (recipe != null && !this.m_machineByRecipe.ContainsKey(recipe))
                        this.m_machineByRecipe[recipe] = machine;
                }
            }

            // 2. Index Native Recipes
            foreach (RecipeProto recipe in this.m_protosDb.All<RecipeProto>())
            {
                if (recipe != null)
                {
                    recipeList.Add(recipe);
                    this.indexRecipesByOutput(byOutput, recipe);
                    this.indexRecipesByInput(byInput, recipe);
                }
            }

            // 3. Generate Virtual Recipes
            GenerateVirtualFarmRecipes();
            GenerateVirtualAnimalRecipes();

            // 4. Index Virtual Recipes
            foreach (RecipeProto virtualRecipe in this.m_virtualRecipes)
            {
                recipeList.Add(virtualRecipe);
                this.indexRecipesByOutput(byOutput, virtualRecipe);
                this.indexRecipesByInput(byInput, virtualRecipe);
            }

            // 5. Finalize
            this.m_allRecipes = recipeList.ToImmutableArray();
            this.m_recipesByOutput = buildLookup(byOutput);
            this.m_recipesByInput = buildLookup(byInput);

            Log.Info($"[FaFOptimiser] RecipeCatalog initialized. Indexed: {this.m_allRecipes.Length} recipes.");
        }

        // ==========================================
        // VIRTUAL RECIPE GENERATORS
        // ==========================================
        private void GenerateVirtualFarmRecipes()
        {
            var water = this.m_protosDb.GetOrThrow<ProductProto>(IdsCore.Products.CleanWater);
            var fertOrganic = this.m_protosDb.GetOrThrow<ProductProto>(Ids.Products.FertilizerOrganic);

            foreach (FarmProto farm in this.m_protosDb.All<FarmProto>())
            {
                if (!farm.HasIrrigationAndFertilizerSupport) continue;

                bool isNormalFarm = farm.Id == Ids.Buildings.FarmT2;
                bool isGreenhouseFarm = farm.Id == Ids.Buildings.FarmT3;

                if (!isNormalFarm && !isGreenhouseFarm) continue;

                foreach (CropProto crop in this.m_protosDb.All<CropProto>())
                {
                    if (crop.ProductProduced.IsEmpty || (!farm.IsGreenhouse && crop.RequiresGreenhouse)) continue;

                    RecipeProto.ID recipeId = new RecipeProto.ID($"VirtualRecipe_{farm.Id.Value}_{crop.Id.Value}");
                    var inputs = new Lyst<RecipeInput>();

                    // 1. Water - using clean float math and rounding
                    if (crop.ConsumedWaterPerDay.IsPositive)
                    {
                        float waterPerDay = crop.GetConsumedWaterPerDay(farm).Value.ToFloat();
                        float exactWater = waterPerDay * 30f;

                        int finalWaterValue = (int)System.Math.Round(exactWater);
                        inputs.Add(new RecipeInput(water, new Quantity(finalWaterValue)));
                    }

                    // 2. Fertility - using your successful float logic
                    if (crop.ConsumedFertilityPerDay.IsPositive)
                    {
                        float dailyDrain = crop.GetConsumedFertilityPerDay(farm).ToFloat();
                        float exactFertilityCost = dailyDrain * 3000f;

                        // Round mathematically to the closest integer step
                        int finalFertilityValue = (int)System.Math.Round(exactFertilityCost);

                        inputs.Add(new RecipeInput(fertOrganic, new Quantity(finalFertilityValue)));
                    }

                    var outputs = new Lyst<RecipeOutput>();
                    ProductQuantity exactYield = crop.GetProductProduced(farm);
                    // all recipe values are per month
                    outputs.Add(new RecipeOutput(exactYield.Product, exactYield.Quantity / crop.MonthsToGrow));

                    var virtualRecipe = new RecipeProto(
                        id: recipeId,
                        strings: crop.Strings,
                        allInputs: inputs.ToImmutableArray(),
                        allOutputs: outputs.ToImmutableArray()
                    );

                    this.m_farmByRecipe[virtualRecipe] = farm;
                    this.m_virtualRecipes.Add(virtualRecipe);
                }
            }
        }

        // The master Whitelist for the BOM explosion


        private void GenerateAllowList()
        {
            this.AllowedBomProducts.Clear();

            Proto.ID[] allowedIds = new Proto.ID[]
            {
                // Settlement food
                Ids.Products.Potato, Ids.Products.Corn, Ids.Products.Bread,
                Ids.Products.Meat, Ids.Products.Eggs, Ids.Products.Tofu,
                Ids.Products.Vegetables, Ids.Products.Fruit,
                Ids.Products.Snack, Ids.Products.Cake, Ids.Products.Sausage,

                // Farm product potato, corn, vegetables, fruit
                Ids.Products.Canola, Ids.Products.Poppy, Ids.Products.Soybean,
                Ids.Products.Wheat, Ids.Products.TreeSapling, Ids.Products.SugarCane,

                // Animal Farm product: eggs 
                Ids.Products.ChickenCarcass,

                // food chain products & byproducts
                Ids.Products.Flour, Ids.Products.AnimalFeed, Ids.Products.Sugar,
                Ids.Products.CookingOil, Ids.Products.MeatTrimmings, Ids.Products.CornMash,

                // Possible non-food products made from crop
                Ids.Products.FuelGas, Ids.Products.FoodPack, Ids.Products.Ethanol,
                Ids.Products.Diesel, Ids.Products.Antibiotics
            };

            foreach (var id in allowedIds)
            {
                // Using TryGetProto instead of GetOrThrow prevents the mod from completely 
                // crashing if a future game update renames or removes a specific product.
                if (this.m_protosDb.TryGetProto(id, out ProductProto proto))
                {
                    this.AllowedBomProducts.Add(proto);
                }
                else
                {
                    Log.Warning($"[FaFOptimiser] AllowList product not found: {id.Value}");
                }
            }
        }

        public IEnumerable<ProductProto> GetAllowedManufacturedProducts(ProtosDb protosDb)
        {
            HashSet<Proto.ID> allowedIds = new HashSet<Proto.ID>
            {
                Ids.Products.Bread,
                Ids.Products.Meat, Ids.Products.Eggs, Ids.Products.Tofu,
                Ids.Products.Snack, Ids.Products.Cake, Ids.Products.Sausage,
                Ids.Products.Flour, Ids.Products.AnimalFeed, Ids.Products.Sugar,
                Ids.Products.CookingOil, Ids.Products.MeatTrimmings, Ids.Products.CornMash
            };

            List<ProductProto> availableProducts = new List<ProductProto>();

            foreach (ProductProto product in protosDb.All<ProductProto>())
            {
                if (allowedIds.Contains(product.Id))
                {
                    availableProducts.Add(product);
                }
            }

            return availableProducts;
        }

        private void GenerateVirtualAnimalRecipes()
        {
            if (!this.m_protosDb.TryGetProto(IdsCore.Products.CleanWater, out ProductProto water) ||
                !this.m_protosDb.TryGetProto(Ids.Products.AnimalFeed, out ProductProto animalFeed) ||
                !this.m_protosDb.TryGetProto(Ids.Products.Eggs, out ProductProto egg) ||
                !this.m_protosDb.TryGetProto(Ids.Products.ChickenCarcass, out ProductProto chickenCarcass))
            {
                Log.Warning("[FaFOptimiser] Could not find products to generate Virtual Chicken Farm recipe.");
                return;
            }

            // Standard Baseline Ratios for exactly 1 Chicken Farm (500 Chickens / 1 Month)
            var inputs = new Lyst<RecipeInput>
            {
                new RecipeInput(animalFeed, new Quantity(15)),
                new RecipeInput(water, new Quantity(18))
            };

            var outputs = new Lyst<RecipeOutput>
            {
                new RecipeOutput(egg, new Quantity(7)),
                new RecipeOutput(chickenCarcass, new Quantity(10))
            };

            RecipeProto.ID recipeId = new RecipeProto.ID("VirtualRecipe_FeedChickens");

            var virtualChickenRecipe = new RecipeProto(
                id: recipeId,
                strings: Proto.CreateStr(recipeId, "Chicken Flock Feeding", "Consumes Feed & Water for 500 Chickens (1 Farm)"),
                allInputs: inputs.ToImmutableArray(),
                allOutputs: outputs.ToImmutableArray()
            );

            if (this.m_protosDb.TryGetProto(Ids.Buildings.ChickenFarm, out Proto rawProto) && rawProto is IProtoWithIcon chickenFarmIcon)
            {
                this.m_machineByRecipe[virtualChickenRecipe] = chickenFarmIcon;
            }

            this.m_virtualRecipes.Add(virtualChickenRecipe);
        }

        // ==========================================
        // LOOKUP & INDEXING HELPERS
        // ==========================================
        public IProtoWithIcon GetMachineForRecipe(RecipeProto recipe) =>
            recipe != null && this.m_machineByRecipe.TryGetValue(recipe, out var machine) ? machine : null;

        public FarmProto GetFarmForRecipe(RecipeProto recipe) =>
            recipe != null && this.m_farmByRecipe.TryGetValue(recipe, out var farm) ? farm : null;

        public bool IsCraftable(ProductProto product) => this.GetRecipesProducing(product).IsNotEmpty;

        public ImmutableArray<RecipeProto> GetRecipesProducing(ProductProto product) =>
            product != null && this.m_recipesByOutput.TryGetValue(product, out var result) ? result : ImmutableArray<RecipeProto>.Empty;

        public ImmutableArray<RecipeProto> GetRecipesConsuming(ProductProto product) =>
            product != null && this.m_recipesByInput.TryGetValue(product, out var result) ? result : ImmutableArray<RecipeProto>.Empty;

        private void indexRecipesByOutput(Dictionary<ProductProto, List<RecipeProto>> index, RecipeProto recipe)
        {
            foreach (var output in recipe.AllUserVisibleOutputs)
                if (!output.HideInUi && shouldIncludeProduct(output.Product))
                    addRecipeToIndex(index, output.Product, recipe);
        }

        private void indexRecipesByInput(Dictionary<ProductProto, List<RecipeProto>> index, RecipeProto recipe)
        {
            foreach (var input in recipe.AllUserVisibleInputs)
                if (!input.HideInUi && shouldIncludeProduct(input.Product))
                    addRecipeToIndex(index, input.Product, recipe);
        }

        private static void addRecipeToIndex(Dictionary<ProductProto, List<RecipeProto>> index, ProductProto product, RecipeProto recipe)
        {
            if (!index.TryGetValue(product, out var list))
            {
                list = new List<RecipeProto>();
                index.Add(product, list);
            }
            if (!list.Contains(recipe)) list.Add(recipe);
        }

        private static Dictionary<ProductProto, ImmutableArray<RecipeProto>> buildLookup(Dictionary<ProductProto, List<RecipeProto>> source)
        {
            return source.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
        }

        // To implement your Stop List later, you will simply add a check here!
        private bool shouldIncludeProduct(ProductProto product) =>
            product != null &&
            !product.IsObsolete &&
            !(product is VirtualProductProto) &&
            this.AllowedBomProducts.Contains(product); // THE GATEKEEPER
    }
}