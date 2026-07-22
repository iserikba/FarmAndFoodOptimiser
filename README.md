# Project Blueprint: Advanced Farm & Food Optimizer Mod
*Captain of Industry - Development Overview & Architecture*

## 1. Project Overview
Building upon the architecture of the Production Calculator, this new mod tackles the most complex, multi-variable optimization problem in *Captain of Industry*: agricultural balancing and food supply. Because the game strictly limits mods to a single compiled `.dll` file, this project will include a custom, lightweight C# mathematical solver built from scratch, ensuring zero external dependencies while delivering enterprise-grade linear optimization.

## 2. The Problems (Current Gameplay Pain Points)
The vanilla agricultural system is opaque and intertwined, leading to several major issues for players:
1. **Dynamic & Variable Demand:** City food demand constantly shifts based on population growth and food variety edicts. Introducing a new food type directly decreases the demand for existing food types.
2. **Tedious Manual Conversions:** Translating a target "Food per Month" quota into a tangible "Raw Crop Production" target requires constant manual spreadsheet math.
3. **Complex Aggregation:** Managing and summing crop production across multiple farms with different tiers and growth cycles is annoying and time-consuming.
4. **The "Butterfly Effect" of Fertility:** Adjusting the production rate of one crop to balance fertility or water inherently changes the production of other crops in the rotation. It is incredibly difficult to achieve a precise crop balance without recalculating the entire farm network.
* **The Result:** Players are forced to blindly oversupply their cities to prevent starvation, resulting in massive wastes of water, fertilizer, and infrastructure.

## 3. The Solutions (Mod Features)
This mod will transform guesswork into precise infrastructure management:
1. **Telemetry Engine (Demand Mapping):** Automatically reads current citizen count, active food edicts, and substitution weights to calculate the exact monthly quota needed for every raw crop type.
2. **Automated Translation:** Instantly converts citizen food consumption rates into required crop yields.
3. **Multi-Factor Optimization Math (The Core):** A custom-built C# solver that calculates the optimal crop distribution and rotation across all available farms, perfectly balancing yield targets against fertility drain and water consumption.
4. **Infrastructure Monitor & Alarms:** Actively monitors current farm capabilities against population demand. It will throw UI warnings and alarms if the current configuration cannot mathematically meet demand, advising the player to upgrade to Tier II/III or build new farms *before* starvation hits.

## 4. Development Roadmap (Step-by-Step)
To successfully build this mod and its custom solver, development will be split into the following sequential phases:

### Phase 1: Data Extraction & Telemetry
* Hook into the game's population manager to read citizen counts.
* Extract food consumption rates, variety substitution weights, and active edicts.
* Build a real-time "Target Quota" generator for raw crops.

### Phase 2: The Custom Math Engine (No External DLLs)
* Design a lightweight Linear Programming (Simplex method) algorithm natively in C#.
* Define the constraints: Target Quotas (Minimum Yield), Fertility (Max Drain), and Water (Max Pipe Throughput).
* Ensure the math engine utilizes the game's native `Fix32` deterministic math to prevent calculation desyncs.

### Phase 3: The Virtual Farm Matrix
* Catalog all user-built farms, noting their Tier (I, II, Greenhouse) and current upgrades.
* Map the decompiled `CropProto` variables (DaysToGrow, ConsumedFertility, YieldMultiplier) into the solver's constraint matrix.

### Phase 4: Optimization Logic & Routing
* Connect the Telemetry Engine's quotas to the Math Engine.
* Write the logic that assigns specific crops to specific farms (e.g., forcing high-yield crops into Greenhouses and balancing high-fertility drain crops with resting periods or fertilizer inputs).

### Phase 5: UI Integration & Alert System
* Build a user-friendly, game-independent interface window to display the optimal setup.
* Implement the warning system to flag deficits (e.g., "Warning: -12% Potato Deficit. Upgrade 1 Farm to Irrigated").
* Integrate the custom JSON save/load system so users can save their optimized agricultural configurations.

## 5. Architectural Decision Record (ADR): Mod Interface & Integration

### Option A: Building Entity ("Food & Agriculture Office")

**Pros:**

* **Granular Scope Control:** Players can assign specific farms and settlements to specific buildings, allowing for localized calculations rather than island-wide averages.
* **Late-Game Scalability:** Ideal for massive infrastructure spanning hundreds of years, where citizens are distributed across multiple distinct cities with independent supply chains.
* **Cluster Management:** Easily handles specialized farm clusters (e.g., separating biofuel crop loops from human food loops).
* **True Automation ("Set-and-Forget"):** A physical building entity could eventually be programmed to automatically adjust crop rotations on the fly to perfectly match fluctuating city demand.

**Cons:**

* **Extended Development Cycle:** Requires significantly longer production time, including Unity 3D modeling, material mapping, and engine-level UI integration.
* **Architectural Complexity:** High risk of serialization bugs and complex data routing between the entity and the settlement managers.
* **UX Friction:** Overly complex for players who simply want a lightweight, on-demand calculator tool.

### Option B: Window-Only UI (Global Manager Overlay)

**Pros:**

* **Rapid Prototyping:** Faster development cycle, allowing immediate focus on the core Linear Programming math.
* **Frictionless UX:** Provides instant, on-demand telemetry without requiring players to spend construction materials, space, or computing power.
* **Clean Architecture:** Global dependency injection reduces the risk of save-state desyncs.

**Cons:**

* **Global Scope Only:** Less flexible for highly segmented, multi-city islands.
* **Manual Execution:** Remains an advisory tool rather than an automated gameplay mechanic.

### ➔ Current Decision: Proceed with Option B for Version 0 (v0.1)

**Rationale:** To ensure the custom math engine and telemetry hooks are stable, the initial release (Version Zero) will utilize the **Window-Only** architecture. This establishes the mod as a powerful, immediate calculator. Once the core solver is proven reliable, development can pivot toward the "Building Entity" model for future major releases to introduce localized cluster management and "set-and-forget" automation.

This is the true "endgame" logic of *Captain of Industry* optimization. You have correctly identified that food demand isn't just a flat number—it's a massive, interwoven dependency graph with alternate routes, byproducts, and external demands.

Trying to build a single "magic bullet" solver that figures out *everything* perfectly on its own is nearly impossible (and usually results in terrible user experiences where the mod does something the player didn't want, like crushing all their potatoes into animal feed when they wanted them for eating).

To make this user-friendly, we need to split the problem into three distinct phases: **The Graph**, **The Solver**, and **The UI**.

Here is how we architect this so it is both mathematically robust and actually enjoyable for the user.

---

### Phase 1: The Production Graph (Data Normalization)

Before the math solver can do anything, it needs a clean map of how things are made. We shouldn't hardcode "Wheat -> Bread". Instead, we must read the game's internal `RecipeProto` database.

**The Approach:**
We build a `ProductionGraphService`. This service crawls the game's recipe database and builds a tree for every demanded food item.

* **Direct (Potatoes):** The tree stops immediately.
* **Simple (Bread):** The tree shows Bread requires Flour, Flour requires Wheat.
* **Complex (Snacks):** The tree shows Snacks require Cooking Oil, Salt, and *either* Corn or Potatoes.

**How it helps the user:** The mod dynamically knows every possible way to make a product based on the specific version of the game (or other mods) they have installed.

### Phase 2: The Math Solver (Linear Programming)

You cannot solve dependency graphs with alternate recipes (like Snacks) using simple algebra. You need a **Simplex Solver** (Linear Programming).

**The Approach:**
We define constraints based on the `FoodDemandMetrics` we already built, plus any manual overrides the user provides.

* *Constraint 1:* We must produce 500 Snacks/mo.
* *Constraint 2:* We must produce 200 Bread/mo.

The solver then tries to find the most efficient way to fulfill those constraints, navigating the alternate recipes based on a "cost" function (e.g., "minimize total farm tiles used").

### Phase 3: The User Interface (The Friendly Part)

This is where you solve the "How do we collect this from the user?" problem. If we just dump a giant equation on them, they will uninstall the mod. We need a clean, step-by-step UI flow.

Here is the proposed UI flow for your F8 window:

#### Step 1: "The Baseline"

The window opens and shows the current metrics (the `FoodDemandMetrics` we just built).

* *UI:* A clean table showing Population, current Food Stock, and calculated Monthly Demand.

#### Step 2: "The Overrides" (Handling External Needs)

This is how we handle your point about *"the user may want to use a crop for something else."*

* *UI:* We provide a simple input field next to the raw crops (Corn, Wheat, etc.) labeled: **"Extra Monthly Target"**.
* *User Action:* If the player knows they need 100 extra Corn a month to make Ethanol, they type `100` into the Corn row.
* *Result:* The solver just adds 100 to the final Corn requirement before doing the math.

#### Step 3: "The Recipe Preferences" (Handling Alternates)

This is how we handle your point about *"Snacks can be made with Corn OR Potatoes."*

* *UI:* If the graph detects multiple recipes for an item, we show a simple dropdown or slider.
* *Example:* For Snacks, we show a dropdown: `[Prefer Corn Recipe | Prefer Potato Recipe | Let Solver Decide]`.
* *Result:* If they pick Corn, the solver disables the Potato-Snack recipe before running the math.

#### Step 4: "The Solution"

The user clicks "Calculate Optimization." The Simplex solver runs in the background (which takes less than a millisecond).

* *UI:* The window displays the final answer in terms of **Farms Needed**.
* *Example Output:* "To meet demand + your overrides, you need: 3 Irrigated Farms growing Wheat, 2 Basic Farms growing Potatoes, 1 Chicken Coop."

---

### The Immediate Next Step

To make this a reality, we must start with **Phase 1: The Production Graph**. If the mod doesn't know *how* Bread is made, the solver can't do the math, and the UI has nothing to show.

---
15-Jul 
---

Here is a project log update drafted for tomorrow, summarizing all the massive architectural wins and technical hurdles we overcame.

---

#### **Executive Summary**

A massive leap forward in both UI architecture and backend accuracy. The monolithic 550+ line main window script was successfully refactored into modular, enterprise-grade panels. The mod now accurately hooks into live game engine data to dynamically calculate highly elastic food demands across multi-settlement maps. The foundation is now completely stable and scalable.

#### **Key Accomplishments**

* **Modular UI Refactor:** Completely dismantled the legacy `OptimiserMainWindow`. Separated logic into isolated, injectable classes: `LogPanel`, `ResultPanel`, and `OptimizerInputPanel`.
* **Dependency Injection Mastery:** Successfully wired up the game engine’s DI container. Custom services (`DemandStateManager`, `SettlementTelemetryService`, `ProductionChainService`) are now properly registered and injected without causing UI panics.
* **Cracked the "Captain of Industry" Elastic Math:** * Replaced hardcoded food assumptions with a dynamic, live-data hybrid approach.
* Discovered `FoodProto` acts as the bridge between `ProductProto` and `FoodCategoryProto`.
* Extracted the true base demand using the engine's internal `GetConsumedQuantityFromPopDays()` formula, bypassing the need for Reflection on private fields.
* Hooked directly into `SettlementsManager` for live population tracking and `IPropertiesDb` to respect active Edict consumption multipliers.


* **Interactive Chain Routing:** Built a floating `ChainSelectionWindow`. Players can now click the ⚙️ gear icon to view alternative production chains (e.g., swapping Corn for Wheat) and override the solver's default behavior.
* **State Preservation:** Upgraded the `RecalculateTheoreticalDemand` engine to remember a player's selected chain overrides even when dynamic population shifts cause the required quantities to scale up or down.

#### **Technical Challenges Overcome**

* **Mafi UI Toolkit Limitations:** Discovered that standard Unity UI methods like `.Disable()` or `.SetInteractable()` are not exposed in Mafi's custom `ButtonText` wrapper. Engineered a clean workaround by dynamically rendering a dimmed `Label` ("✓ ACTIVE CHAIN") instead of a button for currently active routes.
* **Window Management Architecture:** Navigated undocumented engine quirks regarding window spawning (`IWindowView` vs `UiController` vs `UnityInputManager`). Ultimately resolved by utilizing the built-in `WindowManager` inherited directly from the base `Window` class to smoothly spawn floating popups.

#### **Next Steps (Upcoming Sprint)**

1. **Hover Tooltips:** Wire up the `ChainInfoPanel` to render the beautiful flowchart visuals dynamically when a player hovers over the gear button or production items.
2. **Stress Testing:** Run multi-settlement simulations to verify the global multiplier math behaves correctly when new edicts are toggled.
3. **Final Polish:** Clean up any remaining debug logging and prepare for the v1.0 release build.



