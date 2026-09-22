# Coarse Mantle → Fine Volcanism Plan

Rework GeneSys volcanism so a slowly drifting **mantle convection** layer sets interior regimes, while the existing **pixel geology** stack remains the only place materials melt, freeze, erupt, and settle.

**Status:** implemented — V0–V3 shipped (`Geodynamics` slot 2, `Mantle.compute`, geothermal injectors, Tephra ID 16).
**Related:** `Assets/Concept/ClimatePlan.MD`, `Assets/SituationReport.md`. Superceded MaCE notes live in `Assets/Concept/Archive/`.

---

## 1. Verdict

| Question | Answer |
| --- | --- |
| Replace `Geology.compute` volcanism? | **No.** |
| Add coarse mantle that injects heat / lid / conduit envelopes? | **Yes.** |
| Second magma ledger at coarse resolution? | **No.** Fine Material/State stay authoritative. |
| Persistent magma tubes as a dedicated material? | **No.** Mantle stays ID 3; tubes live in `MantleField`. |
| New surface ejecta material? | **Yes.** Tephra (ID 16). |

**Hard rule:** the coarse lattice must not invent or destroy tracked cells. It changes set-points, multipliers, and memory; fine kernels still perform the material/heat transfers. `volcanicMagmaFractionLimit` remains the melt safety throttle.

---

## 2. Motivation

The coarse lattice already had thermal / flow / overpressure / melt per bin, but `thermal` was a damped relaxation toward the fine-grid mean, so no persistent convection pattern formed and nothing drifted. Fine Mantle never received lattice heat: `Volcanism` only flipped Mantle→Magma along event-lifetime dike filaments. When the event budget exhausted, the filament froze to Basalt and the pathway was forgotten. Default display coloured Mantle by material only. Nothing geothermal reached temperature: `AtmosphericForcing` had solar + space cooling, and `ClimateStep` had no interior term. Surface effects were Magma + Ash only; `tectonicSurfaceCoupling` was a dead uniform.

| Fine stack is good at | Fine stack should not have to *own* alone |
| --- | --- |
| Local melt, dike climb, blast, ash loft | Persistent planetary convection memory |
| Magma freeze hysteresis in `PhaseChange` | Hotspot tracks that reopen old plumbing |
| One-tick eruption motion | Lithosphere that thins over upwelling |
| Pixel mass conservation | Regional geothermal warmth in weather/climate |

Without a coarse layer, a volcano is a pulse, not a shifting mantle.

---

## 3. Two-layer architecture

```text
┌──────────────────────────────────────────────────────────┐
│  COARSE GEODYNAMICS (slow, low-res)                      │
│  angular × radial bins, 3 float4 slots                   │
│  slot 2: plume, lidBase, conduitNet, geoFlux             │
│  outputs → injectors in Geodynamics.hlsl                 │
└────────────────────────────┬─────────────────────────────┘
                             │ inject (not a second ledger)
┌────────────────────────────▼─────────────────────────────┐
│  FINE PIXEL GEOLOGY / WEATHER / CLIMATE                  │
│  Volcanism, EruptionMotion, AtmosphericForcing,          │
│  ClimateStep, Mantle.compute conduit memory              │
└──────────────────────────────────────────────────────────┘
```

Same pattern as `ClimatePlan.MD`: coarse layer sets regimes and injects envelopes; fine kernels remain the only owners of material/heat transfers.

---

## 4. Coarse mantle slot

Per lattice cell, slot 2 is:

| Component | Role |
| --- | --- |
| `x` plume | Signed convection pattern (K cells, phase drifts + seeded wobble) |
| `y` lidBase | Lithosphere base radius; thins over upwelling, thickens over downwelling |
| `z` conduitNet | Bin-mean `MantleField` maturity so new events prefer old plumbing |
| `w` geoFlux | Heat delivered to the lid base (plume, thin lid, vent, conduits) |

Radial shaping is strong in mid-mantle and fades at core and lid. `GeoReferenceTemperature` includes the plume anomaly so `AggregateInterior` coupling does not fight the pattern.

---

## 5. Injectors

| Injector | Fine consumer | Role |
| --- | --- | --- |
| `GeodynamicsPlume` | `Volcanism` Mantle `state.x` nudge | Hot columns / cold sinks in the fine grid |
| `GeodynamicsLidBase` | `Volcanism` melt ceiling | Shallow chambers under a thinned lid |
| `GeodynamicsGeothermalFlux` | `AtmosphericForcing`, `ClimateStep`, hydrothermal seep | Surface and climate warmth over plumes |
| `GeodynamicsGeothermalDepthScale` | `AtmosphericForcing` | E-folding so only near-surface cells take geo heat |
| `MantleField.x` maturity | `IsEruptionPermeable`, `ColumnBurden`, `Volcanism` cooling | Magma-tube reuse and insulated conduits |
| `tectonicSurfaceCoupling` (`_GeodynamicsD.y`) | `EruptionMotion` resistance | Named knob finally scales surface breakthrough |

`Mantle.compute` `MantleConduits` is the only writer of `MantleField` (Storm-style ping-pong). Geology kernels bind it as an SRV so the D3D11 8-UAV cap is unchanged.

---

## 6. Tick placement

1. **Geodynamics cadence** (`geodynamicsPeriodTicks`): aggregate interior (including conduit maturity) → step lattice (plume/lid/geoFlux) → select/commit events → `MantleConduits`
2. **Slow geology:** `Volcanism` (Mantle T nudge, melt up to lidBase, insulated tube cooling)
3. **Material substeps:** `EruptionMotion` (Tephra vs Magma/Ash) then `PhaseChange`
4. **Weather / climate:** `AtmosphericForcing` geothermal term; `ClimateStep` slab geothermal with `_ClimateE.y`

Fine order otherwise stays as in `SituationReport.md`.

---

## 7. Phased adoption

### Phase V0 — Shifting mantle on the lattice + visible mantle

- Lattice slot 2, drifting convection, lidBase, geoFlux
- Fine Mantle `state.x` nudged toward `refT + plume`
- Default view shades Mantle/Core by heat; overlay 33 "Mantle"
- Snapshot v17; v15/v16 loads expand 2 slots → 3 and rebuild slot 2

### Phase V1 — Geothermal temperature coupling

- `GeodynamicsGeothermalFlux` / depth scale
- Surface heating in `AtmosphericForcing`; slab energy in `ClimateStep`
- Hydrothermal seep includes geoFlux

### Phase V2 — Persistent magma tubes

- `MantleField` RGBA16F (maturity, plume cache, vent age)
- Conduit reuse in burden / melt / cooling
- Lattice `conduitNet` feedback into volcano scoring
- Vein glow on Magma/Basalt/Mantle

### Phase V3 — Direct surface effects + Tephra

- Tephra ID 16 (granular, repose ~35°, density ~1.6)
- Explosive blasts emit Tephra; effusive eruptions still spread Magma → Basalt
- `tectonicSurfaceCoupling` wired as breakthrough resistance
- Buried Tephra weathers to Soil on the `AshFertilization` path
- Fissures are emergent (dike + thin lid); no special-case code

### Phase V4 — Docs, contracts, tests

- This file, Situation Report, primer, `MantleContractTests`, `MantleIntegrationTests`
- Keep `StandardSoakStaysCloseToDisabledControl` and `DefaultSoakKeepsMagmaSparseAndFinite` green

### Non-goals

- Replacing `PhaseChange` magma freeze
- A second magma mass ledger
- Cubed-sphere mantle circulation
- Snapshotting `MantleField` in v17 (rebuilt on load)

---

## 8. Implementation surfaces

| Area | Touch |
| --- | --- |
| Lattice | `GeodynamicsGrid.cs`, `Geodynamics.compute`, `Geodynamics.hlsl` |
| Conduits | `Mantle.compute`, `SimulationResources.MantleField` |
| Geology | `Volcanism`, `EruptionMotion`, `ColumnBurden` |
| Weather / climate | `AtmosphericForcing`, `ClimateStep` |
| Display | `PlanetoidDisplay.shader` overlay 33, default heat/vein shading |
| Config / UI | `SimulationConfig` Mantle header, tooltips, overlay label |
| Snapshots | `WorldSnapshotService` v17 |
| Materials | `016_Tephra.asset`, registry, Margolus movable list |
| Tests | `MantleContractTests.cs`, `MantleIntegrationTests.cs` |

Feature flags: `mantleLayerEnable`, `mantleConvectionCells`, `mantleDriftRate`, `mantlePlumeHeat`, `mantleLidThinning`, `geothermalSurfaceGain`, `geothermalClimateGain`, `mantleConduitMemory`, `mantleConduitReuse`, `eruptionTephraFraction`.

---

## 9. Validation culture

- Always keep geodynamics soak fixtures green when mantle injectors are on
- Add: plume raises fine Mantle T under upwelling vs downwelling
- Add: geothermal injector yields surface T contrast with solar off
- Add: climate slab geothermal with zero water delta
- Add: conduit maturity survives event exhaustion; re-eruption reuses the column
- Add: explosive surface eruption yields Tephra with local relief
- Safety limit (`ExtremeVolcanismStaysAtOrBelowSafetyLimit`) still holds with tubes
- Same-seed determinism includes slot 2 and `MantleField`
- Do not pin cosmetic overlay colours

---

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| Runaway melt | `volcanicMagmaFractionLimit` stays authoritative; `mantlePlumeHeat` bounded relative to `(solidus - refT)` |
| Soak drift | Default geothermal / plume gains stay small; contract test pins the defaults |
| UAV cap | All new fine-grid writes go through `Mantle.compute` in-place; Geology reads only |
| Snapshot compatibility | v15/v16 loads expand 2-slot lattice into 3 slots and rebuild slot 2 / `MantleField` |
| HighOverdriveBlastCreatesAsh | Explosive Tephra only when `explosive > 0.35`, so hot dry magma still makes Ash |

---

## 11. Decision log

| Date | Decision |
| --- | --- |
| 2026-09-21 | Coarse→fine = Climate-style mantle layer over existing pixel volcanism |
| 2026-09-21 | Keep Mantle ID 3; persist tubes in `MantleField` rather than a new magma material |
| 2026-09-21 | Add Tephra ID 16 for cinder cones; Ash remains the lofted fraction |
| 2026-09-21 | `Mantle.compute` owns conduit writes so Geology stays inside the 8-UAV cap |
| 2026-09-21 | Snapshot v17 writes 3 lattice slots; `MantleField` is rebuilt on load |

---

## 12. References

- Climate two-layer pattern: `Assets/Concept/ClimatePlan.MD`
- Live pass order / ownership: `Assets/SituationReport.md`
- Fine geology: `Assets/GeneSys/Compute/Simulation/Geology.compute`
- Coarse lattice: `Assets/GeneSys/Compute/Simulation/Geodynamics.compute`
