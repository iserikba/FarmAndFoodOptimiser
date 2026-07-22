using Iserik.FaFOptimiser.Persistence; // Ensure this is added
using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Translations;
using Iserik.FaFOptimiser.UI;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;
using System;
using static Mafi.Core.Prototypes.EntityCostsTpl;

namespace Iserik.FaFOptimiser
{
    public sealed class FaFOptimiserMod : IMod, IDisposable
    {
        public ModManifest Manifest { get; private set; }
        public ModJsonConfig JsonConfig { get; private set; }
        public Option<IConfig> ModConfig { get; set; }

        public bool IsUiOnly => true;

        public FaFOptimiserMod(ModManifest manifest)
        {
            this.Manifest = manifest;
            this.JsonConfig = new ModJsonConfig(this);
            Log.Info("FaFOptimiser: Mod successfully loaded.");
        }

        void IMod.RegisterPrototypes(ProtoRegistrator registrator) { }

        void IMod.RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
        {
            // Register Persistence
            depBuilder.RegisterDependency<OptimiserLog>().AsSelf();

            // Register Services
            depBuilder.RegisterDependency<FarmTelemetryService>().AsSelf();
            depBuilder.RegisterDependency<SettlementTelemetryService>().AsSelf();
            depBuilder.RegisterDependency<CommandProcessor>().AsSelf();
            depBuilder.RegisterDependency<Iserik.FaFOptimiser.Catalog.RecipeCatalog>().AsAllInterfaces().AsSelf();
            depBuilder.RegisterDependency<Iserik.FaFOptimiser.Services.ProductionChainService>().AsAllInterfaces().AsSelf();

            depBuilder.RegisterDependency<DemandStateManager>().AsSelf();
            depBuilder.RegisterDependency<GreedyByproductOptimizer>().AsSelf();
        }

        void IMod.EarlyInit(DependencyResolver resolver) { }

        void IMod.Initialize(DependencyResolver resolver, bool gameWasLoaded)
        {
            Log.Info("FaFOptimiser: Mod initialization complete.");
        }

        void IMod.MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues) { }

        public void Dispose() { }
    }
}