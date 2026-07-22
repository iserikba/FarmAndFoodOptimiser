using Mafi;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Entities;
using Mafi.Core.PropertiesDb; // Needed for global edict boosts
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.Services
{
    public class FarmTelemetryService
    {
        private readonly IEntitiesManager m_entitiesManager;
        private readonly IPropertiesDb m_propertiesDb;

        // Inject the Properties database alongside the Entities manager
        public FarmTelemetryService(IEntitiesManager entitiesManager, IPropertiesDb propertiesDb)
        {
            this.m_entitiesManager = entitiesManager;
            this.m_propertiesDb = propertiesDb;
        }

        /// <summary>
        /// Scans the island and returns a list of all fully constructed farms.
        /// </summary>
        public List<Farm> GetAllBuiltFarms()
        {
            // FIX: Use Mafi's native type-filtering method instead of LINQ's OfType
            return this.m_entitiesManager.GetAllEntitiesOfType<Farm>()
                .Where(farm => farm.IsConstructed)
                .ToList();
        }
    

        public FarmInfrastructureMetrics GetInfrastructureMetrics()
        {
            var metrics = new FarmInfrastructureMetrics();

            // 1. Get the Global Farming Boost
            metrics.GlobalCropBoost = this.m_propertiesDb.GetProperty(Mafi.Core.IdsCore.PropertyIds.FarmYieldMultiplier).Value;

            var allFarms = this.m_entitiesManager.GetAllEntitiesOfType<Farm>()
                .Where(farm => farm.IsConstructed)
                .ToList();

            if (allFarms.Count == 0) return metrics;

            Fix32 totalFertility = Fix32.Zero;

            // 2. Scan every farm for counts, fertilizer, and fertility
            foreach (Farm farm in allFarms)
            {
                string farmName = farm.Prototype.Strings.Name.TranslatedString;

                if (!metrics.BuiltFarmsByType.ContainsKey(farmName))
                {
                    metrics.BuiltFarmsByType[farmName] = 0;
                }
                metrics.BuiltFarmsByType[farmName]++;

                // 3. Inspect the Farm's internal state
                // PSEUDO-CODE: We need to find how the game stores current fertility and fertilizer type
                // totalFertility += farm.CurrentFertility;
                // var fertilizer = farm.ActiveFertilizerModule;
            }

            // Calculate the average fertility across all active farms
            metrics.AverageFertility = totalFertility / Fix32.FromInt(allFarms.Count);

            return metrics;
        }
    }
}