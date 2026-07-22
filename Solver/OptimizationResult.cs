using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Iserik.FaFOptimiser.Solver
{
    public class BlueprintEntry
    {
        public int Quantity { get; set; }
        public int Tier { get; set; }
        public string PatternName { get; set; } = string.Empty;
        public double WaterCost { get; set; }
        public double FertCost { get; set; }
        public List<CropRecipe> CropSequence { get; set; } = null!;
    }

    public class CropResult
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPriority { get; set; }
        public double Target { get; set; }
        public double FinalYield { get; set; }
        public double Deficit => Target - FinalYield; // Negative = Surplus
    }

    public class OptimizationResult
    {
        public List<BlueprintEntry> Blueprint { get; private set; } = new List<BlueprintEntry>();
        public List<CropResult> CropSummaries { get; private set; } = new List<CropResult>();

        public int TotalFarmsUsed { get; private set; }
        public double TotalWater { get; private set; }
        public double TotalFertilizer { get; private set; }

        public double TargetFertility { get; private set; }

        // Constructor handles all the final math automatically
        public OptimizationResult(OptimizationRequest request, int[] finalBlueprint, FarmPattern[] patterns, double TargetFertilitySet)
        {
            TargetFertility = TargetFertilitySet;
            // 1. Initialize Crop Summaries from the Original Request
            foreach (var demand in request.Demands)
            {
                CropSummaries.Add(new CropResult
                {
                    Name = demand.Name,
                    Target = demand.Target,
                    IsPriority = demand.IsPriority,
                    FinalYield = 0 // Will be summed below
                });
            }

            // --- ADD VIRTUAL PRODUCTS TO SUMMARY ---
            if (request.virtAnimalFeed > 0)
            {
                CropSummaries.Add(new CropResult { Name = "Animal Feed", Target = request.virtAnimalFeed, IsPriority = false, FinalYield = 0 });
            }
            if (request.virtSnacks > 0)
            {
                CropSummaries.Add(new CropResult { Name = "Snack", Target = request.virtSnacks, IsPriority = false, FinalYield = 0 });
            }

            // 2. Tally up the Blueprint, Raw Yields, and Costs
            for (int p = 0; p < patterns.Length; p++)
            {
                int qty = finalBlueprint[p];
                if (qty > 0)
                {
                    var pattern = patterns[p];

                    // Save Blueprint Entry
                    Blueprint.Add(new BlueprintEntry
                    {
                        Quantity = qty,
                        Tier = pattern.TierIdx,
                        PatternName = pattern.Name,
                        WaterCost = pattern.WaterCost * qty,
                        FertCost = pattern.FertNeed * qty,
                        CropSequence = pattern.Recipes
                    });

                    // Add to Global Totals
                    TotalFarmsUsed += qty;
                    TotalWater += pattern.WaterCost * qty;
                    TotalFertilizer += pattern.FertNeed * qty;

                    // Add to Individual Crop Yields (Only affects real crops)
                    for (int c = 0; c < request.Demands.Count; c++)
                    {
                        CropSummaries[c].FinalYield += pattern.Yields[c] * qty;
                    }
                }
            }

            // 3. --- POST-PROCESS VIRTUAL PRODUCTS (SURPLUS CONVERSION) ---
            var af = CropSummaries.FirstOrDefault(c => c.Name == "Animal Feed");
            var snack = CropSummaries.FirstOrDefault(c => c.Name == "Snack");

            if (af != null || snack != null)
            {
                var corn = CropSummaries.FirstOrDefault(c => c.Name.ToUpper() == "PRODUCT_CORN");
                var wheat = CropSummaries.FirstOrDefault(c => c.Name.ToUpper() == "PRODUCT_WHEAT");
                var potato = CropSummaries.FirstOrDefault(c => c.Name.ToUpper() == "PRODUCT_POTATO");

                // Calculate available surplus (negative deficit means surplus)
                double surplusCorn = corn != null && corn.Deficit < 0 ? System.Math.Abs(corn.Deficit) : 0;
                double surplusWheat = wheat != null && wheat.Deficit < 0 ? System.Math.Abs(wheat.Deficit) : 0;
                double surplusPotato = potato != null && potato.Deficit < 0 ? System.Math.Abs(potato.Deficit) : 0;

                // Process Wheat Surplus (typically for Animal Feed)
                if (af != null && surplusWheat > 0)
                {   
                    wheat!.FinalYield -= (surplusWheat - AddVirtProduct(af, surplusWheat, 96.0 / 60.0));
                }

                // Process Corn Surplus Allocation
                if (surplusCorn > 0)
                {
                    if (af != null && snack != null)
                    {
                        // 1. Give as much as possible to Animal Feed, keep the leftover
                        double leftoverCorn = AddVirtProduct(af, surplusCorn, 72.0 / 60.0);

                        // 2. Give the LEFTOVER to Snacks (Bug fixed here)
                        leftoverCorn = AddVirtProduct(snack, leftoverCorn, 1.0);

                        // 3. Deduct total consumed from the Corn yield (Bug fixed here: was surplusWheat)
                        corn!.FinalYield -= (surplusCorn - leftoverCorn);
                    }
                    else if (af != null)
                    {
                        corn!.FinalYield -= (surplusCorn - AddVirtProduct(af, surplusCorn, 72.0 / 60.0));
                    }
                    else if (snack != null)
                    {
                        // Bug fixed here: was assigning to snack.FinalYield instead of deducting from corn
                        corn!.FinalYield -= (surplusCorn - AddVirtProduct(snack, surplusCorn, 1.0));
                    }
                }

                // Process Potato Surplus (typically for Snacks)
                if (snack != null && surplusPotato > 0)
                {
                    potato!.FinalYield -= (surplusPotato - AddVirtProduct(snack, surplusPotato, 18.0 / 24.0));
                }
            }
        }

        public bool IsSuccessful()
        {
            // If ANY priority crop has a deficit greater than 0.1 (accounting for floating point math), the layout failed.
            foreach (var crop in CropSummaries)
            {
                // Only enforce strict limits on the crops the user actually marked as Priority
                if (crop.Deficit>0.1)
                {
                    return false;
                }
            }
            return true;
        }

        // add crop to virtual product and return rest amount
        private double AddVirtProduct(CropResult v, double dProd, double mult)
        {
            double max = dProd * mult;
            if (v.FinalYield + max > v.Target)
            {
                max -= (v.Target - v.FinalYield);
                v.FinalYield = v.Target;
                return max / mult; // Return the raw product leftover
            }
            else
            {
                v.FinalYield += max;
                return 0.0; // 0 raw product leftover
            }
        }

        // Print out Method
        public void PrintHere(Action<string> _logger)
        {
            _logger("\n=== DETAILED OPTIMIZED SOLUTION ===");
            _logger(" Qty  | Tier   | Pattern Name");
            _logger(new String('-', 60));

            foreach (var entry in Blueprint)
            {
                string sbuf = "";
                foreach(CropRecipe crop in entry.CropSequence)
                {
                    sbuf += $"\t| {crop.Name.Substring(8)}/{crop.RotationProduction:0.0}";
                }
                _logger($" {entry.Quantity}x  | FarmT{entry.Tier} | {entry.PatternName}" + sbuf);
            }

            _logger(new String('-', 60));
            _logger($"Total Farms Used: {TotalFarmsUsed}");

            _logger("\nFinal Deficit (Negative = Surplus):");
            foreach (var crop in CropSummaries)
            {
                string label = crop.Name + (crop.IsPriority ? "+" : "");
                _logger(String.Format("  {0,-15}: {1,8:F2}/{2,8:F2}", label.Substring(8), crop.FinalYield, crop.Deficit));
            }

            _logger($"\nTotal Water Used: {TotalWater:F3}");
            _logger($"Total Fertiliser: {TotalFertilizer:F3}");
        }

    }
}