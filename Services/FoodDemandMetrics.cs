using Mafi.Core.Products;
using Mafi;

namespace Iserik.FaFOptimiser.Services
{
    /// <summary>
    /// Holds the aggregated island-wide metrics for a single type of food.
    /// </summary>
    public class FoodDemandMetrics
    {
        public ProductProto Product { get; }

        // Using CoI's native Fix32 for precise mathematical operations later
        public Fix32 CurrentStock { get; set; }
        public Fix32 EstimatedMonthlyConsumption { get; set; }
        public Fix32 TotalCapacity { get; set; }
        // Flag to indicate if the player has slotted this in a Food Market
        public bool IsConfiguredInMarket { get; set; }

        public FoodDemandMetrics(ProductProto product)
        {
            this.Product = product;
            this.CurrentStock = Fix32.Zero;
            this.EstimatedMonthlyConsumption = Fix32.Zero;
            this.TotalCapacity = Fix32.Zero;
            this.IsConfiguredInMarket = false;
        }

        // Helper to calculate how long this specific food will last
        public int GetMonthsOfSupply()
        {
            if (this.EstimatedMonthlyConsumption <= Fix32.Zero) return 99; // 99 is CoI's "infinite" cap

            return (this.CurrentStock / this.EstimatedMonthlyConsumption).IntegerPart;
        }
    }
}