using Iserik.FaFOptimiser.Catalog;
using Iserik.FaFOptimiser.Persistence;
using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Solver;
using Iserik.FaFOptimiser.UI;
using Iserik.FaFOptimiser.Translations; 
using Mafi;
using Mafi.Base;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Localization;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui;
using Mafi.Unity.Ui.Inspectors;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Library.FloatingPanel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.UI
{
    public class OptimizerInputPanel
    {
        private readonly OptimiserLog m_logger;
        private readonly CommandProcessor m_commandProcessor;
        private readonly ProtosDb m_protosDb;
        private readonly DemandStateManager m_demandManager;
        private readonly SettlementTelemetryService m_telemetryService;
        private readonly ProductionChainService m_chainService;


        private Column m_foodDemandsBody;
        private Column m_cropDemandsBody;
        private ProductProto m_pickerActiveProduct = null;

        private readonly Window m_mainWindow; // The parent window
        private readonly UiContext m_context;

        private readonly FarmTelemetryService m_farmService;

        private double m_targetFertility = 140.0; // Default

        private ButtonText m_solveBtn;
        private bool m_isSolving = false;

        private Dictionary<Proto.ID, int> m_farmCounts = new Dictionary<Proto.ID, int>();

        public OptimizerInputPanel(
                    Window mainWindow,
                    OptimiserLog logger,
                    CommandProcessor commandProcessor,
                    ProtosDb protosDb,
                    DemandStateManager demandManager,
                    SettlementTelemetryService telemetryService,
                    ProductionChainService chainService,
                    FarmTelemetryService farmService,
                    UiContext context)
        {
            this.m_mainWindow = mainWindow;
            this.m_logger = logger;
            this.m_commandProcessor = commandProcessor;
            this.m_protosDb = protosDb;
            this.m_demandManager = demandManager;
            this.m_telemetryService = telemetryService;
            this.m_chainService = chainService;

            this.m_demandManager.OnDemandsUpdated += this.RenderDemandsUi;
            this.m_farmService = farmService;
            this.m_context = context;
        }

        public Column BuildPanel()
        {
            Column panel = new Column(4.pt()).Width(400.px()).FlexShrink(0f).AlignItemsStretch();

            // REPLACED STRING
            PanelWithHeader settingsPanel = new PanelWithHeader(Strings.OptimizerSettings);
            ScrollColumn settingsScroll = new ScrollColumn().AlignItemsStretch().FlexGrow(1f);
            Column settingsBody = new Column(2.pt()).Padding(2.pt());

            // --- FETCH LIVE FARM DATA ---
            var allFarms = this.m_farmService.GetAllBuiltFarms();

            // --- BUILD FARMS UI (Single Column, Tight Spacing) ---
            // REPLACED STRING
            settingsBody.Add(new Label(Strings.MaxAvailableFarms).TextLeftBottom().MarginBottom(2.pt()));

            var unlockedDb = this.m_context.UnlockedProtosDbForUi;
            List<Proto.ID> farmTiers = new List<Proto.ID> {
                Ids.Buildings.FarmT1, Ids.Buildings.FarmT2,
                Ids.Buildings.FarmT3, Ids.Buildings.FarmT4
            };

            foreach (var farmId in farmTiers)
            {
                if (this.m_protosDb.TryGetProto(farmId, out FarmProto farmProto) && unlockedDb.IsUnlocked(farmProto))
                {
                    int count = allFarms.Count(f => f.Prototype.Id == farmId);

                    if (!m_farmCounts.ContainsKey(farmId)) m_farmCounts[farmId] = count;

                    settingsBody.Add(createFarmStepper(farmId, m_farmCounts[farmId], val => m_farmCounts[farmId] = val));
                }
            }

            // --- BUILD TARGET FERTILITY STEPPER ---
            Row fertilityRow = new Row(10.pt()).AlignItemsCenterMiddle().MarginTop(10.pt());

            // REPLACED STRING
            fertilityRow.Add(new Label(Strings.TargetFertility)
                .Width(120.px())
                .TextLeftMiddle());

            int[] fertOptions = { 0, 80, 90, 100, 110, 120, 130, 140 };
            int[] currentFertIndex = new int[] { 7 };

            Row fertStepper = new Row(2.pt()).AlignItemsCenterMiddle();
            Label fertLabel = new Label("140".AsLoc()).Width(60.px());

            fertStepper.Add(new ButtonText(Button.General, "-".AsLoc(), () => {
                if (currentFertIndex[0] > 0) currentFertIndex[0]--;
                string text = fertOptions[currentFertIndex[0]] == 0 ? "0" : fertOptions[currentFertIndex[0]].ToString();
                ((IComponentWithText)fertLabel).SetValue(text.AsLoc());
                this.m_targetFertility = fertOptions[currentFertIndex[0]];
            }).Width(24.px()).Height(24.px()));

            fertStepper.Add(fertLabel);

            fertStepper.Add(new ButtonText(Button.General, "+".AsLoc(), () => {
                if (currentFertIndex[0] < fertOptions.Length - 1) currentFertIndex[0]++;
                string text = fertOptions[currentFertIndex[0]] == 0 ? "0" : fertOptions[currentFertIndex[0]].ToString();
                ((IComponentWithText)fertLabel).SetValue(text.AsLoc());
                this.m_targetFertility = fertOptions[currentFertIndex[0]];
            }).Width(24.px()).Height(24.px()));

            fertilityRow.Add(fertStepper);
            settingsBody.Add(fertilityRow);

            // --- DIVIDER ---
            settingsBody.Add(new Column().Height(1.px()).Background(new ColorRgba(255, 255, 255, 30)).Margin(10.pt(), 0));
            // 1. Declare the button first so it can reference itself
            ButtonText overrideModeBtn = null;

            // 2. Build the button
            // Build the native Mafi Toggle
            // Pass the initial boolean state directly into the constructor
            Toggle manualOverrideToggle = new Toggle(true) // 'true' forces the standard "[ ] Label" visual layout
                .Value(this.m_demandManager.IsManualOverrideEnabled) // <--- THIS sets the actual checkmark state!
                                                                     // REPLACED STRINGS
                .Label(Strings.ManualOverride)
                .Tooltip(Strings.ManualOverrideTooltip, true, false, false)
                .OnValueChanged(state =>
                {
                    // Update the backend manager
                    this.m_demandManager.IsManualOverrideEnabled = state;

                    if (!state)
                    {
                        // this.m_demandManager.RecalculateAll();
                        // resultPanel.RenderVisualResults();
                    }
                })
                .AlignSelfStart<Toggle>()
                .MarginBottom(10.px());

            settingsBody.Add(manualOverrideToggle);

            // Add overrideModeBtn to your layout row!
            settingsBody.Add(overrideModeBtn);
            // --- MANUFACTURED FOOD DEMANDS ---
            // 2. Header placed directly above the list
            // REPLACED STRING
            settingsBody.Add(new Label(Strings.FoodDemands).TextLeftMiddle().FontBold().MarginBottom(2.pt()));

            // 1. Button moved to the top
            // REPLACED STRING
            ButtonText fetchPopBtn = new ButtonText(Button.General, Strings.AutoFillDemands, this.onFetchPopulationDemandClicked);
            settingsBody.Add(fetchPopBtn.Height(30.px()).MarginBottom(2.pt()));

            // 3. Dynamic list container
            this.m_foodDemandsBody = new Column(0.pt());
            settingsBody.Add(this.m_foodDemandsBody);

            // 4. Product Picker snaps tightly to the bottom of the list
            SingleProductPickerUi productPicker = new SingleProductPickerUi(
                this.getAvailableManufacturedProductsForPicker,
                this.onProductSelectedFromPicker,
                () => this.m_demandManager.PendingNewProduct,
                this.onProductSelectionCleared,
                null, true, true
            );
            settingsBody.Add(productPicker.MarginTop(1.pt()).MarginBottom(10.pt()));

            // --- CROP DEMANDS ---
            // REPLACED STRING
            settingsBody.Add(new Label(Strings.CropDemands).TextLeftMiddle().MarginTop(5.pt()).FontBold().MarginBottom(2.pt()));

            this.m_cropDemandsBody = new Column(0.pt());
            settingsBody.Add(this.m_cropDemandsBody);

            SingleProductPickerUi cropPicker = new SingleProductPickerUi(
                this.getAvailableCropsForPicker,
                this.onCropSelectedFromPicker,
                () => this.m_demandManager.PendingNewCrop,
                this.onCropSelectionCleared,
                null, true, true
            );
            settingsBody.Add(cropPicker.MarginTop(1.pt()));

            // --- SOLVE BUTTON ---
            // REPLACED STRING
            this.m_solveBtn = new ButtonText(Button.General, Strings.SolveAndOptimize, this.onSolveClicked);
            settingsBody.Add(this.m_solveBtn.Height(40.px()).MarginTop(15.pt()));

            settingsScroll.Add(settingsBody);
            settingsPanel.Body.Add(settingsScroll);
            panel.Add(settingsPanel.FlexGrow(1f));

            return panel;
        }

        private Row createFarmStepper(Proto.ID farmId, int initialValue, Action<int> onUpdate)
        {
            // Removed MarginBottom entirely to pack the rows tighter
            Row group = new Row(10.pt()).AlignItemsCenterMiddle().MarginBottom(0.pt());

            if (this.m_protosDb.TryGetProto(farmId, out FarmProto farmProto))
            {
                // Shrunk icon slightly from 32px to 28px to reduce vertical stretch
                group.Add(new Icon(farmProto).Size(28.px()).Tooltip(farmProto.Strings.Name));
            }

            Row stepperRow = new Row(2.pt()).AlignItemsCenterMiddle();
            int[] val = new int[] { initialValue };

            string initText = val[0] == -1 ? "?" : val[0].ToString();
            Label valueLabel = new Label(initText.AsLoc()).Width(40.px());

            stepperRow.Add(new ButtonText(Button.General, "-".AsLoc(), () => {
                if (val[0] > 0) val[0]--;

                string text = val[0] == -1 ? "?" : val[0].ToString();
                ((IComponentWithText)valueLabel).SetValue(text.AsLoc());
                onUpdate(val[0]);
            }).Width(24.px()).Height(24.px()));

            stepperRow.Add(valueLabel);

            stepperRow.Add(new ButtonText(Button.General, "+".AsLoc(), () => {
                if (val[0] == -1) val[0] = 0;
                else val[0]++;

                ((IComponentWithText)valueLabel).SetValue(val[0].ToString().AsLoc());
                onUpdate(val[0]);
            }).Width(24.px()).Height(24.px()));

            group.Add(stepperRow);
            return group;
        }

        private IEnumerable<ProductProto> getAvailableManufacturedProductsForPicker()
        {
            var unlockedDb = this.m_context.UnlockedProtosDbForUi;
            // 1. Ask the Catalog for the raw whitelist of what is mathematically allowed
            IEnumerable<ProductProto> allowedBomProducts = m_demandManager.m_catalog.GetAllowedManufacturedProducts(this.m_protosDb);

            List<ProductProto> filteredForUi = new List<ProductProto>();

            foreach (ProductProto product in allowedBomProducts)
            {
                // 2. Check if the player has researched it
                if (!unlockedDb.IsUnlocked(product)) continue;

                // 3. Hide if it is ALREADY in the active UI list
                if (!this.m_demandManager.ManufacturedDemands.ContainsKey(product) && !filteredForUi.Contains(product))
                {
                    filteredForUi.Add(product);
                }
            }

            return filteredForUi;
        }

        private void RenderDemandsUi()
        {
            this.m_foodDemandsBody.Clear();
            this.m_cropDemandsBody.Clear();

            // 1. Group the manufactured demands by their Selected Chain
            var groupedDemands = this.m_demandManager.ManufacturedDemands
                .GroupBy(d => this.m_demandManager.SelectedChains.ContainsKey(d.Key)
                              ? this.m_demandManager.SelectedChains[d.Key]
                              : null);

            // 2. Render each group as a single row
            foreach (var group in groupedDemands)
            {
                // Pass the grouped list to the new function we created earlier
                this.m_foodDemandsBody.Add(CreateGroupedFoodDemandRow(group.ToList()));
            }

            // 3. Render the raw crop demands
            foreach (var kvp in this.m_demandManager.AggregateCropDemands)
            {
                this.m_cropDemandsBody.Add(CreateCropDemandRow(kvp.Key, kvp.Value));
            }
        }


        private UiComponent CreateGroupedFoodDemandRow(List<KeyValuePair<ProductProto, Fix32>> groupedProducts)
        {
            Row container = new Row(4.pt()).AlignItemsCenterMiddle().Height(30.px());

            foreach (var kvp in groupedProducts)
            {
                container.Add(new Icon(kvp.Key, false, false).Size(24.px()).Tooltip(kvp.Key.Strings.Name));

                TextField amountInput = new TextField().Text(kvp.Value.ToStringRounded(1).AsLoc()).Width(50.px());
                ProductProto localProduct = kvp.Key;
                amountInput.OnEditEnd(val =>
                {
                    if (double.TryParse(val, out double num))
                        this.m_demandManager.SetDemandAmount(localProduct, Fix32.FromFloat((float)num));
                });
                container.Add(amountInput);
            }

            var representativeProduct = groupedProducts.First().Key;
            if (this.m_demandManager.SelectedChains.TryGetValue(representativeProduct, out var chain))
            {
                // Check if this specific row was clicked to open the picker
                bool isPickerActive = (this.m_pickerActiveProduct == representativeProduct);

                ButtonIcon chainBtn = new ButtonIcon(Button.General, "Assets/FaFoptimiser/cog1.png", () => {
                    if (this.m_demandManager.ManufacturedDemands.TryGetValue(representativeProduct, out Fix32 amount))
                    {
                        var allOptions = this.m_chainService.GetAlternativeChains(representativeProduct, amount);
                        var pickerWindow = new ChainSelectionWindow(this.m_demandManager, representativeProduct, allOptions);

                        // Use the actual Window as the host!
                        pickerWindow.OpenIn(this.m_mainWindow);
                    }
                })
                .Width(30.px())
                .Height(30.px())
                .Padding(5.px());

                // ON HOVER: Keep the Floater strictly for the quick info flowchart
                chainBtn.Floater(() => {
                    Column tooltipContainer = new Column(0.pt())
                        .Background(new ColorRgba(30, 30, 30, 250))
                        .Padding(8.pt());

                    // REPLACED STRING
                    tooltipContainer.Add(new Label(Strings.ChainScore.Format(chain.ResourceScore.ToString("F1")))
                        .FontBold().TextLeftMiddle().MarginBottom(5.pt()));

                    var chainInfoBuilder = new Iserik.FaFOptimiser.Ui.ChainInfoPanel();
                    tooltipContainer.Add(chainInfoBuilder.BuildPanel(chain));

                    return tooltipContainer;
                }, null, false);

                container.Add(chainBtn);

                var requiredCrops = chain.GetRequiredCrops();
                foreach (var cropKvp in requiredCrops)
                {
                    container.Add(new DisplayWithIcon()
                        .IconValue(cropKvp.Key)
                        .Value(cropKvp.Value.ToStringRounded(1).AsLoc())
                        .SuperCompact());
                }
            }

            return container;
        }


        private UiComponent CreateCropDemandRow(ProductProto product, Fix32 aggregateAmount)
        {
            Row row = new Row(4.pt()).AlignItemsCenterMiddle().Height(30.px());
            row.Add(new Icon(product, false, false).Size(24.px()).Tooltip(product.Strings.Name));

            Fix32 directAmount = this.m_demandManager.DirectCropDemands.ContainsKey(product) ? this.m_demandManager.DirectCropDemands[product] : Fix32.Zero;

            TextField amountInput = new TextField().Text(directAmount.ToStringRounded(1).AsLoc()).Width(50.px());
            amountInput.OnEditEnd(val =>
            {
                if (double.TryParse(val, out double num)) this.m_demandManager.SetDemandAmount(product, Fix32.FromFloat((float)num));
            });
            row.Add(amountInput);

            // REPLACED STRING
            row.Add(new Label(Strings.TotalAmount.Format(aggregateAmount.ToStringRounded(1))).TextLeftMiddle().Opacity(0.7f));
            return row;
        }

        private IEnumerable<ProductProto> getAvailableCropsForPicker()
        {
            var unlockedDb = this.m_context.UnlockedProtosDbForUi;
            List<ProductProto> availableCrops = new List<ProductProto>();

            foreach (CropProto crop in this.m_protosDb.All<CropProto>())
            {
                if (crop.ProductProduced.IsEmpty) continue;

                ProductProto product = crop.ProductProduced.Product;

                // 1. Check if the player has researched it
                if (!unlockedDb.IsUnlocked(product)) continue;

                // 2. Hide if it is ALREADY anywhere in the UI (Aggregate covers direct + chain demands)
                if (!this.m_demandManager.AggregateCropDemands.ContainsKey(product) && !availableCrops.Contains(product))
                {
                    availableCrops.Add(product);
                }
            }

            return availableCrops;
        }

        private void onProductSelectedFromPicker(ProductProto product)
        {
            this.m_demandManager.PendingNewProduct = product;
            this.m_demandManager.SetDemandAmount(product, Fix32.One);
            this.m_demandManager.PendingNewProduct = null;
        }

        private void onProductSelectionCleared()
        {
            this.m_demandManager.PendingNewProduct = null;
        }

        private void onCropSelectedFromPicker(ProductProto product)
        {
            this.m_demandManager.PendingNewCrop = product;
            this.m_demandManager.SetDemandAmount(product, Fix32.One);
            this.m_demandManager.PendingNewCrop = null;
        }

        private void onCropSelectionCleared()
        {
            this.m_demandManager.PendingNewCrop = null;
        }

        private void onSolveClicked()
        {
            // 1. Guard check: Do nothing if already running
            if (this.m_isSolving) return;

            // 2. Lock the UI and change the button text
            this.m_isSolving = true;

            // REPLACED STRING
            ((IComponentWithText)this.m_solveBtn).SetValue(Strings.Solving);

            this.m_logger.AddMessage("\n[UI] Compiling Request from State Manager...");

            // Helper to extract the count safely
            int GetCount(Proto.ID id)
            {
                if (m_farmCounts.TryGetValue(id, out int val))
                    return val == -1 ? 999 : val;
                return 0;
            }

            // 3. Determine flexibleTier (Highest tier selected as '?')
            int flexibleTier = -1;
            if (m_farmCounts.TryGetValue(Ids.Buildings.FarmT4, out int t4Val) && t4Val == -1) flexibleTier = 4;
            else if (m_farmCounts.TryGetValue(Ids.Buildings.FarmT3, out int t3Val) && t3Val == -1) flexibleTier = 3;
            else if (m_farmCounts.TryGetValue(Ids.Buildings.FarmT2, out int t2Val) && t2Val == -1) flexibleTier = 2;
            else if (m_farmCounts.TryGetValue(Ids.Buildings.FarmT1, out int t1Val) && t1Val == -1) flexibleTier = 1;

            int t1 = GetCount(Ids.Buildings.FarmT1);
            int t2 = GetCount(Ids.Buildings.FarmT2);
            int t3 = GetCount(Ids.Buildings.FarmT3);
            int t4 = GetCount(Ids.Buildings.FarmT4);

            double targetFertility = this.m_targetFertility;

            this.m_logger.AddMessage($"[DEBUG] Solver Request: T1:{t1}, T2:{t2}, T3:{t3}, T4:{t4}, Fertility:{targetFertility}, FlexTier:{flexibleTier}");

            List<CropDemand> dynamicDemands = new List<CropDemand>();
            foreach (var kvp in this.m_demandManager.AggregateCropDemands)
            {
                if (kvp.Value > Fix32.Zero)
                {
                    dynamicDemands.Add(new CropDemand
                    {
                        Name = kvp.Key.Id.Value,
                        Target = kvp.Value.ToFloat()
                    });
                }
            }

            OptimizationRequest request = new OptimizationRequest
            {
                MaxFarms = new int[] { 0, t1, t2, t3, t4 },
                MaxRotations = 3,
                TargetFertility = targetFertility,
                Demands = dynamicDemands
            };

            // 4. Run the async job, passing the flexibleTier and a callback to restore the button
            this.m_commandProcessor.RunOptimizationAsync(request, flexibleTier, () => {
                // This fires when the background thread is completely done
                this.m_isSolving = false;

                // REPLACED STRING
                ((IComponentWithText)this.m_solveBtn).SetValue(Strings.SolveAndOptimize);
            });
        }

        private void onFetchPopulationDemandClicked()
        {
            this.m_logger.AddMessage("Fetching population food demands from Settlement...");
            // THE MAGIC: This triggers the telemetry scan and auto-populates the UI!
            this.m_demandManager.LoadFromSettlement(this.m_telemetryService);
        }
    }
}