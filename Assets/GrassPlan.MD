Flowering Grass Integration Plan

Architecture decisions





Store up to three grass organisms in a dedicated ping-ponged Texture2DArray, leaving the anchor cell’s Soil material intact. Each slot owns life/energy state, packed genome, flower/seed timing, root mask, nectar/pollen state, and dominant pollen-donor genome.



Give each slot 1–3 genetically/deterministically selected taps into the three immediate lower-Y cells. Root targets have no occupancy cap; competing taps share available water and nutrients proportionally.



Add a separate propagule array for exact seed counts, pollen load, and dominant maternal/donor genomes. Environmental transport starts at deliberately low rates and supports angular wind, runoff/water carriage, and inward gravity/settling.



Do not add a Grass material ID. Add stable material ID 131 for Detritus; grass is rendered from anchored organism state.

GPU state and scheduling





Define matching C#/HLSL grass layouts and genes in new [Assets/GeneSys/Runtime/Simulation/GrassGenome.cs](Assets/GeneSys/Runtime/Simulation/GrassGenome.cs) and [Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl](Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl). Reuse the flora expression, toxin-dose mutation multiplier, generation, and lineage conventions while adding grass genes for blade height/color, flower size/color, root count/affinity, flowering cadence, and dispersal.



Allocate persistent grass/propagule arrays plus a transient root-flux array in [Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs](Assets/GeneSys/Runtime/Simulation/Gpu/SimulationResources.cs). Keep these out of the generic eight-UAV WriteCell path.



Add [Assets/GeneSys/Compute/Simulation/Grass.compute](Assets/GeneSys/Compute/Simulation/Grass.compute) with separate kernels for root allocation, photosynthesis/lifecycle, pollen transport/reception, seed transport, germination, and detritus-drop claims. Wire transport and slow cadences in [Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs](Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs) after weather/hydrology and mycology updates.



Compute root allocation once into transient flux, apply soil debits in a core-state-only pass, and consume identical per-slot receipts in the grass pass. This preserves water/nutrient mass and avoids float atomics or a ninth UAV.

Grass lifecycle, roots, and mycology





Germinate transported seeds only into free slots on exposed Soil anchors; deterministic slot/claim rules enforce the three-organism cap. Clear or detach grass when its Soil anchor is lost.



Draw water from state.z + aux.y and nutrients from aux.z in tapped cells. Derive a live symbiosis mask from tapped mycology ecology.z: apply matching drought/heat/electric rare-strain modifiers while roots remain connected, and use the strongest/dominant tapped rare strain for flower tint. Basic strains cause no tint.



Extend [Assets/GeneSys/Compute/Simulation/Hydrology.compute](Assets/GeneSys/Compute/Simulation/Hydrology.compute) erosion cohesion with activeRootTapCount * 0.10 * baseSoilCohesion, with no root-count cap beyond normal final cohesion saturation.



Extend the existing flora light pass in [Assets/GeneSys/Compute/Simulation/Flora.compute](Assets/GeneSys/Compute/Simulation/Flora.compute) to include grass canopy opacity so algae and grass share one coherent light field.

Flowering, pollination, and seeds





Track elapsed solar days from configured ticks/day. When an energetic, unflowered adult reaches its deterministic 3–4-day check, create a flower and start a seed batch immediately; release it 1–2 solar days later.



While open, the flower exposes per-slot nectar, emits low-rate pollen, and receives transported pollen. For now nectar is inspectable simulation state and an HLSL access hook, not cricket behavior, so a pollinator organism can consume it next.



If no valid donor pollen was received, release 1–3 maternal clone seeds. If pollinated, release 2 clone seeds plus 3–6 seeds produced by fauna-style per-gene maternal/donor recombination. Apply the existing flora base mutation rate multiplied by toxin dose and the mutation gene to both clone and mixed offspring.



Release seeds and pollen into the outward carrier cell when possible, then let the propagule kernels handle wind, water/runoff, and gravity. Preserve dominant genomes through mass/count-weighted transport and reject self-pollination by lineage when donor identity is available.



After release, drop the flower as Detritus into a claimed outward/open neighbor and reset the flowering timer.

Detritus material and ecology





Add MaterialIds.Detritus = 131, a porous organic material asset, registry entry, and builder definition in [Assets/GeneSys/Runtime/Materials/MaterialRegistry.cs](Assets/GeneSys/Runtime/Materials/MaterialRegistry.cs), [Assets/GeneSys/Data/Materials](Assets/GeneSys/Data/Materials), and [Assets/GeneSys/Editor/GeneSysProjectBuilder.cs](Assets/GeneSys/Editor/GeneSysProjectBuilder.cs).



Initialize fallen Detritus with finite nutrient reserve in aux.z and retained moisture in aux.y. Add a symmetric, pre-tick-read exchange kernel in [Assets/GeneSys/Compute/Simulation/Hydrology.compute](Assets/GeneSys/Compute/Simulation/Hydrology.compute) to absorb vapor from adjacent Air, evaporate substantially slower than ordinary wet surfaces, distribute excess moisture to neighboring groundwater hosts, and leach nutrients into adjacent Soil without creating mass.



Deplete the reserve over time and resolve exhausted Detritus to Air when exposed or Soil when incorporated/buried. Include it in appropriate density, groundwater-host, fuel, support, and display-category helpers.

Rendering, tooling, and persistence





Extend [Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader](Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader) to draw each occupied slot at a stable sub-cell offset: a genetically sized green blade with nearby color variants, dark-brown root lines matching its root mask, and a tip-anchored flower. Bend blades from local/parent-cell angular wind and apply existing solar day/night lighting.



Bind grass textures and add inspection/overlay support through [Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs](Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs), [Assets/GeneSys/Runtime/UI/SimulationUIController.cs](Assets/GeneSys/Runtime/UI/SimulationUIController.cs), and [Assets/GeneSys/Runtime/Tools/SimulationTools.cs](Assets/GeneSys/Runtime/Tools/SimulationTools.cs). Expose slot genomes, roots, flowering countdown, nectar, pollen donor, and symbiosis traits.



Add grass tuning fields to [Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs](Assets/GeneSys/Runtime/Configuration/SimulationConfig.cs), including low initial pollen/seed transport rates, root uptake/cohesion, flowering thresholds, and Detritus exchange/decomposition.



Bump snapshots to v10 in [Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs](Assets/GeneSys/Runtime/Persistence/WorldSnapshotService.cs), serialize both new persistent arrays and all new config blocks, and preserve backward loading by initializing absent grass state for v9 and older saves.

Verification





Add EditMode coverage for genome packing/sanitization, material 131, resource layouts, defaults, shader constants, and registry/builder wiring.



Add PlayMode coverage for three-slot occupancy, root selection and unlimited target sharing, exact mass-conserving uptake, additive erosion resistance, rare-myco bonuses/tint selection, energy gating, 3–4 plus 1–2 solar-day timing, seed counts, recombination/mutation, low-rate pollen reception, all dispersal modes, Detritus moisture/nutrient conservation, flower-drop claims, day/night behavior, determinism, and v9/v10 snapshot round trips.



Run EditMode and focused grass/hydrology/rendering PlayMode tests, then the full PlayMode suite and Unity shader compilation/console check.