using Iserik.FaFOptimiser.Persistence;
using Mafi;
using Mafi.Core.Buildings.Settlements; // Need this for the Settlement and FoodData classes
using Mafi.Core.Products;
using Mafi.Core.Population;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.Services
{
    public class SettlementTelemetryService
    {
        private readonly SettlementsManager m_settlementsManager;

        public SettlementTelemetryService(SettlementsManager settlementsManager)
        {
            this.m_settlementsManager = settlementsManager;
        }

        /// <summary>
        /// Analyzes all settlements on the island and returns a unified map of food demand.
        /// </summary>
        public Dictionary<ProductProto, FoodDemandMetrics> GetUnifiedFoodDemand()
        {
            var aggregatedDemand = new Dictionary<ProductProto, FoodDemandMetrics>();

            // The Settlements property comes from the SettlementsManager as you noted
            foreach (Settlement settlement in this.m_settlementsManager.Settlements)
            {
                // Iterate through the publicly exposed FoodTypesMap 
                foreach (var kvp in settlement.FoodTypesMap)
                {
                    ProductProto foodProduct = kvp.Key;
                    Settlement.FoodData foodData = kvp.Value;

                    // If we haven't seen this food type yet, add it to our dictionary
                    if (!aggregatedDemand.ContainsKey(foodProduct))
                    {
                        aggregatedDemand[foodProduct] = new FoodDemandMetrics(foodProduct);
                    }

                    FoodDemandMetrics metrics = aggregatedDemand[foodProduct];

                    // Extract the data.
                    // Note: 'SupplyTemp' and 'SupplyLeft' are often used interchangeably 
                    // depending on when in the simulation tick you check. We will use SupplyLeft 
                    // as it represents the stock after consumption.
                    metrics.CurrentStock += foodData.SupplyLeft.Value;
                    metrics.TotalCapacity += foodData.Capacity.Value;

                    // The 'EstimatedMonthlyConsumption' is stored as a PartialQuantity.
                    // We pull its float value and convert to Fix32 for our solver math later.
                    metrics.EstimatedMonthlyConsumption += Fix32.FromFloat(foodData.EstimatedMonthlyConsumption.Value.ToFloat());
                }

                // --- PASS 2: Scan Food Markets for zero-stock slotted items ---
                // AllFoodModules holds every Food Market attached to this settlement
                foreach (SettlementFoodModule foodMarket in settlement.AllFoodModules)
                {
                    // BuffersPerSlot represents the UI slots (e.g., the Bread, Corn, Snack, Cake dropdowns)
                    foreach (var bufferOpt in foodMarket.BuffersPerSlot)
                    {
                        // Check if the slot actually has a product selected in it
                        if (bufferOpt.HasValue && bufferOpt.Value.Product != null)
                        {
                            ProductProto slottedProduct = bufferOpt.Value.Product;

                            // If we found it in a market, flip the flag to true
                            if (aggregatedDemand.TryGetValue(slottedProduct, out var metrics))
                            {
                                metrics.IsConfiguredInMarket = true;
                            }
                        }
                    }
                }
            }

            return aggregatedDemand;
        }

        // Wrapper method to print the results to our UI Log
        public void PrintDemandToLog(OptimiserLog logger)
        {
            int totalPop = this.m_settlementsManager.GetTotalPopulation();
            logger.AddMessage($"--- Total Island Population: {totalPop} ---");

            var demandMap = this.GetUnifiedFoodDemand();
            var sortedDemands = demandMap.Values.OrderBy(m => m.Product.Strings.Name.TranslatedString);

            foreach (var metrics in sortedDemands)
            {
                // FIX: Now we also log the item if it is configured in a market, 
                // even if capacity, stock, and current consumption are all zero!
                if (metrics.TotalCapacity > Fix32.Zero ||
                    metrics.CurrentStock > Fix32.Zero ||
                    metrics.EstimatedMonthlyConsumption > Fix32.Zero ||
                    metrics.IsConfiguredInMarket) // <-- The new condition
                {
                    string name = metrics.Product.Strings.Name.TranslatedString;
                    string stock = metrics.CurrentStock.ToStringRounded(1);
                    string consumption = metrics.EstimatedMonthlyConsumption.ToStringRounded(1);

                    // Add a little tag so we know it's a zero-stock demanded item
                    string supplyStr = metrics.GetMonthsOfSupply() >= 99 ? "99+" : metrics.GetMonthsOfSupply().ToString();
                    string warningTag = (metrics.IsConfiguredInMarket && metrics.CurrentStock <= Fix32.Zero) ? " [ZERO STOCK]" : "";

                    logger.AddMessage($"- {name}: Stock: {stock} | Demand: {consumption}/mo | Supply: {supplyStr} months{warningTag}");
                }
            }
        }
    }
}