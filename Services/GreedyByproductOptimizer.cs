using Mafi;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.Services
{
    public class GreedyByproductOptimizer
    {
        private readonly ProductionChainService m_chainService;
        private readonly ProtosDb m_protosDb;

        public GreedyByproductOptimizer(ProductionChainService chainService, ProtosDb protosDb)
        {
            this.m_chainService = chainService;
            this.m_protosDb = protosDb;
        }

        public GreedyOptimizationResult RunTwoPassOptimization(
            Dictionary<ProductProto, Fix32> rawDemands,
            Dictionary<ProductProto, ResolvedChain> pinnedChains)
        {
            var result = new GreedyOptimizationResult();
            var globalCreditPool = new Dictionary<ProductProto, Fix32>();
            var byproductSources = new Dictionary<ProductProto, ResolvedChain>();

            // THE FIX: Order by our strict priority system so deep chains process first!
            var sortedDemands = rawDemands.OrderBy(kvp => GetProductPriority(kvp.Key.Id.Value)).ToList();

            // ==========================================
            // PASS 1: Resolution & Ledger Building
            // ==========================================
            foreach (var demand in sortedDemands)
            {
                ProductProto product = demand.Key;
                Fix32 amountNeeded = demand.Value;

                // 1. Check the Credit Ledger
                if (globalCreditPool.TryGetValue(product, out Fix32 availableCredit) && availableCredit > Fix32.Zero)
                {
                    if (availableCredit >= amountNeeded)
                    {
                        globalCreditPool[product] -= amountNeeded;
                        if (byproductSources.TryGetValue(product, out var sourceChain))
                        {
                            result.FinalChains[product] = sourceChain;
                        }
                        continue;
                    }
                    else
                    {
                        amountNeeded -= availableCredit;
                        globalCreditPool[product] = Fix32.Zero;
                    }
                }

                // 2. Calculate the chain for the remaining amount
                var chains = this.m_chainService.GetAlternativeChains(product, amountNeeded);
                if (chains.Count > 0)
                {
                    // Default to the mathematically best chain
                    ResolvedChain bestChain = chains[0];

                    // THE FIX: Check if the player manually pinned a different chain for this product!
                    if (pinnedChains != null && pinnedChains.TryGetValue(product, out var pinned))
                    {
                        // Find the matching chain in our newly scaled list
                        var matched = chains.Find(c => c.IsEquivalentTo(pinned));
                        if (matched != null)
                        {
                            bestChain = matched; // Override the dictator!
                        }
                    }

                    result.FinalChains[product] = bestChain;

                    // 3. Deposit byproducts into the Credit Ledger AND tag the source
                    foreach (var byproduct in bestChain.NetByproducts)
                    {
                        if (globalCreditPool.ContainsKey(byproduct.Key))
                            globalCreditPool[byproduct.Key] += byproduct.Value;
                        else
                            globalCreditPool[byproduct.Key] = byproduct.Value;

                        byproductSources[byproduct.Key] = bestChain;
                    }

                    // Run the cascade IMMEDIATELY! 
                    ResolveLeftoverByproducts(globalCreditPool, byproductSources);
                }
            }

            // ==========================================
            // PASS 2: Final Cleanup
            // ==========================================
            ResolveLeftoverByproducts(globalCreditPool, byproductSources);

            foreach (var kvp in globalCreditPool)
            {
                if (kvp.Value > Fix32.Zero)
                    result.FinalSurpluses[kvp.Key] = kvp.Value;
            }

            return result;
        }

        private int GetProductPriority(string productId)
        {
            // Lowercase to avoid Mafi ID mismatch issues (e.g. "Product_Meat" vs "Meat")
            string id = productId.ToLower();

            // PRIORITY 1 (Calculate FIRST): Deep Consumers.
            // Calculating these first pulls in the entire production tree (like Chicken Farms)
            // and floods the byproduct pool with shallow items (like Eggs).
            if (id.Contains("meat") || id.Contains("sausage") || id.Contains("snack") || id.Contains("feed"))
                return 1;

            // PRIORITY 3 (Calculate LAST): Shallow Producers.
            // If we calculate these last, their demand is usually completely satisfied 
            // by the massive byproduct pool generated by Priority 1 items.
            if (id.Contains("egg") || id.Contains("oil") || id.Contains("flour") || id.Contains("carcass"))
                return 3;

            // PRIORITY 2: Everything else
            return 2;
        }

        private void ResolveLeftoverByproducts(Dictionary<ProductProto, Fix32> pool, Dictionary<ProductProto, ResolvedChain> sources)
        {
            ProcessSpecificByproduct(pool, sources, "ChickenCarcass", "Meat", 1.0f, new Dictionary<string, float> { { "MeatTrimmings", 0.333f } });
        }

        private void ProcessSpecificByproduct(
            Dictionary<ProductProto, Fix32> pool,
            Dictionary<ProductProto, ResolvedChain> sources,
            string inputId,
            string mainOutputId,
            float mainOutputRatio,
            Dictionary<string, float> extraOutputsRatios = null)
        {
            var inputKey = pool.Keys.FirstOrDefault(p => p.Id.Value == inputId);
            if (inputKey == null || pool[inputKey] <= Fix32.Zero) return;

            Fix32 amountToProcess = pool[inputKey];
            pool[inputKey] = Fix32.Zero; // Consume it

            // Track the source chain so the UI can link them
            sources.TryGetValue(inputKey, out var sourceChain);

            // Convert to Main Output
            if (m_protosDb.TryGetProto(new ProductProto.ID(mainOutputId), out ProductProto mainOutputProto))
            {
                Fix32 outputAmount = amountToProcess * Fix32.FromFloat(mainOutputRatio);
                if (pool.ContainsKey(mainOutputProto)) pool[mainOutputProto] += outputAmount;
                else pool[mainOutputProto] = outputAmount;

                if (sourceChain != null) sources[mainOutputProto] = sourceChain;
            }

            // Convert to Extra Outputs
            if (extraOutputsRatios != null)
            {
                foreach (var extra in extraOutputsRatios)
                {
                    if (m_protosDb.TryGetProto(new ProductProto.ID(extra.Key), out ProductProto extraProto))
                    {
                        Fix32 extraAmount = amountToProcess * Fix32.FromFloat(extra.Value);
                        if (pool.ContainsKey(extraProto)) pool[extraProto] += extraAmount;
                        else pool[extraProto] = extraAmount;

                        if (sourceChain != null) sources[extraProto] = sourceChain;
                    }
                }
            }
        }
    }

    public class GreedyOptimizationResult
    {
        public Dictionary<ProductProto, ResolvedChain> FinalChains { get; set; } = new Dictionary<ProductProto, ResolvedChain>();
        public Dictionary<ProductProto, Fix32> FinalSurpluses { get; set; } = new Dictionary<ProductProto, Fix32>();
    }
}