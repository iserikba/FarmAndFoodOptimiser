using Mafi;
using System.Collections.Generic;

namespace Iserik.FaFOptimiser.Services
{
    /// <summary>
    /// Holds the global supply-side constraints and current infrastructure.
    /// </summary>
    public class FarmInfrastructureMetrics
    {
        // Global edict/research boosts (e.g., +20% crop yield)
        public Percent GlobalCropBoost { get; set; }

        // How many of each farm type are currently built (e.g., "Irrigated Farm" -> 4)
        public Dictionary<string, int> BuiltFarmsByType { get; set; }

        // We will need to track the average or target fertility
        public Fix32 AverageFertility { get; set; }

        public FarmInfrastructureMetrics()
        {
            this.GlobalCropBoost = Percent.Zero;
            this.BuiltFarmsByType = new Dictionary<string, int>();
            this.AverageFertility = Fix32.Zero;
        }
    }
}