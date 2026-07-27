# Advanced Farm & Food Optimizer

*A Captain of Industry Mod*

**Developer:** Iserikba (Igor)
**Status:** Released (v0.1.0 Release Candidate / v0.0.5 Beta)
**Core Technologies:** C#, Mixed-Integer Linear Programming (MILP), Unity UI, Asynchronous Task Management, Dependency Injection

Calculate exact crop, farm, and livestock needs for food and industry while optimising crop rotations and production chains without manual maths.

## 📌 Project Overview

The vanilla agricultural system in *Captain of Industry* presents a complex, multi-variable optimization problem. Dynamic population demands, food variety edicts, and the interconnected "butterfly effect" of soil fertility make balancing crop yields manually nearly impossible.

This project completely overhauls that system by introducing a custom, lightweight C# mathematical solver built from scratch. It bypasses the need for external dependencies or spreadsheets by hooking directly into the game's live telemetry to dynamically calculate, map, and optimize the perfect agricultural supply chain.

## 🧠 Algorithmic Architecture & The Math Engine

At the core of the mod is a high-speed Hybrid "Divide & Conquer" Optimization Algorithm, designed to mitigate the factorially expanding combinatorial explosion of complex farm layouts.

* **Parametric Chunk Algorithm:** Evaluates potential search trees and safely routes them based on complexity. If a permutation threatens to exceed 40 million branches, the engine isolates a micro-chunk of the problem, solves it via a `FastChunkSolver` (capped at a depth of 4 farms), and passes the reduced array back to the main `Standard Optimizer` for a sub-second cleanup pass.


* **Combinatorial Pruning:** Shifted the recursive matrix from evaluating positional slots to evaluating grouped pattern quantities, eliminating $N!$ mathematical redundancies. Aggressive single-pass LINQ filters and early cost-cutoff triggers reduced core processing times for deep edge cases from ~1840ms down to 26ms.


* **Diminishing Returns Priority Matrix:** Replaced linear priority scoring with a Square Root function. By utilizing the principle of $\sqrt{a} + \sqrt{b} > \sqrt{a+b}$, the solver is structurally forced to balance overproduction symmetrically across all requested priority crops.


* **Two-Pass Greedy Byproduct Optimizer:** Resolves complex multi-output food chains (e.g., slaughterhouses) without heavy Simplex matrices by isolating a resolution pass (primary chains) and a cascade pass (unrefined byproducts) using a shared `GlobalCreditPool`.



## 🏗️ Software Engineering & OOP Design

The project was heavily refactored to enforce strict Object-Oriented Programming (OOP) principles, ensuring that data moves cleanly between the UI, the game engine, and the mathematical solvers.

* **Model-View-ViewModel (MVVM) Decoupling:** Transient UI states are centralized within a `DemandStateManager` service, fully separating high-speed logic (`FarmOptimiseSolver`) from post-processing and presentation (`OptimizationResult`).


* **Recursive Graph Structures:** Stripped flat string-paths in favor of a recursive `ChainNode` tree structure that perfectly maps the exact mathematical relationships between raw inputs, fractional machine counts, and output products.


* **Asynchronous Threading Bridge:** Heavy combinatorial math runs on background threads via `OptimizationJobRunner`. It utilizes a `ConcurrentQueue`, a 20-second soft cancel, and a 30-second hard kill timeout to ensure the main game thread never freezes while updating the Unity UI.



## ⚙️ Game Engine Integration & UI

The mod behaves as a native extension of the *Captain of Industry* engine, reading live simulation data and utilizing internal rendering components.

* **Live Telemetry & Elastic Math:** Hooks directly into `SettlementsManager` and `IPropertiesDb` to read live population tracking and global difficulty/edict multipliers dynamically, completely future-proofing the math against game updates.


* **Iterative Hardware Estimator:** Calculates the exact industrial baseline using the game's internal `FertEquilibrium` formulas, tier-scaling multipliers, and exact water/fertilizer costs.


* **Native UI Toolkit:** Built with Mafi's native `UiToolkit` (responsive flex-layouts, `ScrollColumn`, `DisplayWithIcon`). Features interactive spinner controls, native product pickers, and visual flowcharts that dynamically render on `.Floater()` tooltips while respecting the engine's rendering context and Z-indexing.
