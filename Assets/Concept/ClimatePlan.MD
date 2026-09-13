# Coarse Climate → Fine Pixel Weather Plan

Bolster GeneSys weather by adding a slow, low-resolution **climate** layer that sets regimes, while the existing mass-conserving **pixel weather** stack remains the only place tracked water moves.

**Status:** design — not implemented.
**Related:** `Assets/OUTLINE.MD`, `Assets/WaterPlan.MD`, `Assets/MaCEPlan.MD`, `Assets/GrassPlan.MD`.
**Inspiration:** EMIC / PlaSim-style energy + land-surface feedbacks (e.g. SimBA albedo, roughness, soil bucket); not a full GCM port.

---

## 1. Verdict

| Question | Answer |
| --- | --- |
| Replace `Weather.compute`? | **No.** |
| Add coarse climate that injects forcings / envelopes? | **Yes.** |
| Second water mass ledger at coarse resolution? | **No.** Fine `state.z` / `aux.x` / `aux.y` stay authoritative per `WaterPlan.MD`. |
| Requires cubed-sphere first? | **No.** Angular bins on the polar grid are enough for pilots. |

**Hard rule:** the coarse layer must not invent or destroy tracked water mass. It changes set-points, multipliers, and land-surface parameters; fine kernels still perform conservation-correct transfers.

---

## 2. Motivation

The fine stack already covers interactive weather (`Weather.compute` + hydrology):

- `AtmosphericForcing` — `LightField` heating, space/terrain cooling, cloud greenhouse, surface↔air heat
- `AtmosphericContinuity` / `AtmosphericDynamics` — divergence→pressure, virtual-T buoyancy, lapse, Coriolis, prevailing wind
- `AtmosphericTransport` — donor-face vapor/cloud/heat/O₂/soot + adiabatic lapse on radial wind
- `WaterCycle` / `Precipitation` — Magnus saturation, deficit evaporation, dew/frost, cloud retain→precip
- Hydrology — soak, percolate, weep, hydrostatic leveling

| Fine stack is good at | Fine stack should not have to *own* alone |
| --- | --- |
| Land/sea breeze, local updraft, dew, flash steam | Persistent planetary circulation memory |
| Orographic rain *this hour* | Rain-shadow *climate* across seasons |
| Day/night insolation response | Ice-edge / drought lock-in via albedo |
| Pixel mass conservation | Biosphere changing climate forcings (albedo, roughness, bucket) over long times |

Without a coarse layer, thunderstorms and ice ages compete for the same full-res tick budget, and life mostly shades photosynthesis instead of reshaping climate.

---

## 3. Two-layer architecture

```text
┌──────────────────────────────────────────────┐
│  COARSE CLIMATE (slow, low-res, cheap)       │
│  angular bins × few radial shells            │
│  energy + land memory; diagnostic moisture   │
│  outputs → forcings / envelopes / land parms │
└────────────────────┬─────────────────────────┘
                     │ inject (not a second ledger)
┌────────────────────▼─────────────────────────┐
│  FINE PIXEL WEATHER (existing GeneSys)       │
│  full grid, every tick, WaterPlan contract   │
│  local buoyancy, clouds, storms, hydro       │
└──────────────────────────────────────────────┘
```

MaCE (`MaCEPlan.MD`) is orthogonal: it improves mobile **surface mass**. This plan improves **which weather regime** that surface lives in.

---

## 4. Coarse grid on the polar planetoid

### Layout (pilot-friendly)

- **Angular:** `Nθ` bins (start 16–64), wrapping like `WrapTheta`
- **Radial shells:** 2–4 — e.g. deep crust skin / surface / lower air / upper air, keyed to `_PlayableInnerRadius` and `_AtmosphereStartRadius`
- **Cadence:** every `K` fine ticks, or once per solar hour/day (reuse ticks-per-day used by grass flowering)

### Coarse state (per bin / shell)

| Field | Role |
| --- | --- |
| `T_atm`, `T_skin` | Energy memory |
| `U_theta` | Mean prevailing / monsoon bias |
| `cloud_frac` | Diagnostic / greenhouse envelope |
| `ice_frac` / snow proxy | Albedo feedback |
| `albedo`, `roughness`, `bucket_scale` | Land-surface params from aggregated life/soil |
| `drought_index` / wetness | From upward-integrated fine fluxes (diagnostic) |
| Optional slab `heat_capacity` | Ocean/soil thermal inertia per angular bin |

Polar note: coarse angular averages reduce climate sensitivity to pole-cell pathology. Fine polar artifacts can remain local.

---

## 5. Coupling modes

### A. Forcing injection (default)

Map coarse outputs onto knobs fine kernels already use:

| Coarse output | Fine injection |
| --- | --- |
| Seasonal / orbital insolation envelope | Scale solar intensity or multiply `LightField` absorption |
| Albedo / ice cover | Scale heating in `AtmosphericForcing` |
| Prevailing / monsoon wind | Spatially varying `_WeatherI.z` or additive `flow.x` bias in `AtmosphericDynamics` |
| Stability / lapse regime | Modulate `_WeatherG.y` / vertical buoyancy strength |
| Bucket / field-capacity scale | Multiply groundwater capacity / `fieldCapacityFraction` in hydrology |
| Roughness / drag | Scale wind damping / surface coupling |

### B. Weak temperature nudging (optional)

`T_fine ← (1-λ) T_fine + λ T_coarse` on atmosphere cells only, small `λ`. Do **not** nudge `aux.x` / `state.z` unless the operation is a conservative redistribution.

### C. Multipliers / envelopes (safest for mass)

Keep local anomalies; multiply evaporation rate, wind strength, or storminess by coarse indices.

### D. Learned residual (non-goal for early phases)

Offline neural correctors (coarse→fine) are optional later. Hand physics remains the residual generator.

---

## 6. Coarse physics worth implementing

### Energy balance / EMIC slice

- Absorbed SW: insolation × (1 − albedo)
- Albedo from ice + canopy + soil — **aggregate upward** from fine `LightField` contributors and surface materials
- LW cooling toward space temperature (existing radiative knobs)
- Angular heat transport: diffusion or single-layer coarse wind on `T`
- Optional slab ocean / deep soil heat capacity per bin for true climate memory

### Moisture (diagnostic)

Each coupling step, integrate fine precipitation, evaporation, and runoff **up** into coarse bins. Store drought/wetness for vegetation and fire. Fine layer remains the only cell-to-cell water transport.

### Biosphere land-surface feedback (SimBA-shaped, GeneSys-native)

Aggregate flora / grass / tree / mycology / soil:

| Fine signal | Coarse land param | Climate effect |
| --- | --- | --- |
| Canopy / biomass | ↓ albedo, ↑ roughness | Cooler/wetter tendency, more mixing |
| Roots / organics / detritus | ↑ `bucket_scale` | Larger soil water reservoir |
| Burn scar / desertify | ↑ albedo, ↓ bucket | Drought lock-in |
| Ice / cold+precip history | ↑ albedo | Cold stable sector |

This is how green/brown or ice-on/ice-off attractors appear without a full GCM.

### Storms

`Storm.compute` stays fine and local. Coarse may set humidity/stability/shear **envelopes** so lightning seasons exist; do not coarse-simulate leaders.

---

## 7. Timescale split

| Process | Layer | Cadence |
| --- | --- | --- |
| Gusts, cells, dew, flash steam, hydrostatic leveling | Fine | every tick |
| Sea breeze reversal | Fine (emergent) | day cycle |
| Prevailing / monsoon shift, drought index | Coarse | hours–days sim time |
| Ice albedo, forest–climate lock-in | Coarse + life aggregate | days–seasons |
| Orbital / geothermal drift | Coarse | rare |

---

## 8. Tick placement

Suggested slow cadence (not every fine tick):

1. **Aggregate** fine→coarse (T, vapor/precip diagnostics, albedo contributors, life cover)
2. **Step coarse** energy + land params (+ optional angular transport)
3. **Inject** uniforms / bias textures for the upcoming fine weather ticks

Fine order stays as in `WaterPlan.MD` (forcing → continuity → dynamics → transport → water cycle → precip → storm → groundwater → hydrostatics → biology).

---

## 9. Phased adoption

### Phase C0 — Diagnostics only

- Reduce fine fields to coarse bins each couple period
- UI overlay: coarse T, wetness, albedo
- No feedback into sim
- Acceptance: aggregation deterministic; zero effect on conservation fixtures

### Phase C1 — Prevailing + insolation envelope

- Coarse stores seasonal insolation scale + `prevailingWind(θ)`
- Inject into `AtmosphericForcing` / `AtmosphericDynamics` only
- Acceptance: wet/dry angular contrast under forced prevailing; total water unchanged by injector

### Phase C2 — Albedo / ice index

- Coarse ice/snow proxy from fine cold + precip history
- Feeds heating absorption
- Acceptance: self-reinforcing cold sector possible; water fixtures green

### Phase C3 — Vegetation land-surface params

- Aggregate canopy/soil → albedo, roughness, `bucket_scale`
- Close living-world climate loop
- Acceptance: deforest / burn brush changes coarse climate measurably over many days

### Phase C4 — Conservative moisture shaping (optional)

- Within-bin zero-sum reshape of fine vapor toward coarse RH profile
- Still no net mass create/destroy
- Acceptance: mass fixtures + explicit zero-sum test

### Non-goals (early)

- Full ExoCubed / FV3 dycore
- In-engine neural weather
- Replacing Storm, WaterCycle, or hydrostatic mass logic
- Coarse magma/ash (geology stays on its own passes)

---

## 10. Implementation surfaces (expected)

| Area | Likely touch |
| --- | --- |
| Resources | `SimulationResources.cs` — coarse buffers |
| Schedule | `GpuPassScheduler.cs` — aggregate / step / inject on cadence |
| Inject glue | Uniforms or small bias textures read by `Weather.compute` / hydrology |
| Solar | `SolarInsolation.hlsl` / forcing path |
| Config / UI | `SimulationConfig`, overlays, inspect |
| Snapshots | Optional; v1 may rebuild coarse from fine on load |
| Tests | Injector does not change total water; forced prevailing asymmetry; C0 determinism |

Prefer a **dedicated climate pass** over stuffing coarse state into the mega `WriteCell` path.

Feature flags: `climateLayerEnable`, `climateCouplePeriod`, `climatePrevailingInject`, `climateAlbedoFeedback`, `climateBiomeFeedback`.

---

## 11. Validation culture

- Always keep `HydrologyIntegrationTests` / `WeatherIntegrationTests` mass fixtures green when climate injectors are on
- Add: coarse step alone ⇒ `Δ(total water) == 0`
- Add: C1 forced wind ⇒ measurable angular precip or vapor contrast
- Do not pin cosmetic default slider values (see `OUTLINE.MD` validation policy)
- PlayMode: prefer one behavior fixture per phase, not a full-suite gate on ordinary edits

---

## 12. Risks

| Risk | Mitigation |
| --- | --- |
| Hidden mass creation via RH nudge | Ban non-conservative vapor nudges until C4 zero-sum design |
| Over-damping local weather | Prefer multipliers/biases over strong T nudging |
| Double-counting albedo (LightField + coarse) | Define single absorption path; coarse sets envelope, fine applies local attenuation |
| Fighting MaCE / hydro work | Climate injects forcings; MaCE owns mobile solids — little overlap |
| Scope creep into GCM | Stay on angular bins + energy/land until C3 proves value |

---

## 13. Decision log

| Date | Decision |
| --- | --- |
| 2026-09-11 | Coarse→fine = forcing/envelope climate layer over existing pixel weather |
| 2026-09-11 | No second water ledger; `WaterPlan.MD` remains mass authority |
| 2026-09-11 | Start with diagnostics + prevailing/insolation before biome feedbacks |
| 2026-09-11 | Orthogonal to MaCE sediment pilot; can proceed in parallel |

---

## 14. References

- In-repo fine contract: `Assets/WaterPlan.MD`
- Surface mass track: `Assets/MaCEPlan.MD`
- PlaSim / SimBA-style vegetation–climate feedbacks (albedo, roughness, soil water capacity)
- ExoPlaSim / EMIC literature for intermediate-complexity planetary climate (ideas only — not a port requirement)
