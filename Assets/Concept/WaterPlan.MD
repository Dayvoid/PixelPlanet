# Water / Weather Realignment

Emergent weather from one thermodynamic rule set. The mass ledger does not change.

## Mass contract

- `state.z` — surface liquid/ice on solids; cloud condensate on Air
- `aux.x` — atmospheric vapor (humidity carried by Air; never a Gas pixel)
- `aux.y` — groundwater on porous hosts; litter moisture on Detritus
- Water (9) / Ice (10) — phase IDs only. Material IDs never invent mass.
- Vapor (11) is retired. Legacy snapshot pixels migrate to Air.

Every transfer subtracts from a source reservoir before adding to a destination. Neighbor exchanges are sized from pre-tick (read-texture) fields so both sides agree.

## Rule set

1. **Saturation.** One Magnus curve `VaporSaturation(T) * vaporCapacityScale` is used by evaporation, condensation, dew, worldgen humidity, and relative-humidity display. Pressure shifts boiling point only.
2. **Evaporation.** `k * max(0, qsat(Tsurf) - qair) * (1 + wind) * availability`. Stops when the air is saturated. Exposed groundwater uses the same deficit, scaled by porosity.
3. **Condensation.** Excess vapor over `qsat(Tair)` becomes cloud `state.z` and releases latent heat.
4. **Dew / frost.** If an exposed surface is colder than the dew point of the Air above, vapor condenses onto `state.z` (ice when frozen).
5. **Adiabatic lapse.** Radial wind cools rising air and warms sinking air in `AtmosphericTransport`. Orographic rain and rain shadows follow topography.
6. **Virtual-temperature buoyancy.** `T * (1 + 0.61 * q)` versus the lapse reference. No separate humidity-buoyancy slider.
7. **Solar.** `LightField` is rebuilt every tick (cloud, vapor, soot, water, canopy). `AtmosphericForcing` heats by `light / heatCapacity`. Night cooling is reduced by local cloud cover.
8. **Precipitation.** Cloud above `cloudRetainMass` autoconverts; updraft suspends; temperature picks Water vs Ice.
9. **Infiltration / percolation / seep.** Film and Water pixels soak into hosts up to porosity. Excess above field capacity percolates inward, then weeps from exposed hosts.
10. **Groundwater boiling.** Hosts hotter than the pressure-adjusted boil point convert `aux.y` to `aux.x` plus a small pressure pulse and hydrothermal nutrients. Geysers and fumaroles emerge from hot rock plus aquifer; there is no geyser timer.
11. **Transpiration.** Grass and tree hydration decay emit vapor on the local cell (vented upward by existing surface venting). Fauna feeding debits algae `state.z`.
12. **Latent heat.** `WaterLatentHeatDelta` everywhere, including combustion and lightning flash steam.

## Tick order

Material thermal/motion → PhaseChange (pixel phase + groundwater boil) → Combustion → LightAttenuation → AtmosphericForcing → Continuity → PressureDiffusion → Dynamics → AshTransport → Transport (advection + lapse) → WaterCycle (evap/condense/dew) → Precipitation → Storm → Groundwater (soak/percolate/seep) → Hydrostatic leveling → slow erosion/detritus → biology.

## Config (after)

Removed: `saturationCapacityScale` (renamed `vaporCapacityScale`), `humidityBuoyancy`, `capillaryEvaporationFraction`, `rimCoolingRadius`, `rimCoolingStrength`, `springHeadThreshold`, `geyserHeatThreshold`, `geyserDischargeRate`, `geyserCooldownSeconds`, `solarTerrainPenetration`, `terrainSolarHeating`, `atmosphereSolarHeating`, `convectiveBreakthroughTemp`, `windInertiaCoupling`, `cloudPrecipitationThreshold` (renamed `cloudRetainMass`).

Added / merged: `solarIntensity`, `atmosphereAbsorption`, `vaporCapacityScale`, `cloudRetainMass`. `hydrostaticIterations` default is 32.

## Phase 0 baseline (shipping Standard, pre-realignment)

Recorded from `SimulationConfig.asset` and existing conservation fixtures (not a live 2000-tick capture):

- Ocean coverage target 0.5, groundwater saturation 0.65, atmospheric humidity seed 0.7 (absolute vapor mass, not RH)
- Evaporation 0.5, condensation 0.0125, precipitation 0.125, saturation scale 0.01, cloud retain 0.9
- Conservation fixtures: `HydrologyIntegrationTests.FullWaterLoopHoldsItsMassOverManyTicks`, `WeatherIntegrationTests.WeatherCycleConservesTrackedWater`

Post-realignment worldgen seeds vapor as `initialAtmosphericHumidity * VaporSaturation(T)` so 0.7 means 70% RH.

## Phase 7 results

Conservation (required gates):

- `HydrologyIntegrationTests.FullWaterLoopHoldsItsMassOverManyTicks` — passed
- `WeatherIntegrationTests.WeatherCycleConservesTrackedWater` — passed
- EditMode suite — passed (90 tests), including WaterPhase wiring and Vapor-retired brush catalog (14 geology materials)

Mechanism fixtures (PlayMode):

- Humid air suppresses evaporation — passed
- Wind increases evaporation — passed
- Rising air cools adiabatically — passed
- Cold surface collects dew — passed
- Cloud-shaded ground heats less than a clear neighbor — passed
- Saturated exposed soil weeps — passed
- Hot aquifer boils without inventing mass — passed
- Grass patch conserves tracked water including hydration — passed (soil receipts are 1:1; the old 4× fill invented vapor on transpiration)

Standard preset after realignment:

- `solarIntensity` 0.8, `atmosphereAbsorption` 0.35, `vaporCapacityScale` 0.01, `cloudRetainMass` 0.9, `hydrostaticIterations` 32
- Worldgen humidity is relative: `initialAtmosphericHumidity * VaporSaturation(T)`
- Snapshot version 13 writes the full settings JSON; material 11 migrates to Air

Emergent checklist (rule-set + fixtures; live Terrarium playthrough still recommended):

- Land/sea breeze reversal across the day cycle: expected from virtual-temperature buoyancy plus the day/night `LightField`; not visually timed in this pass
- Windward rain and lee shadow on a ridge: expected from adiabatic lapse on radial flow; rising-air cooling fixture is green
- Nocturnal valley fog and dew: dew/frost fixture is green; RH haze is in the material overlay
- Cumulus over hot dry ground: deficit evaporation + lapse condensation; humid-vs-dry evaporation fixture is green
- Storm charge in tall convective towers: unchanged storm path; flash steam now uses `WaterLatentHeatDelta`
- Steam venting over shallow magma: groundwater boil in PhaseChange; hot-aquifer fixture is green
- Standard preset quasi-steady vapor / cloud fraction: conservation holds on Validation; retune `evaporationRate` / `vaporCapacityScale` in play if Standard clouds look too thin after the RH-seeded worldgen change
