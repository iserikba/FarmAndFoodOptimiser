using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Solver;
using Iserik.FaFOptimiser.Translations;
using Iserik.FaFOptimiser.Ui;
using Mafi;
using Mafi.Base;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.UI
{
    public class ResultPanel
    {
        private readonly ProtosDb m_protosDb;
        private readonly DemandStateManager m_demandManager; // <-- Added State Manager

        private Column m_cropsBody;
        private Column m_farmsBody;
        private Column m_chainQaBody;

        private static readonly Px MachineIconSize = 36.px();
        private static readonly Px ProductIconSize = 28.px();

        // Pass the DemandStateManager in via the constructor
        public ResultPanel(ProtosDb protosDb, DemandStateManager demandManager)
        {
            this.m_protosDb = protosDb;
            this.m_demandManager = demandManager;
        }

        public Column BuildPanel()
        {
            Column panel = new Column(2.pt()).FlexGrow(1f).AlignItemsStretch();
            ScrollColumn rightScroll = new ScrollColumn().AlignItemsStretch().FlexGrow(1f);

            PanelWithHeader farmsPanel = new PanelWithHeader(Strings.FarmsList);
            this.m_farmsBody = new Column(0.pt()).Padding(0.pt());
            farmsPanel.Body.Add(this.m_farmsBody);
            rightScroll.Add(farmsPanel.MarginBottom(5.pt()));

            PanelWithHeader cropsPanel = new PanelWithHeader(Strings.ProducedCrops);
            cropsPanel.Body.Add(this.m_cropsBody = new Column(1.pt()));
            rightScroll.Add(cropsPanel.MarginBottom(5.pt()));

            // REPLACED STRING
            PanelWithHeader ioPanel = new PanelWithHeader(Strings.InputsOutputs);

            // Reduced the padding and internal gap to tighten the whole panel
            this.m_chainQaBody = new Column(2.pt()).Padding(2.pt(), 5.pt(), 5.pt(), 5.pt());
            ioPanel.Body.Add(this.m_chainQaBody);
            rightScroll.Add(ioPanel);

            panel.Add(rightScroll);
            return panel;
        }

        public void RenderVisualResults(OptimizationResult result)
        {
            // Store the latest result in the state manager
            this.m_demandManager.LatestResult = result;

            this.m_cropsBody.Clear();
            this.m_farmsBody.Clear();
            this.m_chainQaBody.Clear();

            // 1. RENDER PRODUCED CROPS (Deficit/Surplus)
            Row row = new Row().Wrap(true).PaddingTop(1.pt());
            foreach (var cropSummary in result.CropSummaries)
            {
                if (this.m_protosDb.TryGetProto(new Proto.ID(cropSummary.Name), out ProductProto productProto))
                {
                    float target = (float)cropSummary.Target;
                    float deficit = (float)cropSummary.Deficit;
                    row.Add(renderProductTile(productProto, target - deficit, -deficit));
                }
            }
            this.m_cropsBody.Add(row);

            // 2. RENDER FARMS BLUEPRINT
            for (int tier = 1; tier < 5; tier++)
            {
                foreach (var entry in result.Blueprint)
                {
                    if (entry.Tier == tier)
                        this.renderFarmRow(entry, (float)result.TargetFertility);
                }
            }

            // 3. RENDER INPUTS/OUTPUTS (Chain QA Analysis)
            RenderInputsOutputs(result);
        }

        private void RenderInputsOutputs(OptimizationResult result)
        {
            // --- 1. FARM INPUTS (Water & Fertilizer) ---
            // Use the perfectly calculated totals directly from the solver result!
            double totalWater = result.TotalWater;
            double totalFert = result.TotalFertilizer;

            List<UiComponent> farmInputTiles = new List<UiComponent>();

            if (totalWater > 0 && this.m_protosDb.TryGetProto(Ids.Products.Water, out ProductProto waterProto))
                farmInputTiles.Add(renderSimpleTile(waterProto, (float)totalWater));

            if (totalFert > 0)
            {
                // Set gap entirely to 0pt
                Row fertGroup = new Row(0.pt()).AlignItemsCenterMiddle();
                bool addedFirst = false;

                if (this.m_protosDb.TryGetProto(Ids.Products.FertilizerOrganic, out ProductProto orgFert))
                {
                    fertGroup.Add(renderSimpleTile(orgFert, (float)totalFert));
                    addedFirst = true;
                }
                if (this.m_protosDb.TryGetProto(Ids.Products.FertilizerChemical, out ProductProto fert1))
                {
                    if (addedFirst) fertGroup.Add(new Label("/".AsLoc()).FontSize(14).Opacity(0.5f).Margin(0));
                    fertGroup.Add(renderSimpleTile(fert1, (float)totalFert * 0.5f));
                    addedFirst = true;
                }
                if (this.m_protosDb.TryGetProto(Ids.Products.FertilizerChemical2, out ProductProto fert2))
                {
                    if (addedFirst) fertGroup.Add(new Label("/".AsLoc()).FontSize(14).Opacity(0.5f).Margin(0));
                    fertGroup.Add(renderSimpleTile(fert2, (float)totalFert * 0.4f));
                }

                farmInputTiles.Add(fertGroup.MarginRight(4.pt()));
            }

            // REPLACED STRING
            buildIOSection(Strings.FarmInputs, farmInputTiles);

            // --- 2. CHAIN INPUTS ---
            Dictionary<ProductProto, Fix32> chainInputs = new Dictionary<ProductProto, Fix32>();
            foreach (var chain in this.m_demandManager.SelectedChains.Values)
            {
                // We use GetRequiredInputs here so the UI shows Water, Sulfur, Limestone, etc.
                foreach (var kvp in chain.GetRequiredInputs())
                {
                    if (chainInputs.ContainsKey(kvp.Key)) chainInputs[kvp.Key] += kvp.Value;
                    else chainInputs[kvp.Key] = kvp.Value;
                }
            }

            List<UiComponent> chainInputTiles = new List<UiComponent>();
            foreach (var kvp in chainInputs)
            {
                chainInputTiles.Add(renderSimpleTile(kvp.Key, kvp.Value.ToFloat()));
            }
            // REPLACED STRING
            buildIOSection(Strings.ChainInputs, chainInputTiles);

            // --- 3. PRODUCTS PRODUCED ---
            List<UiComponent> producedTiles = new List<UiComponent>();
            foreach (var kvp in this.m_demandManager.ManufacturedDemands)
            {
                producedTiles.Add(renderSimpleTile(kvp.Key, kvp.Value.ToFloat()));
            }
            // REPLACED STRING
            buildIOSection(Strings.Produced, producedTiles);

            // --- 4. BYPRODUCTS ---
            List<UiComponent> byproductTiles = new List<UiComponent>();
            foreach (var kvp in this.m_demandManager.CurrentSurpluses)
            {
                byproductTiles.Add(renderSimpleTile(kvp.Key, kvp.Value.ToFloat()));
            }
            // REPLACED STRING
            buildIOSection(Strings.Byproducts, byproductTiles);
        }

        // --- HELPER 1: Headers ABOVE icons, tighter section spacing ---
        private void buildIOSection(LocStrFormatted title, List<UiComponent> tiles)
        {
            if (tiles.Count == 0) return;

            Column sectionContainer = new Column(1.pt()).MarginBottom(2.pt());

            sectionContainer.Add(new Label(title).TextLeftMiddle().Opacity(0.7f));

            Row tilesContainer = new Row(2.pt()).Wrap(true);
            foreach (var tile in tiles)
            {
                tilesContainer.Add(tile);
            }

            sectionContainer.Add(tilesContainer);
            this.m_chainQaBody.Add(sectionContainer);
        }

        // --- HELPER 2: Removed fixed width, matched crop panel text spacing ---
        private UiComponent renderSimpleTile(ProductProto product, float amount)
        {
            // Gap reduced to 1.pt to match your Crop panel exactly
            Column column = new Column(1.pt());

            column.Add(new Icon().Value(product, false).Size(ProductIconSize));
            column.Add(new Label(amount.ToStringRoundedAdaptive(2).AsLoc()).TextCenterMiddle());

            return column.AlignItemsCenterMiddle();
        }

        private void renderFarmRow(BlueprintEntry entry, float targetFertility)
        {
            Row row = new Row().Height(36.px()).AlignItemsCenterMiddle().MarginBottom(2.pt());
            row.Add(new Label($"{entry.Quantity}x".AsLoc()).FontBold().Width(25.px()));

            Proto.ID farmProtoId = Ids.Buildings.FarmT1;
            if (entry.Tier == 3) { farmProtoId = Ids.Buildings.FarmT3; }
            else if (entry.Tier == 4) { farmProtoId = Ids.Buildings.FarmT4; }
            else { farmProtoId = Ids.Buildings.FarmT2; }

            if (this.m_protosDb.TryGetProto(farmProtoId, out FarmProto farmProto))
            {
                // Format the dynamic values into a standard string first, then wrap with .AsLoc()
                string formattedText = string.Format(
                    "{0}\nTarget Fertility: {1}\nWater Needed: {2}\nFertility Needed: {3}",
                    farmProto.Strings.Name.TranslatedString, // Or farmProto.Strings.Name if it handles implicit conversion
                    targetFertility.ToStringRoundedAdaptive(2),
                    ((float)entry.WaterCost).ToStringRoundedAdaptive(2),
                    ((float)entry.FertCost).ToStringRoundedAdaptive(2)
                );

                LocStrFormatted Tooltip = formattedText.AsLoc();

                row.Add(new Icon(farmProto, false, false)
                    .Size(MachineIconSize)
                    .Tooltip(Tooltip, true, false, false)
                    .MarginRight(5.pt()));
            }

            Row rotationRow = new Row(2.pt()).AlignItemsCenterMiddle();
            int cropCount = entry.CropSequence.Count;
            for (int i = 0; i < cropCount; i++)
            {
                var cropRecipe = entry.CropSequence[i];
                if (this.m_protosDb.TryGetProto(new Proto.ID(cropRecipe.Name), out ProductProto cropProto))
                {
                    DisplayWithIcon cropDisplay = new DisplayWithIcon()
                        .IconValue(cropProto)
                        .Value(((float)cropRecipe.RotationProduction).ToStringRoundedAdaptive(2).AsLoc())
                        .Tooltip(cropProto.Strings.Name, true, false, false)
                        .SuperCompact();

                    rotationRow.Add(cropDisplay);
                    if (i < cropCount - 1)
                    {
                        rotationRow.Add(new Label(">".AsLoc()).FontSize(14).FontBold().Opacity(0.5f).Margin(0, 2.pt(), 0, 2.pt()));
                    }
                }
            }
            row.Add(rotationRow);
            this.m_farmsBody.Add(row);
        }

        private UiComponent renderProductTile(ProductProto product, float yieldAmount, float surplusAmount)
        {
            Column column = new Column(1.pt());
            column.Add(new Icon().Value(product, false).Size(ProductIconSize));

            string yieldStr = yieldAmount.ToStringRoundedAdaptive(2);
            string surplusStr = surplusAmount.ToStringRoundedAdaptive(2);
            if (surplusAmount < 0) surplusStr = $"<color=#ff6666>{surplusStr}</color>";
            else surplusStr = "+" + surplusStr;

            column.Add(new Label(surplusStr.AsLoc()).FontBold().TextCenterMiddle());
            column.Add(new Label(yieldStr.AsLoc()).TextCenterMiddle());

            return column.AlignItemsCenterMiddle().MarginRight(2.pt()).MarginBottom(1.pt()).Tooltip(product.Strings.Name, true, false, false);
        }
    }
}