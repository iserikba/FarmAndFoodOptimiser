using Iserik.FaFOptimiser.Persistence;
using Iserik.FaFOptimiser.Services;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace Iserik.FaFOptimiser.UI
{
    public class LogPanel
    {
        private readonly OptimiserLog m_logger;
        private readonly CommandProcessor m_commandProcessor;
        private Label m_logDisplay;
        private TextField m_commandInput;

        public LogPanel(OptimiserLog logger, CommandProcessor commandProcessor)
        {
            this.m_logger = logger;
            this.m_commandProcessor = commandProcessor;
        }

        public Column BuildPanel()
        {
            Column panel = new Column(4.pt()).Width(360.px()).FlexShrink(0f).AlignItemsStretch();

            PanelWithHeader logPanel = new PanelWithHeader("Solver Log".AsLoc());
            ScrollColumn logScroll = new ScrollColumn().AlignItemsStretch().FlexGrow(1f);
            this.m_logDisplay = new Label(this.m_logger.GetAllLogs().AsLoc()).TextLeftTop();
            logScroll.Add(this.m_logDisplay);
            logPanel.Body.Add(logScroll);

            panel.Add(logPanel.FlexGrow(1f));

            this.m_commandInput = new TextField().Text("".AsLoc()).OnEditEnd(this.onCommandSubmitted);
            Row inputRow = new Row(2.pt()).AlignItemsCenterMiddle().MarginTop(4.pt()).MarginBottom(4.pt());
            inputRow.Add(new Label("Command: ".AsLoc()));
            inputRow.Add(this.m_commandInput.FlexGrow(1f));
            panel.Add(inputRow);

            this.m_logger.OnLogAdded += this.updateLogDisplay;

            return panel;
        }

        private void onCommandSubmitted(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            this.m_logger.AddMessage($"> {command}");
            this.m_commandProcessor.Execute(command);
            this.m_commandInput.Text("".AsLoc());
        }

        private void updateLogDisplay(string newLog)
        {
            this.m_logDisplay.Value(this.m_logger.GetAllLogs().AsLoc());
        }
    }
}