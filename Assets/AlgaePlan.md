---
name: Algae Physical Dispersal
overview: Add deterministic whole-cell algae dispersal driven by wind, rain/runoff, and a genetically controlled poleward stack-relief behavior. Reuse the currently inert substrate-affinity gene as stack motility, with gene value zero fully disabling only the natural poleward tendency while environmental forces remain effective.
todos:
  - id: movement-kernels
    content: Implement deterministic claim-based algae movement and poleward free-cell search
    status: completed
  - id: scheduler-resources
    content: Add transient movement claims and schedule the pass after environmental forcing
    status: completed
  - id: genetics-config
    content: Repurpose gene 10 and add persisted movement tuning/settings documentation
    status: completed
  - id: movement-tests
    content: Add unit and integration coverage for motility, forcing, collisions, and persistence
    status: in_progress
isProject: false
---

# Algae physical dispersal

## Simulation behavior
- Add a dedicated flora movement stage to [`Assets/GeneSys/Compute/Simulation/Flora.compute`](Assets/GeneSys/Compute/Simulation/Flora.compute):
  - Treat an algae cell as stacked when its radial-inward neighbor is algae.
  - For environmental movement, use the post-weather/hydrology tangential flow (wind, currents, and runoff) plus local rain/film impact to probabilistically dislodge algae into an adjacent open cell in the force direction; use a seeded side choice for rain without lateral flow.
  - For natural stack relief, derive the two ice-cap poles from the same `Hash01(seed * 9829)` rule as world generation, choose the nearest pole, and scan poleward for the first open destination whose inward neighbor is not algae.
  - Scale natural movement as `floraStackMigrationRate * (motilityGene / 255)`, so gene value `0` is a true off switch. Wind/rain movement remains independent of this gene.
  - Move the complete cell payload (material, physical fields, ecology/combustion, life, and genome) rather than cloning biomass.
- Resolve GPU collisions deterministically with three kernels: clear a transient R32-uint claim map, atomically claim destinations, then apply accepted source/destination swaps. This prevents two algae cells from duplicating into one gap and preserves algae count/state.
- Schedule the movement stage in [`Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs`](Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs) after current-tick weather/hydrology/erosion and before flora transport/lifecycle, using a transient claim texture added to [`Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs`](Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs).

## Genetics and tuning
- Repurpose gene 10 from the unused “Substrate affinity” definition to “Stack motility” consistently in [`Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl`](Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl) and [`Assets/GeneSys/Runtime/Simulation/FloraGenome.cs`](Assets/GeneSys/Runtime/Simulation/FloraGenome.cs), preserving the existing 12-gene packed layout and old genome payloads.
- Add separately tunable wind, rain/water-impact, and baseline stack-migration rates to [`Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs`](Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs), bind them as flora shader parameters, validate nonnegative values, and document them in [`Assets/GeneSys/Runtime/UI/SimulationSettingTooltips.cs`](Assets/GeneSys/Runtime/UI/SimulationSettingTooltips.cs).
- Update the probe’s gene description automatically through the renamed gene, and bump [`Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs`](Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs) to a backward-compatible snapshot version that persists the new rates while loading version 7 snapshots with defaults.

## Verification
- Extend [`Assets/GeneSys/Tests/EditMode/FloraTests.cs`](Assets/GeneSys/Tests/EditMode/FloraTests.cs) for the renamed gene, true-zero motility mapping, nonnegative defaults/validation, and transient claim texture format/lifecycle.
- Extend [`Assets/GeneSys/Tests/PlayMode/FloraIntegrationTests.cs`](Assets/GeneSys/Tests/PlayMode/FloraIntegrationTests.cs) to verify:
  - a stacked max-motility organism moves toward the correct nearest seeded pole and lands only where the inward neighbor is non-algae;
  - zero motility leaves a stack unchanged when environmental forcing is disabled;
  - wind and rain/runoff can each move algae even with zero natural motility;
  - competing movers are arbitrated without duplication, loss, or genome/life corruption;
  - fixed seeds remain deterministic and snapshot round-trips preserve the new settings and moved organisms.