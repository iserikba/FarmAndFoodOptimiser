using Mafi.Localization;

namespace Iserik.FaFOptimiser.Translations
{
    /// <summary>
    /// Localization Dictionary.
    /// Centralized storage for all translatable strings in the mod.
    /// This allows for easy language support and keeps the UI code tidy.
    /// </summary>
    internal static class Strings
    {
        // --- Main Window ---
        public static readonly LocStr WindowTitle = Loc.Str("FaFOptimiser__WindowTitle", "Advanced Farm & Food Optimiser", "Mod main window title");
        public static readonly LocStr FarmsList = Loc.Str("FaFOptimiser__FarmsList", "Farms Occupation", "Farms list section title");
        public static readonly LocStr ProducedCrops = Loc.Str("FaFOptimiser__ProducedCrops", "Produced Crops", "Produced Crops list section title");
        public static readonly LocStr SaveCalculation = Loc.Str("FaFOptimiser__SaveCalculation", "Save", "button to save the current calculation preset");
        public static readonly LocStr LoadCalculations = Loc.Str("FaFOptimiser__LoadCalculations", "Load", "button to open saved calculation presets");

        // --- Optimizer Input Panel ---
        public static readonly LocStr OptimizerSettings = Loc.Str("FaFOptimiser__OptimizerSettings", "Optimizer Settings", "Settings panel header");
        public static readonly LocStr MaxAvailableFarms = Loc.Str("FaFOptimiser__MaxAvailableFarms", "Maximum Available Farms:", "Label for maximum farms input");
        public static readonly LocStr TargetFertility = Loc.Str("FaFOptimiser__TargetFertility", "Target Fertility (%):", "Label for target fertility input");
        public static readonly LocStr ManualOverride = Loc.Str("FaFOptimiser__ManualOverride", "Manual Override", "Label for manual override toggle");
        public static readonly LocStr ManualOverrideTooltip = Loc.Str("FaFOptimiser__ManualOverrideTooltip", "Detaches crop demands from population food demands.", "Tooltip for manual override");
        public static readonly LocStr FoodDemands = Loc.Str("FaFOptimiser__FoodDemands", "Food Demands (Monthly):", "Label for food demands list");
        public static readonly LocStr AutoFillDemands = Loc.Str("FaFOptimiser__AutoFillDemands", "Auto-Fill Demands", "Button to auto-fill demands from settlement");
        public static readonly LocStr CropDemands = Loc.Str("FaFOptimiser__CropDemands", "Crop Demands (Monthly):", "Label for crop demands list");
        public static readonly LocStr SolveAndOptimize = Loc.Str("FaFOptimiser__SolveAndOptimize", "SOLVE & OPTIMIZE", "Button to start optimization");
        public static readonly LocStr Solving = Loc.Str("FaFOptimiser__Solving", "SOLVING...", "Button text while solving");

        // --- Parameterized Strings (Optimizer Input) ---
        public static readonly LocStr1 ChainScore = Loc.Str1("FaFOptimiser__ChainScore", "Chain Score: {0}", "Display for the calculated chain score");
        public static readonly LocStr1 TotalAmount = Loc.Str1("FaFOptimiser__TotalAmount", "Total: {0}", "Display for total aggregate amount");

        // --- Result Panel ---
        public static readonly LocStr InputsOutputs = Loc.Str("FaFOptimiser__InputsOutputs", "Inputs/Outputs", "Header for inputs and outputs panel section");
        public static readonly LocStr FarmInputs = Loc.Str("FaFOptimiser__FarmInputs", "Farm Inputs:", "Header for farm inputs subsection");
        public static readonly LocStr ChainInputs = Loc.Str("FaFOptimiser__ChainInputs", "Chain Inputs:", "Header for chain inputs subsection");
        public static readonly LocStr Produced = Loc.Str("FaFOptimiser__Produced", "Produced:", "Header for produced goods subsection");
        public static readonly LocStr Byproducts = Loc.Str("FaFOptimiser__Byproducts", "Byproducts:", "Header for byproducts subsection");

        // --- Parameterized Strings (Result Panel Tooltips) ---
        public static readonly LocStr3 FarmTooltip = Loc.Str3("FaFOptimiser__FarmTooltip", "{0}\nTarget Fertility:{1}\nWater Needed:{2}\nFertility Needed:{3}", "Detailed tooltip for a farm row showing resource requirements");
        // --- Chain Info Panel ---
        public static readonly LocStr Inputs = Loc.Str("FaFOptimiser__Inputs", "Inputs: ", "Label for chain inputs section");
        public static readonly LocStr ByproductsLabel = Loc.Str("FaFOptimiser__ByproductsHeader", "Byproducts: ", "Label for chain byproducts section");
        // --- Chain Select Panel ---
        public static readonly LocStr1 SelectProductionChain = Loc.Str1("FaFOptimiser__SelectProductionChain", "Select Production Chain: {0}", "Window title for selecting a production chain");
    }
}