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

            // ==========================================
            // CYCLE DETECTION (Infinite Loop Prevention)
            // ==========================================
            if (visited == null) visited = new HashSet<ProductProto>();

            if (visited.Contains(targetProduct))
            {
                return alternatives;
            }

            HashSet<ProductProto> currentPathVisited = new HashSet<ProductProto>(visited);
            currentPathVisited.Add(targetProduct);

            // ==========================================
            // 1. Fetch all recipes that make this product
            // ==========================================
            var producingRecipes = this.m_catalog.GetRecipesProducing(targetProduct);

            // BASE CASE: Raw crops / Uncraftable base materials
            if (producingRecipes.IsEmpty)
            {
                var baseChain = new ResolvedChain { TargetProduct = targetProduct, TargetAmount = targetAmount };
                baseChain.RawCropDemands[targetProduct] = targetAmount;

                baseChain.RootNode = new ChainNode
                {
                    IsBaseResource = true,
                    IsFarm = false,
                    MachineName = "Raw Resource",
                    MachineCount = 0,
                    OutputProduct = targetProduct,
                    OutputAmount = targetAmount
                };

                alternatives.Add(baseChain);
                return alternatives;
            }

            // ==========================================
            // RECURSIVE CASE: Iterate through possible recipes
            // ==========================================
            foreach (RecipeProto recipe in producingRecipes)
            {
                bool isVirtualRecipe = recipe.Id.Value.StartsWith("VirtualRecipe_");

                //if (isVirtualRecipe && !recipe.Id.Value.Contains("ChickenFarm"))
                //{
                //    if (!recipe.Id.Value.Contains("FarmT2")) continue;
                //}

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

                // Get the machine or farm proto
                // Get the machine or farm proto
                IProtoWithIcon actualMachineProto = this.m_catalog.GetFarmForRecipe(recipe) ?? (IProtoWithIcon)this.m_catalog.GetMachineForRecipe(recipe);
                string machineName = actualMachineProto?.Strings.Name.TranslatedString ?? "Unknown Factory";
                bool isFarm = this.m_catalog.GetFarmForRecipe(recipe) != null;
                if (isFarm) machineName = "Farm";

                // --- THE FIX: Universal Time Math ---
                float recipeDurationMonths = 1f; // Default for Virtual Farm Recipes (already normalized to 1 month)

                // If this is a real machine, extract the duration from its specific recipe binding
                if (!isFarm && actualMachineProto is MachineProto machineProto)
                {
                    var binding = machineProto.GetRecipeBindingFor(recipe);

                    // Calculate how much of a 60-second month this specific recipe takes to run once
                    recipeDurationMonths = (float)binding.Duration.Ticks / (float)60.Seconds().Ticks;
                }

                // Multiply the total required runs by the time it takes to do one run
                double machineCount = recipeRuns.ToFloat() * recipeDurationMonths;
                // ------------------------------------


                List<ResolvedChain> currentRecipePermutations = new List<ResolvedChain> { new ResolvedChain() };
                bool isDeadPath = false;

                // ==========================================
                // THE FIX: ACCUMULATE INPUTS WITHOUT NESTING
                // ==========================================
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

                            // Safely copy over the pending inputs we gathered from previous ingredients
                            foreach (var pending in existingPerm.PendingInputs)
                            {
                                combined.PendingInputs.Add(pending.Clone());
                            }

                            // Add the new branch for this ingredient
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

                // ==========================================
                // THE FIX: BUILD THE SINGLE PARENT NODE HERE
                // ==========================================
                foreach (var perm in currentRecipePermutations)
                {
                    perm.TargetProduct = targetProduct;
                    perm.TargetAmount = targetAmount;
                    if (isFarm) perm.FarmsNeeded += machineCount;

                    // Create ONE node for the machine
                    perm.RootNode = new ChainNode
                    {
                        IsFarm = isFarm,
                        IsBaseResource = false,
                        MachineName = machineName,
                        MachineCount = machineCount,
                        MachineProto = actualMachineProto,
                        OutputProduct = targetProduct,
                        OutputAmount = targetAmount
                    };

                    // Attach all the accumulated branches directly to this machine!
                    foreach (var pendingInput in perm.PendingInputs)
                    {
                        perm.RootNode.Inputs.Add(pendingInput);
                    }
                    perm.PendingInputs.Clear(); // Cleanup the temporary list

                    // Add byproducts
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

            // --- FINALIZE, DEDUPLICATE, AND SORT CHAINS ---

            // 1. Calculate scores for everything first
            foreach (var chain in alternatives)
            {
                CalculateScore(chain);
            }

            // 2. Sort them so the most efficient chains (lowest score) are processed first
            alternatives = alternatives.OrderBy(c => c.ResourceScore).ToList();

            List<ResolvedChain> uniqueAlternatives = new List<ResolvedChain>();

            // 3. Deduplicate
            foreach (var chain in alternatives)
            {
                if (chain.FarmsNeeded <= 0)
                {
                    continue;
                }

                bool isDuplicate = false;
                foreach (var uniqueChain in uniqueAlternatives)
                {
                    // Because the list is sorted, the first one saved is guaranteed to be the most efficient!
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