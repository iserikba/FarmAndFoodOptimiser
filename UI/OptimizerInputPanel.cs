using Iserik.FaFOptimiser.Catalog;
using Iserik.FaFOptimiser.Persistence;
using Iserik.FaFOptimiser.Services;
using Iserik.FaFOptimiser.Solver;
using Iserik.FaFOptimiser.Translations;
using Iserik.FaFOptimiser.UI;
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

        // Dynamic container for Farm/Livestock products
        private Column m_chickDemandsContainer;

        private ProductProto m_pickerActiveProduct = null;

        private readonly Window m_mainWindow;
        private readonly UiContext m_context;
        private readonly FarmTelemetryService m_farmService;

        private double m_targetFertility = 140.0;
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
            Column panel = new Column(4.pt()).Width(500.px()).FlexShrink(0f).AlignItemsStretch();

            PanelWithHeader settingsPanel = new PanelWithHeader(Strings.OptimizerSettings);
            ScrollColumn settingsScroll = new ScrollColumn().AlignItemsStretch().FlexGrow(1f);
            Column settingsBody = new Column(2.pt()).Padding(2.pt());

            var allFarms = this.m_farmService.GetAllBuiltFarms();

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

            Row fertilityRow = new Row(10.pt()).AlignItemsCenterMiddle().MarginTop(10.pt());
            fertilityRow.Add(new Label(Strings.TargetFertility).Width(120.px()).TextLeftMiddle());

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

            settingsBody.Add(new Column().Height(1.px()).Background(new ColorRgba(255, 255, 255, 30)).Margin(10.pt(), 0));
            ButtonText overrideModeBtn = null;

            Toggle manualOverrideToggle = new Toggle(true)
                .Value(this.m_demandManager.IsManualOverrideEnabled)
                .Label(Strings.ManualOverride)
                .Tooltip(Strings.ManualOverrideTooltip, true, false, false)
                .OnValueChanged(state =>
                {
                    this.m_demandManager.IsManualOverrideEnabled = state;
                })
                .AlignSelfStart<Toggle>()
                .MarginBottom(10.px());

            settingsBody.Add(manualOverrideToggle);
            settingsBody.Add(overrideModeBtn);

            // --- MANUFACTURED FOOD DEMANDS ---
            settingsBody.Add(new Label(Strings.FoodDemands).TextLeftMiddle().FontBold().MarginBottom(2.pt()));
            ButtonText fetchPopBtn = new ButtonText(Button.General, Strings.AutoFillDemands, this.onFetchPopulationDemandClicked);
            settingsBody.Add(fetchPopBtn.Height(30.px()).MarginBottom(2.pt()));

            this.m_foodDemandsBody = new Column(0.pt());
            settingsBody.Add(this.m_foodDemandsBody);

            SingleProductPickerUi productPicker = new SingleProductPickerUi(
                this.getAvailableManufacturedProductsForPicker,
                this.onProductSelectedFromPicker,
                () => this.m_demandManager.PendingNewProduct,
                this.onProductSelectionCleared,
                null, true, true
            );
            settingsBody.Add(productPicker.MarginTop(1.pt()).MarginBottom(10.pt()));

            // --- CHICKEN / FARM PRODUCTS DEMANDS CONTAINER ---
            this.m_chickDemandsContainer = new Column(0.pt());
            settingsBody.Add(this.m_chickDemandsContainer);

            // --- CROP DEMANDS ---
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

            // In BuildPanel():
            this.m_solveBtn = new ButtonText(Button.General, Strings.OptimizeFarms , this.onSolveClicked);
            settingsBody.Add(this.m_solveBtn.Height(40.px()).MarginTop(15.pt()));

            settingsScroll.Add(settingsBody);
            settingsPanel.Body.Add(settingsScroll);
            panel.Add(settingsPanel.FlexGrow(1f));

            return panel;
        }

        private void RenderDemandsUi()
        {
            this.m_foodDemandsBody.Clear();
            this.m_cropDemandsBody.Clear();
            this.m_chickDemandsContainer.Clear();

            var groupedDemands = this.m_demandManager.ManufacturedDemands
                .GroupBy(d => this.m_demandManager.SelectedChains.ContainsKey(d.Key)
                              ? this.m_demandManager.SelectedChains[d.Key]
                              : null);

            foreach (var group in groupedDemands)
            {
                this.m_foodDemandsBody.Add(CreateGroupedFoodDemandRow(group.ToList()));
            }

            // 1. Check if we have livestock items (excluding standalone Animal Feed)
            bool hasLivestockDemands = this.m_demandManager.AggregateChickFarmDemands.Keys.Any(k => k.Id != Ids.Products.AnimalFeed);
            bool hasFlock = (this.m_demandManager.ActualChickenCount > 0 || this.m_demandManager.MinChickenCount > 0 || hasLivestockDemands);

            // 2. NEW: Check if Animal Feed exists OR is currently arriving via either picker!
            bool isPendingFeed = (this.m_demandManager.PendingNewProduct != null && this.m_demandManager.PendingNewProduct.Id == Ids.Products.AnimalFeed) ||
                                 (this.m_demandManager.PendingNewCrop != null && this.m_demandManager.PendingNewCrop.Id == Ids.Products.AnimalFeed);

            bool hasAnimalFeed = isPendingFeed;
            if (this.m_protosDb.TryGetProto(Ids.Products.AnimalFeed, out ProductProto feedProto))
            {
                hasAnimalFeed |= this.m_demandManager.AggregateChickFarmDemands.ContainsKey(feedProto);
            }

            // Render Farm Products Demand section if either Chickens OR Animal Feed exist!
            if (hasFlock || hasAnimalFeed)
            {
                this.m_chickDemandsContainer.Add(new Label(Strings.FarmProductsDemand)
                    .TextLeftMiddle()
                    .FontBold()
                    .MarginTop(5.pt())
                    .MarginBottom(2.pt()));

                Column chickList = new Column(0.pt());

                // Render Eggs and Carcasses (if any exist)
                foreach (var kvp in this.m_demandManager.AggregateChickFarmDemands)
                {
                    if (kvp.Key.Id == Ids.Products.AnimalFeed) continue;
                    chickList.Add(CreateChickFarmDemandRow(kvp.Key, kvp.Value));
                }

                // ONLY render Flock Summary Row if livestock is actually required!
                if (hasFlock)
                {
                    chickList.Add(CreateFlockSummaryRow());
                }

                // Render Dedicated Animal Feed Row (safely passing Fix32.Zero if it just arrived)
                if (hasAnimalFeed && this.m_protosDb.TryGetProto(Ids.Products.AnimalFeed, out ProductProto feedProtoForRow))
                {
                    Fix32 feedQty = Fix32.Zero;
                    this.m_demandManager.AggregateChickFarmDemands.TryGetValue(feedProtoForRow, out feedQty);
                    chickList.Add(CreateAnimalFeedRow(feedProtoForRow, feedQty));
                }

                this.m_chickDemandsContainer.Add(chickList.MarginBottom(10.pt()));
            }

            foreach (var kvp in this.m_demandManager.AggregateCropDemands)
            {
                this.m_cropDemandsBody.Add(CreateCropDemandRow(kvp.Key, kvp.Value));
            }
        }

        /// <summary>
        /// UPDATED: Flock Summary Row is now strictly about Population Size! No Cog or Crops here.
        /// </summary>
        private UiComponent CreateFlockSummaryRow()
        {
            Row row = new Row(6.pt()).AlignItemsCenterMiddle().Height(34.px()).MarginTop(6.pt());

            int currentCount = this.m_demandManager.ActualChickenCount;
            int minCount = this.m_demandManager.MinChickenCount;

            Row stepperRow = new Row(4.pt()).AlignItemsCenterMiddle();

            stepperRow.Add(new ButtonText(Button.General, "-".AsLoc(), () => {
                if (currentCount - 50 >= minCount)
                {
                    this.m_demandManager.SetChickenCountOverride(currentCount - 50);
                }
            }).Width(24.px()).Height(24.px()).Tooltip(Strings.DecreaseFlockTooltip.Format(minCount.ToString())));

            DisplayWithIcon flockDisplay = new DisplayWithIcon()
                .Value(currentCount.ToString().AsLoc())
                .SuperCompact();

            if (this.m_protosDb.TryGetProto(Ids.Products.Chicken, out ProductProto chickenProto))
            {
                flockDisplay.IconValue(chickenProto);
            }

            stepperRow.Add(flockDisplay);

            stepperRow.Add(new ButtonText(Button.General, "+".AsLoc(), () => {
                this.m_demandManager.SetChickenCountOverride(currentCount + 50);
            }).Width(24.px()).Height(24.px()).Tooltip(Strings.IncreaseFlockTooltip));

            row.Add(stepperRow);
            row.Add(new Label(Strings.MinFlockLabel.Format(minCount.ToString())).TextLeftMiddle().Opacity(0.6f).Width(70.px()));

            return row;
        }

        /// <summary>
        /// NEW: Uncluttered Animal Feed row without [i] icons! Tooltips attached natively to elements.
        /// </summary>
        private UiComponent CreateAnimalFeedRow(ProductProto product, Fix32 aggregateAmount)
        {
            Row row = new Row(4.pt()).AlignItemsCenterMiddle().Height(30.px());

            // 1. Product Icon
            row.Add(new Icon(product, false, false).Size(26.px()).Tooltip(product.Strings.Name));

            // 2. Direct Demand Input Field (No [i] icon before it!)
            Fix32 directAmount = this.m_demandManager.DirectChickFarmDemands.ContainsKey(product)
                ? this.m_demandManager.DirectChickFarmDemands[product]
                : Fix32.Zero;

            TextField amountInput = new TextField()
                .Text(directAmount.ToStringRounded(1).AsLoc())
                .Width(50.px());
                //.Tooltip("Direct Animal Feed demand (e.g., for boilers/heating above flock requirements)".AsLoc());

            amountInput.OnEditEnd(val =>
            {
                if (double.TryParse(val, out double num))
                {
                    this.m_demandManager.SetDemandAmount(product, Fix32.FromFloat((float)num));
                }
            });
            row.Add(amountInput);

            // 3. Total Label (No [i] icon!)
            row.Add(new Label(Strings.TotalAmount.Format(aggregateAmount.ToStringRounded(1)))
                .TextLeftMiddle()
                .Width(90.px())
                .Opacity(0.9f)
                .Tooltip(Strings.AnimalFeedTotalTooltip));

            // 4. Chain Cog Button
            ButtonIcon chainBtn = new ButtonIcon(Button.General, "Assets/FaFoptimiser/cog1.png", () => {
                var allOptions = this.m_chainService.GetAlternativeChains(product, aggregateAmount);
                if (allOptions.Count > 0)
                {
                    var pickerWindow = new ChainSelectionWindow(this.m_demandManager, product, allOptions);
                    pickerWindow.OpenIn(this.m_mainWindow);
                }
            })
            .Width(28.px()).Height(28.px()).Padding(4.px());

            if (this.m_demandManager.SelectedFlockChain != null)
            {
                var chain = this.m_demandManager.SelectedFlockChain;
                chainBtn.Floater(() => {
                    Column tooltipContainer = new Column(0.pt())
                        .Background(new ColorRgba(30, 30, 30, 250))
                        .Padding(8.pt());

                    tooltipContainer.Add(new Label(Strings.ChainScore.Format(chain.ResourceScore.ToString("F1")))
                        .FontBold().TextLeftMiddle().MarginBottom(5.pt()));

                    var chainInfoBuilder = new Iserik.FaFOptimiser.Ui.ChainInfoPanel();
                    tooltipContainer.Add(chainInfoBuilder.BuildPanel(chain));

                    return tooltipContainer;
                }, null, false);
            }

            row.Add(chainBtn);

            // 5. Render Required Crops (Corn / Wheat / Potatoes) next to the cog!
            if (this.m_demandManager.SelectedFlockChain != null)
            {
                var requiredCrops = this.m_demandManager.SelectedFlockChain.GetRequiredCrops();
                foreach (var cropKvp in requiredCrops)
                {
                    row.Add(new DisplayWithIcon()
                        .IconValue(cropKvp.Key)
                        .Value(cropKvp.Value.ToStringRounded(1).AsLoc())
                        .SuperCompact());
                }
            }

            return row;
        }

        /// <summary>
        ///  Clean, static row for Eggs and Carcasses without scope toggles or individual chain cogs!
        /// </summary>
        private UiComponent CreateChickFarmDemandRow(ProductProto product, Fix32 aggregateAmount)
        {
            Row row = new Row(4.pt()).AlignItemsCenterMiddle().Height(30.px());

            // 1. Static Product Icon (No checkmark badge, no OnClick scope toggle)
            Icon productIcon = new Icon(product, false, false)
                .Size(26.px())
                .Tooltip(product.Strings.Name);
            row.Add(productIcon);

            // 2. Direct Demand Input Field
            Fix32 directAmount = this.m_demandManager.DirectChickFarmDemands.ContainsKey(product)
                ? this.m_demandManager.DirectChickFarmDemands[product]
                : Fix32.Zero;

            TextField amountInput = new TextField()
                .Text(directAmount.ToStringRounded(1).AsLoc())
                .Width(50.px());

            amountInput.OnEditEnd(val =>
            {
                if (double.TryParse(val, out double num))
                {
                    this.m_demandManager.SetDemandAmount(product, Fix32.FromFloat((float)num));
                }
            });
            row.Add(amountInput);

            // 3. Total Label
            row.Add(new Label(Strings.TotalAmount.Format(aggregateAmount.ToStringRounded(1)))
                .TextLeftMiddle()
                .Width(120.px())
                .Opacity(0.9f));

            return row;
        }

        private Row createFarmStepper(Proto.ID farmId, int initialValue, Action<int> onUpdate)
        {
            Row group = new Row(10.pt()).AlignItemsCenterMiddle().MarginBottom(0.pt());

            if (this.m_protosDb.TryGetProto(farmId, out FarmProto farmProto))
            {
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
            IEnumerable<ProductProto> allowedBomProducts = m_demandManager.m_catalog.GetAllowedManufacturedProducts(this.m_protosDb);
            List<ProductProto> filteredForUi = new List<ProductProto>();

            foreach (ProductProto product in allowedBomProducts)
            {
                if (!unlockedDb.IsUnlocked(product)) continue;
                if (!this.m_demandManager.ManufacturedDemands.ContainsKey(product) && !filteredForUi.Contains(product))
                {
                    filteredForUi.Add(product);
                }
            }

            // NEW: Explicitly allow picking Animal Feed from the top food dropdown too!
            if (this.m_protosDb.TryGetProto(Ids.Products.AnimalFeed, out ProductProto af) && unlockedDb.IsUnlocked(af))
            {
                if (!this.m_demandManager.AggregateChickFarmDemands.ContainsKey(af) && !filteredForUi.Contains(af))
                    filteredForUi.Add(af);
            }

            return filteredForUi;
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
                bool isPickerActive = (this.m_pickerActiveProduct == representativeProduct);

                ButtonIcon chainBtn = new ButtonIcon(Button.General, "Assets/FaFoptimiser/cog1.png", () => {
                    if (this.m_demandManager.ManufacturedDemands.TryGetValue(representativeProduct, out Fix32 amount))
                    {
                        var allOptions = this.m_chainService.GetAlternativeChains(representativeProduct, amount);
                        var pickerWindow = new ChainSelectionWindow(this.m_demandManager, representativeProduct, allOptions);
                        pickerWindow.OpenIn(this.m_mainWindow);
                    }
                })
                .Width(30.px()).Height(30.px()).Padding(5.px());

                chainBtn.Floater(() => {
                    Column tooltipContainer = new Column(0.pt())
                        .Background(new ColorRgba(30, 30, 30, 250))
                        .Padding(8.pt());

                    tooltipContainer.Add(new Label(Strings.ChainScore.Format(chain.ResourceScore.ToString("F1")))
                        .FontBold().TextLeftMiddle().MarginBottom(5.pt()));

                    var chainInfoBuilder = new Iserik.FaFOptimiser.Ui.ChainInfoPanel();
                    tooltipContainer.Add(chainInfoBuilder.BuildPanel(chain));

                    return tooltipContainer;
                }, null, false);

                container.Add(chainBtn);

                // 1. Display Required Raw Crops
                var requiredCrops = chain.GetRequiredCrops();
                foreach (var cropKvp in requiredCrops)
                {
                    container.Add(new DisplayWithIcon()
                        .IconValue(cropKvp.Key)
                        .Value(cropKvp.Value.ToStringRounded(1).AsLoc())
                        .SuperCompact());
                }

                // 2. Display Required Farm/Livestock Products (Eggs, CC) next to the gear icon
                var requiredChickProducts = chain.GetRequiredChickFarmProducts();
                foreach (var chickKvp in requiredChickProducts)
                {
                    container.Add(new DisplayWithIcon()
                        .IconValue(chickKvp.Key)
                        .Value(chickKvp.Value.ToStringRounded(1).AsLoc())
                        .SuperCompact());
                }
            }

            return container;
        }

        private UiComponent CreateCropDemandRow(ProductProto product, Fix32 aggregateAmount)
        {
            Row row = new Row(4.pt()).AlignItemsCenterMiddle().Height(30.px());
            bool isInScope = this.m_demandManager.IsCropInScope(product);

            Icon cropIcon = new Icon(product, false, false)
                .Size(26.px())
                .Tooltip(isInScope
                    ? Strings.CropInScopeTooltip.Format(product.Strings.Name)
                    : Strings.CropOutOfScopeTooltip.Format(product.Strings.Name));

            cropIcon.OnClick(() => {
                this.m_demandManager.ToggleCropScope(product);
            });

            if (isInScope)
            {
                Icon statusBadge = new Icon("Assets/FaFoptimiser/checkmark-16.png").Size(12.px());
                cropIcon.AddAndReturn<Icon>(
                    statusBadge.AbsolutePosition(new Px?(-3.px()), null, null, new Px?(-3.px()), false)
                );
            }
            else
            {
                cropIcon.Opacity(0.4f);
            }
            row.Add(cropIcon);

            if (isInScope)
            {
                Fix32 directAmount = this.m_demandManager.DirectCropDemands.ContainsKey(product)
                    ? this.m_demandManager.DirectCropDemands[product]
                    : Fix32.Zero;

                TextField amountInput = new TextField()
                    .Text(directAmount.ToStringRounded(1).AsLoc())
                    .Width(50.px());

                amountInput.OnEditEnd(val =>
                {
                    if (double.TryParse(val, out double num))
                    {
                        this.m_demandManager.SetDemandAmount(product, Fix32.FromFloat((float)num));
                    }
                });
                row.Add(amountInput);

                row.Add(new Label(Strings.TotalAmount.Format(aggregateAmount.ToStringRounded(1)))
                    .TextLeftMiddle()
                    .Opacity(0.9f));
            }
            else
            {
                Label ignoredLabel = new Label(Strings.CropOutOfScopeLabel.Format(aggregateAmount.ToStringRounded(1)))
                    .TextLeftMiddle()
                    .FontItalic()
                    .Opacity(0.4f);

                row.Add(ignoredLabel);
            }

            return row;
        }

        private IEnumerable<ProductProto> getAvailableCropsForPicker()
        {
            var unlockedDb = this.m_context.UnlockedProtosDbForUi;
            List<ProductProto> availableProducts = new List<ProductProto>();

            // 1. Add Crops
            foreach (CropProto crop in this.m_protosDb.All<CropProto>())
            {
                if (crop.ProductProduced.IsEmpty) continue;
                ProductProto product = crop.ProductProduced.Product;
                if (!unlockedDb.IsUnlocked(product)) continue;

                if (!this.m_demandManager.AggregateCropDemands.ContainsKey(product) && !availableProducts.Contains(product))
                {
                    availableProducts.Add(product);
                }
            }

            // 2. Add Chicken Farm Products (Eggs, Carcasses)
            if (this.m_protosDb.TryGetProto(Ids.Products.Eggs, out ProductProto eggs) && unlockedDb.IsUnlocked(eggs))
            {
                if (!this.m_demandManager.AggregateChickFarmDemands.ContainsKey(eggs) && !availableProducts.Contains(eggs))
                    availableProducts.Add(eggs);
            }
            if (this.m_protosDb.TryGetProto(Ids.Products.ChickenCarcass, out ProductProto cc) && unlockedDb.IsUnlocked(cc))
            {
                if (!this.m_demandManager.AggregateChickFarmDemands.ContainsKey(cc) && !availableProducts.Contains(cc))
                    availableProducts.Add(cc);
            }

            return availableProducts;
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
            if (this.m_isSolving) return;
            this.m_isSolving = true;

            ((IComponentWithText)this.m_solveBtn).SetValue(Strings.Solving);
            this.m_logger.AddMessage("\n[UI] Compiling Request from State Manager...");

            int GetCount(Proto.ID id)
            {
                if (m_farmCounts.TryGetValue(id, out int val))
                    return val == -1 ? 999 : val;
                return 0;
            }

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
                ProductProto cropProto = kvp.Key;
                Fix32 aggregateQty = kvp.Value;

                if (this.m_demandManager.IsCropInScope(cropProto))
                {
                    if (aggregateQty > Fix32.Zero)
                    {
                        dynamicDemands.Add(new CropDemand
                        {
                            Name = cropProto.Id.Value,
                            Target = aggregateQty.ToFloat(),
                            IsPriority = false
                        });
                    }
                }
                else
                {
                    this.m_logger.AddMessage($"[SOLVER SCOPE] Skipping {cropProto.Strings.Name} (Marked as external/ignored by user).");
                }
            }

            OptimizationRequest request = new OptimizationRequest
            {
                MaxFarms = new int[] { 0, t1, t2, t3, t4 },
                MaxRotations = 3,
                TargetFertility = targetFertility,
                Demands = dynamicDemands
            };

            this.m_commandProcessor.RunOptimizationAsync(request, flexibleTier, () => {
                this.m_isSolving = false;
                ((IComponentWithText)this.m_solveBtn).SetValue(Strings.OptimizeFarms);
            });
        }

        private void onFetchPopulationDemandClicked()
        {
            this.m_logger.AddMessage("Fetching population food demands from Settlement...");
            this.m_demandManager.LoadFromSettlement(this.m_telemetryService);
        }
    }
}