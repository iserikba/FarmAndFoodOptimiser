using System;
using System.Collections.Generic;
using System.Text;

namespace Iserik.FaFOptimiser.Solver
{
    public class OptimizationRequest
    {
        public List<CropDemand> Demands { get; set; } = new List<CropDemand>();
        public int[] MaxFarms { get; set; } = new int[5]; // Indices 1-4 for Tiers

        // Virtual Products Request 
        public double virtAnimalFeed { get; set; } = 0.0;
        public double virtSnacks { get; set; } = 0.0;

        // Max crops number in farm patterns <=4 adn >=2
        public int MaxRotations { get; set; } = 3;
        public double TargetFertility { get; set; } = 140.0;

        // --- NEW: Built-in Deep Clone Method ---
        public OptimizationRequest Clone()
        {
            var clone = new OptimizationRequest
            {
                MaxRotations = this.MaxRotations,
                TargetFertility = this.TargetFertility,
                virtAnimalFeed = this.virtAnimalFeed,
                virtSnacks = this.virtSnacks,
                MaxFarms = (int[])this.MaxFarms.Clone() // Clone the array to avoid reference sharing
            };

            // Deep copy the Demands list
            foreach (var d in this.Demands)
            {
                clone.Demands.Add(new CropDemand
                {
                    Name = d.Name,
                    Target = d.Target,
                    IsPriority = d.IsPriority,
                    IsDependingCrop = d.IsDependingCrop,
                    OriginalIndex = d.OriginalIndex
                });
            }

            return clone;
        }
    }
}
