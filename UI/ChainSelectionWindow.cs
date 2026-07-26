using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Translations;
using Mafi;
using Mafi.Base;
using Mafi.Core.Products;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using System.Collections.Generic;

namespace Iserik.FaFOptimiser.UI
{
    public class ChainSelectionWindow : Window
    {
        private readonly DemandStateManager m_demandManager;
        private readonly ProductProto m_targetProduct;
        private readonly List<ResolvedChain> m_chains;

        public ChainSelectionWindow(DemandStateManager demandManager, ProductProto targetProduct, List<ResolvedChain> chains)
                : base(Strings.SelectProductionChain.Format(targetProduct.Strings.Name.TranslatedString), true)
        {
            this.m_demandManager = demandManager;
            this.m_targetProduct = targetProduct; 
            this.m_chains = chains;

            // Use the same sizing logic as RecipePicker
            base.WindowSize(800.px(), Px.Auto);
            base.WindowMaxHeight(60.Percent());
            base.MakeMovable();
            base.CloseOnClickOutside();

            BuildUi();
        }

        private void BuildUi()
        {
            // 1. Setup the ScrollColumn just like RecipePicker
            ScrollColumn scrollColumn = new ScrollColumn();
            scrollColumn.Add(delegate (ScrollColumn c)
            {
                c.Fill<ScrollColumn>();
            });

            // 2. Use Mafi's native RecipesColumn to get the perfect 1.pt gap spacing
            RecipesColumn recipesColumn = new RecipesColumn(new Px?(1.pt())).AlignItemsStretch<RecipesColumn>();
            scrollColumn.Add(recipesColumn);

            var flowchartBuilder = new Iserik.FaFOptimiser.Ui.ChainInfoPanel();

            // 3. Build each chain using the native ButtonRow
            foreach (var chain in this.m_chains)
            {
                // Look in SelectedChains first; fallback to SelectedFlockChain if checking Animal Feed
                ResolvedChain activeChain = null;
                if (!this.m_demandManager.SelectedChains.TryGetValue(this.m_targetProduct, out activeChain))
                {
                    if (this.m_targetProduct.Id == Ids.Products.AnimalFeed)
                    {
                        activeChain = this.m_demandManager.SelectedFlockChain;
                    }
                }

                bool isActiveChain = (activeChain != null && chain.IsEquivalentTo(activeChain));

                // Button.Area provides the dark background, hover glow, and selected green border
                ButtonRow chainLine = new ButtonRow(Button.Area, null)
                    .Selected(isActiveChain)
                    .OnClick(() =>
                    {
                        this.m_demandManager.SetSelectedChain(this.m_targetProduct, chain);
                        base.Close();
                    }, false);

                chainLine.Border(1.px(), isActiveChain ? Mafi.Unity.UiToolkit.Theme.PositiveColor : Mafi.ColorRgba.White.SetA(40));
                chainLine.Padding(5.pt()).AlignItemsCenterMiddle();

                // Left-aligned Checkmark Area
                Column checkArea = new Column().Width(40.px()).AlignItemsCenterMiddle();
                if (isActiveChain)
                {
                    checkArea.Add(new Label("✓".AsLoc())
                        .FontBold()
                        .FontSize(18)
                        .Color(new Mafi.ColorRgba?(Mafi.Unity.UiToolkit.Theme.PositiveColor)));
                }
                chainLine.Add(checkArea);

                // Add the flowchart infographic
                chainLine.Add(flowchartBuilder.BuildPanel(chain).FlexGrow(1f));

                recipesColumn.Add(chainLine);
            }

            // 4. THE MAGIC BULLET: Wrap it in an array and use AddBodySingle
            // This is the specific method that creates the dark window panel!
            UiComponent[] array = new UiComponent[1];
            array[0] = scrollColumn;

            base.AddBodySingle(array);
        }
    }
}
 