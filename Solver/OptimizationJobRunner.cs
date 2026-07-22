#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Iserik.FaFOptimiser.Solver
{
    public class OptimizationJobRunner
    {
        public event Action<string> OnLogMessage = null!;

        // --- PUBLIC STATE VARIABLES FOR UNITY TO POLL ---
        public bool IsRunning { get; private set; } = false;
        public bool IsFinished { get; private set; } = false;
        public OptimizationResult? Result { get; private set; } = null;

        /// <summary>
        /// Fires off the background thread and immediately returns control to Unity.
        /// If flexibleTier is > 0, it uses the HardwareEstimator. Otherwise, it runs a single standard solve.
        /// </summary>
        public void StartJob(OptimizationRequest request, List<CropRecipe> allRecipes, int flexibleTier = -1)
        {
            if (IsRunning)
            {
                Log("A job is already running! Ignoring new request.");
                return;
            }

            // Reset state
            IsRunning = true;
            IsFinished = false;
            Result = null;

            // Push the ENTIRE job tracking into the background thread pool
            Task.Run(async () =>
            {
                try
                {
                    await ExecuteInternalAsync(request, allRecipes, flexibleTier);
                }
                catch (Exception ex)
                {
                    Log($"FATAL RUNNER ERROR: {ex.Message}");
                    Result = null;
                }
                finally
                {
                    // No matter what happens, flag the job as finished so Unity knows to stop waiting
                    IsRunning = false;
                    IsFinished = true;
                }
            });
        }

        // --- INTERNAL EXECUTION ENGINE ---
        private async Task ExecuteInternalAsync(OptimizationRequest request, List<CropRecipe> allRecipes, int flexibleTier)
        {
            using (CancellationTokenSource softCancelSource = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                CancellationToken token = softCancelSource.Token;

                Task<OptimizationResult?> solverTask = Task.Run(() =>
                {
                    try
                    {
                        if (flexibleTier > 0)
                        {
                            Log($"Starting background Hardware Estimator for Tier {flexibleTier}...");
                            HardwareEstimator estimator = new HardwareEstimator();

                            // Let the Estimator handle the loop and return the best blueprint
                            return estimator.FindMinimumFarms(request, allRecipes, flexibleTier, Log, token);
                        }
                        else
                        {
                            Log("Starting background standard optimization thread...");
                            FarmOptimiseSolver solver = new FarmOptimiseSolver();

                            // Run exactly once with the provided hardware limits
                            solver.Initialize(request, allRecipes, Log, token);
                            solver.Solve();
                            return solver.GetResult(request);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log("WARNING: Solver gracefully aborted due to 20-second timeout.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Log($"CRITICAL ERROR in solver thread: {ex.Message}");
                        return null;
                    }
                }, token);

                // Wait for either the solver to finish OR 30 seconds to pass
                Task firstToFinish = await Task.WhenAny(solverTask, Task.Delay(TimeSpan.FromSeconds(30)));

                if (firstToFinish == solverTask)
                {
                    Log("Background thread completed successfully.");
                    Result = await solverTask; // Assign the public result property
                }
                else
                {
                    Log("FATAL TIMEOUT: Solver thread exceeded 30 seconds. Abandoning thread.");
                    Result = null;
                }
            }
        }

        private void Log(string message)
        {
            OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}