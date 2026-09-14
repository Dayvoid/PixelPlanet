using System.Collections.Generic;
using GeneSys.Configuration;
using GeneSys.Materials;

namespace GeneSys.UI
{
    public static class SimulationSettingTooltips
    {
        private static readonly Dictionary<string, string> Tooltips = new()
        {
            [nameof(SimulationConfig.preset)] =
                "Selects polar grid resolution and regenerates the world. Validation is a coarse, fast sandbox; Standard is the intended terrarium; Stress is a high-resolution GPU load test. Geology, hydrology, weather, and ecology all resolve more detail at larger presets.",
            [nameof(SimulationConfig.grid)] =
                "Polar cell count plus atmosphere and visual-core bounds. Driven by Preset rather than edited here. Larger grids increase fidelity for faults, coasts, wind, and colonies at a steep dispatch cost.",
            [nameof(SimulationConfig.ticksPerSecond)] =
                "Target simulation ticks per wall-clock second. Higher values make heat, pressure, water, volcanism, and mycology evolve faster in real time and raise GPU dispatch load.",
            [nameof(SimulationConfig.simulationSpeed)] =
                "Multiplies how many ticks are consumed per frame without changing per-tick physics. Use it to scrub geology, weather, and ecology through time while leaving rates and coefficients unchanged.",
            [nameof(SimulationConfig.materialSubsteps)] =
                "Splits each tick's material pass into extra substeps. Higher values stabilize gravity settling, density sorting, heat, and charge at more GPU cost per tick.",
            [nameof(SimulationConfig.slowPassInterval)] =
                "Runs volcanism, erosion/collapse, ash fertilization, mycology colony updates, flora lifecycle, and flora migration every N ticks. Lower values make geology and ecology more responsive; higher values save GPU time.",
            [nameof(SimulationConfig.transportPassInterval)] =
                "Runs flora light, spore transport, photosynthesis, and mycology spore transport every N ticks. Keep at 1 for tests; 2 cuts ecology GPU work without changing weather or hydrology CFL.",
            [nameof(SimulationConfig.seed)] =
                "Worldgen random seed for layer noise, faults, ocean basins, metal veins, ice caps, and initial spore patches. Change it and regenerate to get a different planetoid with the same settings.",
            [nameof(SimulationConfig.useOgWorldgen)] =
                "Uses the original concentric worldgen instead of V2. Disables metal veins and ice caps, producing simpler layered crust, mantle, and soil at the cost of coastal and ore variety.",

            [nameof(SimulationConfig.coreRatio)] =
                "Radial share of molten core during worldgen. A thicker core stores more heat and mantle pressure, feeding volcanism, hydrothermal vents, and deep temperature gradients.",
            [nameof(SimulationConfig.mantleRatio)] =
                "Radial share of mantle rock. A thicker mantle buffers the crust from core heat, changes fault paths, and alters how magma pressure builds toward the surface.",
            [nameof(SimulationConfig.crustRatio)] =
                "Radial share of crustal rock and basalt. Thicker crust resists extrusion and cave collapse; thinner crust makes volcanic breakthroughs and hydrothermal discharge more likely.",
            [nameof(SimulationConfig.soilRatio)] =
                "Radial share of surface soil. More soil increases infiltration, nutrient storage, mycology habitat, and erodible cover over rock.",
            [nameof(SimulationConfig.borderNoise)] =
                "How irregular layer boundaries are at generation. Higher noise breaks concentric shells into faulted, interfingering contacts that seed uneven pressure, aquifers, and magma paths.",
            [nameof(SimulationConfig.protrusionChance)] =
                "Chance that a denser inner layer punches into the layer above during worldgen. Creates dikes, irregular crust, and local stress that later volcanism and hydrology exploit.",
            [nameof(SimulationConfig.groundwaterDepth)] =
                "How deep the initial water table sits inside the crust. Deeper tables favor aquifers and springs; shallower tables wet the soil and speed infiltration, caves, and mycology.",
            [nameof(SimulationConfig.tectonicFaultSeedCount)] =
                "Number of worldgen weakness bands written into the geodynamics lattice. More seeds bias aquifers and later strain toward a few persistent fault zones without storing tectonic stress in surface cells.",
            [nameof(SimulationConfig.targetOceanCoverage)] =
                "Target fraction of surface angle covered by ocean basins. Higher coverage expands the water cycle, coastal weather, hydrothermal vents, and spore transport by water.",
            [nameof(SimulationConfig.minOceanBasins)] =
                "Minimum separated ocean basins to place. More basins split coastlines, isolate weather cells, and create multiple shoreline habitats for runoff and mycology.",
            [nameof(SimulationConfig.maxOceanBasins)] =
                "Maximum separated ocean basins to place. Used with Min Ocean Basins to control how fragmented the seas, coasts, and vapor sources are.",
            [nameof(SimulationConfig.seaLevelRadius)] =
                "Optional explicit sea-level radius. Leave at 0 to derive sea level from basin carving; raising it floods more crust and shrinks playable land for soil and colonies.",
            [nameof(SimulationConfig.basinDepth)] =
                "How deeply ocean basins are carved into crust and soil. Deeper basins store more surface water, steepen coasts, and couple more strongly to hydrothermal vents.",
            [nameof(SimulationConfig.terrainRelief)] =
                "Vertical roughness of generated land. Higher relief creates headlands, inland ponds, runoff channels, and varied solar heating across slopes.",
            [nameof(SimulationConfig.coastRoughness)] =
                "How jagged generated coastlines are. Rougher coasts lengthen the shoreline, increase spray and vapor exchange, and fragment near-shore mycology habitat.",
            [nameof(SimulationConfig.frozenOceans)] =
                "Fills generated ocean basins with ice instead of liquid water. Glaciers stay frozen only while temperatures stay below 0°C; default warm air will thaw them after regenerate unless you also cool the world.",
            [nameof(SimulationConfig.initialGroundwaterSaturation)] =
                "Starting fill of aquifer cells. Wetter ground feeds springs and capillary moisture into soil; drier ground delays hydrology until rain and infiltration catch up.",
            [nameof(SimulationConfig.initialAtmosphericHumidity)] =
                "Starting airborne vapor load. Higher humidity condenses into clouds and rain sooner and gives spores a moister aerial transport medium.",
            [nameof(SimulationConfig.metalVeinCount)] =
                "Number of metal ore veins placed by V2 worldgen. Veins add dense, conductive inclusions that alter heat, charge, and local geology; ignored by OG worldgen.",
            [nameof(SimulationConfig.metalVeinMinSize)] =
                "Minimum radius of generated metal veins. Smaller veins scatter conductive hotspots; larger minimums make ores more continuous through mantle and crust.",
            [nameof(SimulationConfig.metalVeinMaxSize)] =
                "Maximum radius of generated metal veins. Larger veins create broad dense bodies that resist erosion and strongly conduct heat and electricity.",
            [nameof(SimulationConfig.metalVeinProtrusionChance)] =
                "Chance a metal vein breaches into an outer layer. Surface-reaching ore changes local density sorting, heat, and the materials available to tools and ecology.",
            [nameof(SimulationConfig.metalVeinProtrusionDistance)] =
                "How far a protruding vein may punch outward. Longer protrusions can reach soil or seafloor, exposing metal to water, charge, and weathering.",
            [nameof(SimulationConfig.limestoneDepositCount)] =
                "Number of limestone bodies placed in the granite crust before metal veins. More deposits create porous, insulating patches that steer interior heat and groundwater.",
            [nameof(SimulationConfig.limestoneDepositMinSize)] =
                "Minimum radius of limestone bodies. Smaller pockets scatter karst and heat bottlenecks; larger minimums make more continuous carbonate belts.",
            [nameof(SimulationConfig.limestoneDepositMaxSize)] =
                "Maximum radius of limestone bodies. Larger deposits insulate the crust, hold more groundwater, and karst faster than granite.",
            [nameof(SimulationConfig.limestoneDepositProtrusionChance)] =
                "Chance a limestone body breaches the granite band. Surface-reaching carbonate changes weathering, aquifers, and local heat flux.",
            [nameof(SimulationConfig.limestoneDepositProtrusionDistance)] =
                "How far a protruding limestone body may punch into mantle or soil. Longer protrusions expose porous rock to magma heat or surface water.",
            [nameof(SimulationConfig.clayDepositCount)] =
                "Number of clay lenses placed in the soil band before metal veins. Clay holds water and conducts heat, so more lenses create patchy surface climates.",
            [nameof(SimulationConfig.clayDepositMinSize)] =
                "Minimum radius of clay lenses. Smaller pockets add local thermal and moisture contrast without replacing whole soil provinces.",
            [nameof(SimulationConfig.clayDepositMaxSize)] =
                "Maximum radius of clay lenses. Larger beds store more groundwater and leak interior heat into the air more readily than ordinary soil.",
            [nameof(SimulationConfig.clayDepositProtrusionChance)] =
                "Chance a clay lens punches out of the soil band. Surface clay changes runoff, evaporation, and the heat handed to weather.",
            [nameof(SimulationConfig.clayDepositProtrusionDistance)] =
                "How far a protruding clay lens may reach into granite or air. Longer protrusions couple aquifers to the surface energy budget.",
            [nameof(SimulationConfig.iceCapRadius)] =
                "Angular size of polar ice caps in V2 worldgen. Larger caps lock surface water as ice, cool nearby air, and shrink ice-free habitat for mycology.",
            [nameof(SimulationConfig.iceCapHeight)] =
                "Radial thickness of polar ice. Thicker caps store more frozen surface water and insulate the crust beneath from solar heating.",
            [nameof(SimulationConfig.iceCapRadiusVariation)] =
                "Random variation in ice-cap angular size. Higher variation makes one pole icier than the other, biasing weather, albedo, and coastal freeze patterns.",
            [nameof(SimulationConfig.iceCapHeightVariation)] =
                "Random variation in ice-cap thickness. Uneven caps change how much water is locked in ice versus available to runoff and the vapor cycle.",

            [nameof(SimulationConfig.gravityStrength)] =
                "Downward force on loose grains, liquids, magma, and ash. Stronger gravity speeds settling and density sorting; weaker gravity lets ash, vapor, and eruptions loft farther.",
            [nameof(SimulationConfig.thermalRate)] =
                "How quickly heat conducts between neighboring cells. Higher values even out temperatures, melt and freeze faster, and couple solar weather to geology more tightly.",
            [nameof(SimulationConfig.thermalMoistureBoost)] =
                "How strongly groundwater and surface film raise a cell's effective conductivity. Wet porous ground and clay leak heat faster; dry pores insulate.",
            [nameof(SimulationConfig.thermalPressureEffect)] =
                "How strongly air pressure scales atmospheric conductivity. Thin highs insulate; dense lows transfer more heat between surface and sky.",
            [nameof(SimulationConfig.electricalRate)] =
                "How quickly charge spreads through conductive materials. Affects electrical expansion, mycology electrical stress, and any future bioelectric sensing.",
            [nameof(SimulationConfig.phaseHysteresis)] =
                "Temperature buffer around melt and boil points. Higher hysteresis slows flickering phase changes in magma, water, ice, and vapor, stabilizing weather and geology.",

            [nameof(SimulationConfig.geodynamicsLayerEnable)] =
                "Runs the coarse interior lattice that stores heat anomalies, melt overpressure, tectonic strain, and fault weakness. Disable it to freeze regional geology while leaving painted magma and weather intact.",
            [nameof(SimulationConfig.geodynamicsAngularBins)] =
                "How many angular sectors the interior lattice uses. More bins resolve narrower plumes and fault zones at a small extra dispatch cost; 64 is the Standard default.",
            [nameof(SimulationConfig.geodynamicsRadialBins)] =
                "How many radial shells the interior lattice uses from core to atmosphere. More shells separate deep melt from crustal strain without adding a full-grid geology pass.",
            [nameof(SimulationConfig.geodynamicsPeriodTicks)] =
                "Ticks between lattice aggregate, convection, and event-selection steps. Lower values make pressure and strain evolve faster; higher values keep geology slow relative to weather.",
            [nameof(SimulationConfig.geodynamicsConvectionStrength)] =
                "How strongly thermal anomalies drive slow angular and radial mantle flow on the lattice. Higher convection shifts heat and strain between neighboring sectors over many ticks.",
            [nameof(SimulationConfig.geodynamicsPressureBuildRate)] =
                "How quickly melt-bearing lattice cells accumulate overpressure. This replaces the old global mantle-pressure feed so only active interior regions pressurize.",
            [nameof(SimulationConfig.geodynamicsPressureLeakage)] =
                "How fast overpressure drains through damaged faults and ordinary leakage. Higher leakage prevents runaway reservoirs and lengthens the quiet rebuild after a release.",
            [nameof(SimulationConfig.geodynamicsHeatCoupling)] =
                "How tightly fine-grid temperature feeds the lattice thermal anomaly. Stronger coupling lets core heat and surface cooling reshape convection cells.",
            [nameof(SimulationConfig.tectonicStrainGain)] =
                "How quickly lattice flow, divergence, and overpressure become stored elastic strain. Higher gain shortens the time to the next earthquake-prone local maximum.",
            [nameof(SimulationConfig.tectonicStrainTransfer)] =
                "How much strain diffuses into neighboring sectors, including aftershock loading after a quake. Higher transfer creates event sequences without ringing the whole planet.",
            [nameof(SimulationConfig.tectonicFaultHealing)] =
                "How fast lattice fault weakness recovers between events. Higher healing closes old paths; lower healing keeps dikes and vents reusable.",
            [nameof(SimulationConfig.tectonicEarthquakeThreshold)] =
                "Strain a sector must exceed, as a local maximum, before an earthquake release can fire. Higher thresholds make quakes rarer and more localized.",
            [nameof(SimulationConfig.tectonicReleaseFraction)] =
                "Share of stored regional strain spent when a quake fires. Capped so a single event cannot empty the interior or affect the whole world.",
            [nameof(SimulationConfig.tectonicEventFootprint)] =
                "Angular fraction a release can influence. The default stays well under a tenth of the circumference so most surface columns stay quiet.",
            [nameof(SimulationConfig.tectonicCooldownTicks)] =
                "Refractory time after a sector releases. During cooldown, strain can rebuild but that sector will not fire another event.",
            [nameof(SimulationConfig.tectonicMaxConcurrentEvents)] =
                "Soft cap on how many lattice cells may hold an active release at once. Defaults keep activity regional rather than globally synchronized.",
            [nameof(SimulationConfig.tectonicSurfaceCoupling)] =
                "How much a seismic envelope adds to surfaceFailureStress on weak, wet, exposed, or unsupported crust. It never directly replaces terrain cells.",
            [nameof(SimulationConfig.tectonicKinematicCoupling)] =
                "Master mix for crustal kinematics. At 0, the lid stays put. Raising it lets lattice flow, convergence, and coseismic slip move terrain cells in spaced increments (unlike surface coupling, which only loads failure stress).",
            [nameof(SimulationConfig.tectonicUpliftScale)] =
                "How strongly buoyant flow and overpressure raise or lower the lithospheric lid by shifting whole columns. Higher values grow ridges and basins faster.",
            [nameof(SimulationConfig.tectonicConvergenceScale)] =
                "How much angular shortening lifts a column and how much extension drops it. Shortening builds relief; extension opens grabens.",
            [nameof(SimulationConfig.tectonicDisplacementScale)] =
                "How strongly angular lattice flow shears lid cells sideways. Higher values offset strata along a sector; polar metric keeps outer rings from racing.",
            [nameof(SimulationConfig.tectonicCoseismicScale)] =
                "Extra vertical throw and strike-slip added inside an earthquake envelope. Pulses rupture motion without changing the seismic stress path.",
            [nameof(SimulationConfig.extrusionRate)] =
                "How quickly overpressured magma is driven toward the surface once the lattice supplies a volcanic envelope. Raises flow and cone building without a global pressure feed.",
            [nameof(SimulationConfig.volcanicCoolingRate)] =
                "How fast extruded magma loses heat, especially in air. Higher cooling freezes lava into basalt sooner and shortens surface flows.",
            [nameof(SimulationConfig.magmaViscosity)] =
                "Resistance of magma to flow. Higher viscosity builds steep cones and pressure; lower viscosity favors long, fluid shield-style flows.",
            [nameof(SimulationConfig.volcanicReleaseThreshold)] =
                "Melt-overpressure score a sector must exceed as a local maximum before a volcanic release envelope is created.",
            [nameof(SimulationConfig.volcanicReleaseFraction)] =
                "Share of stored melt overpressure spent when a volcanic release fires. Kept low so vents pulse instead of draining the mantle.",
            [nameof(SimulationConfig.eruptionDriveScale)] =
                "Master mix for fine-grid eruption motion. At 0, magma stays put; raising it enables burden breakthrough, ash blasts, and column flow.",
            [nameof(SimulationConfig.eruptionPressureStrength)] =
                "How much local overpressure contributes to eruption drive. Higher values make trapped magma punch through burden more aggressively.",
            [nameof(SimulationConfig.eruptionFlowStrength)] =
                "How much existing upward magma flow contributes to eruptions. Amplifies ongoing vents into sustained columns rather than one-shot blasts.",
            [nameof(SimulationConfig.eruptionBurdenDepth)] =
                "How many solid cells above magma count as eruptive lid. Deeper burden needs more pressure to blast; shallow burden lets vents open easily.",
            [nameof(SimulationConfig.eruptionBlastThreshold)] =
                "Overdrive level required to explode the lid into ash. Lower thresholds produce frequent ash-rich blasts; higher ones favor quieter lava effusion.",
            [nameof(SimulationConfig.ashUpdraftStrength)] =
                "How strongly eruption and heat loft ash into the atmosphere. Higher values spread ash with wind and weather; lower values keep fallout local.",
            [nameof(SimulationConfig.ashSettlingStrength)] =
                "How quickly airborne ash falls out. Faster settling fertilizes nearby soil; slower settling keeps ash in the air longer for weather and transport.",
            [nameof(SimulationConfig.ashFertilityStrength)] =
                "Nutrient added when ash weathers into soil or sediment. Stronger fertility boosts mycology growth where fallout accumulates.",
            [nameof(SimulationConfig.hydrothermalNutrientRate)] =
                "How strongly hydrothermal boiling deposits nutrients after groundwater boil. Requires heat plus fault weakness or a hydrothermal envelope; mass moves from aux.y to aux.x first.",
            [nameof(SimulationConfig.hydrothermalNutrientYield)] =
                "Nutrient added when hydrothermal boiling occurs. Raises spring and seafloor fertility that mycology and later organisms can exploit.",
            [nameof(SimulationConfig.hydrothermalReleaseThreshold)] =
                "Lattice score needed for a named hydrothermal release. Lower values vent more often along weak, wet sectors; higher values keep chemistry rare.",
            [nameof(SimulationConfig.corePulsePeriodTicks)] =
                "Ticks between core heat pulses. 0 disables pulses. Pulses add temperature only and do not invent interior pressure.",
            [nameof(SimulationConfig.corePulseHeat)] =
                "Temperature added to Core cells on each pulse. Larger pulses warm the geothermal gradient without writing pressure or stress.",
            [nameof(SimulationConfig.coreTemperature)] =
                "Held interior temperature of Core cells and the worldgen geothermal gradient source. Heat filters outward through crust materials into surface weather.",
            [nameof(SimulationConfig.coreHeatRate)] =
                "How quickly Core cells relax toward Core Temperature. Higher rates keep a steadier geothermal source; 0 leaves only the episodic pulse.",

            [nameof(SimulationConfig.infiltrationRate)] =
                "How fast surface water soaks into absorbent, porous ground. Higher infiltration fills aquifers and wets soil for mycology; lower leaves more runoff and ponds.",
            [nameof(SimulationConfig.groundwaterRate)] =
                "How quickly groundwater diffuses through porous crust. Faster aquifers equalize water tables, feed springs, and connect distant basins.",
            [nameof(SimulationConfig.fieldCapacityFraction)] =
                "Share of pore space soil keeps against gravity. Higher values hold rain near the surface for plants and evaporation; lower values let excess soak into deeper aquifers.",
            [nameof(SimulationConfig.dissolutionRate)] =
                "How fast flowing water dissolves non-porous rock into porous cavities. Higher values carve caves, underground rivers, and collapse-prone voids.",
            [nameof(SimulationConfig.collapseRate)] =
                "How readily unsupported cavities fail. Higher collapse reshapes caves into sinkholes and can dam or reroute groundwater.",
            [nameof(SimulationConfig.erosionRate)] =
                "How aggressively surface flow strips soil and sediment. Stronger erosion carves channels, delivers nutrient downstream, and can unroof rock.",
            [nameof(SimulationConfig.baseSoilCohesion)] =
                "Baseline resistance of soil and sediment to erosion. Higher cohesion preserves slopes and mycology habitat; lower cohesion lets rain and runoff reshape land quickly.",
            [nameof(SimulationConfig.surfaceStressRecoveryRate)] =
                "How fast surfaceFailureStress on crust, soil, and clay relaxes. Higher recovery heals slopes; lower recovery lets erosion or seismic coupling accumulate toward collapse.",
            [nameof(SimulationConfig.dryMoistureThreshold)] =
                "Moisture level below which soil loses cohesion. Drier thresholds make arid crust dusty and erodible; wetter thresholds keep banks stable until they dry further.",
            [nameof(SimulationConfig.moistureCohesionStrength)] =
                "How much pore water binds soil. Stronger capillary cohesion resists erosion when damp, then fails suddenly if the ground dries past the threshold.",
            [nameof(SimulationConfig.runoffRate)] =
                "How fast atmosphere-connected surface water levels under hydraulic head. Higher runoff equalizes lakes and spills over sills; lower runoff leaves film and pools in place to soak in.",
            [nameof(SimulationConfig.hydrostaticIterations)] =
                "How many times surface water re-levels against its neighbours each tick. One pass moves water a single column, so low counts leave ocean slopes and rain mounds standing; higher counts settle wide basins quickly at a small solver cost.",
            [nameof(SimulationConfig.pondingRate)] =
                "How strongly shallow surface film resists hydrostatic flow. Higher ponding keeps rain in local puddles instead of sheeting across dry ground; standing water columns still level by head.",
            [nameof(SimulationConfig.springDischargeRate)] =
                "How quickly saturated aquifers above field capacity weep to the surface. Stronger seeps feed streams, wet soil, and can flood low terrain.",

            [nameof(SimulationConfig.enableMaterialTransport)] =
                "Enables discrete Margolus Cellular Automata (MaCA) material transport. Partitions the grid into 2x2 alternating blocks to strictly conserve mass while simulating gravity settling, angle of repose, buoyancy, and fluid leveling.",
            [nameof(SimulationConfig.margolusSubsteps)] =
                "Number of 2-phase Margolus partitioning steps per simulation tick. Higher values accelerate material settling and slope relaxation per frame at a small compute cost.",
            [nameof(SimulationConfig.margolusReposeFriction)] =
                "Resistance against diagonal block sliding. Higher friction maintains steeper natural angles of repose for granular materials like sediment; lower friction lets piles slump flat.",
            [nameof(SimulationConfig.margolusMetricEnable)] =
                "Enables polar metric compensation in Margolus transport, balancing radial vs angular block swap probabilities across differing ring circumferences.",
            [nameof(SimulationConfig.margolusFluidEnable)] =
                "Allows magma to perform lateral leveling swaps. Water stays on the hydrostatic solver for horizontal free-surface leveling.",
            [nameof(SimulationConfig.margolusMagmaLevelingBias)] =
                "Strength of horizontal magma leveling during Margolus block swaps. Water surfaces are leveled only by the hydrostatic column solver.",

            [nameof(SimulationConfig.dayLengthSeconds)] =
                "Orbital period of the solar body in simulated seconds. Shorter days cycle heating, winds, and day/night lighting faster; longer days deepen thermal contrasts.",
            [nameof(SimulationConfig.solarIntensity)] =
                "Peak day heating and photosynthetically active light, falling to zero at the terminators. Cloud, vapor, soot, and canopy in the light field shade both weather and plants.",
            [nameof(SimulationConfig.atmosphereAbsorption)] =
                "How much airborne vapor adds to light attenuation on the way down. Higher values make humid columns shade and heat themselves; zero leaves only cloud, soot, and solid opacity.",
            [nameof(SimulationConfig.solarPolarOutputMin)] =
                "Solar heat and light as a fraction of peak when the sun is over an ice-cap pole. Output eases from 1 at the equator down to this value at both poles, approximating a more distant apoapsis. 1 keeps constant output.",
            [nameof(SimulationConfig.spaceTemperature)] =
                "Temperature the upper atmosphere radiates toward. Colder space strengthens night cooling, lapse, and polar ice; warmer space keeps vapor aloft.",
            [nameof(SimulationConfig.terrainRadiativeCooling)] =
                "How fast the crust radiates heat. Strongest at the outermost terrain cells and falls off exponentially inward, so the mantle stays insulated.",
            [nameof(SimulationConfig.atmosphereRadiativeCooling)] =
                "How fast air above the terrain radius radiates toward space. Stronger cooling steepens night-side temperatures and can collapse vapor into rain or snow. Local cloud cover reduces this loss.",
            [nameof(SimulationConfig.windStrength)] =
                "Forcing that turns pressure and temperature gradients into wind. Stronger wind advects heat, vapor, ash, and spores around the planetoid.",
            [nameof(SimulationConfig.windDamping)] =
                "How quickly wind dies without forcing. Higher damping calms storms; lower damping lets jets persist and carry weather farther.",
            [nameof(SimulationConfig.coriolisStrength)] =
                "Planetary rotation forcing. Deflects vertical updrafts into horizontal winds and vice-versa, breaking diurnal symmetry to generate prevailing trade winds and jet streams. Set to 0 to disable.",
            [nameof(SimulationConfig.velocityAdvectionRate)] =
                "How strongly moving air carries its own momentum across cells. Allows high-speed wind jets to carry forward across weather fronts instead of halting locally. Set to 0 to disable.",
            [nameof(SimulationConfig.frontalLiftStrength)] =
                "Turns horizontal wind convergence into updraft. Opposing fronts lift instead of stalling in a thin band; the rising air feeds lapse cooling, condensation, and lightning. Set to 0 to disable.",
            [nameof(SimulationConfig.frontalCollisionPressure)] =
                "Extra pressure generated where winds collide head-on. Stronger values make fast collisions rebound and let the heavier air mass displace the lighter one. Set to 0 to disable.",
            [nameof(SimulationConfig.frontalDensityDrive)] =
                "Density-driven frontal circulation. Cold dense air undercuts near the surface while warmer air returns aloft, so air masses slide past each other instead of mixing in place. Set to 0 to disable.",
            [nameof(SimulationConfig.frontalSubsidenceScale)] =
                "How strongly divergence sinks relative to convergence lift. Lower values keep frontal updrafts from being cancelled by matching downdrafts. Only applies when frontal lift is enabled.",
            [nameof(SimulationConfig.prevailingWind)] =
                "Direct background zonal wind bias across the atmosphere. Pushes air eastward (positive) or westward (negative) to establish a global prevailing drift. Set to 0 to disable.",
            [nameof(SimulationConfig.evaporationRate)] =
                "How fast a saturation deficit becomes vapor. Evaporation stops when the air is saturated and increases with wind over wet surfaces.",
            [nameof(SimulationConfig.condensationRate)] =
                "How fast saturated air becomes cloud condensate. Higher rates build visible clouds sooner and feed precipitation.",
            [nameof(SimulationConfig.dewRate)] =
                "How fast airborne vapor deposits as dew on surfaces colder than the dew point. Independent of condensationRate, which only forms in-air cloud.",
            [nameof(SimulationConfig.precipitationRate)] =
                "How often dense cloud sheds a rain or snow pixel. Higher rates form drops more often; lower rates keep long-lived clouds. Each airborne drop carries a substantial mass so landed water can pond.",
            [nameof(SimulationConfig.vaporPressureScale)] =
                "Extra pressure generated when liquid boils to vapor. Higher scale makes steam explosions, geysers, and boiling more dynamically violent.",
            [nameof(SimulationConfig.pressureRate)] =
                "How quickly material expansion adds cell pressure, and how fast that pressure relaxes. Couples thermal/electrical swelling to wind, vents, and fractures.",
            [nameof(SimulationConfig.pressureDiffusionRate)] =
                "Master speed of atmospheric and material pressure spreading between neighbors. Faster diffusion smooths storms and mantle pressure; slower diffusion keeps sharp fronts and blasts. This does not level surface water.",
            [nameof(SimulationConfig.gasPressureDiffusivity)] =
                "Pressure conductivity of air and vapor. High values let atmosphere equalize quickly; low values allow local gusts and steam pockets.",
            [nameof(SimulationConfig.fluidPressureDiffusivity)] =
                "Pressure conductivity of liquids for the atmospheric/material pressure field. Affects how storms and magma pressure communicate through water and melt, not free-surface lake leveling.",
            [nameof(SimulationConfig.porousPressureDiffusivity)] =
                "Pressure conductivity of soil, sediment, and porous rock. Controls aquifer transmission and whether buried pressure vents or stays trapped.",
            [nameof(SimulationConfig.rigidPressureDiffusivity)] =
                "Pressure conductivity of solid rock and metal. Near-zero keeps lithostatic loads local; higher values bleed mantle pressure through the crust.",
            [nameof(SimulationConfig.pressureEquilibriumGradient)] =
                "Target radial pressure increase with depth. Higher gradients make denser, more pressurized interiors that feed volcanism and deep flow.",
            [nameof(SimulationConfig.pressureEquilibriumMaximum)] =
                "Cap on equilibrium pressure forcing. Limits how extreme deep pressure can grow relative to the surface.",
            [nameof(SimulationConfig.atmosphericAdvectionRate)] =
                "How strongly wind carries temperature, vapor, and clouds. Higher advection makes weather systems travel; lower keeps local climate locked to terrain.",
            [nameof(SimulationConfig.vaporDiffusionRate)] =
                "Slow mixing of humidity even without wind. Higher diffusion blurs dry and wet air masses; lower keeps sharp humidity fronts.",
            [nameof(SimulationConfig.atmosphericBuoyancy)] =
                "How much virtual-temperature contrast (heat plus humidity) becomes lift. Stronger buoyancy builds updrafts, storms, ash lofting, and vertical mixing of spores.",
            [nameof(SimulationConfig.verticalBuoyancyStrength)] =
                "How strongly a parcel compares itself to the air above and below, after subtracting the expected lapse-rate cooling. Higher values loft uniformly hot or humid surface layers; zero keeps only same-altitude contrast.",
            [nameof(SimulationConfig.vaporCapacityScale)] =
                "Scales the Magnus saturation curve. Higher capacity delays clouds in warm air; lower capacity rains out easily and dries the column.",
            [nameof(SimulationConfig.cloudRetainMass)] =
                "Cloud condensate retained before autoconversion. Higher values keep thicker clouds; lower values let rain and snow form from thinner decks.",
            [nameof(SimulationConfig.waterPressureResponse)] =
                "How strongly local pressure shifts boiling point and precipitation efficiency. Higher response makes highs hold vapor aloft; zero ignores pressure.",
            [nameof(SimulationConfig.latentHeatScale)] =
                "Heat exchanged when water evaporates, condenses, freezes, melts, or boils. Higher values couple storms and thaw to temperature; zero disables latent feedback.",
            [nameof(SimulationConfig.surfaceAirHeatExchange)] =
                "Heat flow between ground and the air above it. Stronger coupling lets soil, water, and lava drive local weather more directly.",
            [nameof(SimulationConfig.temperatureAdvectionRate)] =
                "How much wind carries temperature. Higher values export daytime heat and volcanic warmth downwind; lower values keep hot and cold patches in place.",
            [nameof(SimulationConfig.pressureCompressibility)] =
                "How much flow converges into pressure (and diverges from it). Higher compressibility makes slam-gusts and rarefactions; lower keeps air more incompressible.",
            [nameof(SimulationConfig.atmosphericCflLimit)] =
                "Stability cap on atmospheric transport speed. Lower values prevent blowing up the weather solver; higher values allow faster, riskier advection.",
            [nameof(SimulationConfig.surfaceAirTemperature)] =
                "Baseline air temperature near the ground. Warmer baselines increase evaporation, thaw ice, and widen mycology's growth window.",
            [nameof(SimulationConfig.atmosphericLapseRate)] =
                "How quickly air cools with height. Steeper lapse favors clouds and storms aloft; shallower lapse keeps the column warmer and more stable.",

            [nameof(SimulationConfig.climateLayerEnable)] =
                "Runs the coarse climate pass on a slow cadence. When off, weather uses only the fine pixel stack and climate helpers stay identity.",
            [nameof(SimulationConfig.climatePrevailingInject)] =
                "Injects per-bin thermal/prevailing wind and the seasonal insolation envelope into fine weather. Off keeps local winds and solar unchanged.",
            [nameof(SimulationConfig.climateAlbedoFeedback)] =
                "Lets the coarse ice/snow albedo index scale heating on exposed surfaces. Off leaves LightField absorption as the only shade path.",
            [nameof(SimulationConfig.climateBiomeFeedback)] =
                "Aggregates canopy, organics, and ash into roughness and soil bucket scale. Off keeps wind damping and field capacity at their weather/hydrology sliders.",
            [nameof(SimulationConfig.climateBinCount)] =
                "Number of angular climate bins. More bins resolve rain-shadow and monsoon contrasts; fewer bins stay cheaper and smoother.",
            [nameof(SimulationConfig.climateCouplePeriod)] =
                "Fine ticks between climate aggregate/step/inject updates. Larger periods give seasonal memory; 1 updates every tick for tests.",
            [nameof(SimulationConfig.climateSlabHeatCapacity)] =
                "Thermal inertia of each climate bin. Higher capacity remembers seasons and oceans; lower capacity tracks the surface more closely.",
            [nameof(SimulationConfig.climateHeatTransport)] =
                "How fast neighboring climate bins share heat. Higher transport flattens angular temperature; zero isolates each sector.",
            [nameof(SimulationConfig.climateMemoryRate)] =
                "How quickly ice fraction and wetness ease toward the current surface. Higher memory locks ice edges and droughts in faster.",
            [nameof(SimulationConfig.climateSeasonLengthDays)] =
                "Simulated days in one insolation season cycle. Longer seasons stretch wet/dry envelopes; shorter seasons pulse faster.",
            [nameof(SimulationConfig.climateSeasonalAmplitude)] =
                "How strongly the seasonal envelope scales incoming light. Zero keeps daily insolation only; higher values deepen summers and winters.",
            [nameof(SimulationConfig.climateThermalWindGain)] =
                "How strongly neighboring slab temperatures drive a per-bin zonal wind. Higher gain makes monsoon-like flow toward warm sectors.",
            [nameof(SimulationConfig.climateBaseAlbedo)] =
                "Bare-ground reflectance used when ice, canopy, and ash are absent. Higher base albedo cools the climate slab.",
            [nameof(SimulationConfig.climateIceAlbedo)] =
                "Extra reflectance from the coarse ice index. Stronger ice albedo lets cold sectors lock in.",
            [nameof(SimulationConfig.climateCanopyAlbedoDrop)] =
                "How much living cover darkens a bin. Higher drop makes forests and mats pull more heat into the slab.",
            [nameof(SimulationConfig.climateAshAlbedo)] =
                "Extra reflectance from burn scar and ash cover. Higher values brighten scorched sectors and favor drought lock-in.",
            [nameof(SimulationConfig.climateRoughnessGain)] =
                "How much canopy increases near-surface wind damping. Higher roughness calms local jets over forests.",
            [nameof(SimulationConfig.climateBucketGain)] =
                "How much organics and detritus enlarge soil field capacity. Higher gain holds more groundwater in vegetated bins.",
            [nameof(SimulationConfig.climateBurnBucketPenalty)] =
                "How much ash cover shrinks field capacity. Higher penalty makes burned ground shed water and stay dry.",
            [nameof(SimulationConfig.climateSlabRadiativeCooling)] =
                "Longwave cooling of the coarse climate slab toward space temperature. Independent of atmosphereRadiativeCooling so fine-air and slab energy budgets can be tuned separately. coreTemperature is also the geodynamics radial reference profile.",

            [nameof(SimulationConfig.mycologyInitialSporeLoad)] =
                "Starting airborne and soil spore density at worldgen. Higher loads colonize soil and sediment faster after regenerate.",
            [nameof(SimulationConfig.mycologyRareStrainChance)] =
                "Chance a generated patch carries a rare trait mix. Rare strains can tolerate harsher temperature, moisture, or charge once colonies establish.",
            [nameof(SimulationConfig.mycologyAirTransportRate)] =
                "How strongly wind moves spores. Higher values spread fungi globally with weather; lower values keep inoculation local to source soil.",
            [nameof(SimulationConfig.mycologyWaterTransportRate)] =
                "How strongly runoff, oceans, and groundwater carry spores. Couples hydrology to colonization of distant basins and shores.",
            [nameof(SimulationConfig.mycologyDiffusionRate)] =
                "Slow neighbor mixing of spores without flow. Higher diffusion fills gaps in colonies; lower keeps patchy, isolated stands.",
            [nameof(SimulationConfig.mycologySettlingRate)] =
                "How quickly airborne spores land on soil and sediment. Faster settling inoculates ground under clouds of spores; slower keeps a larger aerial reservoir.",
            [nameof(SimulationConfig.mycologySporulationRate)] =
                "Spores released by established colonies. Higher sporulation feeds air and water transport and can recolonize after die-off.",
            [nameof(SimulationConfig.mycologyGrowthRate)] =
                "How fast colonies expand when soil/sediment sits inside the growth temperature and moisture bands. Stronger growth greens the material view sooner.",
            [nameof(SimulationConfig.mycologyDecayRate)] =
                "How fast colonies and spores die outside survival ranges or under stress. Higher decay makes fungi boom-and-bust with drought, frost, or heat.",
            [nameof(SimulationConfig.mycologyGrowthTempMin)] =
                "Lower temperature for active growth. Raising it confines colonies to warmer soil; lowering it lets fungi spread into cool, damp ground.",
            [nameof(SimulationConfig.mycologyGrowthTempMax)] =
                "Upper temperature for active growth. Lowering it protects fungi from heat but shrinks equatorial and geothermal habitat.",
            [nameof(SimulationConfig.mycologyGrowthMoistureMin)] =
                "Minimum moisture for active growth. Higher values restrict fungi to wet soil, springs, and shores; lower values let them colonize drier crust.",
            [nameof(SimulationConfig.mycologyGrowthMoistureMax)] =
                "Maximum moisture for active growth. Lower values punish waterlogged ground; higher values let colonies thrive in ponds and saturated soil.",
            [nameof(SimulationConfig.mycologySurvivalTempMin)] =
                "Lower temperature colonies can endure without growing. Must stay at or below the growth minimum. Controls winter and polar die-off.",
            [nameof(SimulationConfig.mycologySurvivalTempMax)] =
                "Upper temperature colonies can endure without growing. Must stay at or above the growth maximum. Controls heat-kill near lava and noon tropics.",
            [nameof(SimulationConfig.mycologySurvivalMoistureMin)] =
                "Driest moisture colonies can survive. Below this, drought decay accelerates even if spores remain.",
            [nameof(SimulationConfig.mycologySurvivalMoistureMax)] =
                "Wettest moisture colonies can survive. Extreme floods and submerged sediment kill colonies beyond this cap.",
            [nameof(SimulationConfig.mycologyElectricalTolerance)] =
                "How much charge colonies can withstand. Lower tolerance makes conductive, storm-charged, or metal-rich ground hostile to fungi.",
            [nameof(SimulationConfig.mycologyTraitEffectStrength)] =
                "How strongly rare strain traits shift growth, decay, and transport. 0 ignores genetics; higher values make strain overlays matter more.",

            [nameof(SimulationConfig.floraSeedAtWorldgen)] =
                "When enabled, worldgen places a light flora spore load on air and water so algae can colonize after regenerate. Off keeps existing worlds deterministic.",
            [nameof(SimulationConfig.floraInitialSporeLoad)] =
                "Starting flora spore density when worldgen seeding is on. Higher loads germinate mats faster along coasts and wet soil after a regenerate.",
            [nameof(SimulationConfig.floraAirTransportRate)] =
                "How strongly wind moves flora spores. Higher values spread algae globally with weather; lower values keep inoculation local to source mats.",
            [nameof(SimulationConfig.floraWaterTransportRate)] =
                "How strongly oceans, runoff, and ponds carry flora spores. Couples hydrology to colonization of distant basins, shores, and wet soil.",
            [nameof(SimulationConfig.floraDiffusionRate)] =
                "Slow neighbor mixing of flora spores without flow. Higher diffusion fills gaps in mats; lower keeps patchy, isolated stands.",
            [nameof(SimulationConfig.floraSettlingRate)] =
                "How quickly airborne flora spores land on soil, sediment, water, and existing mats. Faster settling inoculates ground under spore clouds.",
            [nameof(SimulationConfig.floraSporulationRate)] =
                "Spores released by active algae. Higher sporulation feeds air and water transport and can recolonize after drought or fire.",
            [nameof(SimulationConfig.floraGrowthRate)] =
                "How fast active algae biomass expands inside the growth temperature, moisture, and light window. Stronger growth thickens mats sooner.",
            [nameof(SimulationConfig.floraDecayRate)] =
                "How fast desiccated or toxin-stressed algae lose biomass. Higher decay makes boom-and-bust mats; lower lets dry fuel persist for fire.",
            [nameof(SimulationConfig.floraPhotosynthesisRate)] =
                "How quickly sunlit algae convert available light into stored energy toward division. Higher rates fill the reproduction threshold faster.",
            [nameof(SimulationConfig.floraOxygenYield)] =
                "Oxygen emitted into the local combustion field while photosynthesizing. Mats can raise fire risk by oxygenating still air above them.",
            [nameof(SimulationConfig.floraExudationRate)] =
                "Nutrient exudate released by growing algae into a diffusive gradient. Higher exudation marks mats for future chemotactic organisms.",
            [nameof(SimulationConfig.floraReproductionThreshold)] =
                "Stored energy required before an active cell divides into a neighbor. Lower thresholds produce denser mats; higher thresholds rarer splits.",
            [nameof(SimulationConfig.floraBaseMutationRate)] =
                "Baseline per-gene walk applied on division. Higher rates drift the genome faster; toxins and the mutation gene scale this further.",
            [nameof(SimulationConfig.floraToxinMutationScale)] =
                "How much accumulated toxin dose amplifies mutation on division. Couples metal, soot, and charge stress to genetic divergence.",
            [nameof(SimulationConfig.floraGeneExpressionRange)] =
                "How far genes may deviate from baseline envelopes. 0 ignores genetics; higher values make temperature, moisture, and light genes matter more.",
            [nameof(SimulationConfig.floraGrowthTempMin)] =
                "Lower temperature for active photosynthesis and division. Raising it confines mats to warmer shores; lowering it lets algae spread into cool water.",
            [nameof(SimulationConfig.floraGrowthTempMax)] =
                "Upper temperature for active growth. Lowering it protects mats from heat but shrinks equatorial and geothermal habitat.",
            [nameof(SimulationConfig.floraGrowthMoistureMin)] =
                "Minimum moisture for active growth. Higher values restrict algae to wet soil and water; lower values let mossy mats colonize drier crust.",
            [nameof(SimulationConfig.floraGrowthMoistureMax)] =
                "Maximum moisture for active growth. Higher values let mats thrive in ponds; lower values punish fully submerged or waterlogged cells.",
            [nameof(SimulationConfig.floraSurvivalTempMin)] =
                "Lower temperature dormant algae can endure. Must stay at or below the growth minimum. Controls winter and polar die-off versus dormancy.",
            [nameof(SimulationConfig.floraSurvivalTempMax)] =
                "Upper temperature dormant algae can endure. Kept high so fire can consume desiccating mats before heat alone deletes them.",
            [nameof(SimulationConfig.floraSurvivalMoistureMin)] =
                "Driest moisture dormant algae can survive. Below this they desiccate into dry fuel instead of vanishing, so combustion can still find them.",
            [nameof(SimulationConfig.floraSurvivalMoistureMax)] =
                "Wettest moisture dormant algae can survive. Extreme floods beyond this cap desiccate or decay rather than instantly deleting biomass.",
            [nameof(SimulationConfig.floraMinLight)] =
                "Minimum raymarched light for germination and active photosynthesis. Higher values confine mats to the day face and shallow water.",
            [nameof(SimulationConfig.floraGerminationSporeThreshold)] =
                "Spore load required to germinate an air or water cell into Algae/Moss. Higher thresholds need denser inoculation before a mat appears.",
            [nameof(SimulationConfig.floraMaintenanceRate)] =
                "Baseline metabolic drain on stored energy. Higher maintenance makes nights and shade starve mats; lower lets energy bank across days.",
            [nameof(SimulationConfig.floraNightDrain)] =
                "Extra energy drain when local light is below the minimum. Controls how harshly darkness taxes active photosynthesizers.",
            [nameof(SimulationConfig.floraDormancyMetabolicScale)] =
                "Fraction of maintenance applied while dormant. Lower values let spore-like dormancy last through drought; higher values starve sleeping mats.",
            [nameof(SimulationConfig.floraPoleDriftRate)] =
                "How quickly stacked algae drift toward the nearest ice-cap pole. 0 disables pole-seeking; the pole-drift gene can still silence individual cells.",
            [nameof(SimulationConfig.floraWindShearRate)] =
                "How strongly angular wind on neighboring air or water shears exposed algae sideways. Higher values peel crest cells off mats and stacks.",
            [nameof(SimulationConfig.floraRainShearRate)] =
                "How strongly overhead rain and falling water dislodge exposed algae. Couples precipitation to physical spread along the surface.",
            [nameof(SimulationConfig.floraFragmentYield)] =
                "Fraction of sheared biomass converted into airborne or waterborne spores. Higher yield spreads genetics farther when wind and rain strike mats.",
            [nameof(SimulationConfig.floraAnchorGrip)] =
                "How much local biomass resists wind and rain shear. Higher grip keeps thick mats intact; lower grip lets weather tear even dense colonies.",

            [nameof(SimulationConfig.faunaSeedAtWorldgen)] =
                "When enabled, worldgen can initialize cricket state on painted biological cells. Off keeps existing worlds deterministic until you place crickets with the Life brush.",
            [nameof(SimulationConfig.faunaInitialCalories)] =
                "Starting calorie reserve for a newly painted or hatched cricket. Higher values delay the first forage; lower values make new insects hunt immediately.",
            [nameof(SimulationConfig.faunaInitialHydration)] =
                "Starting internal water for a newly spawned cricket or egg yolk. Lower values make desiccation a near-term threat.",
            [nameof(SimulationConfig.faunaMaturityTicks)] =
                "Age in simulation ticks before a juvenile can mate or lay eggs. Default 2000 ticks is about 100 seconds at 20 ticks per second.",
            [nameof(SimulationConfig.faunaDecisionInterval)] =
                "How often grounded crickets re-evaluate forage, flee, wander, and reproduction. Airborne ballistic motion still runs every tick.",
            [nameof(SimulationConfig.faunaMaintenanceRate)] =
                "Calorie drain per second from basal metabolism, scaled by the metabolism gene. Higher values force more frequent feeding.",
            [nameof(SimulationConfig.faunaHydrationDrain)] =
                "Steady internal water loss per second, reduced by the hydration-retention gene. Crickets must drink from eaten algae to survive.",
            [nameof(SimulationConfig.faunaCalorieCapacity)] =
                "Baseline stomach size. The calorie-capacity gene scales this; meals cannot exceed the expressed cap.",
            [nameof(SimulationConfig.faunaFullThreshold)] =
                "Calorie level at which feeding calls are ignored. Sated adults can still answer mating calls.",
            [nameof(SimulationConfig.faunaHungerThreshold)] =
                "Calorie level that promotes urgent foraging even without acoustic cues, following algae exudate and nutrient gradients.",
            [nameof(SimulationConfig.faunaReproductionCalorieThreshold)] =
                "Calories required before an adult may deposit a clutch. Spending on hops and maintenance can delay laying.",
            [nameof(SimulationConfig.faunaHopImpulse)] =
                "Baseline launch impulse. Strength and inverse effective mass (dry mass, calories, and carried moisture) scale the actual hop.",
            [nameof(SimulationConfig.faunaHopCost)] =
                "Calories spent per launch, scaled by effective mass. Heavier, wetter crickets pay more to jump.",
            [nameof(SimulationConfig.faunaFeedCost)] =
                "Calories spent chewing a meal. Remaining algae calories and moisture still transfer to the winner of a feeding claim.",
            [nameof(SimulationConfig.faunaDryMass)] =
                "Baseline body mass used in hop physics. Heavier insects jump shorter distances for the same strength gene.",
            [nameof(SimulationConfig.faunaDrag)] =
                "Air resistance during flight. Combined with the drag gene and wind, it shortens hops into a headwind.",
            [nameof(SimulationConfig.faunaWindResistance)] =
                "How strongly local angular wind accelerates an airborne cricket. Higher values make weather steer ballistic paths.",
            [nameof(SimulationConfig.faunaMoistureMass)] =
                "How much internal hydration adds to effective mass. Wet crickets jump shorter and spend more calories launching.",
            [nameof(SimulationConfig.faunaSupportBoost)] =
                "How much rigid, dry landing ground increases takeoff impulse. Soft or wet surfaces cut jump strength.",
            [nameof(SimulationConfig.faunaWetPenalty)] =
                "How much surface film and groundwater reduce takeoff. Couples hydrology to hopping range.",
            [nameof(SimulationConfig.faunaGeneExpressionRange)] =
                "How far cricket genes may deviate from these baselines. 0 ignores genetics; higher values make strength, mass, and sensing matter more.",
            [nameof(SimulationConfig.faunaBaseMutationRate)] =
                "Per-gene walk applied when eggs are produced, whether cloned asexually or mixed with a retained partner genome.",
            [nameof(SimulationConfig.faunaSenseRadius)] =
                "How many cells nutrient and acoustic gradients are sampled across. Larger radii let crickets home in from farther away.",
            [nameof(SimulationConfig.faunaHearingRange)] =
                "Scales sensitivity to feeding and mating pressure waves. Combined with the hearing gene.",
            [nameof(SimulationConfig.faunaThreatTemperature)] =
                "Temperature above which nearby heat and flame drive a flee hop away from the hazard.",
            [nameof(SimulationConfig.faunaAcousticSpeed)] =
                "Propagation speed of the dedicated feeding/mating wave field. Faster waves notify distant crickets sooner.",
            [nameof(SimulationConfig.faunaAcousticDamping)] =
                "How quickly chirps decay toward silence. Higher damping keeps calls local; lower values let rings travel farther.",
            [nameof(SimulationConfig.faunaFeedCallAmplitude)] =
                "Pulse written to the feeding channel while foraging. Hungry crickets seek this signature unless they are full.",
            [nameof(SimulationConfig.faunaMateCallAmplitude)] =
                "Pulse written to the mating channel while seeking a partner. Juveniles and adults on cooldown ignore it.",
            [nameof(SimulationConfig.faunaMateCooldownTicks)] =
                "Ticks after mating before another partner genome can be stored. Prevents instant re-pairing.",
            [nameof(SimulationConfig.faunaReproduceCooldownTicks)] =
                "Ticks after laying a clutch before the next clutch can be claimed.",
            [nameof(SimulationConfig.faunaClutchMin)] =
                "Smallest egg count an adult will try to place, before the fertility gene scales toward the maximum.",
            [nameof(SimulationConfig.faunaClutchMax)] =
                "Largest egg count an adult will try to place. Default range is 2–4 neighboring supported air cells.",
            [nameof(SimulationConfig.faunaHatchTicksMin)] =
                "Shortest egg incubation in ticks. Each egg hashes a target between min and max (default 1000–1500).",
            [nameof(SimulationConfig.faunaHatchTicksMax)] =
                "Longest egg incubation in ticks. Eggs remain vulnerable to drying, fire, and wind the whole time.",
            [nameof(SimulationConfig.faunaEggDesiccationMoisture)] =
                "Internal hydration below which an egg dies of desiccation. Eggs exchange moisture with their cell.",
            [nameof(SimulationConfig.faunaEggHeatDeath)] =
                "Temperature at which eggs cook. Flame also kills eggs immediately.",
            [nameof(SimulationConfig.faunaEggDisplacement)] =
                "How strongly wind and runoff can claim a neighboring supported cell and blow an egg along the surface.",
            [nameof(SimulationConfig.faunaWanderRate)] =
                "Chance of an idle hop when no food, mate call, or threat is present.",
            [nameof(SimulationConfig.faunaSurvivalTempMin)] =
                "Lower air temperature adult and juvenile crickets can endure. Beyond this they die instead of hopping to safety.",
            [nameof(SimulationConfig.faunaSurvivalTempMax)] =
                "Upper air temperature adult and juvenile crickets can endure. Extreme heat kills even if they are not on fire.",

            [nameof(SimulationConfig.grassSeedAtWorldgen)] =
                "When enabled, worldgen plants a sparse set of adult grasses on exposed soil. Off keeps worlds empty until you seed grass or wait for transported seeds.",
            [nameof(SimulationConfig.grassInitialBiomass)] =
                "Starting blade biomass for a newly germinated or painted grass slot.",
            [nameof(SimulationConfig.grassInitialEnergy)] =
                "Starting photosynthate reserve. Higher values let new plants flower sooner after the 3–4 day wait.",
            [nameof(SimulationConfig.grassPhotosynthesisRate)] =
                "How quickly grass converts light and root water into stored energy.",
            [nameof(SimulationConfig.grassGrowthRate)] =
                "How quickly stored energy becomes blade biomass in the growth niche.",
            [nameof(SimulationConfig.grassDecayRate)] =
                "Biomass and energy loss outside the growth band.",
            [nameof(SimulationConfig.grassMaintenanceRate)] =
                "Baseline energy drain while a grass slot is alive, paid every tick even in the growth band.",
            [nameof(SimulationConfig.grassNightDrain)] =
                "Extra energy drain when photosynthetically active radiation is near zero.",
            [nameof(SimulationConfig.grassWaterUptakeRate)] =
                "How aggressively each slot requests water from its root taps. Competing taps share available moisture.",
            [nameof(SimulationConfig.grassNutrientUptakeRate)] =
                "How aggressively each slot requests soil nutrients from tapped cells.",
            [nameof(SimulationConfig.grassRootCohesionBonus)] =
                "Added soil cohesion per active root tap targeting a cell, scaled by base soil cohesion (0.10 default).",
            [nameof(SimulationConfig.grassFlowerEnergyThreshold)] =
                "Stored energy required before an adult may open a flower at its 3–4 day mark.",
            [nameof(SimulationConfig.grassGeneExpressionRange)] =
                "How far grass genes can shift baseline rates, colors, and root architecture.",
            [nameof(SimulationConfig.grassGrowthTempMin)] =
                "Lower temperature of healthy grass growth.",
            [nameof(SimulationConfig.grassGrowthTempMax)] =
                "Upper temperature of healthy grass growth.",
            [nameof(SimulationConfig.grassGrowthMoistureMin)] =
                "Lower soil moisture of healthy grass growth.",
            [nameof(SimulationConfig.grassGrowthMoistureMax)] =
                "Upper soil moisture of healthy grass growth.",
            [nameof(SimulationConfig.grassSurvivalTempMin)] =
                "Lower temperature grass can endure before dying.",
            [nameof(SimulationConfig.grassSurvivalTempMax)] =
                "Upper temperature grass can endure before dying.",
            [nameof(SimulationConfig.grassSurvivalMoistureMin)] =
                "Lower moisture grass can endure before dying.",
            [nameof(SimulationConfig.grassSurvivalMoistureMax)] =
                "Upper moisture grass can endure before dying.",
            [nameof(SimulationConfig.grassMinLight)] =
                "Minimum PAR required for growth. Night and canopy shade drop plants toward maintenance-only.",
            [nameof(SimulationConfig.grassAdultBiomass)] =
                "Biomass at which a juvenile becomes an unflowered adult.",
            [nameof(SimulationConfig.grassPollenEmitRate)] =
                "Low-rate pollen mass emitted by an open flower into the local cell.",
            [nameof(SimulationConfig.grassPollenTransportRate)] =
                "Baseline pollen movement. Keep low so pollination stays rare without a pollinator.",
            [nameof(SimulationConfig.grassSeedTransportRate)] =
                "Baseline whole-seed movement. Keep low so stands expand slowly.",
            [nameof(SimulationConfig.grassPollenWindRate)] =
                "How strongly angular wind carries pollen between open carriers.",
            [nameof(SimulationConfig.grassPollenWaterRate)] =
                "How strongly runoff and water cells carry pollen.",
            [nameof(SimulationConfig.grassPollenSettlingRate)] =
                "Inward gravity/settling of pollen onto soil and carriers.",
            [nameof(SimulationConfig.grassSeedWindRate)] =
                "How strongly angular wind carries whole seeds.",
            [nameof(SimulationConfig.grassSeedWaterRate)] =
                "How strongly water and runoff carry whole seeds.",
            [nameof(SimulationConfig.grassSeedSettlingRate)] =
                "Inward gravity/settling of seeds onto exposed soil.",
            [nameof(SimulationConfig.grassNectarAmount)] =
                "Nectar written into an open flower. Visiting wasps drink it and carry pollen between lineages.",
            [nameof(SimulationConfig.treeSeedAtWorldgen)] =
                "When enabled, worldgen plants a sparse set of tree sprouts on exposed soil. Off keeps worlds empty until you place a Tree Sprout with the Life brush.",
            [nameof(SimulationConfig.treeInitialEnergy)] =
                "Starting photosynthate reserve for a newly planted tree pixel.",
            [nameof(SimulationConfig.treeInitialHydration)] =
                "Starting water stored in a newly planted tree pixel.",
            [nameof(SimulationConfig.treeInitialNutrient)] =
                "Starting nutrient reserve for a newly planted tree pixel.",
            [nameof(SimulationConfig.treeInitialHealth)] =
                "Starting health of a newly planted tree pixel. Fire, exposure, and starvation debit this independently of the Wood or Leaf material.",
            [nameof(SimulationConfig.treePhotosynthesisRate)] =
                "How quickly leaves and juvenile shoots convert light and hydration into stored energy.",
            [nameof(SimulationConfig.treeGrowthRate)] =
                "How readily a living tip claims a new Wood or Leaf cell when reserves and the growth niche allow it.",
            [nameof(SimulationConfig.treeDecayRate)] =
                "Health drain outside the survival band, paid every tick until the pixel recovers or dies.",
            [nameof(SimulationConfig.treeMaintenanceRate)] =
                "Baseline energy drain paid by every living tree pixel, even in the growth band.",
            [nameof(SimulationConfig.treeNightDrain)] =
                "Extra energy drain when photosynthetically active radiation is near zero.",
            [nameof(SimulationConfig.treeWaterUptakeRate)] =
                "How aggressively live roots request water from neighboring soil. Grass and trees share one soil debit.",
            [nameof(SimulationConfig.treeNutrientUptakeRate)] =
                "How aggressively live roots request soil nutrients from neighboring soil.",
            [nameof(SimulationConfig.treeVascularRate)] =
                "How quickly energy, water, and nutrients mix between a pixel and its parent or children each tick.",
            [nameof(SimulationConfig.treeGrowthCost)] =
                "Energy a winning growth tip spends when it claims a new cell. Water and nutrients are spent at a fraction of this.",
            [nameof(SimulationConfig.treeWindBias)] =
                "How strongly wind and the wind-response gene lean new shoots and the sprout L leaf. Established Wood does not move.",
            [nameof(SimulationConfig.treeGeneExpressionRange)] =
                "How far tree genes may push metabolism, height, branching, leaf life, and wind response away from the baselines.",
            [nameof(SimulationConfig.treeGrowthTempMin)] =
                "Lower temperature of the healthy tree growth band.",
            [nameof(SimulationConfig.treeGrowthTempMax)] =
                "Upper temperature of the healthy tree growth band.",
            [nameof(SimulationConfig.treeGrowthMoistureMin)] =
                "Lower soil moisture of the healthy tree growth band.",
            [nameof(SimulationConfig.treeGrowthMoistureMax)] =
                "Upper soil moisture of the healthy tree growth band.",
            [nameof(SimulationConfig.treeSurvivalTempMin)] =
                "Lower temperature a living tree pixel can endure before taking decay damage.",
            [nameof(SimulationConfig.treeSurvivalTempMax)] =
                "Upper temperature a living tree pixel can endure before taking decay damage.",
            [nameof(SimulationConfig.treeSurvivalMoistureMin)] =
                "Lower moisture a living tree pixel can endure before taking decay damage.",
            [nameof(SimulationConfig.treeSurvivalMoistureMax)] =
                "Upper moisture a living tree pixel can endure before taking decay damage.",
            [nameof(SimulationConfig.treeMinLight)] =
                "Minimum PAR required for growth claims. Crowns still photosynthesize below this, just more slowly.",
            [nameof(SimulationConfig.treeSproutHeight)] =
                "Radial cells a sprout grows as a green juvenile shoot before placing its inverted-L leaf.",
            [nameof(SimulationConfig.treeSaplingHeight)] =
                "Radial cells at which a sprout becomes a sapling with a Wood trunk and the first 2–4 branches.",
            [nameof(SimulationConfig.treeMaxHeight)] =
                "Soft cap on mature crown height, scaled per genome. Wind curvature stays bounded inside this envelope.",
            [nameof(SimulationConfig.treeMaxTrunkWidth)] =
                "Maximum angular thickness of a mature trunk, in cells.",
            [nameof(SimulationConfig.treeSaplingBranchMin)] =
                "Fewest sapling branches a genome may select.",
            [nameof(SimulationConfig.treeSaplingBranchMax)] =
                "Most sapling branches a genome may select.",
            [nameof(SimulationConfig.treeRootCohesionBonus)] =
                "Added soil cohesion per adjacent live tree root, scaled by base soil cohesion.",
            [nameof(SimulationConfig.treeCanopyOpacity)] =
                "How strongly Leaf and Wood attenuate the shared flora light field so crowns shade grass, algae, and lower leaves.",
            [nameof(SimulationConfig.treeExposureDamage)] =
                "Health drain per second for live roots that touch Air. Buried roots are safe.",
            [nameof(SimulationConfig.treeLeafLifeTicks)] =
                "Baseline leaf lifetime in ticks, scaled by the leaf-longevity gene. Expired leaves become Detritus in place.",
            [nameof(SimulationConfig.treeRotTicks)] =
                "How long dead Wood stays Wood before converting to Detritus.",
            [nameof(SimulationConfig.treeDisconnectTicks)] =
                "How many ticks a pixel may survive after its parent is gone before it is marked dead.",
            [nameof(SimulationConfig.waspSeedAtWorldgen)] =
                "Scatter a starting wasp population during world generation instead of waiting for the brush or probe.",
            [nameof(SimulationConfig.waspInitialCalories)] =
                "Calories a freshly seeded or hatched wasp starts with.",
            [nameof(SimulationConfig.waspInitialHydration)] =
                "Hydration a freshly seeded or hatched wasp starts with. Nectar is the main way to top this back up.",
            [nameof(SimulationConfig.waspMaturityTicks)] =
                "Ticks a juvenile must survive before it can mate and lay.",
            [nameof(SimulationConfig.waspDecisionInterval)] =
                "Ticks between behavior re-evaluations. Larger values make wasps commit longer to a hunt or a flower.",
            [nameof(SimulationConfig.waspMaintenanceRate)] =
                "Baseline calorie burn per second, scaled by the metabolism gene.",
            [nameof(SimulationConfig.waspFlightDrain)] =
                "Extra calorie burn for staying airborne, scaled by mass and by climbing against gravity. Riding an updraft is cheap; fighting a downdraft is expensive.",
            [nameof(SimulationConfig.waspHydrationDrain)] =
                "Hydration lost per second, divided by the retention gene.",
            [nameof(SimulationConfig.waspCalorieCapacity)] =
                "Baseline stomach size. The calorie-capacity gene scales this; prey and nectar cannot push past the expressed cap.",
            [nameof(SimulationConfig.waspFullThreshold)] =
                "Calorie level at which a wasp stops hunting and switches to mating or cruising.",
            [nameof(SimulationConfig.waspHungerThreshold)] =
                "Calorie level that promotes active hunting of crickets.",
            [nameof(SimulationConfig.waspStarvationThreshold)] =
                "Calorie level below which a wasp will attack other adult wasps. Cannibalism damps predator population crashes.",
            [nameof(SimulationConfig.waspCruiseAltitude)] =
                "Preferred clearance in cells above the surface. The cruise-altitude gene scales this per individual.",
            [nameof(SimulationConfig.waspAltitudeGain)] =
                "How aggressively a wasp corrects altitude error. High values hold station tightly but bob in gusts.",
            [nameof(SimulationConfig.waspLiftPower)] =
                "Baseline lift authority, scaled by the flight-power gene and divided by mass. Too low and wasps sink.",
            [nameof(SimulationConfig.waspSurfaceScanRange)] =
                "How many cells down a wasp looks for ground. Beyond this range it reads as open sky and descends.",
            [nameof(SimulationConfig.waspBodyMass)] =
                "Baseline body mass used in flight physics. Heavier wasps resist wind but pay more to stay aloft.",
            [nameof(SimulationConfig.waspDrag)] =
                "Air resistance on wasp velocity. Higher values settle motion faster and shorten glides.",
            [nameof(SimulationConfig.waspWindCoupling)] =
                "How strongly angular wind pushes a flying wasp sideways.",
            [nameof(SimulationConfig.waspUpdraftCoupling)] =
                "How strongly radial wind lifts or drops a flying wasp. Ties the population to storms and fire updrafts.",
            [nameof(SimulationConfig.waspSwoopImpulse)] =
                "Downward impulse when diving on prey or descending to lay.",
            [nameof(SimulationConfig.waspSenseRadius)] =
                "Search radius for prey and flowers, scaled by the prey-sense and hearing genes.",
            [nameof(SimulationConfig.waspPreyCalorieConversion)] =
                "Fraction of a victim's calories the killer absorbs. The remainder stays in the Detritus corpse.",
            [nameof(SimulationConfig.waspPreyHydrationTransfer)] =
                "Fraction of a victim's hydration the killer absorbs.",
            [nameof(SimulationConfig.waspNectarDraw)] =
                "Nectar removed from a flower per visit. Larger draws empty a bloom faster and push wasps to keep moving.",
            [nameof(SimulationConfig.waspNectarCalories)] =
                "Calories gained per unit of nectar. Small on purpose; nectar is a drink, not a meal.",
            [nameof(SimulationConfig.waspNectarHydration)] =
                "Hydration gained per unit of nectar. This is the main reason to visit flowers.",
            [nameof(SimulationConfig.waspPollenCapacity)] =
                "How many distinct pollen samples a wasp can carry. More capacity means gene flow across longer routes.",
            [nameof(SimulationConfig.waspGeneExpressionRange)] =
                "How far wasp genes may push a trait away from its baseline value.",
            [nameof(SimulationConfig.waspBaseMutationRate)] =
                "Baseline per-gene mutation step, scaled by the individual's mutation gene.",
            [nameof(SimulationConfig.waspMateCooldownTicks)] =
                "Ticks after mating before a wasp may pair again.",
            [nameof(SimulationConfig.waspReproduceCooldownTicks)] =
                "Ticks after laying before a wasp may lay again.",
            [nameof(SimulationConfig.waspClutchMin)] =
                "Fewest eggs in a clutch. The fertility gene interpolates between min and max.",
            [nameof(SimulationConfig.waspClutchMax)] =
                "Most eggs in a clutch. The fertility gene interpolates between min and max.",
            [nameof(SimulationConfig.waspHatchTicksMin)] =
                "Shortest incubation for a wasp egg. Each egg rolls its own hatch time between the min and max.",
            [nameof(SimulationConfig.waspHatchTicksMax)] =
                "Longest incubation for a wasp egg. Wide ranges spread a clutch out instead of hatching it at once.",
            [nameof(SimulationConfig.waspReproductionCalorieThreshold)] =
                "Calories required before an adult will swoop to the surface and lay.",
            [nameof(SimulationConfig.waspEggDesiccationMoisture)] =
                "Moisture below which a wasp egg dries out and dies.",
            [nameof(SimulationConfig.waspEggHeatDeath)] =
                "Temperature at which a wasp egg cooks. Fire near a laying site wipes out the whole clutch.",
            [nameof(SimulationConfig.waspSurvivalTempMin)] =
                "Coldest temperature an adult wasp survives.",
            [nameof(SimulationConfig.waspSurvivalTempMax)] =
                "Hottest temperature an adult wasp survives.",
            [nameof(SimulationConfig.waspThreatTemperature)] =
                "Temperature above which a wasp treats a neighbor cell as dangerous and flees.",
            [nameof(SimulationConfig.grassCanopyOpacity)] =
                "How much living grass biomass attenuates the shared flora light field.",
            [nameof(SimulationConfig.detritusVaporAbsorbRate)] =
                "How quickly fallen flowers soak atmospheric vapor into retained moisture.",
            [nameof(SimulationConfig.detritusEvaporationRate)] =
                "Detritus drying rate. Substantially slower than ordinary wet surfaces.",
            [nameof(SimulationConfig.detritusMoistureDistributeRate)] =
                "How quickly excess detritus moisture moves into neighboring groundwater hosts.",
            [nameof(SimulationConfig.detritusNutrientLeachRate)] =
                "How quickly the finite nutrient reserve leaches into adjacent soil.",
            [nameof(SimulationConfig.detritusDecompositionRate)] =
                "How quickly the nutrient reserve is exhausted. Empty exposed detritus becomes air; buried detritus becomes soil.",
            [nameof(SimulationConfig.detritusInitialNutrient)] =
                "Nutrient reserve written when a flower drops as detritus.",
            [nameof(SimulationConfig.detritusInitialMoisture)] =
                "Retained moisture written when a flower drops as detritus.",

            [nameof(SimulationConfig.combustionAmbientOxygen)] =
                "Target oxygen fill as a fraction of each cell's capacity. Atmosphere and porous ground relax toward this level; lowering it starves fire and favors smolder.",
            [nameof(SimulationConfig.combustionOxygenReplenishRate)] =
                "How quickly oxygen recovers toward the ambient target. High values keep open air fires burning; low values let sealed pockets suffocate after a short burn.",
            [nameof(SimulationConfig.combustionOxygenDiffusionRate)] =
                "How fast oxygen mixes between neighboring air cells. Higher diffusion feeds fire from surrounding atmosphere and prevents sharp oxygen holes.",
            [nameof(SimulationConfig.combustionIgnitionAccumulationRate)] =
                "How quickly a hot, fueled, oxygenated cell soaks toward ignition. Higher values light faster; lower values add delay so a single hot tick cannot flicker a flame.",
            [nameof(SimulationConfig.combustionIgnitionDecayRate)] =
                "How quickly the ignition accumulator cools when temperature, fuel, or oxygen drop below the threshold. Higher decay makes failed sparks fade immediately.",
            [nameof(SimulationConfig.combustionSeedIntensity)] =
                "Flame intensity written when the ignition accumulator fills. Higher seeds start a hotter, faster burn; lower seeds produce a weaker initial fire.",
            [nameof(SimulationConfig.combustionBurnRate)] =
                "How quickly mycology biomass is consumed once a cell is burning. Higher rates eat fuel faster, dump more heat, and leave less colony behind.",
            [nameof(SimulationConfig.combustionHeatYield)] =
                "Heat released per unit of burned fuel, scaled by the material's caloric content. Higher yield drives stronger updrafts, steam flashes, and neighbor ignition.",
            [nameof(SimulationConfig.combustionPressureScale)] =
                "Pressure added by burning mass. The existing continuity and diffusion passes turn this into a blast wave and return flow around the fire.",
            [nameof(SimulationConfig.combustionUpdraftStrength)] =
                "Direct radial kick applied while a cell burns. Weather buoyancy then sustains the hot column, creating visible atmospheric turbulation.",
            [nameof(SimulationConfig.combustionSmokeYield)] =
                "Global scale on soot emitted per burn, multiplied by each material's smoke yield. Smoke advects with wind and later settles as fertility.",
            [nameof(SimulationConfig.combustionSootSettlingRate)] =
                "How quickly airborne soot falls from air onto the exposed surface below and fertilizes aux nutrients. Higher settling darkens ground near fires sooner.",
            [nameof(SimulationConfig.combustionPyroFertilityYield)] =
                "Nutrient added locally as fuel burns, mirroring ash fertilization. Higher values leave richer soil after a fire front passes.",
            [nameof(SimulationConfig.combustionMoistureIgnitionPenalty)] =
                "How much local water, groundwater, and steam raise the ignition temperature. Wet ground is harder to light and easier to keep from catching.",
            [nameof(SimulationConfig.combustionSteamSuppression)] =
                "How strongly airborne vapor counts as extinguishing moisture and displaces oxygen during a flash. Higher values make steam plumes smother fire.",
            [nameof(SimulationConfig.combustionFlameDecay)] =
                "How quickly flame intensity fades each tick. Higher decay needs continuous fuel and oxygen; lower decay lets embers linger after the front moves on.",
            [nameof(SimulationConfig.combustionFlashVaporizationRate)] =
                "How fast surface liquid near flame converts to vapor once temperature exceeds the material flash point. Pays latent heat and can drown the fire in steam.",
            [nameof(SimulationConfig.combustionMinFuel)] =
                "Minimum mycology biomass required to ignite or keep burning. Below this floor a hot, oxygenated cell still cannot sustain flame.",
            [nameof(SimulationConfig.combustionMinOxygen)] =
                "Minimum oxygen required to ignite. Burning can continue at a lower floor until the pocket is starved, producing a short smolder.",
            [nameof(SimulationConfig.combustionSuppressionMoisture)] =
                "Local water plus steam above this amount forces the flame to decay rapidly. Use it with flash vaporization to make dousing extinguish fire.",

            [nameof(SimulationConfig.stormChargeSeparationRate)] =
                "How quickly cloud, updraft, and mixed-phase temperature separate signed space charge. Higher values build thunderstorm dipoles faster in existing weather fronts.",
            [nameof(SimulationConfig.stormChargeLeakRate)] =
                "How quickly atmospheric space charge relaxes toward zero. Higher leak prevents stale pockets; lower leak lets slow-moving clouds keep a charge for longer.",
            [nameof(SimulationConfig.stormChargeDiffusionRate)] =
                "How fast space charge mixes between neighboring air cells. Higher diffusion smears dipoles; lower diffusion keeps sharp storm-front charge layers.",
            [nameof(SimulationConfig.stormChargeAdvectionRate)] =
                "How strongly wind carries space charge with the air mass. Couples lightning initiation to the same fronts and stationary pockets the weather chain already produces.",
            [nameof(SimulationConfig.stormRimingTempMin)] =
                "Cold edge of the mixed-phase band where ice-crystal charge separation is active. Temperatures below this generate little storm charge.",
            [nameof(SimulationConfig.stormRimingTempMax)] =
                "Warm edge of the mixed-phase band. Charge separation peaks between this and the riming minimum, matching cold-cloud thunderstorm physics.",
            [nameof(SimulationConfig.stormBreakdownThreshold)] =
                "Neighbor charge contrast that must be exceeded before the breakdown accumulator starts filling. Higher thresholds make lightning rare except in strong storms.",
            [nameof(SimulationConfig.stormBreakdownAccumulationRate)] =
                "How quickly a supercritical charge gradient soaks toward a discharge. Higher values fire soon after a dipole forms; lower values add delay and cooldown spacing.",
            [nameof(SimulationConfig.stormChannelDecay)] =
                "How quickly the visible lightning channel fades after a strike. Higher decay makes bolts flicker for a tick; lower decay leaves lingering plasma trails.",
            [nameof(SimulationConfig.stormFlashDecay)] =
                "How quickly the ambient sky flash fades. Higher decay keeps illumination tight to the instant of discharge.",
            [nameof(SimulationConfig.stormFlashDiffusion)] =
                "How far the discharge glow bleeds into neighboring sky cells. Higher diffusion lights a broader sheet of atmosphere around the channel.",
            [nameof(SimulationConfig.stormCooldownRate)] =
                "How quickly a spent cell returns from negative cooldown to a neutral breakdown state. Lower rates space repeated strikes in the same pocket.",
            [nameof(SimulationConfig.stormStrikeHeat)] =
                "Temperature added along a lightning channel, falling off toward side branches. Hits at the terminus can ignite dry mycology fuel on the next combustion pass.",
            [nameof(SimulationConfig.stormStrikeAirHeatFraction)] =
                "Share of strike heat applied to air cells versus solids after heat-capacity scaling. Lower values keep lightning hot at the ground while the channel warms the sky more gently.",
            [nameof(SimulationConfig.stormThunderPressure)] =
                "Pressure pulse written along the channel. Continuity and pressure diffusion turn this into a thunder shock that couples back into wind.",
            [nameof(SimulationConfig.stormChargeDeposit)] =
                "Electrical charge dumped into state.w at the strike terminus. The existing Electrical pass conducts it away, stressing mycology on conductive ground.",
            [nameof(SimulationConfig.stormIgnitionImpulse)] =
                "How much the terminus ignition accumulator fills when lightning hits. Combined with heat, this can spark fueled, oxygenated soil on the next fire pass.",
            [nameof(SimulationConfig.stormFlashVaporization)] =
                "Surface liquid converted to vapor at the strike terminus. Moves existing water mass only, paying latent heat the same way combustion flashpoint steam does.",
            [nameof(SimulationConfig.stormChannelChargeDrain)] =
                "Fraction of local space charge removed as the channel passes. Spent pockets must recharge from weather before they can fire again.",
            [nameof(SimulationConfig.stormTortuosity)] =
                "How much a cloud-to-ground leader wanders instead of taking the shortest path to its conductive target. Higher values look more jagged and branchy.",
            [nameof(SimulationConfig.stormTargetRange)] =
                "Angular and radial search radius, in cells, used to pick a conductive terminus. Metal outranks wet rock and soil inside this window.",
            [nameof(SimulationConfig.stormMaxChannelLength)] =
                "Maximum steps a leader may walk in one tick, including side branches. Caps GPU work and keeps bolts from wrapping the whole planetoid.",
            [nameof(SimulationConfig.stormMaxStrikesPerTick)] =
                "Hard cap on discharges spawned in a single tick. Zero disables lightning; higher values allow storm fronts to fire several bolts at once.",
            [nameof(SimulationConfig.stormStrikeBranchChance)] =
                "Chance a cloud-to-ground leader sprouts a short side branch each step. Lower values keep a single jagged bolt; higher values add forks.",
            [nameof(SimulationConfig.stormSheetBranchChance)] =
                "Chance an in-cloud sheet sprouts extra tangential branches. Higher values spread a broad sky flash instead of a narrow channel.",
            [nameof(SimulationConfig.stormMinimumHeight)] =
                "Lowest radial cell (Y) that may originate lightning. Breakdown below this height is ignored, so bolts only start in the upper atmosphere while still being allowed to strike the surface.",

            [nameof(SimulationConfig.enableStarfield)] =
                "Toggles background stars. Visual only; does not change solar heating, weather, or ecology.",
            [nameof(SimulationConfig.starCount)] =
                "Number of star particles. Higher counts cost more to draw; no effect on simulation systems.",
            [nameof(SimulationConfig.starfieldStrength)] =
                "Opacity of the starfield. Visual only; it does not change solar heating, weather, or ecology.",
            [nameof(SimulationConfig.starTwinkleStrength)] =
                "How much stars scintillate. Visual only; simulation lighting and temperature still come from the weather pass.",
            [nameof(SimulationConfig.enableNebula)] =
                "Toggles nebula clouds behind the planetoid. Visual only.",
            [nameof(SimulationConfig.nebulaCount)] =
                "Number of nebula particles. Draw cost only.",
            [nameof(SimulationConfig.nebulaStrength)] =
                "Opacity of nebula color. Visual only; weather and hydrology are unaffected.",
            [nameof(SimulationConfig.enableAtmosphereGlow)] =
                "Toggles the limb glow around the planetoid. Visual only; independent of atmospheric physics.",
            [nameof(SimulationConfig.atmosphereGlowStrength)] =
                "Brightness of the atmospheric halo. Visual only.",
            [nameof(SimulationConfig.atmosphereGlowPixelScale)] =
                "How chunky the glow sampling looks. Visual only.",
            [nameof(SimulationConfig.atmosphereGlowRayCount)] =
                "Number of glow rays around the limb. Visual only.",
            [nameof(SimulationConfig.enableSolarBody)] =
                "Toggles the orbiting sun mesh. Lighting can still follow orbit; disabling hides the disc and corona.",
            [nameof(SimulationConfig.solarBodyStrength)] =
                "Brightness of the sun's core disc. Visual only; solar heating uses Terrain Solar Heating and Atmosphere Solar Heating instead.",
            [nameof(SimulationConfig.solarCoronaStrength)] =
                "Brightness of the sun's corona. Visual only.",
            [nameof(SimulationConfig.solarOrbitRadius)] =
                "How far the sun mesh orbits the planetoid. Visual placement only; day length still comes from Day Length Seconds.",
            [nameof(SimulationConfig.dayNightLightingStrength)] =
                "How strongly the display shades the night side. Visual lighting only; temperatures still come from the weather pass.",
            [nameof(SimulationConfig.enableCoreVisual)] =
                "Toggles the animated molten metal planetary core visual effect anchored to the center of the planetoid. Visual only.",
            [nameof(SimulationConfig.coreVisualStrength)] =
                "Overall brightness and opacity of the molten metal core and convective circulation. Visual only.",
            [nameof(SimulationConfig.coreCirculationSpeed)] =
                "Speed of fluid convection, differential rotation, and eddy currents in the molten core dynamo. Visual only.",
            [nameof(SimulationConfig.coreHeatGlow)] =
                "Intensity of incandescent thermal radiation and thermal bloom at the core-mantle boundary. Visual only.",
            [nameof(SimulationConfig.coreVisualScale)] =
                "Multiplier on the visual extent of the molten metal core quad relative to the planetary core boundary. Visual only.",
            [nameof(SimulationConfig.uiFadeDelay)] =
                "Seconds after the last inspect refresh or probe HUD idle before chrome fades. Inspect info fades out completely; probe controls fade to about 10% opacity and return on mouse-over.",

            [nameof(SimulationConfig.brushRadius)] =
                "Radius of the paint/inspect brush in cells. Larger brushes edit more geology, water, and ecology at once.",
            [nameof(SimulationConfig.brushStrength)] =
                "How strongly the brush applies heat, water, charge, or material. Higher strength makes faster local changes to coupled systems.",
            [nameof(SimulationConfig.validationIntervalTicks)] =
                "How often automatic validation samples finite values and tracked water mass. Lower intervals catch conservation bugs sooner at a small readback cost.",
            [nameof(SimulationConfig.conservationTolerance)] =
                "Allowed relative error in tracked water (surface + ground + vapor). Tighter tolerance flags leaks in hydrology and weather mass transfer.",

            [nameof(SimulationConfig.probeOrbitRadius)] =
                "How far the probe orbits the planetoid, in the same units as Solar Orbit Radius. 1.0 sits on the disc edge; 1.28 rides the outer atmosphere glow.",
            [nameof(SimulationConfig.probeSpriteScale)] =
                "Local scale of the probe sprite. Raise it to make the orbiter easier to see against the glow; lower it to keep the disc unobstructed.",
            [nameof(SimulationConfig.probeSpriteRotationOffset)] =
                "Degrees added to the probe sprite's tangent facing. The same offset aligns the art for clockwise and counterclockwise travel.",
            [nameof(SimulationConfig.probeOrbitPeriodSeconds)] =
                "Seconds of simulation time for one clockwise lap. Independent of Day Length Seconds, so the probe can cross the sun instead of locking to night-side.",
            [nameof(SimulationConfig.probeVaporRate)] =
                "Humidity added to the outer atmosphere ring each frame while the vapor icon is held. Higher rates seed clouds faster ahead of the probe's path.",
            [nameof(SimulationConfig.probeWaterRate)] =
                "Surface water added to the outer atmosphere ring each frame while the water icon is held. Higher rates seed rain and runoff faster ahead of the probe.",
            [nameof(SimulationConfig.probeHeatRate)] =
                "Temperature added at the outer ring each frame while the heat icon is held. Use it to warm atmosphere and surface along the probe heading.",
            [nameof(SimulationConfig.probeCoolRate)] =
                "Temperature subtracted at the outer ring each frame while the cool icon is held. Use it to chill atmosphere and encourage condensation along the path.",
            [nameof(SimulationConfig.probeDepositRadius)] =
                "Radius in cells of probe deposits. Larger radii seed vapor, water, soil, and temperature over a wider patch ahead of the orbiter.",
            [nameof(SimulationConfig.probeLeadDegrees)] =
                "How many degrees ahead of the probe heading deposits land. A small lead lets vapor and water trail into the clockwise path instead of sitting under the sprite.",
            [nameof(SimulationConfig.probeFollowZoom)] =
                "Orthographic size applied when switching to the probe camera. Smaller values frame the orbiter tightly; larger values keep more of the planetoid in view.",
            [nameof(SimulationConfig.probeEnergyMax)] =
                "Maximum probe energy shown on the HUD bar. Drain and regen clamp to this cap. Actions still fire when the bar is empty.",
            [nameof(SimulationConfig.probeEnergyActionDrain)] =
                "Energy subtracted each sim tick while a hold tool or life seed is active. Future builds will use per-action costs; empty energy does not block tools.",
            [nameof(SimulationConfig.probeEnergyRegenPerSecond)] =
                "Energy restored per simulation second while no tool is active and the probe is not stopped. Scales with tick rate and sim speed; pause and hold-station both freeze regen.",
            [nameof(SimulationConfig.probeLifeSeedIntervalSeconds)] =
                "Simulation seconds between life-seed bursts while the plants toggle is on. Each burst drops 1-5 mixed dormant organisms one tick apart from the probe aim cell.",
            [nameof(SimulationConfig.probeLifeSeedMinCount)] =
                "Minimum organisms in a life-seed burst. Each spawn is independently an algae spore or a cricket egg.",
            [nameof(SimulationConfig.probeLifeSeedMaxCount)] =
                "Maximum organisms in a life-seed burst. Spawns are sequential ticks, not a same-tick clump.",
            [nameof(SimulationConfig.probeLifeSeedSporeLoad)] =
                "Spore load written when life seed drops flora. Eggs use the cricket-egg material instead of this value.",
        };

        public static bool TryGet(string fieldName, out string tooltip)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                tooltip = null;
                return false;
            }

            return Tooltips.TryGetValue(fieldName, out tooltip);
        }

        public static bool TryGetMaterial(string fieldName, out string tooltip)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                tooltip = null;
                return false;
            }

            return MaterialTooltips.TryGetValue(fieldName, out tooltip);
        }

        private static readonly Dictionary<string, string> MaterialTooltips = new()
        {
            [nameof(MaterialDefinition.displayName)] =
                "Label used by the inspector and material dropdown. Cosmetic; simulation identity is Stable Id.",
            [nameof(MaterialDefinition.category)] =
                "Broad physical class (gas, liquid, granular, solid, magma). Selects which motion, weather, and hydrology rules apply to this pixel type.",
            [nameof(MaterialDefinition.shadeVariation)] =
                "How much neighboring cells of this material dither their display color. Visual only.",
            [nameof(MaterialDefinition.density)] =
                "Mass per cell used by Margolus gravity settling. Denser movable cells sink through lighter neighbors; lighter cells rise.",
            [nameof(MaterialDefinition.rigidity)] =
                "Resistance to grain flow and collapse. High rigidity keeps crust and metal in place; low rigidity lets piles slump toward their angle of repose.",
            [nameof(MaterialDefinition.angleOfRepose)] =
                "Steepest stable slope for granular motion. Lower angles spread soil and sediment; higher angles hold cliffs until erosion or collapse.",
            [nameof(MaterialDefinition.grainSize)] =
                "Characteristic grain scale. Larger grains move more sluggishly under gravity and affect how piles pack.",
            [nameof(MaterialDefinition.buoyancyBias)] =
                "Extra density weighting hint for biology and display. Margolus density sort uses physical density plus groundwater saturation.",
            [nameof(MaterialDefinition.thermalConductivity)] =
                "How quickly this material exchanges heat with neighbors. High conductivity couples it to solar weather, magma, and phase changes.",
            [nameof(MaterialDefinition.heatCapacity)] =
                "Energy needed to change temperature. High capacity buffers climate and delays melting; low capacity spikes hot and cold quickly.",
            [nameof(MaterialDefinition.electricalConductivity)] =
                "How fast charge spreads through this material. Feeds electrical expansion and mycology electrical stress on conductive ground.",
            [nameof(MaterialDefinition.absorbency)] =
                "How readily this material takes up surface water. High absorbency drives infiltration, soil moisture, and mycology habitat.",
            [nameof(MaterialDefinition.porosity)] =
                "How easily groundwater and pressure move through the cell. Porous rock hosts aquifers, caves, and hydrothermal pathways.",
            [nameof(MaterialDefinition.meltingTemperature)] =
                "Temperature where the solid phase becomes the liquid phase. Controls magma generation, ice melt, and lava crusting.",
            [nameof(MaterialDefinition.boilingTemperature)] =
                "Temperature where the liquid phase becomes gas. Drives boiling, vapor explosions, and steam in the weather cycle.",
            [nameof(MaterialDefinition.thermalExpansion)] =
                "Pressure added as the cell heats. Couples temperature to fractures, vents, and wind via the pressure system.",
            [nameof(MaterialDefinition.electricalExpansion)] =
                "Pressure added as charge builds. Links electrical conductivity to mechanical stress and expansion.",
            [nameof(MaterialDefinition.latentHeat)] =
                "Energy absorbed when this material melts and released when it freezes. High values hold rock and magma near their phase thresholds instead of snapping instantly.",
            [nameof(MaterialDefinition.solidPhaseId)] =
                "Material id to become when freezing or cooling below melt. Completes geology and ice phase loops.",
            [nameof(MaterialDefinition.liquidPhaseId)] =
                "Material id to become when melting. Water, magma, and other fluids depend on this link.",
            [nameof(MaterialDefinition.gasPhaseId)] =
                "Material id to become when boiling. Ties liquid reservoirs to vapor and atmospheric transport.",
            [nameof(MaterialDefinition.toxicity)] =
                "Harm factor reserved for phase-2 organisms. Stored on the pixel for future ecology and feeding rules.",
            [nameof(MaterialDefinition.caloricContent)] =
                "Energy released when this material burns and later available to organisms feeding on it. Soil calories currently scale combustion heat yield.",
            [nameof(MaterialDefinition.ignitionTemperature)] =
                "Base temperature where this material can ignite if fuel and oxygen are present. Mycology heat traits shift the threshold on colonized soil.",
            [nameof(MaterialDefinition.flashPoint)] =
                "Temperature where nearby flame begins converting this cell's surface liquid into vapor. Water flashes below its boiling point so fire can steam itself out.",
            [nameof(MaterialDefinition.oxygenDemand)] =
                "Oxygen consumed per unit of burned fuel. Higher demand starves a sealed fire faster and favors smolder over open flame.",
            [nameof(MaterialDefinition.smokeYield)] =
                "Soot produced per unit of burned fuel, scaled by the global smoke yield. Higher values make thicker plumes that later settle as fertility.",
            [nameof(MaterialDefinition.bioModifiable)] =
                "Whether biology may alter this material (roots, bioerosion, nutrient conversion). Required for mycology and later life to rewrite the pixel.",
        };
    }
}
