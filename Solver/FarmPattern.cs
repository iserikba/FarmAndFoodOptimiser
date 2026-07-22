using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace Iserik.FaFOptimiser.Solver
{
    public class CropRecipe
    {
        public string Name { get; set; } = string.Empty;
        public int Months { get; set; }
        public double Water { get; set; }
        public double Fertility { get; set; }
        public double Production { get; set; }
        public int FarmTier { get; set; }
        public double RotationProduction { get; set; } // Rotation related avarage monthly production

        public CropRecipe Clone()
        {
            return new CropRecipe
            {
                Name = this.Name,
                Months = this.Months,
                Water = this.Water,
                Fertility = this.Fertility,
                Production = this.Production,
                FarmTier = this.FarmTier,
                RotationProduction = this.RotationProduction
            };
        }
    }

    public class FarmPattern
    {
        public string Name { get; set; }
        public int TierIdx { get; set; }
        public double WaterCost { get; set; }
        public double FertNeed { get; set; }
        public double TargetFert { get; set; }
        public double[] Yields { get; set; }
        public double Weight { get; set; } // Used for sorting
        public List<CropRecipe> Recipes { get; private set; }

        public FarmPattern(string name, int tier, int numCrops, List<CropRecipe> recipes)
        {
            Name = name;
            TierIdx = tier;
            Yields = new double[numCrops];
            Recipes = recipes.Select(r => r.Clone()).ToList();
        }
    }

    public class PatternGenerator
    {
        public static List<FarmPattern> Generate(int maxSeasons, int[] maxFarms, List<CropRecipe> availableRecipes, string[] requestCrops, double targetFertility)
        {
            List<FarmPattern> patterns = new List<FarmPattern>();

            for (int tier = 1; tier <= 4; tier++)
            {
                if (maxFarms[tier] <= 0) continue;
                int Pattern4TypeCount = 1;

                var validCrops = availableRecipes.Where(r => r.FarmTier == tier).ToList();

                for (int size = 2; size <= maxSeasons; size++)
                {
                    var combinations = GetCombinations(validCrops, size);

                    foreach (var combo in combinations)
                    {
                        string patternName = string.Join(",", combo.Select(c => c.Name.Replace("Product_", "").Replace("Crop_", "")));
                        FarmPattern p = new FarmPattern($"P{tier}-{Pattern4TypeCount++}({patternName})", tier, requestCrops.Length, combo);

                        double totalMonths = combo.Sum(c => c.Months);
                        double totalProd = combo.Sum(c => c.Production);
                        double FertCost = combo.Sum(c => c.Fertility) / totalMonths;

                        double FertEquilibrium = 100.0 - (FertCost / 0.3);
                        double fertilityMultiplier = (tier == 1 ? FertEquilibrium : System.Math.Max(FertEquilibrium, targetFertility)) / 100.0;

                        p.WaterCost =  combo.Sum(c => c.Water) / totalMonths;
                        p.Weight = fertilityMultiplier * totalProd / totalMonths;

                        if (FertEquilibrium >= targetFertility || tier == 1)
                            p.FertNeed = 0;
                        else
                            p.FertNeed = targetFertility < 100 ?
                                        FertCost - 0.3 * (100 - targetFertility) :
                                        FertCost + 0.02 * (targetFertility - 100) * (FertCost + 3);

                        for (int i = 0; i < requestCrops.Length; i++)
                        {
                            var cropInCombo = p.Recipes.FirstOrDefault(c => c.Name.Equals(requestCrops[i], StringComparison.OrdinalIgnoreCase));
                            if (cropInCombo != null)
                            {
                                cropInCombo.RotationProduction =
                                p.Yields[i] = fertilityMultiplier * cropInCombo.Production / totalMonths;
                            }
                        }

                        patterns.Add(p);
                    }
                }
            }

            return patterns.OrderByDescending(p => -p.Weight).ToList();
        }

        // Recursive method strictly for UNIQUE combinations
        private static IEnumerable<List<CropRecipe>> GetCombinations(List<CropRecipe> list, int length)
        {
            if (length == 1) return list.Select(t => new List<CropRecipe> { t });

            return GetCombinations(list, length - 1)
                .SelectMany(t => list.Where(e => list.IndexOf(e) > list.IndexOf(t.Last())),
                    (t1, t2) => t1.Concat(new List<CropRecipe> { t2 }).ToList());
        }

        public static double FarmsNeeded(List<CropRecipe> availableRecipes, string[] requestCrops, double[] targets, int tierFarm, double targetFertility, double cropMultiplierGlobal = 1.0)
        {
            double farmsNum = 0;

            // Use a standard for-loop to guarantee target index alignment
            for (int i = 0; i < requestCrops.Length; i++)
            {
                if (targets[i] <= 0) continue; // Skip if we don't need this crop

                string sName = requestCrops[i];

                // Find the matching recipe specifically generated for THIS tier
                var r = availableRecipes.FirstOrDefault(cr =>
                    cr.FarmTier == tierFarm &&
                    cr.Name.Equals(sName, StringComparison.OrdinalIgnoreCase));

                if (r != null)
                {
                    // Calculate monthly fertility cost 
                    // (Fertilizer tier multipliers are already baked into r.Fertility)
                    double monthlyFertCost = r.Fertility / r.Months;

                    // Apply Equilibrium Fertility formula
                    double FertEquilibrium = 100.0 - (monthlyFertCost / 0.3);

                    // Tier 1 farms cannot accept fertilizer, so they are locked to natural equilibrium
                    double fertilityMultiplier = (tierFarm == 1 ? FertEquilibrium : System.Math.Max(FertEquilibrium, targetFertility)) / 100.0;

                    // Calculate maximum theoretical monthly yield for this crop
                    // (Farm tier multiplier is already baked into r.Production)
                    double maxMonthlyProd = cropMultiplierGlobal * (r.Production / r.Months) * fertilityMultiplier;

                    // Add the fractional farm requirement to the total
                    farmsNum += (targets[i] / maxMonthlyProd);
                }
            }

            return farmsNum;
        }
    }
}