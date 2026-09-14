# Project History

## How to maintain this file

Use this as a **living milestone log**, not a raw commit dump.

- Add new entries at the top (reverse chronological).
- Group related commits into one entry per meaningful change wave.
- For each entry include:
  - **Date or date range**
  - **Systems affected**
  - **What changed**
  - **Why / impact** (explicit if known, otherwise mark as inferred)
- When intent is unclear from commits alone, say so rather than guessing.
- Keep links to major plan docs current when scope shifts.

Suggested entry template:

```markdown
## YYYY-MM-DD (or YYYY-MM-DD to YYYY-MM-DD) — Title
- Systems: ...
- What changed: ...
- Why/impact: ...
- Evidence: commit subjects ..., relevant docs/files ...
```

---

## 2026-09-13 — Planetary systems realignment (ownership, dead scaffolding, docs)

- **Systems:** Margolus transport, Geology (ash/magma), Hydrology / hydrostatic, Weather / Climate, PhaseChange thermal, config / snapshots / metrics / UI, grass–soil coupling.
- **What changed:**
  - Retired leftover MaCE / liquid-density-exchange surface: `_DensityExchange`, `densityDisplaceable`, dummy V14/V15 mobile-mass writes, `_MaceMobileDisplay` claims in docs.
  - Hybrid cell-motion ownership: Margolus owns gravity/repose settling; `AshTransport` is buoyant lift only; `EruptionMotion` is pressure eruption only; hydrostatic is the only horizontal Water leveler; `ErosionAndCollapse` only changes identity.
  - `PhaseChange` is the sole Magma↔Basalt freeze path (Volcanism keeps melt creation and cooling sink). AtmosphericForcing uses one insolation value for heat and night cooling. Climate slab has its own LW knob.
  - One `EvaporateToAir` Magnus helper for WaterCycle / detritus / transpiration; Groundwater residue returns to host film instead of venting vapor. `dewRate` split from `condensationRate`.
  - Grass slots ride Margolus soil swaps; tree root receipts share `transportPassInterval` with `SoilDebit`.
  - Docs reset: `SituationReport.md`, `ClimatePlan.MD` (C0–C3 shipped), `WaterPlan.MD` (hydrothermal boil owner), MaCE plans archived.
- **Why/impact:** Direction changes had stacked redundant movers and knobs on the same IDs/fields. Ownership plus dead-code removal stops passes from undoing each other and makes the live architecture match the docs/tests.
- **Evidence:** `GpuPassScheduler.cs`, `MargolusCommon.hlsl`, `Geology.compute`, `Hydrology.compute`, `Weather.compute`, `Climate.compute`, `SimulationConfig.cs`, `WorldSnapshotService.cs`, `SituationReport.md`.

## 2026-09-13 — Transition from MaCE to MaCA (Affinity-Driven Margolus CA) material transport and surface interface rework

- **Systems:** MaCA (Margolus Cellular Automata) discrete material transport, Legacy MaCE bridging, Hydrology / Geodynamics surface interface, World generation stability, Rendering / PlanetoidDisplay.
- **What changed:**
  - Transitioned the simulation from continuous fractional flux advection (MaCE) to exact, discrete $2 \times 2$ block partition cellular automata (MaCA) in `MargolusTransport.compute` and `MargolusCommon.hlsl`.
  - Implemented Hamiltonian energy minimization across all candidate $2 \times 2$ permutations (Identity, Vertical Swap, Horizontal Swap, Diagonal Swaps, Rotations, Diagonal Projections) driven by gravity, momentum, and pairwise material affinity.
  - Implemented polar metric aspect ratio compensation ($\gamma(y) = \frac{2\pi \cdot \text{Radius}(y) \cdot N_y}{N_x}$) ensuring physically consistent, altitude-invariant angles of repose across standard polar grids.
  - Added toroidal seam wrapping modulo grid width across the $x = 0 \leftrightarrow x = W - 1$ boundary, eliminating edge collapse or boundary shearing.
  - Integrated stress-to-sediment detachment in `Hydrology.compute` (`aux.w > 1.0` detaches into mobile Sediment ID 8) and locked deep bedrock beneath standing liquid bodies.
  - Retuned world generation with pre-relaxed talus aprons, exposed crystalline granite cliff faces, and continental shelf marine sedimentation.
  - Recalibrated surface granular collapse envelopes (Soil, Clay, Sediment), preventing unnatural sheer vertical columns and needles from freezing on polar terrain.
  - Removed continuous mobile-mass rendering (`_MaceMobileDisplay`) so discrete Air/Soil cells vacated by sediment do not retain brown tinting.
  - MaCE / `useLegacyTransport` were transitional and have since been removed (see the realignment entry above).
  - Authored full test coverage: `MargolusContractTests` (EditMode), `MargolusPrototypeTests` (PlayMode), `MargolusMetricTests` (PlayMode), `MargolusGeoInterfaceTests` (PlayMode), and `MargolusWorldGenStabilityTests` (PlayMode).
- **Why/impact:** Eliminates fractional mass dissipation, float rounding drift, and advective blur inherent in continuous Euler flux models. Guarantees exact, discrete pixel conservation ($N(t) \equiv N(0)$) while delivering natural granular talus slopes, dynamic slope collapse, and stable bedrock foundations.
- **Evidence:** `Assets/GeneSys/Compute/Simulation/MargolusTransport.compute`, `Assets/GeneSys/Shaders/Simulation/Common/MargolusCommon.hlsl`, `Assets/GeneSys/Tests/EditMode/MargolusContractTests.cs`, `Assets/GeneSys/Tests/PlayMode/Margolus*.cs`, `Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs`, `Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs`, `Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader`.

## 2026-09-11 — Climate + MaCE integration and AI crew iteration

- **Systems:** Climate layer, MaCE transport, AI crew tooling, planning docs
- **What changed:**
  - Added climate and MaCE design docs (`Add coarse-to-fine climate layer design doc.`, `Add MaCE transport design and sediment pilot docs.`).
  - Added runtime milestones for climate and MaCE integration (`milestone - climate change`, `milestone - MaCE transport`).
  - Iterated AI chat/agent loop behavior (`milestone - user chat LLM ACT loop`, `milestone - blank chat fix`, `milestone - verbose crew logs`).
  - Included rejected/revert cycles around MaCA/MaCE direction.
- **Why/impact:** Expanded the simulation toward slower-timescale climate forcing and mass-conserving mobile solids while maturing in-sim AI interaction tooling; the same-day reject/revert commits indicate active design stabilization.
- **Evidence:** commits on 2026-09-11; `Assets/ClimatePlan.MD`, `Assets/MaCEPlan.MD`, `Assets/MaCE_SedimentPilot.MD`, runtime files under `Assets/GeneSys/Runtime/Simulation/Gpu/`.

## 2026-09-10 — Thermal, magma, and local LLM capability push

- **Systems:** Thermal/geology behavior, AI/LLM integration
- **What changed:** `milestone - thermal overhaul`, `milestone - magma fix`, `milestone - LLM integration`, `milestone - LLM vision`.
- **Why/impact:** Tightened core planetary energy/material behavior while introducing local LLM-assisted control/analysis capabilities in parallel.
- **Evidence:** commits on 2026-09-10; AI runtime code under `Assets/GeneSys/Runtime/AI/`.

## 2026-09-09 — Hydrology realignment milestone

- **Systems:** Hydrology/weather mass accounting
- **What changed:** `milestone - hydrology rework - unifying water`.
- **Why/impact:** Consolidated water representation and transfer behavior consistent with the mass-conservation direction now documented in `WaterPlan.MD`.
- **Evidence:** commit on 2026-09-09; `Assets/WaterPlan.MD`; hydrology/weather integration tests.

## 2026-09-01 to 2026-09-03 — Atmospheric dynamics and solar forcing tuning

- **Systems:** Weather transport/buoyancy/coriolis, polar solar behavior, cooling
- **What changed:**
  - Vapor stability fixes and precipitation/rain behavior adjustments.
  - Vertical buoyancy, momentum/coriolis, dynamic wind damping/inertia coupling.
  - Solar concentration/orbit/polar diminishment tuning and rim cooling fixes.
- **Why/impact:** Built a more stable and expressive atmospheric loop capable of directional circulation patterns and fewer runaway states.
- **Evidence:** commits including `milestone - vertical temp buoyancy`, `milestone - atmospheric momentum + coriolis`, `milestone - dynamic wind damping / intertia coupling`, `milestone - rim cooling inversion fix`.

## 2026-08-30 — Trees + ecology UI expansion + precipitation rework

- **Systems:** Tree subsystem, ecology settings UI, precipitation
- **What changed:** `milestone - trees`, `milestone - ecology settings sub-tabs`, `milestone - percipitation rework 1`.
- **Why/impact:** Expanded organism diversity and supporting controls while continuing water-cycle behavior adjustments.
- **Evidence:** commits on 2026-08-30.

## 2026-08-28 to 2026-08-29 — Hydrology stabilization wave

- **Systems:** Hydrology, rain behavior, defaults/tests, wasps
- **What changed:**
  - Multiple hydrology fixes (`milestone - hydrology fix`, `part 1`, `part 2`).
  - Rain and compute fixes, defaults tuning, and wasp milestone (`milestone waspss`).
- **Why/impact:** Iterative reliability and balancing pass across water + ecosystem interactions.
- **Evidence:** commits on 2026-08-28/29.

## 2026-08-27 — Grass, crickets, probe controls, snapshots, and test expansion

- **Systems:** Grass ecology, fauna (crickets), player/probe controls, persistence, tests
- **What changed:**
  - Introduced crickets (`milestone - crickets!`) and grass milestone commits.
  - Added probe steering/life seeding workflow updates.
  - Added world save milestone and test battery update.
  - Included rejected/revert attempts during grass iteration.
- **Why/impact:** Major ecosystem and tooling expansion day, with evidence of rapid prototyping and correction loops.
- **Evidence:** commits on 2026-08-27; integration tests for grass/fauna/persistence.

## 2026-08-26 — Algae behavior experimentation and UI restructuring

- **Systems:** Flora behavior, cadence scheduling, UI architecture
- **What changed:** Algal shift attempts plus revert/reject cycles, cadence tier milestone, settings/inspector UI rework, history panel addition.
- **Why/impact:** Tuned biological behavior while improving operator controls and observability.
- **Evidence:** commits on 2026-08-26.

## 2026-08-24 to 2026-08-25 — Fire, lightning, probe UX, and algae/light coupling

- **Systems:** Combustion, storm/lightning, probe tooling, algae/photosynthesis-light interplay
- **What changed:** `milestone - combustion`, `milestone - lightning`, `milestone - probe PoC`, `milestone - probe + buttons + view`, `milestone - algae + light attenuation`.
- **Why/impact:** Added key atmosphere-energy hazard loops and better hands-on interaction with the simulation.
- **Evidence:** commits on 2026-08-24/25.

## 2026-08-22 to 2026-08-23 — Closed-loop water cycle groundwork

- **Systems:** Hydrology/weather water cycle
- **What changed:** Water soakage milestone, closed-loop water system attempts, evaporation/vapor fixes, and vapor realignment.
- **Why/impact:** Established the path toward the later unified water contract and conservation-focused weather behavior.
- **Evidence:** commits on 2026-08-22/23; `Assets/WaterPlan.MD` references this realignment direction.

## 2026-08-19 to 2026-08-20 — Mycology, worldgen, and simulation controls

- **Systems:** Mycology ecology, world generation, presets/settings UX
- **What changed:** Added mycology milestone, worldgen update, preset save/load, settings tooltips, and thermal survivability tuning (`heat death delay`).
- **Why/impact:** Strengthened both ecosystem depth and usability/configurability.
- **Evidence:** commits on 2026-08-19/20.

## 2026-08-17 to 2026-08-18 — Project bootstrap and first simulation spine

- **Systems:** Repository bootstrap, planetoid foundation, UI, early fluid/erosion/thermal visuals
- **What changed:** Initial project import plus first planetoid and modern UI milestones, liquid water baseline, early erosion/vapor/fluid-density and visualization updates.
- **Why/impact:** Established the initial architecture that later milestones iterated on.
- **Evidence:** earliest commits beginning 2026-08-17.
