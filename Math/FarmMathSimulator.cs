using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using System.Collections.Generic;
using System.Linq;

namespace Iserik.FaFOptimiser.Math
{
    public enum FertilizerTier
    {
        None, Organic, Tier1, Tier2
    }

    public enum FarmTier
    {
        Tier1_Basic, Tier2_Irrigated, Tier3_Greenhouse, Tier4_GreenhouseII
    }

    static public class FarmSimulatorTypeMuliplyer
    {
        public static (Fix32 yieldMult, Fix32 waterMult) GetFarmTierMultipliers(FarmTier tier)
        {
            switch (tier)
            {
                case FarmTier.Tier3_Greenhouse: return (Fix32.FromFloat(1.2f), Fix32.FromFloat(1.2f));
                case FarmTier.Tier4_GreenhouseII: return (Fix32.FromFloat(1.5f), Fix32.FromFloat(1.5f));
                case FarmTier.Tier1_Basic:
                case FarmTier.Tier2_Irrigated:
                default: return (Fix32.One, Fix32.One);
            }
        }
    }

    public static class FarmConstants
    {
        public static readonly Fix32 FERTILITY_PENALTY_FOR_SAME_CROP = Fix32.FromFloat(0.5f);
        public static readonly Fix32 NATURAL_REPLENISH_PER_DAY = Fix32.FromFloat(0.006f);
        public static readonly Fix32 FERTILITY_REPLENISH_MULT_WHEN_ABOVE_100 = Fix32.FromFloat(0.2f);
        public static readonly Fix32 CROP_FERTILITY_DEMAND_MULT_WHEN_ABOVE_100 = Fix32.FromFloat(2.0f);

        public static readonly int DAYS_PER_YEAR = 360;
        public static readonly int DAYS_PER_MONTH = 30;

        public static Fix32 GetMaxFertilityCap(FertilizerTier tier)
        {
            switch (tier)
            {
                case FertilizerTier.Organic: return Fix32.One;
                case FertilizerTier.Tier1: return Fix32.FromFloat(1.2f);
                case FertilizerTier.Tier2: return Fix32.FromFloat(1.4f);
                default: return Fix32.Zero;
            }
        }

        public static Fix32 GetFertilityPerUnit(FertilizerTier tier)
        {
            switch (tier)
            {
                case FertilizerTier.Tier2: return Fix32.FromFloat(0.025f); // +2.5% per item
                case FertilizerTier.Tier1: return Fix32.FromFloat(0.015f);
                case FertilizerTier.Organic: return Fix32.FromFloat(0.005f);
                default: return Fix32.One;
            }
        }
    }

    public class FarmSimulationOutput
    {
        // Added to expose raw base recipes to the log
        public Dictionary<ProductProto, Fix32> BaseRecipes { get; set; } = new Dictionary<ProductProto, Fix32>();
        public Dictionary<ProductProto, Fix32> MonthlyYields { get; set; } = new Dictionary<ProductProto, Fix32>();
        public Fix32 MonthlyWaterConsumption { get; set; } = Fix32.Zero;
        public Fix32 MonthlyFertilizerNeeded { get; set; } = Fix32.Zero;
        public Fix32 SettledOperatingFertility { get; set; } = Fix32.Zero;

        // Added to expose the raw percentage deficit to the log
        public Fix32 MonthlyFertilizerPercentageDeficit { get; set; } = Fix32.Zero;
    }

    public static class FarmMathSimulator
    {
        public static FarmSimulationOutput SimulateTheoreticalFarm(
            FarmProto farmProto, // Passed in to get tier-scaled stats
            List<CropProto> schedule,
            FertilizerTier fertTier,
            Fix32 requestedTargetFertility,
            Fix32 globalYieldMultiplier,
            Fix32 globalWaterMultiplier)
        {
            var result = new FarmSimulationOutput();
            if (schedule == null || schedule.Count == 0) return result;

            int totalRotationDays = 0;
            Fix32 totalFertilityConsumed = Fix32.Zero;
            Fix32 totalWaterConsumed = Fix32.Zero;

            CropProto previousCrop = schedule.Last();

            // --- STEP 1: Process Rotation & Penalties ---
            foreach (var crop in schedule)
            {
                totalRotationDays += crop.DaysToGrow;

                // ---> THE CRITICAL FIX: Use farmProto to pull the engine-scaled values! <---
                Fix32 dailyFC = Fix32.FromFloat(crop.GetConsumedFertilityPerDay(farmProto).ToFloat());
                Fix32 dailyWater = Fix32.FromFloat(crop.GetConsumedWaterPerDay(farmProto).Value.ToFloat());

                if (crop == previousCrop)
                {
                    dailyFC += dailyFC * FarmConstants.FERTILITY_PENALTY_FOR_SAME_CROP;
                }

                totalFertilityConsumed += dailyFC * Fix32.FromInt(crop.DaysToGrow);
                totalWaterConsumed += dailyWater * globalWaterMultiplier * Fix32.FromInt(crop.DaysToGrow);

                previousCrop = crop;
            }

            // --- STEP 2: Averages & Natural Equilibrium ---
            Fix32 avgFCPerDay = totalFertilityConsumed / Fix32.FromInt(totalRotationDays);
            result.MonthlyWaterConsumption = (totalWaterConsumed * Fix32.FromInt(FarmConstants.DAYS_PER_MONTH)) / Fix32.FromInt(totalRotationDays);

            Fix32 naturalEquilibrium = Fix32.One - (avgFCPerDay / FarmConstants.NATURAL_REPLENISH_PER_DAY);
            if (naturalEquilibrium < Fix32.Zero) naturalEquilibrium = Fix32.Zero;

            // --- STEP 3: Actual Operating Fertility & Fertilizer Costs ---
            Fix32 actualOperatingFertility;

            if (fertTier == FertilizerTier.None)
            {
                actualOperatingFertility = naturalEquilibrium;
                result.MonthlyFertilizerNeeded = Fix32.Zero;

                // Optional: If you added this to your output class for the UI log
                // result.MonthlyFertilizerPercentageDeficit = Fix32.Zero; 
            }
            else
            {
                Fix32 maxCap = FarmConstants.GetMaxFertilityCap(fertTier);
                actualOperatingFertility = requestedTargetFertility.Clamp(Fix32.Zero, maxCap).Max(naturalEquilibrium);

                if (actualOperatingFertility <= naturalEquilibrium)
                {
                    result.MonthlyFertilizerNeeded = Fix32.Zero;
                }
                else
                {
                    Fix32 naturalReplenishAtTarget = (Fix32.One - actualOperatingFertility) * FarmConstants.NATURAL_REPLENISH_PER_DAY;
                    Fix32 adjustedFCPerDay = avgFCPerDay;

                    if (actualOperatingFertility > Fix32.One)
                    {
                        adjustedFCPerDay += adjustedFCPerDay * (actualOperatingFertility - Fix32.One) * FarmConstants.CROP_FERTILITY_DEMAND_MULT_WHEN_ABOVE_100;
                        naturalReplenishAtTarget *= FarmConstants.FERTILITY_REPLENISH_MULT_WHEN_ABOVE_100;
                    }

                    Fix32 fertilityNeededPerDay = adjustedFCPerDay - naturalReplenishAtTarget;
                    if (fertilityNeededPerDay < Fix32.Zero) fertilityNeededPerDay = Fix32.Zero;

                    Fix32 fertilityNeededPerMonth = fertilityNeededPerDay * Fix32.FromInt(FarmConstants.DAYS_PER_MONTH);

                    // Division by physical item value (e.g. 2.5%) remains intact
                    result.MonthlyFertilizerNeeded = fertilityNeededPerMonth / FarmConstants.GetFertilityPerUnit(fertTier);

                    // Optional: If you added this to your output class for the UI log
                    // result.MonthlyFertilizerPercentageDeficit = fertilityNeededPerMonth;
                }
            }

            result.SettledOperatingFertility = actualOperatingFertility;

            // --- STEP 4: Calculate Output Yield ---
            foreach (var crop in schedule.Distinct())
            {
                // Engine base extraction
                ProductQuantity engineBaseYield = crop.GetProductProduced(farmProto);
                Fix32 baseQuantity = Fix32.FromFloat(engineBaseYield.Quantity.Value);

                int daysThisCropGrows = schedule.Where(c => c == crop).Sum(c => c.DaysToGrow);
                Fix32 rotationTimeShare = Fix32.FromInt(daysThisCropGrows) / Fix32.FromInt(totalRotationDays);

                Fix32 theoreticalYearly = baseQuantity
                    * Fix32.FromInt(FarmConstants.DAYS_PER_YEAR) / Fix32.FromInt(crop.DaysToGrow)
                    * rotationTimeShare
                    * actualOperatingFertility
                    * globalYieldMultiplier;

                result.MonthlyYields[crop.ProductProduced.Product] = theoreticalYearly / Fix32.FromInt(12);
            }

            return result;
        }
    }
}