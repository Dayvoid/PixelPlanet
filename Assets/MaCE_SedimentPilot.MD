# MaCE Sediment Pilot

Executable Phase 0/1 specification for `Assets/MaCEPlan.MD`. Later phases reuse the same ledger.

## Channel contract

`MobileMass` is a persistent `R32F` Tex2DArray. One full simulation pixel is mass `1`. Conservation is the unweighted sum of channel values. Polar geometry changes slope and affinity only.

| Slice | Name | Phase |
| --- | --- | --- |
| 0 | Coarse / ordinary sediment | 1 |
| 1 | Fine / clay sediment | 3 |
| 2 | Solute | 4 |
| 3 | Magma | 5 |
| 4 | Ash | 5 |
| 5 | Structural mass | 1+ |

Derived scratch (never snapshotted): affinity, neighborhood normalizer, transfer/overflow, sediment columns.

`state.z` remains the only surface-water ledger. Air `state.z` is cloud condensate.

## Loose-fill IDs

- Mobile sediment may occupy Air/Void/Sediment.
- Soil/Clay/hard lithology stay structural until structural mass is debited.
- ID `8` is hysteretic: fill at `maceSedimentFillThreshold`, clear at `maceSedimentClearThreshold`.
- Combined solid budget: `structural + coarse + fine + solute + magma + ash`.

## Transport

Under-relaxed source-normalized MaCE on the Moore neighborhood (self included, radial edges skipped, theta wraps):

```
retain (1-λ) at the source
distribute λ · exp(β(A_d - Amax)) / Z
```

`λ = 0` when a pile is supported and at or below repose. Overflow never destroys mass: unused excess stays at the source.

## Tick order

Material thermal / legacy motion → PhaseChange → MaCE build/normalize/transport → geology/combustion/weather/water → groundwater/hydrostatic → stress + structural transfers → ID reconcile → biology.

## Phase flags

All default off. Previous path stays live until that phase's fixtures pass.

## Non-goals

No vapor invention, no `rho_water_mobile`, no cubed-sphere change, no flora/fauna MaCE.
