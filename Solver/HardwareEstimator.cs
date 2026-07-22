#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Iserik.FaFOptimiser.Solver
{
    public class HardwareEstimator
    {
        private Action<string> _logger = null!;

        private void AsyncLog(string msg)
        {
            _logger?.Invoke(msg);
        }

        public OptimizationResult? FindMinimumFarms(OptimizationRequest baseRequest, List<CropRecipe> allRecipes, int flexibleTier, Action<string> logger, CancellationToken token)
        {
            _logger = logger;

            AsyncLog($"\n=== INITIATING HARDWARE ESTIMATOR FOR TIER {flexibleTier} ===");

            // 1. Get the current inventory state
            int existingTotal = baseRequest.MaxFarms.Sum();
            int tierBaseline = baseRequest.MaxFarms[flexibleTier];

            // 2. We define the search space as the 'additional' farms needed above baseline.
            int minNeededTotal = CalculateSmartMinimum(baseRequest, allRecipes, flexibleTier);
            int startAdditional = System.Math.Max(0, minNeededTotal - existingTotal);
            int maxAdditional = 50; // Hard cap

            for (int additional = startAdditional; additional <= maxAdditional; additional++)
            {
                // --- THE CANCEL CHECK ---
                // If the watchdog timer expired, break the estimator loop instantly.
                if (token.IsCancellationRequested)
                {
                    AsyncLog("\n[ESTIMATOR ABORTED] Search timed out before a minimum layout could be found.");
                    return null;
                }

                int currentFarms = tierBaseline + additional;

                // Create a fresh clone of the request so we don't mutate the original arrays
                OptimizationRequest testRequest = baseRequest.Clone();

                // Inject our current guess for the flexible tier
                testRequest.MaxFarms[flexibleTier] = currentFarms;

                AsyncLog($"\n[ESTIMATOR] Testing layout with {currentFarms}x Tier {flexibleTier} farms...");

                // Run the Lightning-Fast Solver
                FarmOptimiseSolver solver = new FarmOptimiseSolver();

                // Pass the logger and token down into the main solver
                solver.Initialize(testRequest, allRecipes, _logger, token);
                solver.Solve();

                OptimizationResult result = solver.GetResult(testRequest);

                // Check if this hardware was enough to meet all targets
                // If the token tripped inside the solver, it returns a partial result, which will fail this check.
                if (result.IsSuccessful())
                {
                    AsyncLog(new String('=', 60));
                    AsyncLog($"[ESTIMATOR SUCCESS] Minimum hardware found! Exactly {currentFarms}x Tier {flexibleTier} farms required.");
                    return result; // Return the perfect blueprint
                }
            }

            AsyncLog("[ESTIMATOR FAILED] Could not satisfy demand even with max hardware limit.");
            return null; // Or return the best-effort result
        }

        private int CalculateSmartMinimum(OptimizationRequest req, List<CropRecipe> allRecipes, int flexibleTier)
        {
            double farmsNum = 0;

            // 1. --- CALCULATE RAW CROPS ---
            foreach (var demand in req.Demands)
            {
                if (demand.Target <= 0) continue;

                double maxMonthlyProd = GetMaxMonthlyProd(demand.Name, flexibleTier, allRecipes, req.TargetFertility);
                if (maxMonthlyProd > 0)
                {
                    farmsNum += (demand.Target / maxMonthlyProd);
                }
            }

            // 2. --- CALCULATE ANIMAL FEED (Optimistic) ---
            if (req.virtAnimalFeed > 0)
            {
                // FIXED: Used proper internal IDs for the lookup
                double wheatProd = GetMaxMonthlyProd("Product_Wheat", flexibleTier, allRecipes, req.TargetFertility);
                double cornProd = GetMaxMonthlyProd("Product_Corn", flexibleTier, allRecipes, req.TargetFertility);

                double farmsViaWheat = wheatProd > 0 ? (req.virtAnimalFeed * 60.0 / 96.0) / wheatProd : double.MaxValue;
                double farmsViaCorn = cornProd > 0 ? (req.virtAnimalFeed * 60.0 / 72.0) / cornProd : double.MaxValue;

                // Pick the most farm-efficient path to ensure we don't overshoot the minimum guess
                double cheapestPath = System.Math.Min(farmsViaWheat, farmsViaCorn);
                if (cheapestPath != double.MaxValue) farmsNum += cheapestPath;
            }

            // 3. --- CALCULATE SNACKS (Optimistic) ---
            if (req.virtSnacks > 0)
            {
                // FIXED: Used proper internal IDs for the lookup
                double potatoProd = GetMaxMonthlyProd("Product_Potato", flexibleTier, allRecipes, req.TargetFertility);
                double cornProd = GetMaxMonthlyProd("Product_Corn", flexibleTier, allRecipes, req.TargetFertility);

                double farmsViaPotato = potatoProd > 0 ? (req.virtSnacks * 24.0 / 18.0) / potatoProd : double.MaxValue;
                double farmsViaCorn = cornProd > 0 ? (req.virtSnacks * 1.0) / cornProd : double.MaxValue;

                // Pick the most farm-efficient path
                double cheapestPath = System.Math.Min(farmsViaPotato, farmsViaCorn);
                if (cheapestPath != double.MaxValue) farmsNum += cheapestPath;
            }

            // Floor the result to safely underestimate
            return System.Math.Max(1, (int)System.Math.Floor(farmsNum));
        }

        // --- NEW HELPER: Keeps the math DRY (Don't Repeat Yourself) ---
        private double GetMaxMonthlyProd(string cropName, int tier, List<CropRecipe> allRecipes,  double targetFertility)
        {
            // FIXED: Filter strictly by FarmTier to get the mathematically scaled recipe for this tier
            var r = allRecipes.FirstOrDefault(cr =>
                cr.FarmTier == tier &&
                cr.Name.Equals(cropName, StringComparison.OrdinalIgnoreCase));

            if (r == null) return 0;

            double monthlyFertCost = r.Fertility / r.Months;

            // FIXED: Lock Tier 1 farms to natural equilibrium since they cannot accept piped fertilizer
            double FertEquilibrium = 100.0 - (monthlyFertCost / 0.3);
            double fertilityMultiplier = (tier == 1 ? FertEquilibrium : System.Math.Max(FertEquilibrium, targetFertility)) / 100.0;

            // Math relies solely on the global multiplier now, as the Farm Proto scaling is natively inside r.Production
            return (r.Production / r.Months) * fertilityMultiplier;
        }
    }
}