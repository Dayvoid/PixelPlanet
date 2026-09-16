# Situation Report

**Date:** 2026-09-15  
**Project:** GeneSys / PixelPlanet living planetoid simulation

## Project overview

GeneSys is a Unity-based, GPU-driven planetoid sandbox that couples geology, geodynamics, hydrology, weather, fire/storm dynamics, discrete cellular transport, and multiple biological layers so planetary conditions can evolve to support emergent ecosystems and rich life simulation. Solid-cell transport is exact discrete Margolus CA (MaCA). Continuous fractional MaCE advection and its mobile-mass ledger have been removed.

## High-level architecture and system interaction

- **Runtime orchestration:** `Assets/GeneSys/Runtime/Simulation/SimulationHost.cs` boots simulation resources and delegates tick execution to `GpuPassScheduler`.
- **Simulation loop / pass ordering:** `Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs` runs compute passes in a staged order: geodynamics/tectonics, core heat, volcanism (slow), thermal/eruption/electrical substeps, PhaseChange, Margolus CA, combustion, climate couple, atmospheric loop (light → forcing → continuity → pressure diffusion → dynamics → ash lift → transport → water cycle → precip), storm, liquid-only Margolus (hydrometeor fall), groundwater, hydrothermal boil, hydrostatic leveling, slow erosion/detritus, then biology.
- **State model:** `Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs` maintains ping-ponged textures and buffers for materials, state, flow, aux fields, ecology, combustion, storm, life/fauna/grass/tree/wasp, climate, and geodynamics. There is no mobile-mass / MaCE buffer.
- **Configuration surface:** `Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs` centralizes worldgen, weather, climate, geodynamics, Margolus transport, ecology, and rendering knobs.
- **Persistence/versioning:** `Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs` snapshots and migrates state up to **version 16** (geodynamics without legacy mobile-mass slices). V14/V15 loads still skip the retired mobile-mass payloads.

At a system level, weather/hydrology own the water-energy ledger; geology and geodynamics own melt creation, tectonic kinematics, and buoyant ash lift; Margolus CA owns gravity/repose settling of movable cells; hydrostatic owns horizontal free-surface water leveling; combustion and storm convert local state into heat/pressure/electrical impulses; biology consumes and modifies local resources; climate injects coarse wind/albedo/bucket envelopes into the fine stack.

### Cell-motion ownership

| Process | Owner | Does not do |
| --- | --- | --- |
| Gravity / repose settling of movable IDs | Margolus CA | Horizontal Water leveling; cricket/egg occupancy |
| Cricket / egg ballistic hops, unsupported falls, and landing | `Fauna.compute` | Generic material settling |
| Identity change (stress / karst / collapse → Sediment) | `ErosionAndCollapse` | Relocate cells |
| Buoyant ash lift | `AshTransport` | Downward ash settle |
| Pressure-driven magma eruption | `EruptionMotion` | Magma fall / lateral spread |
| Horizontal free-surface water leveling | Hydrostatic column solver | Vertical Water fall; Ice rewrite; hydrometeors |
| Crustal lid kinematics | `TectonicDisplacement` / `TectonicVertical` | Magma, trees, wasps, crickets; seismic `aux.w` |
| Magma / Water / Ice pixel phase flips | `PhaseChange` | Spatial transport |
| Groundwater boil (`aux.y` → `aux.x`) | `HydrothermalRelease` | Pixel Water→Air boil (`PhaseChange`) |

## Major systems status (with evidence)

| System | Status | Evidence | Notes |
| --- | --- | --- | --- |
| Core GPU simulation pipeline and pass scheduler | **Mature** | `SimulationHost.cs`, `GpuPassScheduler.cs`, `SimulationResources.cs` | End-to-end tick loop with explicit pass sequencing, sub-stepping, and dedicated resources for all major domains. |
| Material transport (MaCA Margolus CA) | **Mature** | `MargolusTransport.compute`, `MargolusCommon.hlsl`, `MargolusContractTests.cs`, `Margolus*.cs` (PlayMode) | Discrete $2 \times 2$ block CA. Exact pixel conservation ($N(t) \equiv N(0)$), polar metric $\gamma(y)$, toroidal seam wrap. Gated by `enableMaterialTransport`. Organism IDs (`IsAnyFaunaMaterial`, trees) are pinned so sidecar state is not orphaned. MaCE / `_MaceMobileDisplay` / `useLegacyTransport` are gone. |
| Polar-grid world generation and material seeding | **Mature** | `WorldGeneration.compute`, `GpuPassScheduler.GenerateWorld`, `WorldGenIntegrationTests.cs`, `MargolusWorldGenStabilityTests.cs` | V2 worldgen with pre-relaxed talus aprons, exposed granite cliff faces, continental shelf marine sedimentation. |
| Hydrology + water mass contract | **Mature** | `Assets/Concept/WaterPlan.MD`, `HydrologyIntegrationTests.cs`, `WeatherIntegrationTests.cs` | Conservation posture around evaporation/condensation/precipitation/infiltration/hydrostatic. Groundwater boil lives in `HydrothermalRelease`. |
| Atmospheric/weather dynamics | **Mature** | `Weather.compute`, `WeatherIntegrationTests.cs` | Buoyancy, lapse, advection, pressure diffusion, precipitation, seam wrapping, conservation checks. |
| Geology & Geodynamics | **Mature** | `Geology.compute`, `Geodynamics.compute`, `GeodynamicsContractTests.cs`, `GeodynamicsIntegrationTests.cs`, snapshot v16 | Angular/radial lattice plus lid kinematics (v5): relative buoyancy, isostatic restoring, directional block shear, water-riding columns. Magma freeze is owned by `PhaseChange`. GPU move-counting is omitted (D3D11 8-UAV cap on Geology kernels). |
| Combustion and storm/lightning | **In progress** | `Combustion.compute`, `Storm.compute`, integration tests | Dedicated fields and pass chain exist; balancing continues. |
| Biology (mycology, flora, fauna, grass, trees, wasps) | **In progress** | Dedicated compute + PlayMode coverage | Multiple trophic layers; speciation goals remain open. Grass slots ride Margolus soil swaps. Crickets and eggs stay Margolus-pinned; `Fauna.compute` owns hops, unsupported falls, and landing. |
| Climate coarse layer | **Mature (C0–C3)** | `Climate.compute`, `DispatchClimate`, `ClimateIntegrationTests.cs` | Coarse T/albedo/moisture/wind injectors are live (`ClimateWindBias`, `ClimateSurfaceAbsorb`, `ClimateBucketScale`, `ClimateInsolationScale`). Fine layer remains the only water-mass ledger. |
| Snapshot persistence/migration | **Mature** | `WorldSnapshotService.cs` | Current write version 16. V14/V15 mobile-mass slices are skipped on load. |
| AI crew / LLM-driven tooling | **In progress** | `Assets/GeneSys/Runtime/AI/*`, `AiCrewTests.cs` | Settings, tool registry, prompt queue, and client for in-editor inspection. |

## Current simulation capabilities vs missing pieces for realism/life-seeding readiness

### Capabilities currently present

- Exact discrete mass-conserving particle transport (MaCA) via $2 \times 2$ block partition cellular automata with Hamiltonian energy minimization, altitude-invariant physical angle of repose via polar metric compensation, and toroidal seam wrapping.
- Procedural planetoid generation with layered geology, seeded basins/oceans/ice/deposits, and pre-relaxed granular talus aprons.
- Multi-pass atmospheric + hydrology loop with explicit water-accounting conventions.
- Groundwater, hydrothermal release, and hydrostatic leveling mechanics beyond simple falling-fluid behavior.
- Planetary geodynamics layer with radial/angular stress tracking, fault ruptures, and deep mantle thermal plumes.
- Coupled fire and storm systems that interact with heat/pressure/water pathways.
- Multiple biological subsystems (fungal, plant, insect/pollinator/predator-like roles) integrated into the same world state.
- Hybrid cell-motion ownership: Margolus settles, specialized kernels keep only non-gravitational drive.
- Unified Magnus evaporation (`EvaporateToAir`) and a dedicated climate-slab longwave knob (`climateSlabRadiativeCooling`).

### Missing or incomplete for “realistic” planetary conditions and robust life-seeding

- **Evolutionary/speciation goals** in `Assets/ORIGINAL OUTLINE-OUT OF DATE.MD` (fitness hemispheres, richer brains, broader organism ladder) are not yet realized as full systems.
- **Long-horizon planetary realism calibration** is incomplete (long-duration climate and biological equilibrium benchmarking across thousands of simulated days).
- **Internal biology consolidation** (shared plant climate-envelope helper, unified spore transport) is deferred.
- **Orientation documentation:** Contributor-facing orientation docs (top-level README or onboarding guide) remain minimal.

## Known risks, gaps, and open questions

1. **Archived MaCE docs:** `Assets/Concept/Archive/MaCEPlan.MD` and `MaCE_SedimentPilot.MD` describe a superseded Euler flux design. Live transport is `MargolusTransport.compute`.
2. **Complex-parameter fragility:** `SimulationConfig.cs` has a large tuning surface; contract tests assert clamp boundaries and defaults.
3. **Biological equilibrium balancing:** Interactions between weather, soil transport, and vegetation root anchoring still need multi-season calibration.

## Immediate next priorities

- Advance biological roadmap from baseline flora/fauna toward broader life and speciation goals in `Assets/ORIGINAL OUTLINE-OUT OF DATE.MD`.
- Continue climate layer fine-tuning (coarse-to-fine coupling and long-period orbital forcing).
- Expand long-horizon automated stability tests across coupled geodynamics + weather + ecosystem regimes.
- Author a top-level repository `README.md` summarizing the engine architecture, pass order, and test execution workflows.

## Key documents and source paths

### Plan and design documents

- `Assets/ORIGINAL OUTLINE-OUT OF DATE.MD`
- `Assets/ProjectHistory.md`
- `Assets/Concept/WaterPlan.MD`
- `Assets/Concept/ClimatePlan.MD`
- `Assets/Concept/GrassPlan.MD`
- `Assets/Concept/Archive/MaCEPlan.MD` *(superseded)*
- `Assets/Concept/Archive/MaCE_SedimentPilot.MD` *(superseded)*

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
