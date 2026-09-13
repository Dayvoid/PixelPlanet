# Situation Report

**Date:** 2026-09-13  
**Project:** GeneSys / PixelPlanet living planetoid simulation

## Project overview

GeneSys is a Unity-based, GPU-driven planetoid sandbox that couples geology, geodynamics, hydrology, weather, fire/storm dynamics, discrete cellular transport, and multiple biological layers so planetary conditions can evolve to support emergent ecosystems and rich life simulation. The codebase features a robust real-time simulation loop with broad subsystem coverage, having recently completed the architectural transition from continuous fractional flux transport (MaCE) to exact, discrete Affinity-Driven Margolus Cellular Automata (MaCA).

## High-level architecture and system interaction

- **Runtime orchestration:** `Assets/GeneSys/Runtime/Simulation/SimulationHost.cs` boots simulation resources and delegates tick execution to `GpuPassScheduler`.
- **Simulation loop / pass ordering:** `Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs` runs compute passes in a staged order (material/thermal/phase, discrete Margolus CA transport, early/late MaCE when legacy transport is toggled, atmospheric loop, hydrology/hydrostatic, and biology).
- **State model:** `Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs` maintains ping-ponged textures and buffers for materials, state, flow, aux fields, ecology, combustion, storm, life/fauna/grass/tree/wasp, climate, geodynamics, and mobile mass.
- **Configuration surface:** `Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs` centralizes a large parameter space for worldgen, weather, climate, geodynamics, transport, ecology, and rendering.
- **Persistence/versioning:** `Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs` snapshots and migrates state up to **version 15** (including mobile-mass fields in v14 and geodynamics buffers in v15).

At a system level, weather/hydrology provide the water-energy backbone; geology, geodynamics, and discrete Margolus CA (MaCA) transport shape terrain and material redistribution (with legacy continuous MaCE advection retained as a configurable option); combustion and storm convert local state into heat/pressure/electrical impulses; biology consumes and modifies local resources; climate and geodynamics layers drive planetary-scale circulation, tectonic forcing, and thermal release.

## Major systems status (with evidence)

| System | Status | Evidence | Notes |
| --- | --- | --- | --- |
| Core GPU simulation pipeline and pass scheduler | **Mature** | `SimulationHost.cs`, `GpuPassScheduler.cs`, `SimulationResources.cs` | Established end-to-end tick loop with explicit pass sequencing, sub-stepping, and dedicated resources for all major domains. |
| Material transport (MaCA Margolus CA + Legacy MaCE) | **Mature** | `MargolusTransport.compute`, `MargolusCommon.hlsl`, `MargolusContractTests.cs`, `Margolus*.cs` (PlayMode), `MaceTransport.compute` | **Major Architecture Shift:** Successfully transitioned to discrete $2 \times 2$ block partition cellular automata (MaCA). Delivers exact discrete pixel conservation ($N(t) \equiv N(0)$), polar metric aspect ratio compensation ($\gamma(y)$), toroidal seam wrapping, stress detachment, and decoupled continuous mobile mass rendering (`_MaceMobileDisplay`) to eliminate ghost sediment artifacts. Legacy MaCE retained via `useLegacyTransport`. |
| Polar-grid world generation and material seeding | **Mature** | `WorldGeneration.compute`, `GpuPassScheduler.GenerateWorld`, `WorldGenIntegrationTests.cs`, `MargolusWorldGenStabilityTests.cs` | V2 worldgen tuned with pre-relaxed talus aprons, exposed granite cliff faces, continental shelf marine sedimentation, and deterministic seed placement. |
| Hydrology + water mass contract | **Mature** | `Assets/WaterPlan.MD`, `HydrologyIntegrationTests.cs`, `WeatherIntegrationTests.cs` | Strong conservation posture and extensive fixtures around evaporation/condensation/precipitation/infiltration/hydrostatic behavior. |
| Atmospheric/weather dynamics | **Mature** | `Weather.compute` usage in `GpuPassScheduler.cs`, large `WeatherIntegrationTests.cs` suite | Includes buoyancy, lapse, advection, pressure diffusion, precipitation behavior, seam wrapping, and conservation checks. |
| Geology & Geodynamics (core heat, faults, volcanism) | **Mature** | `Geology.compute`, `Geodynamics.compute`, `GeodynamicsContractTests.cs`, `GeologyIntegrationTests.cs`, snapshot v15 | Full angular/radial geodynamics grid modeling tectonic stress, deep mantle heat flow, fault rupture, and hydrothermal venting. |
| Combustion and storm/lightning | **In progress** | `Combustion.compute`, `Storm.compute`, `CombustionIntegrationTests.cs`, `StormIntegrationTests.cs` | Dedicated fields and pass chain exist; behavior operational with iterative balancing ongoing. |
| Biology (mycology, flora, fauna/crickets, grass, trees, wasps) | **In progress** | Dedicated compute passes + broad PlayMode coverage (`MycologyIntegrationTests`, `FloraIntegrationTests`, `FaunaIntegrationTests`, `GrassIntegrationTests`, `TreeIntegrationTests`, `WaspIntegrationTests`) | Multiple trophic layers are implemented; evolutionary and ecosystem-level goals remain open for future expansion. |
| Climate coarse layer | **In progress** | `Climate.compute`, climate state buffers in `SimulationResources.cs`, `DispatchClimate` in `GpuPassScheduler.cs`, `ClimateIntegrationTests.cs` | Coarse planetary thermal/albedo/moisture buffering active in tick cadence; integrates with seasonal insolation. |
| Snapshot persistence/migration | **Mature** | `WorldSnapshotService.cs` | Supports serialization and backward-compatible migration up to version 15 (geodynamics and mobile mass payloads). |
| AI crew / LLM-driven tooling in simulation loop | **In progress** | `Assets/GeneSys/Runtime/AI/*`, `AiCrewTests.cs` | Functional scaffolding exists (settings/tool registry/prompt queue/client) for in-editor inspection and agent interaction. |

## Current simulation capabilities vs missing pieces for realism/life-seeding readiness

### Capabilities currently present

- Exact discrete mass-conserving particle transport (MaCA) via $2 \times 2$ block partition cellular automata with Hamiltonian energy minimization, altitude-invariant physical angle of repose via polar metric compensation, and toroidal seam wrapping.
- Procedural planetoid generation with layered geology, seeded basins/oceans/ice/deposits, and pre-relaxed granular talus aprons.
- Multi-pass atmospheric + hydrology loop with explicit water-accounting conventions.
- Groundwater, hydrothermal release, and hydrostatic leveling mechanics beyond simple falling-fluid behavior.
- Planetary geodynamics layer with radial/angular stress tracking, fault ruptures, and deep mantle thermal plumes.
- Coupled fire and storm systems that interact with heat/pressure/water pathways.
- Multiple biological subsystems (fungal, plant, insect/pollinator/predator-like roles) integrated into the same world state.
- Decoupled continuous mobile mass rendering (`_MaceMobileDisplay`), ensuring no visual ghost artifacts appear when discrete transport is active.

### Missing or incomplete for “realistic” planetary conditions and robust life-seeding

- **Evolutionary/speciation goals** in `Assets/OUTLINE.MD` (fitness hemispheres, richer brains, broader organism ladder) are not yet realized as full systems.
- **Long-horizon planetary realism calibration** is incomplete (long-duration climate and biological equilibrium benchmarking across thousands of simulated days).
- **Plan/document coherence:** `Assets/MaCEPlan.MD` and `Assets/MaCE_SedimentPilot.MD` are now historical reference documents superseded by the discrete MaCA architecture.
- **Orientation documentation:** Contributor-facing orientation docs (top-level README or onboarding guide) remain minimal.

## Known risks, gaps, and open questions

1. **Documentation drift risk:** `MaCEPlan.MD` and `MaCE_SedimentPilot.MD` describe pre-MaCA Euler flux transport; new developers should refer to `Assets/ProjectHistory.md` and `MargolusTransport.compute`.
2. **Complex-parameter fragility:** `SimulationConfig.cs` has a very large tuning surface, though contract tests now explicitly assert clamp boundaries and defaults.
3. **Biological equilibrium balancing:** Interactions between rapid weather shifts, soil erosion/transport, and vegetation root anchoring require ongoing multi-season calibration.

## Immediate next priorities

### Explicit priorities from existing plans/docs

- Advance biological roadmap from baseline flora/fauna toward broader life and speciation goals in `Assets/OUTLINE.MD`.
- Continue climate layer fine-tuning (coarse-to-fine atmospheric coupling and long-period orbital forcing).
- Expand long-horizon automated stability tests across coupled geodynamics + weather + ecosystem regimes.

### Inferred priorities from code + commit trajectory

- Create/update a dedicated architectural design note for MaCA transport to replace or formally supersede `MaCEPlan.MD`.
- Author a top-level repository `README.md` summarizing the engine architecture, pass order, and test execution workflows.
- Calibrate plant root cohesion factors against MaCA slope failure envelopes to promote diverse biome stabilization along cliff faces and river basins.

## Key documents and source paths

### Plan and design documents

- `Assets/OUTLINE.MD`
- `Assets/ProjectHistory.md`
- `Assets/WaterPlan.MD`
- `Assets/ClimatePlan.MD`
- `Assets/GrassPlan.MD`
- `Assets/MaCEPlan.MD` *(Historical / Legacy)*
- `Assets/MaCE_SedimentPilot.MD` *(Historical / Legacy)*

### Core implementation paths

- `Assets/GeneSys/Runtime/Simulation/SimulationHost.cs`
- `Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs`
- `Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs`
- `Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs`
- `Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs`
- `Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs`
- `Assets/GeneSys/Compute/Simulation/MargolusTransport.compute`
- `Assets/GeneSys/Shaders/Simulation/Common/MargolusCommon.hlsl`
- `Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader`
- `Assets/GeneSys/Compute/Simulation/*.compute`
- `Assets/GeneSys/Tests/EditMode/*`
- `Assets/GeneSys/Tests/PlayMode/*`
