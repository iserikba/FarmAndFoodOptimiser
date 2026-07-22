#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Iserik.FaFOptimiser.Solver
{
    public class CropDemand
    {
        // --- INPUT PAYLOAD ---
        // These are provided by the user/UI
        public string Name { get; set; } = string.Empty;
        public double Target { get; set; } = 0.0;
        public bool IsPriority { get; set; } = false;

        // --- INTERNAL ROUTING STATE ---
        public bool IsDependingCrop { get; set; } = false;
        // The solver populates these internally during the chunking phase
        public int OriginalIndex { get; set; } = -1;
    }

    public class FarmOptimiseSolver
    {
        // Data passed in from the game
        private string[] CropNames = null!;
        private double[] Target = null!;
        private bool[] IsPriority = null!;
        private bool[] IsDepending = null!; // Depends on Virtual Product
        private int[] MaxFarms = new int[5]; // Indices 1-4 for Tiers
        private FarmPattern[] Patterns = null!;
        private int NumCrops;
        // Virtual Products Request 
        private double virtAnimalFeed = 0.0;
        private double virtSnacks = 0.0;
        //
        private double TargetFertilitySet = 0.0;

        private int[] MinTierRequired = null!; 
        public int PatternsCount { get { return Patterns.Length; } }

        // Unified Output Array
        public int[] FinalBlueprint { get; private set; } = null!;

        // Async running support
        private Action<string> _logger = null!;
        private CancellationToken _cancelToken;

        public static double CalculateMaxLeaves(int N, int P)
        {
            double result = 1;
            for (int i = 1; i <= N; i++)
            {
                result = result * (P + i) / i;
            }
            return result;
        }

        // --- NEW: Calculate Independent Combinations Per Tier ---
        private double CalculateTotalMaxLeaves(int[] currentMaxFarms, List<FarmPattern> currentPatterns)
        {
            double totalLeaves = 1.0;

            // Iterate through valid Tiers (1 to 4)
            for (int tier = 1; tier <= 4; tier++)
            {
                int farmsInTier = currentMaxFarms[tier];

                if (farmsInTier > 0)
                {
                    // Count how many patterns belong specifically to this tier
                    int patternsInTier = currentPatterns.Count(p => p.TierIdx == tier);

                    if (patternsInTier > 0)
                    {
                        // Multiply the independent combinations
                        for (int i = 1; i <= farmsInTier; i++)
                        {
                            totalLeaves = totalLeaves * (patternsInTier + i) / i;
                        }
                    }
                }
            }

            return totalLeaves;
        }

        private void AsyncLog(string msg)
        {
            _logger?.Invoke(msg); // Replaces Console.WriteLine
        }

        public void Initialize(OptimizationRequest request, List<CropRecipe> allRecipes, Action<string> logger, CancellationToken token)
        {
            _logger = logger;
            _cancelToken = token;

            NumCrops = request.Demands.Count;
            TargetFertilitySet = request.TargetFertility;

            // Unpack into raw arrays INTERNALLY to protect loop speed
            CropNames = new string[NumCrops];
            Target = new double[NumCrops];
            IsPriority = new bool[NumCrops];
            IsDepending = new bool[NumCrops]; // <--- INITIALIZE ARRAY
            virtAnimalFeed = System.Math.Max(request.virtAnimalFeed, 0);
            virtSnacks = System.Math.Max(request.virtSnacks, 0.0);

            for (int i = 0; i < NumCrops; i++)
            {
                CropNames[i] = request.Demands[i].Name;
                Target[i] = request.Demands[i].Target;
                IsPriority[i] = request.Demands[i].IsPriority;

                string uName = CropNames[i].ToUpper();
                // Convert uName to uppercase once for the entire block

                IsDepending[i] = (uName == "PRODUCT_WHEAT" && virtAnimalFeed > 0)
                              || (uName == "PRODUCT_POTATO" && virtSnacks > 0)
                              || (uName == "PRODUCT_CORN" && (virtAnimalFeed > 0 || request.virtSnacks > 0));
            }

            int MaxRotation = request.MaxRotations > 4 ? 4 : (request.MaxRotations < 2 ? 2 : request.MaxRotations);
            MaxFarms = (int[])request.MaxFarms.Clone(); // Clone to safely mutate during chunking
            var activeRecipes = allRecipes.Where(r => CropNames.Contains(r.Name)).ToList();

            Patterns = PatternGenerator.Generate(MaxRotation, MaxFarms, activeRecipes, CropNames,
                request.TargetFertility).ToArray();

            // --- QUICK PATCH: Identify Greenhouse-Strict Crops ---
            MinTierRequired = new int[NumCrops];
            for (int i = 0; i < NumCrops; i++)
            {
                MinTierRequired[i] = 4; // Assume highest tier initially
                foreach (var p in Patterns)
                {
                    // Find the absolute lowest tier farm that can grow this crop
                    if (p.Yields[i] > 0 && p.TierIdx < MinTierRequired[i])
                    {
                        MinTierRequired[i] = p.TierIdx;
                    }
                }
            }
        }

        public OptimizationResult GetResult(OptimizationRequest originalRequest)
        {
            return new OptimizationResult(originalRequest, FinalBlueprint, Patterns, TargetFertilitySet);
        }

        // 3. The Hybrid Routing Loop
        public void Solve()
        {
            FinalBlueprint = new int[Patterns.Length];

            // Local state to modify as the loop progresses
            double[] currentTargets = (double[])Target.Clone();
            int[] currentMaxFarms = (int[])MaxFarms.Clone();
            List<FarmPattern> currentPatterns = Patterns.ToList();

            bool isComplete = false;
            int loopCount = 1;

            while (!isComplete)
            {
                // --- THE SOFT STOP CHECK ---
                if (_cancelToken.IsCancellationRequested)
                {
                    AsyncLog("Time limit reached! Halting Root Solver and returning partial blueprint.");
                    break; // Exit the while loop to build the final result with what we have
                }

                // 1. Check how many crops strictly still need to be solved
                int activeCropCount = currentTargets.Count(t => t > 0);

                // 2. REBUILD COMBINATIONS (Single Pass for max performance)
                currentPatterns = currentPatterns.Where(p =>
                {
                    bool helpsWithRemainingTarget = false;

                    for (int c = 0; c < NumCrops; c++)
                    {
                        // Check only crops this pattern actually produces
                        if (p.Yields[c] > 0)
                        {
                            if (currentTargets[c] > 0)
                            {
                                // It produces something we still need. Good!
                                helpsWithRemainingTarget = true;
                            }
                            else
                            {
                                // --- YOUR PRUNING RULE ---
                                // It produces a crop we DO NOT need.
                                // If we still have more than 2 active crops to solve, AND it's an illegal byproduct...
                                if (activeCropCount > 2 && !IsPriority[c] && !IsDepending[c])
                                {
                                    // ...Trash the pattern instantly.
                                    return false;
                                }
                            }
                        }
                    }

                    // Keep the pattern only if it actually helps us build something we need
                    return helpsWithRemainingTarget;
                }).ToList();

                int totalFarms = currentMaxFarms.Sum();
                int totalPatterns = currentPatterns.Count;

                // Safety break if we ran out of farms or patterns
                if (totalFarms == 0 || totalPatterns == 0) break;

                // --- APPLY THE TIER-BASED COMBINATORIAL MATH ---
                double maxLeaves = CalculateTotalMaxLeaves(currentMaxFarms, currentPatterns);

                AsyncLog(new String('=', 60));
                AsyncLog($"[ROOT SOLVER - PASS {loopCount}] Global Assessment");
                AsyncLog($"Farms Left: {totalFarms} | Valid Patterns: {totalPatterns}");
                AsyncLog($"Estimated Combinatorial Leaves: {maxLeaves:E2}");
                AsyncLog(new String('=', 60));

                // The Threshold Check (Maintained at 45M)
                if (maxLeaves < 45_000_000)
                {
                    AsyncLog("\n[ROOT SOLVER] Complexity is safe. Routing to Standard Optimizer to finish...\n");
                    RunStandardOptimizer(currentTargets, currentMaxFarms, currentPatterns);
                    isComplete = true;
                }
                else
                {
                    AsyncLog("\n[ROOT SOLVER] Threshold Exceeded! Evaluating problem profile...\n");

                    int currentTotalFarms = currentMaxFarms.Sum();
                    activeCropCount = currentTargets.Count(t => t > 0);
                    bool chunkSuccess = false;

                    // --- THE HEURISTIC BRANCH ---
                    if (activeCropCount < 5 && currentTotalFarms > 10)
                    {
                        AsyncLog("\n[ROOT SOLVER] Narrow/Deep profile detected. Routing to Bulk Allocator...");
                        chunkSuccess = ExecuteBulkReduction(ref currentTargets, ref currentMaxFarms, currentPatterns);
                    }
                    else
                    {
                        AsyncLog("\n[ROOT SOLVER] Wide profile detected. Extracting parametric chunk...");

                        int chunkFarmLimit = currentTotalFarms - 4;
                        if (chunkFarmLimit <= 0) chunkFarmLimit = 1;

                        int solvingProductCount = System.Math.Max(1, (int)(activeCropCount * 0.4));
                        int fillerProductCount = 2;

                        chunkSuccess = ExtractSingleChunk(ref currentTargets, ref currentMaxFarms, currentPatterns, solvingProductCount, fillerProductCount, chunkFarmLimit);
                    }

                    // If EITHER the Bulk Allocator or the Chunk Solver failed, force the end of the line.
                    if (!chunkSuccess)
                    {
                        AsyncLog("\n[ROOT SOLVER] CRITICAL: Reduction failed. Routing to Bulk Allocator...");
                        //AsyncLog("\n[ROOT SOLVER] Narrow/Deep profile detected. Routing to Bulk Allocator...");
                        chunkSuccess = ExecuteBulkReduction(ref currentTargets, ref currentMaxFarms, currentPatterns);

                        if (!chunkSuccess)
                        {
                            AsyncLog("\n[ROOT SOLVER] FATAL: Bulk Allocator failed to reduce problem. Forcing Standard Optimizer...");
                            RunStandardOptimizer(currentTargets, currentMaxFarms, currentPatterns);
                            isComplete = true; // Only end the loop if we forced the standard solver!
                        }
                    }
                }
                loopCount++;
            }

            AsyncLog("\n[ROOT SOLVER COMPLETE] Final Master Blueprint ready.");
        }

        // --- THE TWO SOLVING PATHS ---

        private void RunStandardOptimizer(double[] currentTargets, int[] currentMaxFarms, List<FarmPattern> currentPatterns)
        {
            // 1. Calculate how many virtual crops we are appending
            int extraCrops = 0;
            if (virtAnimalFeed > 0) extraCrops++;
            if (virtSnacks > 0) extraCrops++;

            // 2. Create expanded arrays for the Optimizer
            int totalOptCrops = NumCrops + extraCrops;
            string[] optCropNames = new string[totalOptCrops];
            double[] optTargets = new double[totalOptCrops];
            bool[] optPriorities = new bool[totalOptCrops];

            // Copy real crops over
            Array.Copy(CropNames, optCropNames, NumCrops);
            Array.Copy(currentTargets, optTargets, NumCrops);
            Array.Copy(IsPriority, optPriorities, NumCrops);

            // Append virtual crops to the end of the arrays
            int currentIndex = NumCrops;
            if (virtAnimalFeed > 0)
            {
                optCropNames[currentIndex] = "Animal Feed";
                optTargets[currentIndex] = virtAnimalFeed;
                optPriorities[currentIndex] = false;
                currentIndex++;
            }
            if (virtSnacks > 0)
            {
                optCropNames[currentIndex] = "Snack";
                optTargets[currentIndex] = virtSnacks;
                optPriorities[currentIndex] = false;
                currentIndex++;
            }

            // 3. Create "Padded" patterns so the Optimizer doesn't throw IndexOutOfRangeExceptions
            var paddedPatterns = new List<FarmPattern>();
            foreach (var p in currentPatterns)
            {
                FarmPattern newP = new FarmPattern(p.Name, p.TierIdx, totalOptCrops,p.Recipes);
                Array.Copy(p.Yields, newP.Yields, NumCrops);
                newP.WaterCost = p.WaterCost;
                newP.FertNeed = p.FertNeed;
                paddedPatterns.Add(newP);
            }

            // 4. Run the standard solver on the expanded sub-problem
            Optimizer standardSolver = new Optimizer();

            // --> FIXED: Now passing _logger and _cancelToken to the Optimizer
            standardSolver.Initialize(optCropNames, optTargets, optPriorities, currentMaxFarms, paddedPatterns.ToArray(), _logger, _cancelToken);

            // Wrap the solve execution in a stopwatch to track performance
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            standardSolver.Solve();

            sw.Stop();

            // --> NEW: Read the stats directly from the solver object
            if (standardSolver.BestCost == double.MaxValue)
            {
                AsyncLog("No valid solution found.");
            }
            else
            {
                if (standardSolver.Cancelled) AsyncLog($"\n--- OPTIMIZATION TIMEOUT (PARTIAL RESULT) ---");
                else AsyncLog($"\n--- OPTIMIZATION COMPLETE ---");

                AsyncLog($"Time Elapsed: {sw.ElapsedMilliseconds} ms");
                AsyncLog($"Total Iterations: {standardSolver.Iterations:N0}");
                AsyncLog($"Leaves Reached: {standardSolver.NumLeaves:N0}");
                AsyncLog($"Best Options Applied: {standardSolver.BestApplied}");
            }

            // 5. Merge the tail-end results into the FinalBlueprint
            for (int i = 0; i < currentPatterns.Count; i++)
            {
                int qty = standardSolver.BestQty[i];
                if (qty > 0)
                {
                    int globalIndex = Array.IndexOf(Patterns, currentPatterns[i]);
                    FinalBlueprint[globalIndex] += qty;
                }
            }
        }


        private bool ExtractSingleChunk(ref double[] currentTargets, ref int[] currentMaxFarms, List<FarmPattern> currentPatterns, int solvingProductCount, int fillerProductCount, int chunkFarmLimit)
        {
            var activeCrops = new List<CropDemand>();

            for (int i = 0; i < NumCrops; i++)
            {
                activeCrops.Add(new CropDemand
                {
                    OriginalIndex = i,
                    Name = CropNames[i],
                    Target = currentTargets[i],
                    IsPriority = IsPriority[i],
                    IsDependingCrop = IsDepending[i]
                });
            }

            var remaining = activeCrops.Where(c => c.Target > 0).ToList();

            var regularPool = remaining.Where(c => !c.IsDependingCrop)
                .OrderByDescending(c => MinTierRequired[c.OriginalIndex]) // <--- NEW: Force strict crops to the front
                .ThenBy(c => c.IsPriority ? 1 : 0)
                .ThenBy(c => c.Target).ToList();

            if (regularPool.Count < solvingProductCount)
                regularPool = remaining.Where(c => !c.Name.ToUpper().Contains("FEED") && !c.Name.ToUpper().Contains("SNACK"))
                    .OrderByDescending(c => MinTierRequired[c.OriginalIndex]) // <--- NEW: Force strict crops to the front
                    .ThenBy(c => c.IsPriority ? 1 : 0)
                    .ThenBy(c => c.Target).ToList();

            int smallCount = System.Math.Max(1, solvingProductCount - 1);
            var smallCrops = regularPool.Take(System.Math.Min(smallCount, regularPool.Count)).ToList();
            var largeCrop = chunkFarmLimit > 2 ? regularPool.Except(smallCrops).OrderByDescending(c => c.Target).FirstOrDefault() : null;

            var fillerPool = remaining.Except(smallCrops).ToList();
            if (largeCrop != null) fillerPool.Remove(largeCrop);

            var fillerCrops = fillerPool.OrderBy(c => c.IsDependingCrop ? 0 : 1)
                                      .ThenBy(c => c.IsPriority ? 0 : 1)
                                      .ThenByDescending(c => c.Target)
                                      .Take(fillerProductCount)
                                      .ToList();

            var chunkRoster = new List<CropDemand>();
            chunkRoster.AddRange(fillerCrops);
            chunkRoster.AddRange(smallCrops);
            if (largeCrop != null) chunkRoster.Add(largeCrop);

            var rosterIndices = chunkRoster.Select(c => c.OriginalIndex).ToHashSet();
            var validPatterns = new List<FarmPattern>();
            var originalPatternMap = new List<int>();

            for (int p = 0; p < currentPatterns.Count; p++)
            {
                bool isValid = true;
                for (int c = 0; c < NumCrops; c++)
                {
                    if (currentPatterns[p].Yields[c] > 0 && !rosterIndices.Contains(c)) { isValid = false; break; }
                }

                if (isValid)
                {
                    FarmPattern mappedPattern = new FarmPattern(currentPatterns[p].Name, currentPatterns[p].TierIdx, chunkRoster.Count, currentPatterns[p].Recipes);
                    for (int i = 0; i < chunkRoster.Count; i++) mappedPattern.Yields[i] = currentPatterns[p].Yields[chunkRoster[i].OriginalIndex];
                    validPatterns.Add(mappedPattern);
                    originalPatternMap.Add(p);
                }
            }

            if (validPatterns.Count == 0) return false;

            double[] chunkTargets = chunkRoster.Select(c => c.Target).ToArray();

            int totalAvailable = currentMaxFarms.Sum();
            int absoluteMaxFarms = System.Math.Min(4, chunkFarmLimit);
            absoluteMaxFarms = System.Math.Min(totalAvailable - 2, chunkFarmLimit);

            bool chunkSolved = false;
            int currentFarmsForChunk = 1;
            FastChunkSolver fastSolver = new FastChunkSolver();

            while (!chunkSolved && currentFarmsForChunk <= absoluteMaxFarms)
            {
                fastSolver.Solve(validPatterns.ToArray(), chunkTargets, currentMaxFarms, currentFarmsForChunk, fillerCrops.Count);

                if (fastSolver.BestCost < 100000) chunkSolved = true;
                else currentFarmsForChunk++;
            }

            if (chunkSolved)
            {
                AsyncLog("\n[CHUNK SOLVER] Successfully resolved micro-problem. Blueprint locked:");
                int farmsUsedThisChunk = 0;

                for (int fp = 0; fp < validPatterns.Count; fp++)
                {
                    int qty = fastSolver.BestQty[fp];
                    if (qty > 0)
                    {
                        var sourcePattern = currentPatterns[originalPatternMap[fp]];
                        int globalIndex = Array.IndexOf(Patterns, sourcePattern);

                        FinalBlueprint[globalIndex] += qty;
                        currentMaxFarms[sourcePattern.TierIdx] -= qty;
                        farmsUsedThisChunk += qty;

                        // --- REPLACED CONSOLE PRINT WITH ASYNCLOG ---
                        AsyncLog($"  -> Locked: {qty}x {sourcePattern.Name}");

                        for (int c = 0; c < NumCrops; c++)
                        {
                            if (sourcePattern.Yields[c] > 0)
                            {
                                currentTargets[c] -= (sourcePattern.Yields[c] * qty);
                            }
                        }
                    }
                }

                AsyncLog($"  -> Total Farms Assigned: {farmsUsedThisChunk}");
                return true;
            }
            return false;
        }

        private bool ExecuteBulkReduction(ref double[] currentTargets, ref int[] currentMaxFarms, List<FarmPattern> currentPatterns)
        {
            int targetCropIndex = -1;
            double highestTarget = 0;
            int highestTierRequirement = -1;

            for (int i = 0; i < NumCrops; i++)
            {
                if (currentTargets[i] > 0)
                {
                    // PRIORITIZE STRICT CROPS: Solve crops that require high tiers FIRST
                    // If the tier requirement is the same, fall back to solving the highest target
                    if (MinTierRequired[i] > highestTierRequirement ||
                       (MinTierRequired[i] == highestTierRequirement && currentTargets[i] > highestTarget))
                    {
                        highestTierRequirement = MinTierRequired[i];
                        highestTarget = currentTargets[i];
                        targetCropIndex = i;
                    }
                }
            }
            if (targetCropIndex == -1) return false;

            int[] localMaxFarms = currentMaxFarms;
            int maxAllowedBulk = System.Math.Max(1, currentMaxFarms.Sum() / 4);

            FarmPattern? bestPattern = null;
            int bestAllocation = 0;
            double bestEfficiencyScore = double.MaxValue; // Lower is better

            var candidatePatterns = currentPatterns
                .Where(p => p.Yields[targetCropIndex] > 0 && localMaxFarms[p.TierIdx] > 0)
                .ToList();

            foreach (var p in candidatePatterns)
            {
                int farmsNeeded = (int)System.Math.Ceiling(highestTarget / p.Yields[targetCropIndex]);
                int maxSafeFarms = farmsNeeded;

                for (int c = 0; c < NumCrops; c++)
                {
                    if (c != targetCropIndex && p.Yields[c] > 0 && currentTargets[c] > 0)
                    {
                        int safeForThisCrop = (int)System.Math.Ceiling(currentTargets[c] / p.Yields[c]) + 1;
                        maxSafeFarms = System.Math.Min(maxSafeFarms, safeForThisCrop);
                    }
                }
                maxSafeFarms = System.Math.Min(maxSafeFarms, System.Math.Min(localMaxFarms[p.TierIdx], maxAllowedBulk));

                if (maxSafeFarms <= 0) continue;

                double byproductTotal = 0;
                for (int c = 0; c < NumCrops; c++)
                    if (c != targetCropIndex) byproductTotal += p.Yields[c];

                double efficiency = byproductTotal / p.Yields[targetCropIndex];

                if (efficiency < bestEfficiencyScore)
                {
                    bestEfficiencyScore = efficiency;
                    bestPattern = p;
                    bestAllocation = maxSafeFarms;
                }
            }

            if (bestPattern == null) return false;

            int globalIndex = Array.IndexOf(Patterns, bestPattern);
            FinalBlueprint[globalIndex] += bestAllocation;
            currentMaxFarms[bestPattern.TierIdx] -= bestAllocation;
            for (int c = 0; c < NumCrops; c++) currentTargets[c] -= (bestPattern.Yields[c] * bestAllocation);

            // --- REPLACED CONSOLE PRINT WITH ASYNCLOG ---
            AsyncLog($"[BULK ALLOCATOR] Assigned {bestAllocation}x {bestPattern.Name} (Efficiency: {bestEfficiencyScore:F2}).");
            return true;
        }
    }
}