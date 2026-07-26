using Iserik.FaFOptimiser.Persistence;
using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Translations;
using Mafi;
using Mafi.Core.Prototypes;
using Mafi.Unity.Ui;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit;

namespace Iserik.FaFOptimiser.UI
{
    public sealed class OptimiserMainWindow : Window
    {
        public OptimiserMainWindow(
            OptimiserLog logger,
            CommandProcessor commandProcessor,
            ProtosDb protosDb,
            DemandStateManager demandManager,
            SettlementTelemetryService telemetryService,
            ProductionChainService chainService,
            FarmTelemetryService farmService,
            UiContext context) // <-- Kept this, removed the other two
                    : base(Strings.WindowTitle, false)
        {
#if PUBLIC_BUILD
            base.WindowSize(950.px(), 750.px());
#else
            base.WindowSize(1250.px(), 750.px());
#endif
            base.MakeMovable();
            base.EnablePinning();

            // 1. Instantiate the modular panels
            var inputPanel = new OptimizerInputPanel(this, logger, commandProcessor, protosDb, demandManager, 
                telemetryService, chainService, farmService, context);
#if !PUBLIC_BUILD
            var logPanel = new LogPanel(logger, commandProcessor);
#endif
            var resultPanel = new ResultPanel(protosDb, demandManager);

            // 2. Assemble the main layout
            Row mainLayout = new Row(6.pt()).AlignItemsStretch().FlexGrow(1f);
            mainLayout.Add(inputPanel.BuildPanel());
#if !PUBLIC_BUILD
            mainLayout.Add(logPanel.BuildPanel());
#endif
            mainLayout.Add(resultPanel.BuildPanel());

            base.Body.Add(mainLayout);

            // 3. Connect the backend events to the results panel
            commandProcessor.OnOptimizationFinished += resultPanel.RenderVisualResults;
            //commandProcessor.OnChainTestFinished += resultPanel.RenderChainQaResults;
        }
    }
}