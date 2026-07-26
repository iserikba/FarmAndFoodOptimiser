using Iserik.FaFOptimiser.Math;
using Iserik.FaFOptimiser.Persistence;
using Iserik.FaFOptimiser.Solver;
using Mafi;
using Mafi.Base;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Products;
using Mafi.Core.PropertiesDb;
using Mafi.Core.Prototypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // Required for the Byproduct string.Join
using System.Threading.Tasks;

namespace Iserik.FaFOptimiser.Services
{
    /// <summary>
    /// Parses string inputs from the UI and executes the corresponding logic.
    /// </summary>
    public class CommandProcessor
    {
        // Create the event (The Megaphone)
        public event System.Action<OptimizationResult> OnOptimizationFinished;

        // --- NEW: Event to send QA Chains to the UI ---
        //public event System.Action<string, List<ResolvedChain>> OnChainTestFinished;

        private readonly OptimiserLog m_logger;
        private readonly FarmTelemetryService m_farmService;
        private readonly SettlementTelemetryService m_settlementService;
        private readonly ProtosDb m_protosDb;
        private readonly IPropertiesDb m_propertiesDb;

        // --- NEW: Inject the Chain Service ---
        private readonly ProductionChainService m_chainService;

        public CommandProcessor(
            OptimiserLog logger,
            FarmTelemetryService farmService,
            SettlementTelemetryService settlementService,
            ProtosDb protosDb,
            IPropertiesDb propertiesDb,
            ProductionChainService chainService) // <--- Added here
        {
            this.m_logger = logger;
            this.m_farmService = farmService;
            this.m_settlementService = settlementService;
            this.m_protosDb = protosDb;
            this.m_propertiesDb = propertiesDb;
            this.m_chainService = chainService; // <--- Assigned here
        }

        public void Execute(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            string[] parts = input.Trim().ToLower().Split(' ');
            string command = parts[0];

            switch (command)
            {
                case "clear":
                    this.m_logger.Clear();
                    break;
                case "farms":
                    this.executeFarmsCommand();
                    break;
                case "demand":
                    this.m_logger.AddMessage("Analyzing unified settlement demand...");
                    this.m_settlementService.PrintDemandToLog(this.m_logger);
                    break;
                case "farmtest":
                    this.executeFarmTestCommand(parts);
                    break;
                case "testthread":
                    this.executeTestThreadCommand();
                    break;
                case "recipes":
                    this.TestGetRecipes();
                    break;
                // --- NEW QA COMMANDS ---
                //case "meat":
                //case "bread":
                //case "gas":
                //    this.executeQaChainTestCommand(command, parts);
                //    break;
                default:
                    this.m_logger.AddMessage($"Unknown command: '{command}'. Try 'farms', 'testthread', 'meat [N]', 'bread [N]', or 'clear'.");
                    break;
            }
        }

        /*
        // ==========================================
        // NEW QA COMMAND EXECUTION
        // ==========================================
        private void executeQaChainTestCommand(string command, string[] parts)
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out int amount))
            {
                this.m_logger.AddMessage($"Syntax error. Use: {command} [amount]");
                return;
            }

            Proto.ID targetId = command == "bread" ? Ids.Products.Bread :
                (command == "meat" ? Ids.Products.Meat : Ids.Products.FuelGas);

            if (this.m_protosDb.TryGetProto(targetId, out ProductProto product))
            {
                string title = $"{product.Strings.Name.TranslatedString} x{amount}";
                this.m_logger.AddMessage($"=== QA CHAIN TEST: {title} ===");

                // Run the recursive resolution
                var chains = this.m_chainService.GetAlternativeChains(product, Fix32.FromInt(amount));

                if (chains.Count == 0)
                {
                    this.m_logger.AddMessage("Result: No valid production chains found.");
                    return;
                }

                this.m_logger.AddMessage($"Found {chains.Count} valid chains. Rendering in UI...");

                // --- NEW: Broadcast the results to the UI! ---
                OnChainTestFinished?.Invoke(title, chains);
            }
            else
            {
                this.m_logger.AddMessage($"Error: Could not locate product prototype for {command}.");
            }
        }
        */

        // Add 'int flexibleTier' and 'Action onComplete = null' to the signature
        public async void RunOptimizationAsync(OptimizationRequest request, int flexibleTier, Action onComplete = null)
        {
            this.m_logger.AddMessage("MAIN THREAD: Fetching live game multipliers...");

            // 1. Get live recipes and multipliers
            Catalog.RecipeCatalog catalog = new Catalog.RecipeCatalog(this.m_protosDb, this.m_propertiesDb);
            catalog.RefreshGlobalMultipliers();
            this.m_logger.AddMessage($"Global Settings Yield x{catalog.GlobalCropMultiplier}, Water x {catalog.GlobalWaterMultiplier}, Fertilizer x {catalog.GlobalFertilizerMultiplier}");

            List<CropRecipe> allRecipes = catalog.GetCropRecipes();

            // 2. Setup Runner
            Solver.OptimizationJobRunner runner = new Solver.OptimizationJobRunner();
            ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();

            // 3. Bind the mailbox
            runner.OnLogMessage += (msg) => logQueue.Enqueue(msg);

            this.m_logger.AddMessage("MAIN THREAD: Dispatching background job...");

            // 4. Start Background Job with the flexible tier parameter
            runner.StartJob(request, allRecipes, flexibleTier);

            // 5. Polling Intercept (Non-blocking wait for Unity)
            while (!runner.IsFinished)
            {
                await Task.Delay(100);
                while (logQueue.TryDequeue(out string queuedMsg))
                {
                    this.m_logger.AddMessage(queuedMsg);
                }
            }

            // 6. Output Result & Broadcast to UI
            if (runner.Result != null)
            {
                this.m_logger.AddMessage("MAIN THREAD: Job completed successfully!");
                runner.Result.PrintHere(this.m_logger.AddMessage);

                OnOptimizationFinished?.Invoke(runner.Result);
            }
            else
            {
                this.m_logger.AddMessage("MAIN THREAD: Job finished but Result was null (Timeout or Error).");
            }

            // 7. Tell the UI to unlock the button
            onComplete?.Invoke();
        }

        public void TestGetRecipes()
        {
            this.m_logger.AddMessage("Read recipes data engine data...");
            Catalog.RecipeCatalog catalog = new Catalog.RecipeCatalog(this.m_protosDb, this.m_propertiesDb);
            catalog.RefreshGlobalMultipliers();
            this.m_logger.AddMessage($"Global Settings Yield x{catalog.GlobalCropMultiplier}, Water x {catalog.GlobalWaterMultiplier}, Fertilizer x {catalog.GlobalFertilizerMultiplier}");
            List<CropRecipe> allRecipes = catalog.GetCropRecipes();

            // Dump procedure
            this.m_logger.AddMessage("--- RECIPE DUMP ---");
            this.m_logger.AddMessage("Name, Farm Tier, Days to grow, Water, Fertility, Yield"); // Header for easy reading/CSV export

            foreach (CropRecipe recipe in allRecipes)
            {
                // 1 month = 30 days in CoI mechanics
                int daysToGrow = recipe.Months * 30;

                // Outputting the exact requested format
                this.m_logger.AddMessage($"{recipe.Name}, {recipe.FarmTier}, {daysToGrow}, {recipe.Water:F1}, {recipe.Fertility:F1}, {recipe.Production:F2}");
            }
            this.m_logger.AddMessage("--- END OF DUMP ---");
        }

        // --- NEW: Async Multithreading Test ---
        private async void executeTestThreadCommand()
        {
            this.m_logger.AddMessage("Setting up multithread test with REAL engine data...");

            // Assuming 'this.m_propertiesDb' or 'this.m_context.PropertiesDb' is available 
            // in your command processor. Pass it as the second argument:
            Catalog.RecipeCatalog catalog = new Catalog.RecipeCatalog(this.m_protosDb, this.m_propertiesDb);

            catalog.RefreshGlobalMultipliers();
            this.m_logger.AddMessage($"Global Settings Yield x{catalog.GlobalCropMultiplier}, Water x {catalog.GlobalWaterMultiplier}, Fertilizer x {catalog.GlobalFertilizerMultiplier}");
            List<CropRecipe> allRecipes = catalog.GetCropRecipes();

            // 2. Setup Runner Request using the new dynamic multipliers
            OptimizationRequest request = new OptimizationRequest
            {
                MaxFarms = new int[] { 0, 0, 2, 0, 0 }, // Indices 1-4
                MaxRotations = 3,
                TargetFertility = 140,
                Demands = new List<CropDemand>
                {
                    new CropDemand { Name = "Product_Corn", Target = 18 },
                    new CropDemand { Name = "Product_Wheat", Target = 6 },
                    new CropDemand { Name = "Product_Potato", Target = 13 },
                    new CropDemand { Name = "Product_Vegetables", Target = 27 },
                    new CropDemand { Name = "Product_Fruit", Target = 20 },
                    new CropDemand { Name = "Product_Soybean", Target = 6 },
                    new CropDemand { Name = "Product_TreeSapling", Target = 5 },
                    new CropDemand { Name = "Product_SugarCane", Target = 16.4 }
                }
            };

            // 2. Setup Runner
            // Ensure you reference Iserik.FaFOptimiser.Solver if OptimizationJobRunner is in that namespace
            Solver.OptimizationJobRunner runner = new Solver.OptimizationJobRunner();

            // 3. Create a thread-safe mailbox for the logs
            ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();

            // 4. The background thread only drops messages into the queue, it DOES NOT touch m_logger
            runner.OnLogMessage += (msg) => logQueue.Enqueue(msg);
            
            this.m_logger.AddMessage("MAIN THREAD: Dispatching job...");

            // 5. Start Background Job
            runner.StartJob(request, allRecipes,3);

            // 6. Polling Intercept (Non-blocking wait for Unity)
            while (!runner.IsFinished)
            {
                // 7. Empty the mailbox and update the UI safely on the main thread
                while (logQueue.TryDequeue(out string queuedMsg))
                {
                    this.m_logger.AddMessage(queuedMsg);
                }
                await Task.Delay(100); // Wait 100ms and check again
            }

            // 7. NEW: The Final Flush! (Catches messages if the solver was lightning fast)
            while (logQueue.TryDequeue(out string queuedMsg))
            {
                this.m_logger.AddMessage(queuedMsg);
            }

            // 8. Output Result
            if (runner.Result != null)
            {
                this.m_logger.AddMessage("MAIN THREAD: Job completed successfully! Intercepted result.");

                // Pass the logger's AddMessage function directly to the result!
                runner.Result.PrintHere(this.m_logger.AddMessage);

                // --- 2. NEW: Broadcast the result to anyone listening! ---
                OnOptimizationFinished?.Invoke(runner.Result);
            }
            else
            {
                this.m_logger.AddMessage("MAIN THREAD: Job finished but Result was null (Timeout or Error).");
            }
        }

        private void executeFarmsCommand()
        {
            this.m_logger.AddMessage("Scanning island for farms...");

            List<Farm> farms = this.m_farmService.GetAllBuiltFarms();

            if (farms.Count == 0)
            {
                this.m_logger.AddMessage("No constructed farms found on the island.");
                return;
            }

            this.m_logger.AddMessage($"Found {farms.Count} active farm(s):");

            foreach (Farm farm in farms)
            {
                // Print the localized name of the farm (e.g., "Irrigated Farm") and its unique entity ID
                this.m_logger.AddMessage($"- {farm.Prototype.Strings.Name.TranslatedString} [ID: {farm.Id.Value}]");
            }
        }


        private void executeFarmTestCommand(string[] parts)
        {
            // Syntax: farmtest Greenhouse 2 1.4
            if (parts.Length < 4)
            {
                this.m_logger.AddMessage("Syntax: farmtest [FarmType] [FertTier: 0-3] [TargetFertility]");
                this.m_logger.AddMessage("Example: farmtest Greenhouse 2 1.4");
                return;
            }

            string farmTypeId = parts[1]; // "FarmIrrigated", "Greenhouse", etc.

            if (!int.TryParse(parts[2], out int fertIndex))
            {
                this.m_logger.AddMessage($"Error: Invalid fertilizer index '{parts[2]}'.");
                return;
            }
            FertilizerTier selectedFertTier = (FertilizerTier)fertIndex;

            if (!float.TryParse(parts[3], out float parsedFertility))
            {
                this.m_logger.AddMessage($"Error: Invalid fertility target '{parts[3]}'.");
                return;
            }
            Fix32 targetFertility = Fix32.FromFloat(parsedFertility);

            this.FarmRunTest(this.m_protosDb, this.m_logger, farmTypeId, selectedFertTier, targetFertility);
        }

        public void FarmRunTest(ProtosDb protosDb, OptimiserLog logger, string farmTypeId, FertilizerTier fertTier, Fix32 userSliderTarget)
        {
            logger.AddMessage("--- STARTING NATIVE MATH SIMULATOR TEST ---");

            // 1. Fetch exact Database Protos using LINQ to completely bypass ID namespace errors
            // 1. Fetch exact Database Protos safely without LINQ
            CropProto potato = protosDb.GetOrThrow<CropProto>(Ids.Crops.Potato);
            CropProto wheat = protosDb.GetOrThrow<CropProto>(Ids.Crops.Wheat);

            FarmProto farmProto = null;
            foreach (FarmProto proto in protosDb.All<FarmProto>())
            {
                if (proto.Id.Value.ToLower() == farmTypeId.ToLower())
                {
                    farmProto = proto;
                    break;
                }
            }

            if (farmProto == null)
            {
                logger.AddMessage($"Error: Could not find Farm ID '{farmTypeId}'. Try 'Greenhouse' or 'FarmIrrigated'.");
                return;
            }

            // 2. Replicate UI Screenshot Schedule
            var schedule = new List<CropProto> { potato, wheat };

            // 3. CALL THE SIMULATOR (Now passing farmProto!)
            FarmSimulationOutput result = FarmMathSimulator.SimulateTheoreticalFarm(
                farmProto: farmProto, // <-- Passed here to fix the 58 -> 73 yield issue
                schedule: schedule,
                fertTier: fertTier,
                requestedTargetFertility: userSliderTarget,
                globalYieldMultiplier: Fix32.One, // Edicts off
                globalWaterMultiplier: Fix32.One  // Edicts off
            );

            // 4. PRINT RESULTS
            logger.AddMessage($"Configuration: {farmProto.Id.Value}, {fertTier} Fertilizer");
            logger.AddMessage($"Operating Fertility: {(result.SettledOperatingFertility * Fix32.FromInt(100)).ToStringRounded(1)}%");
            logger.AddMessage($"--------------------------------------------------");

            logger.AddMessage("--- Raw Engine Base Recipes ---");
            foreach (var kvp in result.BaseRecipes)
            {
                ProductProto crop = kvp.Key;
                Fix32 baseYield = kvp.Value;
                logger.AddMessage($"- {crop.Strings.Name.TranslatedString}: {baseYield.ToStringRounded(2)} raw units");
            }
            logger.AddMessage($"--------------------------------------------------");

            logger.AddMessage($"Monthly Water Needed: {result.MonthlyWaterConsumption.ToStringRounded(1)}");

            // Exposed the raw percentage to prove it matches the UI's "Needed: 23.6%"
            logger.AddMessage($"Fertilizer Percentage Deficit: {(result.MonthlyFertilizerPercentageDeficit * Fix32.FromInt(100)).ToStringRounded(1)}%");
            logger.AddMessage($"Fertilizer Items Needed: {result.MonthlyFertilizerNeeded.ToStringRounded(2)} items");

            logger.AddMessage("--- Projected Monthly Crop Yields ---");
            foreach (var kvp in result.MonthlyYields)
            {
                ProductProto crop = kvp.Key;
                Fix32 yield = kvp.Value;
                logger.AddMessage($"- {crop.Strings.Name.TranslatedString}: {yield.ToStringRounded(1)} / month");
            }
        }
    }
}
