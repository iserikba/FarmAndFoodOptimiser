using System;

namespace Iserik.FaFOptimiser.Solver
{
    public class FastChunkSolver
    {
        private FarmPattern[] Patterns = null!;
        private double[] Targets = null!;
        private int[] MaxFarmsPerTier = null!; // NEW: Tracks max hardware per tier
        private int TargetTotalFarms;          // NEW: The depth limit for the current iteration
        private int NumFillers;

        public int[] BestQty { get; private set; } = null!;
        public double BestCost { get; private set; }

        private int[] CurrentQty = null!;
        private double[] CurrentYields = null!;
        private int[] CurrentFarmsUsed = null!; // NEW: Tracks live hardware usage by tier

        // --- PENALTY WEIGHTS ---
        private const double PENALTY_FILLER_OVER = 10.0;
        private const double PENALTY_MAIN_UNDER = 1000000.0;
        private const double PENALTY_MAIN_OVER = 10000.0;

        // UPGRADED SIGNATURE
        public void Solve(FarmPattern[] patterns, double[] targets, int[] maxFarmsPerTier, int targetTotalFarms, int numFillers)
        {
            Patterns = patterns;
            Targets = targets;
            MaxFarmsPerTier = maxFarmsPerTier;
            TargetTotalFarms = targetTotalFarms;
            NumFillers = numFillers;

            int numPatterns = patterns.Length;
            int numCrops = targets.Length;

            BestQty = new int[numPatterns];
            CurrentQty = new int[numPatterns];
            CurrentYields = new double[numCrops];
            CurrentFarmsUsed = new int[5]; // Covers Tiers 1-4
            BestCost = double.MaxValue;

            Search(0, 0);
        }

        private void Search(int startPatternIdx, int farmsUsed)
        {
            EvaluateScore();

            if (farmsUsed == TargetTotalFarms) return;

            for (int i = startPatternIdx; i < Patterns.Length; i++)
            {
                int tier = Patterns[i].TierIdx;

                // STRICT HARDWARE ENFORCEMENT: Cannot use a farm if we ran out of that tier
                if (CurrentFarmsUsed[tier] >= MaxFarmsPerTier[tier]) continue;

                // 1. Apply Pattern
                CurrentFarmsUsed[tier]++;
                CurrentQty[i]++;
                for (int c = 0; c < Targets.Length; c++)
                {
                    CurrentYields[c] += Patterns[i].Yields[c];
                }

                // 2. Go Deeper 
                Search(i, farmsUsed + 1);

                // 3. Remove Pattern (Backtrack)
                CurrentFarmsUsed[tier]--;
                CurrentQty[i]--;
                for (int c = 0; c < Targets.Length; c++)
                {
                    CurrentYields[c] -= Patterns[i].Yields[c];
                }
            }
        }

        private void EvaluateScore()
        {
            double currentCost = 0;

            // 1. Evaluate Fillers dynamically (Indices 0 up to NumFillers - 1)
            for (int i = 0; i < NumFillers; i++)
            {
                double diff = CurrentYields[i] - Targets[i];

                if (diff > 0)
                {
                    // If it overproduces the global target ceiling, slam it with the heavy penalty
                    currentCost += diff * PENALTY_MAIN_OVER;
                }

                // THE FIX: Apply constant, gentle pressure to minimize filler volume.
                // Even if we are safe under the global cap, penalizing the raw yield 
                // forces the solver to pick the combinations that generate the least byproduct.
                currentCost += CurrentYields[i] * PENALTY_FILLER_OVER;
            }

            // 2. Evaluate Main Crops dynamically (Indices starting AFTER the fillers)
            for (int c = NumFillers; c < Targets.Length; c++)
            {
                double diff = CurrentYields[c] - Targets[c];

                if (diff < 0)
                {
                    currentCost += System.Math.Abs(diff) * PENALTY_MAIN_UNDER; // Starvation
                }
                else if (diff > 0)
                {
                    currentCost += diff * PENALTY_MAIN_OVER; // Surplus
                }
            }

            // 3. Record Best Solution
            if (currentCost < BestCost)
            {
                BestCost = currentCost;
                Array.Copy(CurrentQty, BestQty, Patterns.Length);
            }
        }
    }
}