using Iserik.FaFOptimiser.Catalog;
using Mafi;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.Services
{
    public class ProductionChainService
    {
        private readonly RecipeCatalog m_catalog;

        public ProductionChainService(RecipeCatalog catalog)
        {
            this.m_catalog = catalog;
            this.m_catalog.RefreshBOMIndexing();
        }

        public List<ResolvedChain> GetAlternativeChains(ProductProto targetProduct, Fix32 targetAmount, HashSet<ProductProto> visited = null)
        {
            List<ResolvedChain> alternatives = new List<ResolvedChain>();

            if (visited == null) visited = new HashSet<ProductProto>();
            if (visited.Contains(targetProduct)) return alternatives;

            HashSet<ProductProto> currentPathVisited = new HashSet<ProductProto>(visited);
            currentPathVisited.Add(targetProduct);

            var producingRecipes = this.m_catalog.GetRecipesProducing(targetProduct);
            bool isCrop = this.m_catalog.IsCrop(targetProduct);
            bool isLivestock = this.m_catalog.IsChickFarmProduct(targetProduct);

            // Treat Crops and Livestock Products (Eggs/CC) strictly as terminal leaf nodes!
            // Their feeding requirements are now handled centrally by the Unified Flock Model.
            if (producingRecipes.IsEmpty || isLivestock)
            {
                var baseChain = new ResolvedChain { TargetProduct = targetProduct, TargetAmount = targetAmount };
                if (isCrop)
                {
                    baseChain.RawCropDemands[targetProduct] = targetAmount;
                }

                baseChain.RootNode = new ChainNode
                {
                    IsBaseResource = !isCrop && !isLivestock,
                    IsFarm = isCrop,
                    IsChickFarm = isLivestock,
                    MachineName = isCrop ? "Farm" : (isLivestock ? "Chicken Farm Output" : "Raw Resource"),
                    MachineCount = 0,
                    OutputProduct = targetProduct,
                    OutputAmount = targetAmount
                };

                alternatives.Add(baseChain);
                return alternatives;
            }

            foreach (RecipeProto recipe in producingRecipes)
            {
                bool isVirtualRecipe = recipe.Id.Value.StartsWith("VirtualRecipe_");
                if (!isVirtualRecipe && !recipe.IsUnlockedAndAvailable) continue;

                Fix32 outputQty = Fix32.One;
                foreach (var output in recipe.AllUserVisibleOutputs)
                {
                    if (output.Product == targetProduct)
                    {
                        outputQty = output.Quantity.Value.ToFix32();
                        break;
                    }
                }
                Fix32 recipeRuns = targetAmount / outputQty;

                IProtoWithIcon actualMachineProto = this.m_catalog.GetFarmForRecipe(recipe) ?? (IProtoWithIcon)this.m_catalog.GetMachineForRecipe(recipe);
                string machineName = actualMachineProto?.Strings.Name.TranslatedString ?? "Unknown Factory";

                bool isFarm = this.m_catalog.GetFarmForRecipe(recipe) != null;
                if (isFarm) machineName = "Farm";

                bool isChickFarmRecipe = recipe.Id.Value.Contains("FeedChickens");
                if (isChickFarmRecipe) machineName = "Chicken Farm Flock";

                float recipeDurationMonths = 1f;
                if (!isFarm && actualMachineProto is MachineProto machineProto)
                {
                    var binding = machineProto.GetRecipeBindingFor(recipe);
                    recipeDurationMonths = (float)binding.Duration.Ticks / (float)60.Seconds().Ticks;
                }

                double machineCount = recipeRuns.ToFloat() * recipeDurationMonths;

                List<ResolvedChain> currentRecipePermutations = new List<ResolvedChain> { new ResolvedChain() };
                bool isDeadPath = false;

                foreach (var input in recipe.AllUserVisibleInputs)
                {
                    Fix32 inputAmountNeeded = input.Quantity.Value.ToFix32() * recipeRuns;
                    var inputChains = GetAlternativeChains(input.Product, inputAmountNeeded, currentPathVisited);

                    if (inputChains.Count == 0)
                    {
                        isDeadPath = true;
                        break;
                    }

                    List<ResolvedChain> nextPermutations = new List<ResolvedChain>();

                    foreach (var existingPerm in currentRecipePermutations)
                    {
                        foreach (var inputAlternative in inputChains)
                        {
                            ResolvedChain combined = new ResolvedChain();

                            foreach (var kvp in existingPerm.RawCropDemands) combined.RawCropDemands[kvp.Key] = kvp.Value;
                            foreach (var kvp in inputAlternative.RawCropDemands)
                            {
                                if (combined.RawCropDemands.ContainsKey(kvp.Key)) combined.RawCropDemands[kvp.Key] += kvp.Value;
                                else combined.RawCropDemands[kvp.Key] = kvp.Value;
                            }

                            foreach (var kvp in existingPerm.NetByproducts) combined.NetByproducts[kvp.Key] = kvp.Value;
                            foreach (var kvp in inputAlternative.NetByproducts)
                            {
                                if (combined.NetByproducts.ContainsKey(kvp.Key)) combined.NetByproducts[kvp.Key] += kvp.Value;
                                else combined.NetByproducts[kvp.Key] = kvp.Value;
                            }

                            combined.FarmsNeeded = existingPerm.FarmsNeeded + inputAlternative.FarmsNeeded;

                            foreach (var pending in existingPerm.PendingInputs)
                            {
                                combined.PendingInputs.Add(pending.Clone());
                            }

                            if (inputAlternative.RootNode != null)
                            {
                                combined.PendingInputs.Add(inputAlternative.RootNode.Clone());
                            }

                            nextPermutations.Add(combined);
                        }
                    }
                    currentRecipePermutations = nextPermutations;
                }

                if (isDeadPath) continue;

                foreach (var perm in currentRecipePermutations)
                {
                    perm.TargetProduct = targetProduct;
                    perm.TargetAmount = targetAmount;
                    if (isFarm || isChickFarmRecipe) perm.FarmsNeeded += machineCount;

                    perm.RootNode = new ChainNode
                    {
                        IsFarm = isFarm,
                        IsChickFarm = isChickFarmRecipe,
                        IsBaseResource = false,
                        MachineName = machineName,
                        MachineCount = machineCount,
                        MachineProto = actualMachineProto,
                        OutputProduct = targetProduct,
                        OutputAmount = targetAmount
                    };

                    foreach (var pendingInput in perm.PendingInputs)
                    {
                        perm.RootNode.Inputs.Add(pendingInput);
                    }
                    perm.PendingInputs.Clear();

                    foreach (var output in recipe.AllUserVisibleOutputs)
                    {
                        if (output.Product != targetProduct)
                        {
                            Fix32 byproductQty = output.Quantity.Value.ToFix32() * recipeRuns;
                            if (perm.NetByproducts.ContainsKey(output.Product)) perm.NetByproducts[output.Product] += byproductQty;
                            else perm.NetByproducts[output.Product] = byproductQty;
                        }
                    }

                    alternatives.Add(perm);
                }
            }

            foreach (var chain in alternatives)
            {
                CalculateScore(chain);
            }

            alternatives = alternatives.OrderBy(c => c.ResourceScore).ToList();
            List<ResolvedChain> uniqueAlternatives = new List<ResolvedChain>();

            foreach (var chain in alternatives)
            {
                if (chain.RootNode == null) continue;

                bool isDuplicate = false;
                foreach (var uniqueChain in uniqueAlternatives)
                {
                    if (chain.IsEquivalentTo(uniqueChain))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    uniqueAlternatives.Add(chain);
                }
            }

            return uniqueAlternatives;
        }

        private void CalculateScore(ResolvedChain chain)
        {
            double score = chain.FarmsNeeded * 100.0;
            foreach (var bp in chain.NetByproducts.Values) score += bp.ToFloat();
            foreach (var inlet in chain.RawCropDemands.Values) score += inlet.ToFloat();
            chain.ResourceScore = score;
        }
    }
}