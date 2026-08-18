# GeneSys
working title


## GOAL 

SimLife style artificial life sandbox
familiar organisms/species juxtaposed with fantastic mash-ups/abominations
complex digital terrarium favoring recognizable behaviors/ecological niches
scale: 100s of complex active agents supported by an ecosystem consisting of compute driven subsystems
all relevant simulation settings tweakable via tab based settings menu UI  
platform: Unity 6 - 7950x3d, 32GB DDR5 , RTX 4080 super

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

### UI

- pause/play, step, sim speed controls
- tab based settings menu containing sim system settings
- selectable overlays displaying material/temp/pressure/wind/etc.
- inspect tool displaying stats for selected material
- brush tool for painting/editing world
## PHASE 2 - LIFE
### ORGANISMS

base organism template with "evolutionary path" via genetic expression to accommodate simple simulated plankton all the way to complex apex predator 
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



