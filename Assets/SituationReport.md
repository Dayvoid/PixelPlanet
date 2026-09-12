# Situation Report

**Date:** 2026-09-12  
**Project:** GeneSys / PixelPlanet living planetoid simulation

## Project overview

GeneSys is a Unity-based, GPU-driven planetoid sandbox that aims to couple geology, hydrology, weather, fire/storm dynamics, and multiple biological layers so planetary conditions can evolve in ways that support emergent ecosystems and eventually richer life simulation. The current codebase already contains a substantial real-time simulation loop and broad subsystem coverage, but several parts are still in active iteration and some plan documents now lag behind what has been implemented.

## High-level architecture and system interaction

- **Runtime orchestration:** `Assets/GeneSys/Runtime/Simulation/SimulationHost.cs` boots simulation resources and delegates tick execution to `GpuPassScheduler`.
- **Simulation loop / pass ordering:** `Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs` runs compute passes in a staged order (material/thermal/phase/geology, atmospheric loop, hydrology/hydrostatic, then biology).
- **State model:** `Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs` maintains ping-ponged textures and buffers for materials, state, flow, aux fields, ecology, combustion, storm, life/fauna/grass/tree/wasp, climate, and mobile mass.
- **Configuration surface:** `Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs` centralizes a large parameter space for worldgen, weather, climate, transport, ecology, and rendering.
- **Persistence/versioning:** `Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs` snapshots and migrates state up to **version 14**, including mobile-mass fields.

At a system level, weather/hydrology provide the water-energy backbone; geology and MaCE transport shape terrain/material distribution; combustion and storm convert local state into heat/pressure/electrical impulses; biology consumes and modifies local resources; climate and MaCE layers influence slower-scale redistribution and forcing.

## Major systems status (with evidence)

| System | Status | Evidence | Notes |
| --- | --- | --- | --- |
| Core GPU simulation pipeline and pass scheduler | **Mature** | `SimulationHost.cs`, `GpuPassScheduler.cs`, `SimulationResources.cs` | Established end-to-end tick loop with explicit pass sequencing and dedicated resources for major domains. |
| Polar-grid world generation and material seeding | **In progress** | `GpuPassScheduler.GenerateWorld`, `WorldGenIntegrationTests.cs` | V2 worldgen tests cover metal/limestone/clay/ice/ocean behavior and determinism; still actively tuned through config/commits. |
| Hydrology + water mass contract | **Mature** | `Assets/WaterPlan.MD`, `HydrologyIntegrationTests.cs`, `WeatherIntegrationTests.cs` | Strong conservation posture and extensive fixtures around evaporation/condensation/precipitation/infiltration/hydrostatic behavior. |
| Atmospheric/weather dynamics | **Mature** | `Weather.compute` usage in `GpuPassScheduler.cs`, large `WeatherIntegrationTests.cs` suite | Includes buoyancy, lapse, advection, pressure diffusion, precipitation behavior, seam wrapping, and conservation checks. |
| Geology (core/magma/eruption/ash coupling) | **In progress** | `Geology.compute` scheduling in `GpuPassScheduler.cs`, `GeologyIntegrationTests.cs`, recent magma-related commits | Integrated in loop but still receiving milestone-level fixes and tuning. |
| Combustion and storm/lightning | **In progress** | `Combustion.compute`, `Storm.compute`, `CombustionIntegrationTests.cs`, `StormIntegrationTests.cs` | Dedicated fields and pass chain exist; behavior appears operational but still in iterative balancing. |
| Biology (mycology, flora, fauna/crickets, grass, trees, wasps) | **In progress** | Dedicated compute passes + broad PlayMode coverage (`MycologyIntegrationTests`, `FloraIntegrationTests`, `FaunaIntegrationTests`, `GrassIntegrationTests`, `TreeIntegrationTests`, `WaspIntegrationTests`) | Multiple trophic layers are implemented; evolutionary and ecosystem-level goals are broader than current scope. |
| Climate coarse layer | **In progress** | `Climate.compute`, climate state buffers in `SimulationResources.cs`, `DispatchClimate` in `GpuPassScheduler.cs`, `ClimateIntegrationTests.cs` | **Doc drift:** `Assets/ClimatePlan.MD` says “design — not implemented,” but code/tests indicate at least a working initial implementation. |
| MaCE mobile-mass transport | **In progress** | `MaceTransport.compute`, MaCE flags/config in `SimulationConfig.cs`, `MaceSedimentIntegrationTests.cs`, snapshot v14 support | **Doc drift:** MaCE docs are phrased as design, but sediment/mobile-mass paths, tests, and persistence are present. |
| Snapshot persistence/migration | **Mature** | `WorldSnapshotService.cs` | Supports multiple versions, migrations (including legacy vapor/mobile mass handling), and broad subsystem serialization. |
| AI crew / LLM-driven tooling in simulation loop | **In progress** | `Assets/GeneSys/Runtime/AI/*`, `AiCrewTests.cs`, commits on 2026-09-10/11 | Functional scaffolding exists (settings/tool registry/prompt queue/client), but this is adjunct and still rapidly evolving. |

## Current simulation capabilities vs missing pieces for realism/life-seeding readiness

### Capabilities currently present

- Procedural planetoid generation with layered geology and seeded basins/oceans/ice/deposits.
- Multi-pass atmospheric + hydrology loop with explicit water-accounting conventions.
- Groundwater and hydrostatic leveling mechanics beyond simple falling-fluid behavior.
- Coupled fire and storm systems that interact with heat/pressure/water pathways.
- Multiple biological subsystems (fungal, plant, insect/pollinator/predator-like roles) integrated into the same world state.
- Climate and mobile-mass (MaCE) infrastructure sufficiently implemented to run tests and persist state.

### Missing or incomplete for “realistic” planetary conditions and robust life-seeding

- **Long-horizon planetary realism calibration** is incomplete (no evidence of long-duration climate stability benchmarking beyond focused fixtures).
- **Evolutionary/speciation goals** in `Assets/OUTLINE.MD` (fitness hemispheres, richer brains, broader organism ladder) are not yet realized as full systems.
- **Plan/document coherence** is behind implementation for climate and MaCE, creating onboarding and decision-tracking risk.
- **Mass/energy invariants outside water** are less explicit than the water contract (MaCE tests help, but full cross-domain conservation posture is still maturing).
- **Project-level orientation docs** are minimal (no repo README detected), raising ramp-up costs for new contributors.

## Known risks, gaps, and open questions

1. **Documentation drift risk:** climate/MaCE plan docs describe pre-implementation state while code has advanced.
2. **Complex-parameter fragility:** `SimulationConfig.cs` has a very large tuning surface, increasing regression risk from coupled changes.
3. **Unclear production baseline:** many “milestone/revert/reject” commits indicate rapid iteration; stable baseline criteria are not yet explicit.
4. **Validation scope tradeoff:** the project intentionally avoids running full suites for ordinary edits (`Assets/OUTLINE.MD` validation policy), which is practical but can miss cross-system interactions.
5. **Tracker visibility gap:** no open/closed PRs were listed via `gh pr list --state all` at inspection time, and issue listing was not accessible with current permissions; external planning state may be incomplete from local visibility alone.

## Immediate next priorities

### Explicit priorities from existing plans/docs

- Continue climate layer rollout (coarse-to-fine forcing/feedback phases in `Assets/ClimatePlan.MD`).
- Continue MaCE rollout (entrainment, shoreline mixtures, additional channels in `Assets/MaCEPlan.MD` and `Assets/MaCE_SedimentPilot.MD`).
- Advance biological roadmap from baseline flora/fauna toward broader life goals in `Assets/OUTLINE.MD`.

### Inferred priorities from code + commit trajectory

- **(Inferred)** Reconcile plan docs with implemented climate/MaCE code/tests to restore trustworthy planning artifacts.
- **(Inferred)** Define and publish a “simulation baseline” preset + acceptance criteria spanning weather/geology/biology interactions.
- **(Inferred)** Expand cross-system integration tests for multi-day scenarios (climate + hydrology + biology + transport simultaneously).
- **(Inferred)** Add contributor-facing orientation docs (top-level architecture/readme) to reduce context loss.

## Key documents and source paths

### Plan and design documents

- `Assets/OUTLINE.MD`
- `Assets/WaterPlan.MD`
- `Assets/ClimatePlan.MD`
- `Assets/MaCEPlan.MD`
- `Assets/MaCE_SedimentPilot.MD`
- `Assets/GrassPlan.MD`

### Core implementation paths

- `Assets/GeneSys/Runtime/Simulation/SimulationHost.cs`
- `Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs`
- `Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs`
- `Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs`
- `Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs`
- `Assets/GeneSys/Compute/Simulation/*.compute`
- `Assets/GeneSys/Tests/EditMode/*`
- `Assets/GeneSys/Tests/PlayMode/*`
