> **SUPERSEDED.** Continuous MaCE transport was never the live path. GeneSys uses discrete Margolus CA (MargolusTransport.compute). Kept as historical design notes only.
# MaCE Sediment Pilot (v1)

Pressure-test continuous mobile solids transport on soft crust before wider MaCE adoption.
See `Assets/MaCEPlan.MD` for the full transport design.

**Status:** design only — not implemented.
**Refs:** Papadopoulos & Guichard, *MaCE* (arXiv:2507.12306); soft crust helpers in `SimulationStructs.hlsl`.

---

## Goal

Prove that affinity-driven, mass-conserving redistribution of a sediment channel improves geo/surface behavior versus whole-cell sand slides and erosion ID flips — without touching the water mass ledger, magma, or ash.

Success = visibly better piles/scarps **and** a green solids-conservation fixture. Fail = mushy crust, or no improvement until flow entrainment is added.

---

## In scope

- Materials: Soil `7`, Sediment `8`, Clay `15` (`IsSoftCrustMaterial`)
- One continuous channel: `ρ_sediment` (mobile fines / soft solids mass)
- Affinity from **gravity + angle of repose only**
  - `MaterialGpuData.physical.z` = repose radians
  - `MaterialGpuData.physical.y` = rigidity
- Replace for these materials only:
  - diagonal slide branch in `MaterialMotion` (`MaterialSimulation.compute`)
  - soft-crust climax of `ErosionAndCollapse` as the *sole* solids mover (stress may still accrue; conversion feeds `ρ` instead of teleporting identity alone)

## Out of scope

- Magma `6` / `Volcanism` / `EruptionMotion`
- Ash `12` / `AshTransport`
- Water contract (`state.z`, `aux.x`, `aux.y`) — read-only for cohesion/capacity hints at most
- Hard crust `4` / `5` / `14` → sediment cross-channel wear
- Flow / wind / hydraulic-head terms in affinity (pilot v2+)
- Shoreline dual water+sediment occupancy (pilot v3)
- Changing `aux.w` fault-vs-erosion dual use

---

## Cell contract

| Field | Role |
| --- | --- |
| `materialId` | Structural host. Soft crust stays `7/8/15`. Open carriers may hold loose `ρ`. |
| `ρ_sediment` | Conserved mobile solids. New ping-ponged texture (or Tex2DArray slice). |
| `physical.z` | Repose angle → unsupported lateral drive. |
| `physical.y` | Rigidity → damps lateral affinity / raises stick. |
| `aux.w` | Erosion stress on crust unchanged; high stress **feeds** `Δρ` from structural mass instead of only flipping ID. |

### Structural reconcile (after MaCE)

1. Soft host with near-zero `ρ` and cleared stress → keep Soil/Clay identity.
2. Soft host that has shed most structural mass into `ρ` → Sediment `8`, or Air if empty.
3. Open cell with `ρ` above display/occupancy threshold → prefer material `8` for brush/inspect compatibility (alternative: keep Air and render from `ρ`; pick one before coding).

---

## Affinity (v1)

Per eligible cell (soft crust or open cell with `ρ > ε`):

```text
A = -k_g * y_radial
    - k_lat * unsupported_vs_repose
    + k_pile * support_from_below
```

- **Gravity:** mass prefers lower radius (planetoid down).
- **Unsupported / repose:** slopes steeper than `physical.z` get strong downhill lateral affinity; rigidity scales `k_lat` down.
- **Pile support:** high `ρ` or rigid neighbor below reduces lateral drive.
- **β:** start in ~4–8; expose as sim setting. No flow, wind, or head in `A` for v1.

Use pre-tick `ρ` and materials only (match Groundwater donor/receiver agreement culture).

---

## Tick placement

After `PhaseChange` / `LiquidDensityExchange`, before Groundwater slow erosion:

1. Build `A` from pre-tick fields.
2. MaCE gather update on `ρ` (Moore neighborhood; polar clamp/wrap as today).
3. Structural reconcile / optional ID rewrite.
4. `ErosionAndCollapse` may still accrue `aux.w`; threshold preferably writes `Δρ` rather than hard ID teleport for soft crust.

Leave `AshTransport`, `EruptionMotion`, `Volcanism`, `CoreReaction` untouched.

---

## Resources / persistence

- Allocate `ρ` ping-pong in `SimulationResources` (not jammed into the generic 8-UAV `WriteCell` path if it forces packing games — dedicated pass like grass/storm).
- Snapshot: bump version; serialize `ρ`; older saves initialize `ρ = 1` on Sediment `8` cells and `0` elsewhere (migration).
- Config: `k_g`, `k_lat`, `k_pile`, `β`, occupancy threshold, enable flag (`maceSedimentPilot`).

---

## Acceptance tests

1. **Closed box:** `∑ρ` constant over N ticks with brushes off.
2. **Repose cone:** dumped pile relaxes to angle ≈ `physical.z` (± tolerance).
3. **No water regression:** existing hydrology/weather conservation fixtures stay green.
4. **Cliff:** unsupported soft column sheds `ρ` downhill instead of flickering ID swaps.
5. **Determinism:** same seed → bit-identical `ρ` field.
6. **Non-interference:** magma eruption + ash settle smoke unchanged.

Limit automated validation to fixtures that exercise this pilot (GeneSys validation policy in `OUTLINE.MD`).

---

## Implementation checklist

- [ ] `ρ_sediment` resources + ping-pong
- [ ] Affinity kernel (gravity + repose + rigidity)
- [ ] MaCE redistribute kernel (gather form)
- [ ] Structural reconcile
- [ ] Gate soft-crust diagonal motion when pilot enabled
- [ ] Soft-crust erosion → `Δρ` path
- [ ] Config + overlay/inspect for `ρ`
- [ ] Snapshot migration
- [ ] EditMode packing/defaults + PlayMode conservation/repose fixtures

---

## Exit criteria → next pilots

| Outcome | Next |
| --- | --- |
| Pass | Pilot v2: add `\|flow\|` / runoff entrainment to `A` |
| Pass | Pilot v3: shoreline Soil host + `(ρ_sed, ρ_water)` coexistence |
| Fail (mush) | Raise `β` / capacity caps; narrow eligible mask |
| Fail (needs continuum rheology) | Escalate soft mud to MLS-MPM hybrid; keep MaCE for dry fines |
