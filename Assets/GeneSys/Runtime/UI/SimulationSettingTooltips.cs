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
                "Runs volcanism, erosion/collapse, ash fertilization, and mycology colony updates every N ticks. Lower values make geology and ecology more responsive; higher values save GPU time.",
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
            [nameof(SimulationConfig.faultCount)] =
                "Number of generated crustal faults. More faults raise starting stress, give magma preferential paths, and fragment aquifers along weakness planes.",
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
            [nameof(SimulationConfig.electricalRate)] =
                "How quickly charge spreads through conductive materials. Affects electrical expansion, mycology electrical stress, and any future bioelectric sensing.",
            [nameof(SimulationConfig.phaseHysteresis)] =
                "Temperature buffer around melt and boil points. Higher hysteresis slows flickering phase changes in magma, water, ice, and vapor, stabilizing weather and geology.",
            [nameof(SimulationConfig.densityExchangeRate)] =
                "How readily density-displaceable materials swap with fluids. Higher rates make wood, ash, and sediment float or sink faster through water and magma.",
            [nameof(SimulationConfig.densityExchangeEpsilon)] =
                "Minimum density difference required before a swap. Larger epsilon prevents jittery mixing; smaller epsilon lets close-density materials keep sorting.",

            [nameof(SimulationConfig.mantlePressure)] =
                "Background pressure added in the deep interior each volcanism pass. Higher mantle pressure drives extrusion, eruptions, geysers, and hydrothermal vents.",
            [nameof(SimulationConfig.fractureRate)] =
                "How fast pressure gradients turn into fault stress. Higher values crack crust more readily, opening magma dikes, caves, and fluid pathways.",
            [nameof(SimulationConfig.extrusionRate)] =
                "How quickly overpressured magma and rock are driven toward the surface. Raises volcanic flow, cone building, and ash production when eruptions are enabled.",
            [nameof(SimulationConfig.volcanicCooling)] =
                "How fast extruded magma loses heat, especially in air. Higher cooling freezes lava into basalt sooner and shortens surface flows.",
            [nameof(SimulationConfig.magmaViscosity)] =
                "Resistance of magma to flow. Higher viscosity builds steep cones and pressure; lower viscosity favors long, fluid shield-style flows.",
            [nameof(SimulationConfig.hydrothermalStrength)] =
                "Heat and water exchanged at magma–ocean or magma–aquifer contacts. Stronger vents warm seawater, drive geysers, and seed nutrient-rich chemistry.",
            [nameof(SimulationConfig.ventChemicalRate)] =
                "Nutrient added when hydrothermal heat is transferred. Raises seafloor and spring fertility that mycology and later organisms can exploit.",
            [nameof(SimulationConfig.magmaEruption)] =
                "Master mix for explosive eruptions. At 0, pressure still builds quietly; raising it enables blast events, ash lofting, and violent surface breakout.",
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
            [nameof(SimulationConfig.coreReactionFrequency)] =
                "Ticks between core thermal pulses. 0 disables pulses. More frequent reactions inject heat and pressure into mantle convection and volcanism.",
            [nameof(SimulationConfig.coreReactionMagnitude)] =
                "Size of each core heat/pressure pulse. Larger pulses can trigger widespread volcanism, hydrothermal spikes, and atmospheric heating.",

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
            [nameof(SimulationConfig.stressDecayRate)] =
                "How fast stored fault and slope stress relaxes. Higher decay heals fractures; lower decay lets stress accumulate toward collapse and eruptive breakout.",
            [nameof(SimulationConfig.dryMoistureThreshold)] =
                "Moisture level below which soil loses cohesion. Drier thresholds make arid crust dusty and erodible; wetter thresholds keep banks stable until they dry further.",
            [nameof(SimulationConfig.moistureCohesionStrength)] =
                "How much pore water binds soil. Stronger capillary cohesion resists erosion when damp, then fails suddenly if the ground dries past the threshold.",
            [nameof(SimulationConfig.capillaryEvaporationFraction)] =
                "Share of near-surface groundwater that can evaporate into air. Higher values couple aquifers to the weather cycle and dry soils from below.",
            [nameof(SimulationConfig.runoffRate)] =
                "How fast excess surface water flows downhill. Higher runoff builds streams and waterfalls; lower runoff ponds in place and soaks in.",
            [nameof(SimulationConfig.pondingRate)] =
                "How readily water collects in depressions. Higher ponding creates lakes and wetlands that buffer the water cycle and mycology moisture.",
            [nameof(SimulationConfig.surfaceWaterPixelThreshold)] =
                "Surface liquid film required before standing Water or Ice pixels spawn above wet ground. Zero keeps moisture as a field on soil; higher values delay visible pooling until rain has accumulated.",
            [nameof(SimulationConfig.springHeadThreshold)] =
                "Groundwater saturation needed before a spring discharges. Lower thresholds weep widely; higher ones concentrate flow into fewer, stronger springs.",
            [nameof(SimulationConfig.springDischargeRate)] =
                "How quickly saturated aquifers vent to the surface. Stronger springs feed streams, wet soil, and can flood low terrain.",
            [nameof(SimulationConfig.geyserHeatThreshold)] =
                "Temperature required for geyser discharge. Lower thresholds make hydrothermal ground erupt more often; higher ones reserve geysers for magma-heated sites.",
            [nameof(SimulationConfig.geyserDischargeRate)] =
                "Water and vapor expelled per geyser event. Stronger geysers loft humidity, nutrients, and heat into weather and nearby ecology.",
            [nameof(SimulationConfig.geyserCooldownSeconds)] =
                "Simulated time before a geyser can fire again. Longer cooldowns space events; shorter cooldowns sustain steaming hydrothermal fields.",

            [nameof(SimulationConfig.dayLengthSeconds)] =
                "Orbital period of the solar body in simulated seconds. Shorter days cycle heating, winds, and day/night lighting faster; longer days deepen thermal contrasts.",
            [nameof(SimulationConfig.solarIntensity)] =
                "How strongly the sun heats exposed surfaces and air. Higher intensity drives evaporation, buoyancy, wind, and daytime thaw; lower intensity favors ice and calm air.",
            [nameof(SimulationConfig.spaceTemperature)] =
                "Temperature the upper atmosphere radiates toward. Colder space strengthens night cooling, lapse, and polar ice; warmer space keeps vapor aloft.",
            [nameof(SimulationConfig.radiativeCooling)] =
                "How fast heat is lost to space. Stronger cooling steepens night-side temperatures and can collapse vapor into rain or snow.",
            [nameof(SimulationConfig.windStrength)] =
                "Forcing that turns pressure and temperature gradients into wind. Stronger wind advects heat, vapor, ash, and spores around the planetoid.",
            [nameof(SimulationConfig.windDamping)] =
                "How quickly wind dies without forcing. Higher damping calms storms; lower damping lets jets persist and carry weather farther.",
            [nameof(SimulationConfig.evaporationRate)] =
                "How fast surface water and moist ground become vapor. Higher rates dry soils, load clouds, and couple hydrology to weather.",
            [nameof(SimulationConfig.condensationRate)] =
                "How fast saturated air becomes cloud condensate. Higher rates build visible clouds sooner and feed precipitation.",
            [nameof(SimulationConfig.precipitationRate)] =
                "How quickly cloud water falls as rain or snow. Higher rates dump storms fast; lower rates keep long-lived clouds and drizzle.",
            [nameof(SimulationConfig.vaporPressureScale)] =
                "Extra pressure generated when liquid boils to vapor. Higher scale makes steam explosions, geysers, and boiling more dynamically violent.",
            [nameof(SimulationConfig.pressureRate)] =
                "How quickly material expansion adds cell pressure, and how fast that pressure relaxes. Couples thermal/electrical swelling to wind, vents, and fractures.",
            [nameof(SimulationConfig.pressureDiffusionRate)] =
                "Master speed of pressure spreading between neighbors. Faster diffusion smooths storms and mantle pressure; slower diffusion keeps sharp fronts and blasts.",
            [nameof(SimulationConfig.gasPressureDiffusivity)] =
                "Pressure conductivity of air and vapor. High values let atmosphere equalize quickly; low values allow local gusts and steam pockets.",
            [nameof(SimulationConfig.fluidPressureDiffusivity)] =
                "Pressure conductivity of liquids. Affects how waves, hydrostatic load, and magma pressure communicate through water and melt.",
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
                "How much warm air rises. Stronger buoyancy builds updrafts, storms, ash lofting, and vertical mixing of spores.",
            [nameof(SimulationConfig.humidityBuoyancy)] =
                "Extra lift from moist air. Higher values make humid parcels rise into clouds; lower values treat dry and wet air more equally.",
            [nameof(SimulationConfig.saturationCapacityScale)] =
                "How much vapor air can hold before condensing. Higher capacity delays clouds in warm air; lower capacity rains out easily and dries the column.",
            [nameof(SimulationConfig.cloudPrecipitationThreshold)] =
                "Cloud condensate needed before rain starts. Higher thresholds build thicker clouds; lower thresholds produce light, frequent precipitation.",
            [nameof(SimulationConfig.rainPixelFormationThreshold)] =
                "Cloud condensate required before an Air cell becomes a falling Water or Ice pixel. Zero keeps rain as a field flux onto terrain; lower values make dense clouds rain out as discrete drops.",
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
                "Brightness of the sun's core disc. Visual only; solar heating uses Solar Intensity instead.",
            [nameof(SimulationConfig.solarCoronaStrength)] =
                "Brightness of the sun's corona. Visual only.",
            [nameof(SimulationConfig.solarOrbitRadius)] =
                "How far the sun mesh orbits the planetoid. Visual placement only; day length still comes from Day Length Seconds.",
            [nameof(SimulationConfig.dayNightLightingStrength)] =
                "How strongly the display shades the night side. Visual lighting only; temperatures still come from the weather pass.",

            [nameof(SimulationConfig.brushRadius)] =
                "Radius of the paint/inspect brush in cells. Larger brushes edit more geology, water, and ecology at once.",
            [nameof(SimulationConfig.brushStrength)] =
                "How strongly the brush applies heat, water, charge, or material. Higher strength makes faster local changes to coupled systems.",
            [nameof(SimulationConfig.validationIntervalTicks)] =
                "How often automatic validation samples finite values and tracked water mass. Lower intervals catch conservation bugs sooner at a small readback cost.",
            [nameof(SimulationConfig.conservationTolerance)] =
                "Allowed relative error in tracked water (surface + ground + vapor). Tighter tolerance flags leaks in hydrology and weather mass transfer.",
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
                "Mass per cell used by gravity settling and density displacement. Denser materials sink through fluids; lighter ones float when Density Displaceable is on.",
            [nameof(MaterialDefinition.rigidity)] =
                "Resistance to grain flow and collapse. High rigidity keeps crust and metal in place; low rigidity lets piles slump toward their angle of repose.",
            [nameof(MaterialDefinition.angleOfRepose)] =
                "Steepest stable slope for granular motion. Lower angles spread soil and sediment; higher angles hold cliffs until erosion or collapse.",
            [nameof(MaterialDefinition.grainSize)] =
                "Characteristic grain scale. Larger grains move more sluggishly under gravity and affect how piles pack.",
            [nameof(MaterialDefinition.buoyancyBias)] =
                "Scales density-exchange rate against liquids, not the float/sink decision. Positive bias swaps faster; density still decides direction.",
            [nameof(MaterialDefinition.densityDisplaceable)] =
                "Allows this material to density-sort against liquids. Density alone decides float versus sink; buoyancy bias only changes how fast swaps happen.",
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
            [nameof(MaterialDefinition.solidPhaseId)] =
                "Material id to become when freezing or cooling below melt. Completes geology and ice phase loops.",
            [nameof(MaterialDefinition.liquidPhaseId)] =
                "Material id to become when melting. Water, magma, and other fluids depend on this link.",
            [nameof(MaterialDefinition.gasPhaseId)] =
                "Material id to become when boiling. Ties liquid reservoirs to vapor and atmospheric transport.",
            [nameof(MaterialDefinition.toxicity)] =
                "Harm factor reserved for phase-2 organisms. Stored on the pixel for future ecology and feeding rules.",
            [nameof(MaterialDefinition.caloricContent)] =
                "Energy available to future organisms feeding on this material. Currently stored for phase-2 metabolism.",
            [nameof(MaterialDefinition.bioModifiable)] =
                "Whether biology may alter this material (roots, bioerosion, nutrient conversion). Required for mycology and later life to rewrite the pixel.",
        };
    }
}
