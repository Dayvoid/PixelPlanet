# MaCE Material Transport Plan

Adopt Mass-Conserving Evolution (MaCE) as GeneSys’s **mobile-phase transport law** for geo / surface / hydro interface dynamics.

**Status:** design. Pilot v1: `Assets/MaCE_SedimentPilot.MD`.
**Paper:** Papadopoulos & Guichard, *MaCE: General Mass Conserving Dynamics for Cellular Automata* (arXiv:2507.12306, ISAL 2025).
**Related:** `Assets/OUTLINE.MD`, `Assets/WaterPlan.MD`, `Assets/GrassPlan.MD`.

---

## 1. Verdict

| Question | Answer |
| --- | --- |
| Replace all material CA with MaCE? | **No.** |
| Use MaCE for mobile geo–surface–hydro mass? | **Yes.** |
| Touch water vapor/cloud ledger? | **Not as a rewrite** — keep `WaterPlan.MD` contract; optionally unify shallow films later. |
| Touch magma/ash special motion in v1? | **No** — `EruptionMotion` / `AshTransport` stay until sediment pilot proves out. |

MaCE answers only: *given affinity `A`, how do we move mass so total mass is exact?* GeneSys still authors physics by defining `A` (and capacities) from existing material parameters and fields.

---

## 2. Motivation

GeneSys already couples two representations:

1. **Discrete material IDs** — whole-cell swaps (`MaterialMotion`, `LiquidDensityExchange`, ash/magma claim winners).
2. **Continuous mass fields** — water/vapor/groundwater with a hardened conservation culture (`WaterPlan.MD`).

Geo/surface/hydro pain concentrates at the glue:

- Occupancy is binary (Soil *or* Water *or* Sediment).
- Erosion often **flips identity** (`aux.w` stress → Sediment `8`) instead of transporting conserved solids into a carrier.
- Motion is swap/claim-based (falling-sand races, special-case diagonals).
- Water is mass-correct; **solids and mixtures** at shoreline, scarp, and aquifer boundaries are not.

OUTLINE goals (caves, calving, bioerosion, volcanic ejecta, wet granular behavior) need continuous mobile solids — not more ID special cases.

---

## 3. MaCE core (reference)

Affinity `A` per cell (any real field derived from state). Mass `ρ` redistributes over the Moore neighborhood:

```text
Z_ij = sum_{k,l in N_ij} exp(β * A_kl)
ρ_ij ← sum_{i',j' in N_ij} [exp(β * A_ij) / Z_i'j'] * ρ_i'j'
```

Each cell redistributes **its own** mass; total mass is exactly conserved. Continuous limit:

```text
∂t ρ = Δρ − 2β ∇·(ρ ∇A)
```

i.e. diffusion + advection up the affinity gradient.

- `β = 0` → pure smear
- large `β` → sharp downhill/high-affinity flow
- `β → ∞` → discrete winner-take-all (poor fit for GeneSys’s multi-field ledger; avoid as primary path)

**Cross-channel extension:** spatial MaCE first, then slow share across channels with rate `α` (rock ↔ sediment ↔ slurry ↔ water). Keep vapor creation on `PhaseChange` / latent paths so MaCE never invents water mass.

**Vs Flow-Lenia:** same affinity-driven idea; MaCE has a clearer continuous limit and CA-shaped update without flow-clip mass loss.

---

## 4. Current GeneSys map (what we are changing)

### Material / motion (`MaterialSimulation.compute`)

- `ThermalAndPressure`, `PhaseChange`, `LiquidDensityExchange`, `MaterialMotion`, `Electrical`, `PressureDiffusion`
- Movable categories swap into open cells; ice/water precip have dedicated destination logic; ash/wasps/trees excluded from generic motion

### Hydrology (`Hydrology.compute`)

- `Groundwater` — soak / percolate / weep; pre-tick agreed fluxes
- `ErosionAndCollapse` — moisture-aware cohesion; stress in `aux.w`; soft/hard conversion to Sediment `8`
- `AshFertilization`, `DetritusExchange`, hydrostatic column apply

### Geology (`Geology.compute`)

- `Volcanism`, `EruptionMotion`, `AshTransport`, `CoreReaction`
- Discrete ID flips for mantle→magma→basalt; ash buoyancy via claim winners
- `aux.w` dual use: fault on mantle/magma (no decay) vs erosion/karst on crust (decay in hydrology)

### MaterialGpuData (relevant)

- `physical`: density, rigidity, **repose radians**, grain size
- `biology`: porosity, buoyancy bias
- Soft crust: `7/8/15`; hard crust: `4/5/14`; magma `6`; ash `12`

---

## 5. Target architecture

### Keep

- Locked lithology as discrete IDs (core / mantle / hard crust) where motion is rare
- Water mass contract and Weather tick order for vapor / cloud / precip
- Pre-tick read / agree-on-flux discipline
- Organism Tex2DArrays; grass/tree root cohesion as modifiers of entrainment/`A`
- Ash and magma special passes until dedicated follow-on pilots

### Add

Per-cell continuous **mobile channels** (Tex2DArray recommended):

| Channel | Role |
| --- | --- |
| `ρ_sediment` | Fines / soft solids (pilot v1) |
| `ρ_water_mobile` | Optional later unification with `state.z` / Water pixels for shallow mixtures |
| `ρ_magma_mobile` | Optional; only if EruptionMotion is retired or hybridized |
| `ρ_solute` | Optional karst / dissolved load |

Host `materialId` remains the structural / porosity / conductivity authority and primary inspect ID.

### Replace over time

- Soft-crust diagonal `MaterialMotion` → MaCE on `ρ_sediment`
- Soft erosion ID-only climax → stress feeds `Δρ` + MaCE transport; ID rewrite is **consequence** of structural mass loss
- Eventually: shoreline binary Water-vs-Soil → host + multi-channel fill
- Eventually: bedload / wash from `\|flow\|` and runoff in `A`

### Display

Keep pixel-crisp presentation: category/shade from host ID + channel thresholds (same pattern as grass: state without requiring a unique material for every mode).

---

## 6. Affinity authoring (GeneSys physics → `A`)

| Drive | Affinity sketch | Phase |
| --- | --- | --- |
| Gravity / repose | `A −= k_g * y`; unsupported lateral vs `physical.z`, damped by rigidity | v1 |
| Pile support | `A +=` support from below `ρ` / rigid neighbors | v1 |
| Shear / flow | `A_sed += k_e * \|flow\|` on exposed faces | v2 |
| Runoff / head | couple to hydrostatic column / weep discharge | v2–v3 |
| Buoyancy / density | `A += -k_b * (ρ_self - ρ_neighbor)` for sorting | v2 |
| Porosity capacity | clamp channel to `biology.z`; excess spills | v1+ |
| Wind | angular bias on surface fines | v2 (ash may stay special) |
| Thermal / phase | cross-channel `α` toward melt/water; vapor only via PhaseChange | later |
| Roots / turf | reduce entrainment `A` (reuse grass/tree cohesion bonuses) | v2 |

`β` is a material/category dial (clay smear vs dry sand grainy). Expose in tab settings.

---

## 7. Tick order (target)

Align with `WaterPlan.MD` spirit:

1. Material thermal / phase (existing)
2. **Build affinities** (read-only)
3. **MaCE redistribute** mobile channels
4. Capacity projection + structural ID reconcile
5. Combustion / light / weather / water cycle (existing)
6. Groundwater / hydrostatic (existing; consume water fields, don’t double-transport)
7. Slow erosion stress accrual (feeds channels; limited ID flips)
8. Geology volcanism / ash (unchanged until follow-on)
9. Biology

MaCE must not bypass latent-heat boil/freeze accounting.

---

## 8. Conservation and validation culture

Extend the water discipline to solids:

- Every channel transfer subtracts from a source before adding to a destination (or equivalent gather that conserves by construction — MaCE gather does).
- Neighbor agreement from **pre-tick** fields.
- Fixtures:
  - closed-box `∑ρ_sediment` (and later multi-channel totals)
  - repose cone angle vs `physical.z`
  - no negative channels
  - hydrology fixtures remain green
  - determinism under fixed seed
- Do not pin default slider cosmetics (OUTLINE validation policy).

Snapshot versioning: bump when channels appear; migrate Sediment `8` cells → `ρ ≈ 1`, others `0`.

---

## 9. Phased adoption

### Phase 0 — Docs / flags

- This plan + sediment pilot doc
- Feature flag `maceSedimentPilot` (default off)

### Phase 1 — Sediment + gravity/repose

See `Assets/MaCE_SedimentPilot.MD`.

Exit: conservation + repose fixtures green; scarps/piles improve; magma/ash unaffected.

### Phase 2 — Entrainment

- Add `\|flow\|`, runoff, exposed-face terms to `A`
- Soft-crust `ErosionAndCollapse` primarily injects `ρ` under load
- Root cohesion modulates entrainment

### Phase 3 — Shoreline mixtures

- Soil/Clay host may hold `(ρ_sediment, surface water)` without forcing a full Water ID for thin films
- Clarify single source of truth vs Water pixels (quantize view vs retire shallow Water IDs)
- Beach cross-shore sorting demo fixture

### Phase 4 — Karst / solute (optional)

- Dissolved load channel; carving reduces structural integrity → ID/porosity change after mass loss
- Keep groundwater `aux.y` ledger

### Phase 5 — Magma / ash revisit

- Only if claim-based `EruptionMotion` / `AshTransport` remain limiting
- Candidate: MaCE channel for mobile melt/fines with eruption affinity impulses
- Do not merge `aux.w` fault semantics casually

### Phase 6 — Hard-crust wear

- Slow `α` cross-channel from hard crust under sustained stress / bioerosion
- Locked lithology remains default

### Non-goals / escalations

- Full MPM/LBM replace — escalate only if rheology still fails after Phases 1–3
- Cubed-sphere topology — separate track (`OUTLINE` polar stays for now)
- MaCE on flora/fauna genomes — out of scope (morphogenetic life is a different proposal)

---

## 10. Implementation surfaces (expected touch list)

| Area | Likely files |
| --- | --- |
| Channels / ping-pong | `Runtime/Simulation/Gpu/SimulationResources.cs` |
| Schedule | `Runtime/Simulation/Gpu/GpuPassScheduler.cs` |
| Kernels | new `Compute/Simulation/MaceTransport.compute` (or sediment-only first) |
| Motion gating | `Compute/Simulation/MaterialSimulation.compute` |
| Erosion feed | `Compute/Simulation/Hydrology.compute` |
| Structs / helpers | `Shaders/Simulation/Common/SimulationStructs.hlsl` |
| Config / UI | `SimulationConfig.cs`, overlays, inspect |
| Snapshots | `WorldSnapshotService.cs` |
| Tests | EditMode layouts; PlayMode conservation/repose |

Prefer a **dedicated MaCE pass** over stuffing channels into the historical 8-UAV `WriteCell` mega-kernel (DX12 headroom exists; keep passes readable).

---

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| Double-counted water | Don’t MaCE vapor; Phase 3 carefully unifies films |
| Mushy planet | High `β`, capacity caps, locked hard IDs, eligibility mask |
| Perf | Skip cells with `ρ < ε` and non-soft hosts; mask dispatch |
| Visual identity loss | Threshold rendering + existing shade pipeline |
| `aux.w` confusion | Document fault vs erosion; don’t overload further in v1 |
| Scope creep into life | Sediment only until geo/surface wins |

---

## 12. Decision log

| Date | Decision |
| --- | --- |
| 2026-09-11 | MaCE = hybrid mobile-phase transport, not full material CA replace |
| 2026-09-11 | Pilot v1 = sediment + gravity/repose on soft crust `7/8/15` |
| 2026-09-11 | Magma/ash special motion out of scope for v1 |
| 2026-09-11 | Water ledger remains authoritative per `WaterPlan.MD` |
| 2026-09-11 | Substrate = affinity-driven Margolus 2x2 (Ad-MaCA), not Moore softmax gather |
| 2026-09-11 | Mobile channels live in LifeGenome slice 2 so they follow every WriteCell swap |
| 2026-09-11 | Water is source-only until Phase 4 suspend capacity; vapor never enters MaCA |

---

## 13. References

- MaCE paper: https://arxiv.org/abs/2507.12306
- Flow-Lenia (related mass-conserving CA): Plantec et al.
- In-repo: `OUTLINE.MD`, `WaterPlan.MD`, `MaCE_SedimentPilot.MD`
