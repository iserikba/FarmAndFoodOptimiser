using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Translations; // <--- ADDED TRANSLATION REFERENCE
using Mafi;
using Mafi.Core.Products;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using System.Collections.Generic;
using Mafi.Core.Prototypes;
using System.Linq;

namespace Iserik.FaFOptimiser.Ui
{
    public class ChainInfoPanel
    {
        public ChainInfoPanel() { }

        public UiComponent BuildPanel(ResolvedChain chain)
        {
            var mainContainer = new Column(5.pt());

            // ==========================================
            // 1. RESTORED: THE INPUTS ROW
            // ==========================================
            if (chain.RawCropDemands.Count > 0)
            {
                var inputsRow = new Row().Wrap(true).AlignItemsCenterMiddle();
                // REPLACED STRING
                inputsRow.Add(new Label(Strings.Inputs).FontBold().MarginRight(5.pt()));

                foreach (var kvp in chain.RawCropDemands)
                {
                    inputsRow.Add(CreateIconWithText(kvp.Key, kvp.Value));
                }
                mainContainer.Add(inputsRow);
            }

            // ==========================================
            // THE FLOWCHART (Linear & Wrapping)
            // ==========================================
            var flowchartRow = new Row().Wrap(true).AlignItemsCenterMiddle().MarginTop(2.pt()).MarginBottom(2.pt());

            if (chain.RootNode != null)
            {
                DrawNode(chain.RootNode, flowchartRow);
            }
            mainContainer.Add(flowchartRow);

            // ==========================================
            // BYPRODUCTS
            // ==========================================
            if (chain.NetByproducts.Count > 0)
            {
                var byproductsRow = new Row().Wrap(true).AlignItemsCenterMiddle();
                // REPLACED STRING
                byproductsRow.Add(new Label(Strings.ByproductsLabel).FontBold().MarginRight(5.pt()));

                foreach (var kvp in chain.NetByproducts)
                {
                    byproductsRow.Add(CreateIconWithText(kvp.Key, kvp.Value));
                }
                mainContainer.Add(byproductsRow);
            }

            return mainContainer;
        }

        private void DrawNode(ChainNode node, UiComponent parentRow)
        {
            // Strictly filter out base resources so missing icons won't delete entire factories!
            var primaryInputs = node.Inputs.Where(n => !n.IsBaseResource).ToList();

            if (primaryInputs.Count == 1)
            {
                DrawNode(primaryInputs[0], parentRow);
            }
            else if (primaryInputs.Count > 1)
            {
                var inputsColumn = new Column(2.pt()).AlignItemsCenterMiddle();
                foreach (var inputNode in primaryInputs)
                {
                    var singleInputRow = new Row().AlignItemsCenterMiddle();
                    DrawNode(inputNode, singleInputRow);
                    inputsColumn.Add(singleInputRow);
                }
                parentRow.Add(inputsColumn);
            }

            if (primaryInputs.Count > 0)
            {
                parentRow.Add(CreateArrow());
            }

            // ==========================================
            // THE DENSIFIED MACHINE ICON
            // ==========================================
            var machineBox = new Column(0.pt()).AlignItemsCenterMiddle().Margin(0, 2.pt(), 0, 2.pt());

            if (node.MachineProto != null)
            {
                machineBox.Add(new Icon(node.MachineProto, false, false)
                    .Size(32.px())
                    .Tooltip(node.MachineProto.Strings.Name, true, false, false));
            }
            else
            {
                machineBox.Add(new Label($"[{node.MachineName}]".AsLoc()).FontBold().FontSize(11));
            }

            // 2. THE FIX: Format multiplier as x0.00
            machineBox.Add(new Label($"x{node.MachineCount:F2}".AsLoc())
                .FontBold()
                .FontSize(11)
                .Opacity(0.8f));

            parentRow.Add(machineBox);
            parentRow.Add(CreateArrow());

            // --- THE OUTPUT PRODUCT ---
            parentRow.Add(CreateIconWithText(node.OutputProduct, node.OutputAmount));
        }

        // 4. THE FIX: Reduced margins around the arrows
        private UiComponent CreateArrow()
        {
            return new Label(">".AsLoc())
                .FontSize(14)
                .FontBold()
                .Opacity(0.5f)
                .Margin(0, 4.px(), 0, 4.px()); // Squished from 1.pt() down to 4.px()
        }

        private UiComponent CreateIconWithText(ProductProto product, Fix32 amount)
        {
            return new DisplayWithIcon()
                .IconValue(product)
                .Value((amount).ToStringRoundedAdaptive(2).AsLoc())
                .Tooltip(product.Strings.Name, true, false, false)
                .SuperCompact()
                .MarginRight(2.pt());
        }
    }
}