using System;
using System.Diagnostics;
using System.Threading;

namespace Iserik.FaFOptimiser.Solver
{
    // Main Branch-and-Bound engine 
    public class Optimizer
    {
        private bool UseSecondaryPenalties = false;
        public bool Cancelled { get; set; } = false;

        public long Iterations { get; private set; }
        public long NumLeaves { get; private set; }
        public long BestApplied { get; private set; }

        private int NumCrops;
        private string[] CropNames = null!;
        private double[] Target = null!;
        private bool[] IsPriority = null!;
        private double[] CurrentYields = null!;

        private int idxCorn = -1, idxWheat = -1, idxPotato = -1, idxAF = -1, idxSnack = -1;

        private int[] MaxFarms = new int[5]; // Indices 1-4 for Tiers
        private FarmPattern[] Patterns = null!;
        private int NumPatterns;

        public int[] BestQty { get; private set; } = null!;
        public double BestCost { get; private set; }
        private int[] CurrentQty = null!;

        // --- ASYNC SUPPORT ---
        private Action<string> _logger = null!;
        private CancellationToken _cancelToken;

        private void AsyncLog(string msg)
        {
            _logger?.Invoke(msg);
        }

        public void Initialize(string[] cropNames, double[] targets, bool[] priorities, int[] maxFarms, FarmPattern[] patterns, Action<string> logger, CancellationToken token)
        {
            NumCrops = cropNames.Length;
            CropNames = cropNames;
            Target = targets;
            IsPriority = priorities;
            CurrentYields = new double[NumCrops];

            MaxFarms = maxFarms;
            Patterns = patterns;
            NumPatterns = patterns.Length;

            BestQty = new int[NumPatterns];
            CurrentQty = new int[NumPatterns];
            BestCost = double.MaxValue;

            _logger = logger;
            _cancelToken = token;

            // Map Virtual Indices (C# uses 0-based arrays)
            for (int c = 0; c < NumCrops; c++)
            {
                string name = CropNames[c].ToUpper();
                if (name == "PRODUCT_CORN") idxCorn = c;
                else if (name == "PRODUCT_WHEAT") idxWheat = c;
                else if (name == "PRODUCT_POTATO") idxPotato = c;
                else if (name == "ANIMAL FEED") idxAF = c;
                else if (name == "SNACK") idxSnack = c;
            }
        }

        public void Solve()
        {
            Iterations = 0;
            NumLeaves = 0;
            BestApplied = 0;
            Cancelled = false;

            Stopwatch sw = Stopwatch.StartNew();

            AsyncLog("Starting high-speed C# optimization...");
            SearchQuantities(0, 0, 0, 0, 0, 0.0, 0.0);

            sw.Stop();


        }

        private void SearchQuantities(int patIdx, int usedT1, int usedT2, int usedT3, int usedT4, double totalWater, double totalFert)
        {
            if (Cancelled) return;

            Iterations++;

            // Only update the log every 5 million iterations to keep the background thread fast
            if (Iterations % 100_000_000 == 0)
            {
                AsyncLog($"Searching... {(Iterations / 1000000.0):0.00}M branches | Solutions: {BestApplied}");
            }

            // EARLY PRUNING
            int currentFields = usedT1 + usedT2 + usedT3 + usedT4;
            if ((currentFields * 100000) - 50000 >= BestCost) return;

            // BASE CASE: All patterns evaluated
            if (patIdx >= NumPatterns)
            {
                NumLeaves++;
                EvaluateScore(usedT1, usedT2, usedT3, usedT4, totalWater, totalFert);
                return;
            }

            int pTier = Patterns[patIdx].TierIdx;
            int maxK = 0;

            if (pTier == 1) maxK = MaxFarms[1] - usedT1;
            else if (pTier == 2) maxK = MaxFarms[2] - usedT2;
            else if (pTier == 3) maxK = MaxFarms[3] - usedT3;
            else if (pTier == 4) maxK = MaxFarms[4] - usedT4;

            if (pTier == 0 || maxK <= 0)
            {
                CurrentQty[patIdx] = 0;
                SearchQuantities(patIdx + 1, usedT1, usedT2, usedT3, usedT4, totalWater, totalFert);
                return;
            }

            for (int k = 0; k <= maxK; k++)
            {
                CurrentQty[patIdx] = k;

                if (k > 0)
                {
                    for (int c = 0; c < NumCrops; c++)
                        CurrentYields[c] += Patterns[patIdx].Yields[c] * k;
                }

                int nT1 = usedT1 + (pTier == 1 ? k : 0);
                int nT2 = usedT2 + (pTier == 2 ? k : 0);
                int nT3 = usedT3 + (pTier == 3 ? k : 0);
                int nT4 = usedT4 + (pTier == 4 ? k : 0);

                SearchQuantities(patIdx + 1, nT1, nT2, nT3, nT4,
                                 totalWater + (Patterns[patIdx].WaterCost * k),
                                 totalFert + (Patterns[patIdx].FertNeed * k));

                if (k > 0)
                {
                    for (int c = 0; c < NumCrops; c++)
                        CurrentYields[c] -= Patterns[patIdx].Yields[c] * k;
                }
            }
        }

        private void EvaluateScore(int usedT1, int usedT2, int usedT3, int usedT4, double totalWater, double totalFert)
        {
            double shortageCost = 0;
            double priorityReward = 0;

            double surplusCorn = 0, surplusWheat = 0, surplusPotato = 0;

            for (int c = 0; c < NumCrops; c++)
            {
                if (c != idxAF && c != idxSnack)
                {
                    double diff = CurrentYields[c] - Target[c];

                    if (diff < 0)
                    {
                        shortageCost += System.Math.Abs(diff) * 1000000.0;
                    }
                    else
                    {
                        if (c == idxCorn) surplusCorn = diff;
                        else if (c == idxWheat) surplusWheat = diff;
                        else if (c == idxPotato) surplusPotato = diff;
                        else if (IsPriority[c]) priorityReward += System.Math.Sqrt(diff) * 2000.0;
                    }
                }
            }

            double yieldAF = 0, yieldSnack = 0;
            double targetAF = idxAF >= 0 ? Target[idxAF] : 0;
            double targetSnack = idxSnack >= 0 ? Target[idxSnack] : 0;

            bool wantAF = idxAF >= 0 && (targetAF > 0 || IsPriority[idxAF]);
            bool wantSnack = idxSnack >= 0 && (targetSnack > 0 || IsPriority[idxSnack]);

            if (wantAF && idxWheat >= 0 && surplusWheat > 0)
            {
                if (IsPriority[idxAF]) { yieldAF += surplusWheat * 1.6; surplusWheat = 0; } // Unlimited if AF is priority
                else
                {
                    double needed = Target[idxAF] - yieldAF;
                    if (needed > 0)
                    {
                        double wheatNeeded = needed / 1.6;
                        if (surplusWheat >= wheatNeeded) { yieldAF += needed; surplusWheat -= wheatNeeded; }
                        else { yieldAF += surplusWheat * 1.6; surplusWheat = 0; }
                    }
                }
            }

            if (wantSnack && idxPotato >= 0 && surplusPotato > 0)
            {
                if (IsPriority[idxSnack]) { yieldSnack += surplusPotato * 0.75; surplusPotato = 0; } // Unlimited if Snack is priority
                else
                {
                    double needed = Target[idxSnack] - yieldSnack;
                    if (needed > 0)
                    {
                        double potNeeded = needed / 0.75;
                        if (surplusPotato >= potNeeded) { yieldSnack += needed; surplusPotato -= potNeeded; }
                        else { yieldSnack += surplusPotato * 0.75; surplusPotato = 0; }
                    }
                }
            }

            double neededAF = (wantAF && targetAF > yieldAF) ? targetAF - yieldAF : 0;
            double neededSnack = (wantSnack && targetSnack > yieldSnack) ? targetSnack - yieldSnack : 0;

            if (wantAF && neededAF > 0 && surplusCorn > 0)
            {
                double cornForAF = neededAF / 1.2;
                if (surplusCorn >= cornForAF)
                {
                    yieldAF += neededAF;
                    surplusCorn -= cornForAF;
                }
                else
                {
                    yieldAF += surplusCorn * 1.2;
                    surplusCorn = 0;
                }
            }

            if (wantSnack && neededSnack > 0 && surplusCorn > 0)
            {
                if (surplusCorn >= neededSnack)
                {
                    yieldSnack += neededSnack;
                    surplusCorn -= neededSnack;
                }
                else
                {
                    yieldSnack += surplusCorn;
                    surplusCorn = 0;
                }
            }

            if (surplusCorn > 0)
            {
                bool prioAF = wantAF && IsPriority[idxAF];
                bool prioSnack = wantSnack && IsPriority[idxSnack];

                if (prioAF && prioSnack)
                {
                    yieldAF += (surplusCorn / 2.0) * 1.2;
                    yieldSnack += (surplusCorn / 2.0) * 1.0;
                    surplusCorn = 0;
                }
                else if (prioSnack)
                {
                    yieldSnack += surplusCorn * 1.0;
                    surplusCorn = 0;
                }
                else if (prioAF)
                {
                    yieldAF += surplusCorn * 1.2;
                    surplusCorn = 0;
                }

                if (surplusCorn > 0 && idxCorn >= 0 && IsPriority[idxCorn])
                {
                    priorityReward += System.Math.Sqrt(surplusCorn) * 2000.0;
                }
            }

            if (surplusWheat > 0 && idxWheat >= 0 && IsPriority[idxWheat]) priorityReward += System.Math.Sqrt(surplusWheat) * 2000.0;
            if (surplusPotato > 0 && idxPotato >= 0 && IsPriority[idxPotato]) priorityReward += System.Math.Sqrt(surplusPotato) * 2000.0;

            if (wantAF)
            {
                if (yieldAF < targetAF) shortageCost += (targetAF - yieldAF) * 1000000.0;
                else if (IsPriority[idxAF]) priorityReward += System.Math.Sqrt(yieldAF - targetAF) * 2000.0;
            }

            if (wantSnack)
            {
                if (yieldSnack < targetSnack) shortageCost += (targetSnack - yieldSnack) * 1000000.0;
                else if (IsPriority[idxSnack]) priorityReward += System.Math.Sqrt(yieldSnack - targetSnack) * 2000.0;
            }

            int totalFields = usedT1 + usedT2 + usedT3 + usedT4;
            double currentCost = shortageCost + (totalFields * 100000.0) - priorityReward;

            if (UseSecondaryPenalties)
                currentCost += (totalFert * 1000.0) + totalWater;

            // Check if this is a new Best Cost
            if (currentCost < BestCost)
            {
                BestCost = currentCost;
                Array.Copy(CurrentQty, BestQty, NumPatterns);
                BestApplied++;
                // Removed log output here to save performance and prevent console spam
            }

            // --- THE CANCEL EXIT CONDITION ---
            // If the 20-second timeout was triggered, set the cancelled flag.
            // SearchQuantities() will instantly return out of all recursive layers.
            if (_cancelToken.IsCancellationRequested)
            {
                Cancelled = true;
            }
        }

        /*
        public void PrintResults()
        {
            if (BestCost == double.MaxValue) return;

            AsyncLog("\n\n=== DETAILED OPTIMIZED SOLUTION ===");
            AsyncLog(String.Format("{0,-4} | {1,-6} | {2,-15} | {3}", "Qty", "Tier", "Pattern Name", "Resource Usage"));
            AsyncLog(new String('-', 60));

            double[] finalYields = new double[NumCrops];
            double totalWater = 0;
            double totalFert = 0;
            int totalFarms = 0;

            // 1. Print the blueprint and sum up the base yields
            for (int p = 0; p < NumPatterns; p++)
            {
                int qty = BestQty[p];
                if (qty > 0)
                {
                    for (int c = 0; c < NumCrops; c++)
                    {
                        double yld = Patterns[p].Yields[c] * qty;
                        finalYields[c] += yld;
                    }

                    double w = Patterns[p].WaterCost * qty;
                    double f = Patterns[p].FertNeed * qty;
                    totalWater += w;
                    totalFert += f;
                    totalFarms += qty;

                    // Consolidated formatting into a single AsyncLog pass
                    AsyncLog(String.Format("{0,-4} | FarmT{1,-1} | {2,-15} | Water: {3:F1}, Fert: {4:F1}", qty + "x", Patterns[p].TierIdx, Patterns[p].Name, w, f));
                }
            }

            // 2. Re-apply Base-Fenced Virtual Allocation for accurate printout
            double surplusCorn = 0, surplusWheat = 0, surplusPotato = 0;

            if (idxCorn >= 0 && finalYields[idxCorn] > Target[idxCorn]) { surplusCorn = finalYields[idxCorn] - Target[idxCorn]; finalYields[idxCorn] = Target[idxCorn]; }
            if (idxWheat >= 0 && finalYields[idxWheat] > Target[idxWheat]) { surplusWheat = finalYields[idxWheat] - Target[idxWheat]; finalYields[idxWheat] = Target[idxWheat]; }
            if (idxPotato >= 0 && finalYields[idxPotato] > Target[idxPotato]) { surplusPotato = finalYields[idxPotato] - Target[idxPotato]; finalYields[idxPotato] = Target[idxPotato]; }

            double targetAF = idxAF >= 0 ? Target[idxAF] : 0;
            double targetSnack = idxSnack >= 0 ? Target[idxSnack] : 0;

            bool wantAF = idxAF >= 0 && (targetAF > 0 || IsPriority[idxAF]);
            bool wantSnack = idxSnack >= 0 && (targetSnack > 0 || IsPriority[idxSnack]);

            double yieldAF = 0;
            double yieldSnack = 0;

            if (wantAF && idxWheat >= 0 && surplusWheat > 0)
            {
                if (IsPriority[idxAF]) { yieldAF += surplusWheat * 1.6; surplusWheat = 0; }
                else
                {
                    double needed = Target[idxAF] - yieldAF;
                    if (needed > 0)
                    {
                        double wheatNeeded = needed / 1.6;
                        if (surplusWheat >= wheatNeeded) { yieldAF += needed; surplusWheat -= wheatNeeded; }
                        else { yieldAF += surplusWheat * 1.6; surplusWheat = 0; }
                    }
                }
            }

            if (wantSnack && idxPotato >= 0 && surplusPotato > 0)
            {
                if (IsPriority[idxSnack]) { yieldSnack += surplusPotato * 0.75; surplusPotato = 0; }
                else
                {
                    double needed = Target[idxSnack] - yieldSnack;
                    if (needed > 0)
                    {
                        double potNeeded = needed / 0.75;
                        if (surplusPotato >= potNeeded) { yieldSnack += needed; surplusPotato -= potNeeded; }
                        else { yieldSnack += surplusPotato * 0.75; surplusPotato = 0; }
                    }
                }
            }

            double neededAF = (wantAF && targetAF > yieldAF) ? targetAF - yieldAF : 0;
            double neededSnack = (wantSnack && targetSnack > yieldSnack) ? targetSnack - yieldSnack : 0;

            if (wantAF && neededAF > 0 && surplusCorn > 0)
            {
                double cornForAF = neededAF / 1.2;
                if (surplusCorn >= cornForAF) { yieldAF += neededAF; surplusCorn -= cornForAF; }
                else { yieldAF += surplusCorn * 1.2; surplusCorn = 0; }
            }

            if (wantSnack && neededSnack > 0 && surplusCorn > 0)
            {
                if (surplusCorn >= neededSnack) { yieldSnack += neededSnack; surplusCorn -= neededSnack; }
                else { yieldSnack += surplusCorn; surplusCorn = 0; }
            }

            if (surplusCorn > 0)
            {
                bool prioAF = wantAF && IsPriority[idxAF];
                bool prioSnack = wantSnack && IsPriority[idxSnack];

                if (prioAF && prioSnack) { yieldAF += (surplusCorn / 2.0) * 1.2; yieldSnack += (surplusCorn / 2.0) * 1.0; surplusCorn = 0; }
                else if (prioSnack) { yieldSnack += surplusCorn * 1.0; surplusCorn = 0; }
                else if (prioAF) { yieldAF += surplusCorn * 1.2; surplusCorn = 0; }
            }

            if (idxAF >= 0) finalYields[idxAF] = yieldAF;
            if (idxSnack >= 0) finalYields[idxSnack] = yieldSnack;
            if (idxCorn >= 0) finalYields[idxCorn] += surplusCorn;
            if (idxWheat >= 0) finalYields[idxWheat] += surplusWheat;
            if (idxPotato >= 0) finalYields[idxPotato] += surplusPotato;

            // 3. Print Summary
            AsyncLog(new String('-', 60));
            AsyncLog($"Total Farms Used: {totalFarms}");
            AsyncLog("\nFinal Deficit (Negative = Surplus):");

            for (int c = 0; c < NumCrops; c++)
            {
                string label = CropNames[c] + (IsPriority[c] ? "+" : "");
                double deficit = Target[c] - finalYields[c];
                AsyncLog(String.Format("  {0,-15}: {1,8:F3}", label, deficit));
            }

            AsyncLog($"\nTotal Water Used: {totalWater:F3}");
            AsyncLog($"Total Fertiliser: {totalFert:F3}");
        }
        */
    }
}