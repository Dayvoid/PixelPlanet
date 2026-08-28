# GeneSys
working title


## GOAL 

SimLife style artificial life sandbox
familiar organisms/species juxtaposed with fantastic mash-ups/abominations
complex digital terrarium favoring recognizable behaviors/ecological niches
scale: 100s of complex active agents supported by an ecosystem consisting of compute driven subsystems
all relevant simulation settings tweakable via tab based settings menu UI  
platform: Unity 6 - 7950x3d, 32GB DDR5 , RTX 4080 super

## VALIDATION

Limit automated validation to tests that are explicitly necessary for the change in hand. PlayMode fixtures load Terrarium and wait on GPU readback; they are not a default gate.

- Run only the EditMode/PlayMode fixture that exercises the code you touched (e.g. flora kernel → `FloraTests` + `FloraIntegrationTests`). Filter by fixture name in the Test Runner, or `GeneSysTestRunner.RunPlayModeGroup("GeneSys.Tests.FloraIntegrationTests")`.
- Do not run the full EditMode or PlayMode suite after ordinary edits. Broaden the run only when the change can break a cross-system contract: water/mass conservation, snapshot versioning, GPU resource layout, polar topology, or worldgen determinism.
- Do not add tests that pin default slider values, `RestoreDefaults` literals, overlay enum numbers, visual tint constants, or smoke that the terrarium scene loads. Tune settings without fighting the suite.
- Keep one PlayMode case per behavior, plus the invariant tests for that subsystem (mass/energy conservation, occupancy claims, genome packing, OnValidate envelope ordering, material GPU packing, snapshot round-trip).
- Do not add a mirror-image, legacy-path, or second conservation test unless that path is still a user-facing contract.
- Worldgen organism seeding (`floraSeedAtWorldgen`, `faunaSeedAtWorldgen`, `grassSeedAtWorldgen`) stays off by default so a generated world is abiotic until the Life brush introduces populations — not to keep determinism tests green.

## PHASE 1
### WORLD

"Material pixel" based 2D world
2D mario galaxy style planetoid presentation
polar coordinate system (core 'squished' visually(non 'playable area), surface maintains consistent Y up/down)
planetoids follow procedural generation pixel conventions (molten core, dense mantle, crust rocks, soil surface) 
discrete atomic/molecular pixel types
adjustable parameters for each 
- size
- rigidity
- buoyancy
- angle of repose
- absorbency
- electrical conductivity
- thermal conductivity
- expand/contract under thermal/electric load
- toxicity
- caloric content/availability
etc.

#### GEOLOGY

in order to achieve a 'living world' we can't start with uniform concentric layers for our worldgen

- irregular core with procedurally generated (amount within a defined range) fault lines/extrusions
- pressure generation with material extrusion/expulsion mechanics to create multiple types of volcanic formation/activity (shield/cone)
- controls for layer ratios, Y noise at layer boarders, chance for layer to protrude
- variable materials (higher/lower melt temps, porous/non-porous)
- crust/soil layer water-table/ground water system allowing for carving/calving of caves/underground rivers
- erosion/gravity system with stems for biomorphogenetic formation of microbiomes (bioerosion of non-porous into porous, flora roots/turf increasing soil cohesion, burrowing fauna creating crusts doing similar)

#### SOLAR/WEATHER

- orbital 'solar' body drive day/night temperature differential
- differing materials warm/cool at different rates creating wind/weather systems
- water cycle: surface materials soak/diffuse moisture to/from underground reserves - water vapor evaporates(allowing for tunable expansion/steam pressure) and collects at atmospheric boundary, shifted by wind/air pressure - water cools and condenses falling as rain
- canonical water mass contract: `state.z` surface liquid/ice, `aux.x` vapor, `aux.y` groundwater; material IDs never create mass implicitly
- worldgen seeds 2-3 separated ocean basins targeting ~50% angular coastline coverage, aquifers, and initial atmospheric humidity
- runtime hydrology adds rain deposition, runoff/waterfalls, aquifer springs, geysers, and hydrothermal ocean vents coupled to geology

#### COMBUSTION
- fire requires temperature, oxygen, and fuel (mycology biomass on soil/sediment plus flora `life.y` on Algae/Moss)
- dedicated combustion field: oxygen, flame intensity, soot, ignition accumulator
- oxygen capacity is full in air and limited by porosity in solids; sealed fires smolder then suffocate
- burning dumps heat, pressure, and updraft into the existing weather chain for atmospheric turbulation
- flashpoint vaporization converts nearby surface liquid to steam without inventing water mass; steam suppresses further ignition

#### STORMS
- mixed-phase cloud plus updraft separates signed atmospheric space charge in existing weather fronts and slow pockets
- dedicated storm field: space charge, lightning channel, sky flash, breakdown/cooldown
- discharges as in-cloud sheets or cloud-to-ground strikes that prefer conductive materials (especially metal)
- strikes dump heat, thunder pressure, electrical charge, and an ignition impulse; terminus flash-vaporizes existing surface water

### UI

- pause/play, step, sim speed controls
- tab based settings menu containing sim system settings
- selectable overlays displaying material/temp/pressure/wind/etc.
- inspect tool displaying stats for selected material
- brush tool for painting/editing world
## PHASE 2 - LIFE

### ORGANISMS

base organism template with "evolutionary path" via genetic expression to accommodate simple simulated algae all the way to complex apex predator 
base organism suitable to morphological diversity 
- multiple sensory input modules/nodes of differing signal types (chemotaxis, pressure/sound, visible light) 
- differing mechanisms/modes for procedural motility
- diverse appendage configuration (mouths, tails, limbs, pinchers etc.) 
organism components (skin, bones, muscles, fluids, etc.) composed of appropriate "pixel materials"
begin with populations of simulated immobile photosynthesizers that emit (compute shader near neighbor diffusion) nutrient fields, and basic cilium and chemotaxis prey niche, coordinated cilial (rapid acceleration) motion-sensitive(attack trigger) predator?
#### FITNESS

fitness for evolutionary promotion should be a sum of weighted values, with a "left/right hemispheric" divide creating categories of 'left hemisphere' or self-associated values (personal metabolic state, feeding history, traumatic events, etc.) and 'right hemisphere' or group/environment associated values (population size, birth rates, average energy/health, etc.). These dual fitness indexes should be normalized and combined in a weighted manner such that a global (default 50/50) left/right weight can be applied, and that individual organisms have a randomized left/right weighting (not exceeding defined values, default 60/40) determined by genetic expression


#### BRAIN

The left/right hemisphere approach should be adopted when developing behavior trees or predictive coding/LSTM wiring. One set of 'motivating factors' should be calculated per tick/cycle for each the left and the right brain hemisphere: left values concerning only the immediate self/environment, while right values concern group/bigger picture states such as species population and environmental health/equilibrium. any action taken should first be selected from one of the two hemispheres with a weight dependent on stress level (max stress excludes right brain selection, zero stress excludes left brain).

### MILESTONE - First species

- photosynthesizer: algae/moss analog. all values should have associated genes/expression levels that drive deviation from a baseline. temp/moisture survival thresholds that are tuned long term enough to allow combustion to occur before 'death' from temp/drought. 'dormant' spore form acts as seed as well as 'last ditch' survival mechanism, when environmental factors are outside of 'growth/reproduction' values but within survival/destruction values. during growth/reproduction periods sunlight should be collected while available towards a reproduction threshold that when reached causes the organism to divide with a gentle base mutation rate augmented by toxins encountered. while growing/active and receiving sunlight the organism should diffuse nutrients acting as a gradient for other organisms to follow. the morphology should be simple/circular with a size/caloric index that isn't strictly tied to visual display(breakpoints for scale, etc). we'll treat it as a 'single cell' that is bouyant, forming 'mats' on water/soil in appropriate environmental coniditions. a new brush mode "Life" should be added with "Algae/Moss" as an entry in the material field, using the established template this mode should introduce spores into the simulation.

#### Implemented flora contract
- material `128 Algae/Moss`, category Biological, density 0.45 (floats on water via existing `LiquidDensityExchange`), rigidity ~0.95 (no diagonal slump; lateral spread is division)
- one colonized cell is one organism; mycology is left untouched as a sibling subsystem
- `LifeGenomeRead/Write` Tex2DArray RGBA32F (2 slices) so WriteCell stays at the DX11 8-UAV cap: slice 0 = life (`x` spore load, `y` biomass/caloric fuel `[0,1]`, `z` stored photosynthate energy, `w` nutrient exudate); slice 1 = `asfloat(genome uint4)` for AsyncGPUReadback. Genome `.x/.y/.z` = 12 genes × 8 bits; `.w` = `stage(8) | generation(8) | lineage(8) | toxinDose(8)`
- genes express as deviations from config baselines: `1 + (gene/255 - 0.5) * 2 * geneExpressionRange`
- derived `LightField` R32F, recomputed each tick by a radial inward march (cloud condensate, soot, water depth, material opacity); not ping-ponged
- life stages: `SPORE`, `ACTIVE` (growth envelope + light, accumulate energy, divide with mutation), `DORMANT` (survival envelope, biomass kept, revival), `DESICCATED` (outside survival: dry fuel with a lowered ignition threshold so fire can consume the mat), `DEAD` → Ash `12` plus `aux.z` fertility
- race-free division is a gather from Read-state on one neighbor direction per slow tick
- brush mode `Material` paints IDs 0–13 plus `131 Detritus`; `Life` reuses the Material dropdown for `128–130`, Myco Spores (GPU 7, rare strains at `mycologyRareStrainChance`), and Grass Seeds (`PaintGrass`). Algae still seeds flora spores (GPU 14); painting material 128 plants an active organism
- worldgen flora seeding is off by default (`floraSeedAtWorldgen`); Life brush introduces spores/organisms
- snapshots are Version7 with Life/Genome payloads; Light is derived and omitted
- Version9 adds cricket fauna (`129 Cricket`, `130 Cricket Egg`), a dedicated fauna Tex2DArray (vitals/motion/genome/partner), a two-channel acoustic field, and fauna config. Crickets occupy surface-adjacent air cells, hop with gene- and environment-scaled impulse, graze algae, retain partner genomes, lay 2–4 eggs, and chirp on feeding/mating channels.

#### Implemented fauna contract
- materials `129 Cricket` and `130 Cricket Egg`, category Biological, high rigidity, `densityDisplaceable` off so fauna kernels own occupancy
- one surface-adjacent cell is one organism; fauna state is a 4-slice Tex2DArray swapped like Storm so `WriteCell` stays at the DX11 8-UAV cap
- slice 0 = calories, hydration, age ticks, cooldown; slice 1 = velocity + sub-cell accumulators; slice 2 = 12 genes + `stage|behavior|generation|lineage`; slice 3 = retained partner genome (`w != 0` when valid)
- stages: `EGG`, `JUVENILE`, `ADULT`, `DEAD`; behaviors: `Idle`, `Wander`, `Forage`, `MateSeek`, `Flee`, `Airborne`
- hops integrate ballistically every tick; launch impulse is strength / effective mass (dry mass + calories + moisture) scaled by support rigidity and wetness, then steered by wind drag
- feeding claims adjacent algae (`life.w` / biomass gradient); the winning cricket destroys the algae pixel and takes calories plus moisture
- typed acoustic field (RG32F, not `state.y`): R = feeding calls, G = mating calls; full crickets ignore feeding, juveniles/cooldown adults ignore mating
- adults reproduce after `faunaMaturityTicks` (default 2000); asexual clone if no partner, otherwise per-locus mix, then mutation; clutch 2–4 eggs hashed to a 1000–1500 tick hatch
- eggs exchange moisture, desiccate, burn, and can be wind-displaced through the same claim system
- Life brush paints cricket/egg when those materials are selected; worldgen fauna seeding is off by default
- snapshots are Version9 with fauna slices + both acoustic temporal fields; older snapshots load with cleared fauna/acoustics


## PHASE X - BEYOND

When life becomes too intelligent it might realize it's in a simulation and attempt to influence/escape the world
