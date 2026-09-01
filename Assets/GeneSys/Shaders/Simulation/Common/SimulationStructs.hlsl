#ifndef GENESYS_SIMULATION_STRUCTS_INCLUDED
#define GENESYS_SIMULATION_STRUCTS_INCLUDED

// Water mass contract:
//   state.z  = surface liquid/ice mass on non-atmosphere cells; cloud condensate on atmosphere cells
//   aux.x    = atmospheric vapor mass (humidity carried by Air cells; never a separate Gas pixel)
//   aux.y    = subsurface groundwater mass
//   aux.z    = nutrient / fertility
//   aux.w    = shared fault / erosion stress
//              Rock, Basalt, and Soil recover linearly via stressDecayRate (_Erosion.w)
//              in ErosionAndCollapse; Mantle/Magma keep tectonic fault semantics without decay.
// Ecology (dedicated RGBA32F field, not packed into aux):
//   ecology.x = viable spore load on air/water carriers and colonized substrate
//   ecology.y = myco value in [0, 1] on Soil/Sediment (flora fertility threshold)
//   ecology.z = dominant-strain trait flags (see MYCO_* below); exact integer codes
//   ecology.w = reserved
// Combustion (dedicated RGBA32F field, not packed into aux):
//   combustion.x = oxygen mass. Capacity is 1.0 in Empty/Gas cells and
//                  saturate(biology.z) porosity in solids/liquids, so soil holds
//                  trapped air and dense rock does not.
//   combustion.y = flame intensity in [0, 1]
//   combustion.z = soot / smoke mass (airborne tracer; settles into aux.z)
//   combustion.w = ignition accumulator in [0, 1]
// Milestone 1 fuel is ecology.y on Soil/Sediment plus life.y on Algae (ID 128).
// Milestone 2 also burns cricket/egg calories sampled from the dedicated fauna field.
// Burning consumes that biomass locally and writes heat/pressure/updraft/steam
// into the existing weather chain.
// Combustion never invents water: flashpoint vaporization only moves state.z -> aux.x.
// Life (dedicated RGBA32F field, follows cells through WriteCell):
//   life.x = spore load on air/water carriers and dormant reserve on substrate
//   life.y = biomass / caloric index in [0, 1] — combustion fuel on Algae pixels
//   life.z = stored photosynthate energy toward the reproduction threshold
//   life.w = nutrient exudate / chemoattractant (diffuses; slowly deposits into aux.z)
// Genome (dedicated RGBA32U field, follows cells through WriteCell):
//   genome.x/y/z = 12 genes, 8 bits each (see FLORA_GENE_*). Slot 10 is pole-drift rate.
//   genome.w = stage(8) | generation(8) | lineage(8) | toxinDose(8)
// Light (derived R32F field, recomputed every tick, excluded from Swap/WriteCell):
//   light.x = available photosynthetically active radiation after radial attenuation
// Storm (dedicated RGBA32F field, excluded from the global ping-pong swap):
//   storm.x = signed separated space charge on atmosphere cells
//   storm.y = channel plasma luminance in [0, 1], decays via stormChannelDecay
//   storm.z = ambient flash glow, diffused and decayed so a strike lights nearby sky
//   storm.w = breakdown state in [-1, 1]; positive accumulates toward 1 (ready to fire),
//             negative is post-strike cooldown climbing back to 0
// Storm values do not follow cell-moving kernels (MaterialMotion, LiquidDensityExchange)
// because charge lives in non-moving Air and the channel/flash channels are transient.
// Fauna (dedicated Tex2DArray RGBA32F, swapped like Storm, excluded from WriteCell):
//   slice 0 vitals: calories, hydration, age ticks, reproduction/mate cooldown ticks
//   slice 1 motion: vel.theta, vel.radius, subcell.theta, subcell.radius
//   slice 2 genome: 12×8-bit genes + stage|behavior|generation|lineage
//   slice 3 partner genome (x/y/z genes, w = 1 when a mate has been retained)
// Acoustic (dedicated RG32F current/previous pair, excluded from WriteCell):
//   .x feeding call, .y mating call; signed damped waves, never physical state.y
// Claims (transient R32_UInt Tex2DArray depth 4): move, feed, mate, egg destinations.
// Grass (dedicated Tex2DArray RGBA32F depth 12, swapped like Fauna, excluded from WriteCell):
//   3 slots × 4 slices: life, genome, timing, donor. Soil occupancy stays Soil.
//   life: biomass, energy, hydration, nectar
//   genome: 12×8-bit genes + stage|generation|lineage|toxinDose (flora meta)
//   timing: age ticks, flower elapsed, seed-release countdown, packed root/flower flags
//   donor: dominant pollen-donor genome; w low bit = valid donor
// Propagule (dedicated Tex2DArray RGBA32F depth 3): seedCount, pollenMass + maternal/donor genomes.
// Root flux (transient RGBA32F): taken water/nutrients and pre-tick demand totals per soil target.
// Flower-drop claims (transient R32_UInt): exclusive Detritus destinations.
// Wasp (dedicated Tex2DArray RGBA32F depth 7, swapped like Fauna, excluded from WriteCell):
//   slices 0-3 mirror Fauna (vitals, motion, genome, partner) with wasp gene meanings
//   slices 4-6 pollen cargo: up to three grass donor genomes, w low bit = valid sample
// Wasp claims (transient R32_UInt Tex2DArray depth 5): move, prey, mate, egg, flower.
// Grass visit (transient RGBA32F Tex2DArray depth 2): wasp -> grass handoff consumed one
//   tick later. Slice 0 = nectar drawn per slot in xyz; slice 1 = deposited donor genome.
// Atmosphere representation:
//   Air (ID 1) is the permanent atmospheric carrier. Vapor (ID 11) is a phase descriptor only;
//   runtime boiling / legacy cells normalize to Air while keeping vapor mass in aux.x.
//   Clouds are atmospheric state.z. Precipitation converts cloud-base condensate into
//   Water/Ice pixels or surface film from temperature, pressure, and updraft; it is not
//   a configurable pixel-mass threshold.
// Moisture-aware soil erosion:
//   Exposed soil only. Local moisture (state.z + aux.y) raises cohesion and suppresses
//   erosion-stress gain; dryness enables wind/runoff erosion but never converts alone.
//   Sediment does not auto-revert to soil; ash fertilization remains the pedogenesis path.
// Groundwater hosts (Soil/Sediment/porous Rock/Ash/Metal):
//   Film soak and Water-pixel contact drain state.z into aux.y up to porosity capacity.
//   Excess above field capacity percolates radially inward; lateral flow is host-only.
// Every transfer must subtract from a source reservoir before adding to a destination.
// Neighbor transfers are unsynchronized: a donor and its receiver run as separate threads and
// each writes only its own cell. So both sides must derive the transferred mass from the same
// pre-tick (read-texture) fields. Sizing a transfer from a value the current kernel already
// mutated makes the two sides disagree, and the difference silently leaks or invents water.
// Purely local moves between one cell's own reservoirs may use running values freely.
//
// Pressure (state.y):
//   Local sources/sinks (thermal expansion, mantle feed, vapor, brushes) still write absolute
//   pressure. AtmosphericContinuity adds divergence feedback so rising columns lower pressure
//   and converging columns raise it, enabling return flow. PressureDiffusion then transports
//   the anomaly relative to a radial equilibrium profile
//   min(pressureEquilibriumMaximum, (1 - radius) * pressureEquilibriumGradient).
//   Edge conductance is category-weighted (gas / fluid / porous / rigid) so highs and lows
//   persist for material-appropriate durations while fields eventually equilibrate.
// Atmosphere dynamics:
//   flow.x = angular wind, flow.y = radial wind (positive = outward/up). Signed buoyancy from
//   same-altitude temperature/humidity anomalies drives updrafts and downdrafts. Heat, vapor,
//   and cloud condensate advect with flow under a CFL outbound-mass cap.
// Liquid density exchange:
//   LiquidDensityExchange swaps whole cells across liquid interfaces when both materials opt in
//   via motion.x (densityDisplaceable). Density (physical.x) alone decides direction: a denser
//   upper neighbor sinks / a lighter lower neighbor rises. buoyancyBias (biology.w) only scales
//   exchange probability and cannot reverse float/sink. Equal densities within epsilon stay put.
//   Foundational solids (Core/Mantle) and Ash remain opted out; Ash keeps AshTransport.
// Surface hydrostatic leveling:
//   After groundwater and precipitation, hydrology profiles each angular column's
//   atmosphere-connected liquid (film plus contiguous Water pixels), then exchanges mass
//   across wrapped faces from hydraulic head. Head is substrate radius plus liquid volume.
//   Film-capable beds keep the fractional remainder; complete cells become Water/Ice pixels.
//   Terrain saddles block flow; ice lids and enclosed cave water are excluded.
//   state.y remains atmospheric/material pressure and is never reused as water head.

struct MaterialGpuData
{
    float4 color;
    float4 physical;   // density, rigidity, repose radians, grain size
    float4 transport;  // thermal conductivity, heat capacity, electrical conductivity, absorbency
    float4 phase;      // melt point, boil point, thermal expansion, electric expansion
    float4 biology;    // toxicity, calories, porosity, buoyancy bias
    float4 metadata;   // category, packed phase IDs, bio-modifiable, stable ID
    float4 motion;     // densityDisplaceable, reserved, reserved, reserved
    float4 combustion; // ignitionTemperature, flashPoint, oxygenDemand, smokeYield
};

struct BrushCommand
{
    int2 center;
    int radius;
    uint materialId;
    float4 values; // mode, amount, reserved, reserved
};

struct StrikeSeed
{
    int2 cell;
    float charge;
    uint kind; // 0 = cloud-to-ground strike, 1 = in-cloud sheet
};

#define STORM_KIND_STRIKE 0u
#define STORM_KIND_SHEET 1u
#define STORM_MAX_STRIKES 32u
#define STORM_BRANCH_STACK 32

uint WrapTheta(int theta, int width)
{
    int wrapped = theta;
    if (wrapped < 0) wrapped += width;
    else if (wrapped >= width) wrapped -= width;
    return (uint)wrapped;
}

int2 ClampCell(int2 cell, int2 size)
{
    return int2(WrapTheta(cell.x, size.x), clamp(cell.y, 0, size.y - 1));
}

float Radius01(int radiusIndex, int radialSize)
{
    return (radiusIndex + 0.5) / max(1.0, (float)radialSize);
}

// Polar area weight increases outward (matches PolarGridDefinition.CellAreaWeight).
float CellAreaWeight(int radiusIndex, int radialSize)
{
    return max(0.5 / max(1.0, (float)radialSize), Radius01(radiusIndex, radialSize));
}

// Tangential (angular) edge weight shrinks outward so θ transport respects arc length.
float TangentialEdgeWeight(int radiusIndex, int radialSize)
{
    return 1.0 / max(CellAreaWeight(radiusIndex, radialSize), 1e-4);
}

float RadialDelta(int radialSize)
{
    return 1.0 / max(1.0, (float)radialSize);
}

float SafeFinite(float value, float fallback)
{
    return clamp(value, -1e20, 1e20);
}

float4 SafeFinite4(float4 value, float4 fallback)
{
    return float4(
        SafeFinite(value.x, fallback.x),
        SafeFinite(value.y, fallback.y),
        SafeFinite(value.z, fallback.z),
        SafeFinite(value.w, fallback.w));
}

float AngularDistance01(float a, float b)
{
    float d = abs(a - b);
    return min(d, 1.0 - d);
}

uint Hash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352d;
    value ^= value >> 15;
    value *= 0x846ca68b;
    value ^= value >> 16;
    return value;
}

float Hash01(uint value)
{
    return (Hash(value) & 0x00ffffff) / 16777215.0;
}

// Ice-cap angular centers used by V2 worldgen and flora pole-drift. Must stay in lockstep
// with PolarPoleGeometry.cs: pole = Hash01(seed * 9829), antipode = pole + 0.5.
float PoleAngle01(int seed)
{
    return Hash01((uint)seed * 9829u);
}

// -1 / +1 angular step toward the nearer ice cap; 0 when already on that column.
int NearestPoleStep(int theta, int width, int seed)
{
    float angular = (theta + 0.5) / max(1.0, (float)width);
    float a = frac(PoleAngle01(seed));
    float b = frac(a + 0.5);
    float pole = AngularDistance01(angular, a) <= AngularDistance01(angular, b) ? a : b;
    float delta = pole - angular;
    if (delta > 0.5) delta -= 1.0;
    else if (delta < -0.5) delta += 1.0;
    float cells = delta * width;
    return abs(cells) < 0.5 ? 0 : (cells > 0.0 ? 1 : -1);
}

// Liquid, Granular, Solid, Magma, Biological — not Empty/Gas.
bool IsMottledCategory(float category)
{
    return category >= 2.0 && category <= 6.0;
}

uint HashMaterialShade(uint2 cell, uint materialId, int seed)
{
    return (uint)(Hash01(cell.x * 73856093u + cell.y * 19349663u + materialId * 83492791u + (uint)seed * 9137u) * 2.999);
}

uint PickMaterialShade(float category, uint2 cell, uint materialId, int seed)
{
    if (!IsMottledCategory(category)) return 0u;
    return HashMaterialShade(cell, materialId, seed);
}

#define MYCO_DROUGHT_PRONE 1u
#define MYCO_DROUGHT_RESISTANT 2u
#define MYCO_ELECTRIC_PRONE 4u
#define MYCO_ELECTRIC_RESISTANT 8u
#define MYCO_HEAT_PRONE 16u
#define MYCO_HEAT_RESISTANT 32u
#define MYCO_ALL_FLAGS 63u

uint SanitizeMycologyTraits(uint flags)
{
    flags &= MYCO_ALL_FLAGS;
    if ((flags & MYCO_DROUGHT_PRONE) != 0u && (flags & MYCO_DROUGHT_RESISTANT) != 0u)
        flags &= ~MYCO_DROUGHT_PRONE;
    if ((flags & MYCO_ELECTRIC_PRONE) != 0u && (flags & MYCO_ELECTRIC_RESISTANT) != 0u)
        flags &= ~MYCO_ELECTRIC_PRONE;
    if ((flags & MYCO_HEAT_PRONE) != 0u && (flags & MYCO_HEAT_RESISTANT) != 0u)
        flags &= ~MYCO_HEAT_PRONE;
    return flags;
}

uint DecodeMycologyTraits(float packed)
{
    return SanitizeMycologyTraits((uint)round(packed));
}

float4 SanitizeEcology(float4 ecology)
{
    float4 value = max(SafeFinite4(ecology, 0.0), 0.0);
    value.y = saturate(value.y);
    value.z = (float)DecodeMycologyTraits(value.z);
    return value;
}

bool IsSporeAirCarrier(uint material)
{
    return material == 1u || material == 11u;
}

bool IsSporeWaterCarrier(uint material)
{
    return material == 9u;
}

bool IsSporeCarrier(uint material)
{
    return IsSporeAirCarrier(material) || IsSporeWaterCarrier(material);
}

bool IsMycologySubstrate(uint material)
{
    return material == 7u || material == 8u;
}

bool IsGroundwaterHost(uint material, MaterialGpuData definition)
{
    if (material == 0u || material == 1u || material == 11u) return false;
    if (material == 2u || material == 3u || material == 6u) return false;
    if (material == 9u || material == 10u) return false;
    return saturate(definition.biology.z) > 0.05;
}

float GroundwaterCapacity(MaterialGpuData definition)
{
    return saturate(definition.biology.z);
}

float OxygenCapacity(uint material, MaterialGpuData definition)
{
    float category = definition.metadata.x;
    if (material == 0u || material == 1u || material == 11u || category == 1.0)
        return 1.0;
    return saturate(definition.biology.z);
}

float4 SanitizeCombustion(float4 combustion)
{
    float4 value = max(SafeFinite4(combustion, 0.0), 0.0);
    value.y = saturate(value.y);
    value.w = saturate(value.w);
    return value;
}

float4 SeedCombustion(uint material, MaterialGpuData definition)
{
    return SanitizeCombustion(float4(OxygenCapacity(material, definition), 0.0, 0.0, 0.0));
}

float MycologyFuel(uint material, float4 ecology)
{
    return IsMycologySubstrate(material) ? saturate(ecology.y) : 0.0;
}

#define FLORA_ALGAE_ID 128u
#define FLORA_STAGE_SPORE 0u
#define FLORA_STAGE_ACTIVE 1u
#define FLORA_STAGE_DORMANT 2u
#define FLORA_STAGE_DESICCATED 3u
#define FLORA_STAGE_DEAD 4u

#define FLORA_GENE_TEMP_OPTIMUM 0u
#define FLORA_GENE_TEMP_TOLERANCE 1u
#define FLORA_GENE_MOISTURE_OPTIMUM 2u
#define FLORA_GENE_MOISTURE_TOLERANCE 3u
#define FLORA_GENE_LIGHT_AFFINITY 4u
#define FLORA_GENE_REPRODUCTION 5u
#define FLORA_GENE_METABOLIC 6u
#define FLORA_GENE_DORMANCY 7u
#define FLORA_GENE_TOXIN_TOLERANCE 8u
#define FLORA_GENE_EXUDATION 9u
#define FLORA_GENE_POLE_DRIFT 10u
#define FLORA_GENE_MUTATION 11u

bool IsFloraMaterial(uint material)
{
    return material == FLORA_ALGAE_ID;
}

bool IsFloraOpenHabitat(uint material)
{
    return material == 0u || material == 1u || material == 11u;
}

bool IsFloraAnchor(uint material)
{
    return material == 7u || material == 8u || material == 9u || material == 10u || material == FLORA_ALGAE_ID;
}

bool IsFloraSporeDepositTarget(uint material)
{
    return material == 7u || material == 8u || material == 9u || material == 10u || material == FLORA_ALGAE_ID;
}

bool IsFloraGerminationTarget(uint material)
{
    return IsFloraOpenHabitat(material) || material == 9u || material == 10u;
}

uint DecodeGene(uint4 genome, uint index)
{
    uint word = index < 4u ? genome.x : (index < 8u ? genome.y : genome.z);
    uint shift = (index & 3u) * 8u;
    return (word >> shift) & 255u;
}

void EncodeGene(inout uint4 genome, uint index, uint value)
{
    value &= 255u;
    uint shift = (index & 3u) * 8u;
    uint mask = ~(255u << shift);
    if (index < 4u)
        genome.x = (genome.x & mask) | (value << shift);
    else if (index < 8u)
        genome.y = (genome.y & mask) | (value << shift);
    else
        genome.z = (genome.z & mask) | (value << shift);
}

uint FloraStage(uint4 genome)
{
    return genome.w & 255u;
}

uint FloraGeneration(uint4 genome)
{
    return (genome.w >> 8) & 255u;
}

uint FloraLineage(uint4 genome)
{
    return (genome.w >> 16) & 255u;
}

uint FloraToxinDose(uint4 genome)
{
    return (genome.w >> 24) & 255u;
}

uint PackFloraMeta(uint stage, uint generation, uint lineage, uint toxinDose)
{
    return (stage & 255u) | ((generation & 255u) << 8) | ((lineage & 255u) << 16) | ((toxinDose & 255u) << 24);
}

#define ORGANISM_HISTORY_CAPACITY 2048u
#define ORGANISM_KIND_BIRTH 0u
#define ORGANISM_KIND_REPRODUCE 1u
#define ORGANISM_KIND_DEATH 2u
#define ORGANISM_KIND_MATE 3u
#define ORGANISM_CAUSE_NONE 0u
#define ORGANISM_CAUSE_DESICCATION 1u
#define ORGANISM_CAUSE_TOXIN 2u
#define ORGANISM_CAUSE_FIRE 3u
#define ORGANISM_CAUSE_PAINTED 4u
#define ORGANISM_CAUSE_CONSUMED 5u
#define ORGANISM_CAUSE_STARVATION 6u
#define ORGANISM_CAUSE_DEHYDRATION 7u
#define ORGANISM_CAUSE_LAID 8u
#define ORGANISM_CAUSE_MATED 9u
#define ORGANISM_CAUSE_HATCHED 10u

struct OrganismHistoryEvent
{
    uint tick;
    uint kind;
    uint generation;
    uint lineage;
    uint cause;
    uint pad0;
    uint pad1;
    uint pad2;
};

uint4 SanitizeGenome(uint4 genome)
{
    uint stage = FloraStage(genome);
    if (stage > FLORA_STAGE_DEAD)
        stage = FLORA_STAGE_SPORE;
    genome.w = PackFloraMeta(stage, FloraGeneration(genome), FloraLineage(genome), FloraToxinDose(genome));
    return genome;
}

float4 SanitizeLife(float4 life)
{
    float4 value = max(SafeFinite4(life, 0.0), 0.0);
    value.y = saturate(value.y);
    value.z = saturate(value.z);
    return value;
}

// Life and genome share one Tex2DArray UAV (slice 0 = life, slice 1 = asfloat(genome))
// so WriteCell stays at the DX11 8-UAV cap. Genome bits ride float channels for AsyncGPUReadback.
#define FLORA_LIFE_SLICE 0
#define FLORA_GENOME_SLICE 1

float4 SampleLife(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, FLORA_LIFE_SLICE, 0));
}

uint4 SampleGenome(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, FLORA_GENOME_SLICE, 0)));
}

void WriteLifeGenome(RWTexture2DArray<float4> tex, int2 cell, float4 life, uint4 genome)
{
    tex[uint3((uint2)cell, FLORA_LIFE_SLICE)] = SanitizeLife(life);
    tex[uint3((uint2)cell, FLORA_GENOME_SLICE)] = asfloat(SanitizeGenome(genome));
}

float FloraExpressFactor(uint gene, float range)
{
    return 1.0 + ((gene / 255.0) - 0.5) * 2.0 * saturate(range);
}

float FloraExpressShift(uint gene, float range)
{
    return ((gene / 255.0) - 0.5) * 2.0 * range;
}

// Alleles at or below 16 are silent, so "no drift at all" is reachable and heritable.
float FloraPoleDrift(uint4 genome)
{
    uint g = DecodeGene(genome, FLORA_GENE_POLE_DRIFT);
    return g <= 16u ? 0.0 : (float)(g - 16u) / 239.0;
}

float FloraFuel(uint material, float4 life, uint stage)
{
    if (!IsFloraMaterial(material))
        return 0.0;
    if (stage == FLORA_STAGE_SPORE || stage == FLORA_STAGE_DEAD)
        return 0.0;
    return saturate(life.y);
}

#define FAUNA_CRICKET_ID 129u
#define FAUNA_EGG_ID 130u
#define WASP_ID 132u
#define WASP_EGG_ID 133u
#define TREE_LEAF_ID 134u
#define TREE_WOOD_ID 135u
#define FAUNA_STAGE_EMPTY 0u
#define FAUNA_STAGE_EGG 1u
#define FAUNA_STAGE_JUVENILE 2u
#define FAUNA_STAGE_ADULT 3u
#define FAUNA_STAGE_DEAD 4u
#define FAUNA_BEHAVIOR_IDLE 0u
#define FAUNA_BEHAVIOR_WANDER 1u
#define FAUNA_BEHAVIOR_FORAGE 2u
#define FAUNA_BEHAVIOR_MATE_SEEK 3u
#define FAUNA_BEHAVIOR_FLEE 4u
#define FAUNA_BEHAVIOR_AIRBORNE 5u
#define FAUNA_GENE_JUMP_STRENGTH 0u
#define FAUNA_GENE_DRY_MASS 1u
#define FAUNA_GENE_DRAG 2u
#define FAUNA_GENE_MOISTURE_LOAD 3u
#define FAUNA_GENE_HYDRATION_RETENTION 4u
#define FAUNA_GENE_METABOLISM 5u
#define FAUNA_GENE_CALORIE_CAPACITY 6u
#define FAUNA_GENE_NUTRIENT_SENSE 7u
#define FAUNA_GENE_HEARING 8u
#define FAUNA_GENE_THREAT_RESPONSE 9u
#define FAUNA_GENE_FERTILITY 10u
#define FAUNA_GENE_MUTATION 11u
#define FAUNA_VITALS_SLICE 0
#define FAUNA_MOTION_SLICE 1
#define FAUNA_GENOME_SLICE 2
#define FAUNA_PARTNER_SLICE 3
#define FAUNA_CLAIM_MOVE 0
#define FAUNA_CLAIM_FEED 1
#define FAUNA_CLAIM_MATE 2
#define FAUNA_CLAIM_EGG 3
#define FAUNA_CLAIM_EMPTY 0xffffffffu

bool IsCricketMaterial(uint material)
{
    return material == FAUNA_CRICKET_ID;
}

bool IsFaunaEggMaterial(uint material)
{
    return material == FAUNA_EGG_ID;
}

bool IsFaunaMaterial(uint material)
{
    return IsCricketMaterial(material) || IsFaunaEggMaterial(material);
}

bool IsWaspMaterial(uint material)
{
    return material == WASP_ID;
}

bool IsWaspEggMaterial(uint material)
{
    return material == WASP_EGG_ID;
}

// Every mobile organism material. Combustion and metrics use this; occupancy and
// support rules must not, because adult wasps fly and cannot be stood on.
bool IsAnyFaunaMaterial(uint material)
{
    return IsFaunaMaterial(material) || IsWaspMaterial(material) || IsWaspEggMaterial(material);
}

bool IsFaunaOpenHabitat(uint material)
{
    return material == 0u || material == 1u || material == 11u;
}

bool IsFaunaSupport(uint material)
{
    return material == 4u || material == 5u || material == 7u || material == 8u
        || material == 9u || material == 10u || material == 12u || material == 13u
        || material == 131u || material == FLORA_ALGAE_ID || material == TREE_WOOD_ID || IsFaunaMaterial(material)
        || IsWaspEggMaterial(material);
}

uint FaunaStage(uint4 genome)
{
    return genome.w & 255u;
}

uint FaunaBehavior(uint4 genome)
{
    return (genome.w >> 8) & 255u;
}

uint FaunaGeneration(uint4 genome)
{
    return (genome.w >> 16) & 255u;
}

uint FaunaLineage(uint4 genome)
{
    return (genome.w >> 24) & 255u;
}

uint PackFaunaMeta(uint stage, uint behavior, uint generation, uint lineage)
{
    return (stage & 255u) | ((behavior & 255u) << 8) | ((generation & 255u) << 16) | ((lineage & 255u) << 24);
}

uint4 SanitizeFaunaGenome(uint4 genome)
{
    uint stage = FaunaStage(genome);
    if (stage > FAUNA_STAGE_DEAD)
        stage = FAUNA_STAGE_EMPTY;
    uint behavior = FaunaBehavior(genome);
    if (behavior > FAUNA_BEHAVIOR_AIRBORNE)
        behavior = FAUNA_BEHAVIOR_IDLE;
    genome.w = PackFaunaMeta(stage, behavior, FaunaGeneration(genome), FaunaLineage(genome));
    return genome;
}

float4 SanitizeFaunaVitals(float4 vitals)
{
    float4 value = max(SafeFinite4(vitals, 0.0), 0.0);
    value.x = saturate(value.x);
    value.y = saturate(value.y);
    return value;
}

float4 SanitizeFaunaMotion(float4 motion)
{
    float4 value = SafeFinite4(motion, 0.0);
    value.xy = clamp(value.xy, -8.0, 8.0);
    value.zw = clamp(value.zw, -1.5, 1.5);
    return value;
}

float4 SampleFaunaVitals(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, FAUNA_VITALS_SLICE, 0));
}

float4 SampleFaunaMotion(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, FAUNA_MOTION_SLICE, 0));
}

uint4 SampleFaunaGenome(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, FAUNA_GENOME_SLICE, 0)));
}

uint4 SampleFaunaPartner(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, FAUNA_PARTNER_SLICE, 0)));
}

void WriteFaunaState(RWTexture2DArray<float4> tex, int2 cell, float4 vitals, float4 motion, uint4 genome, uint4 partner)
{
    tex[uint3((uint2)cell, FAUNA_VITALS_SLICE)] = SanitizeFaunaVitals(vitals);
    tex[uint3((uint2)cell, FAUNA_MOTION_SLICE)] = SanitizeFaunaMotion(motion);
    tex[uint3((uint2)cell, FAUNA_GENOME_SLICE)] = asfloat(SanitizeFaunaGenome(genome));
    tex[uint3((uint2)cell, FAUNA_PARTNER_SLICE)] = asfloat(partner);
}

void ClearFaunaState(RWTexture2DArray<float4> tex, int2 cell)
{
    WriteFaunaState(tex, cell, 0.0, 0.0, uint4(0u, 0u, 0u, 0u), uint4(0u, 0u, 0u, 0u));
}

float FaunaExpressFactor(uint gene, float range)
{
    return 1.0 + ((gene / 255.0) - 0.5) * 2.0 * saturate(range);
}

bool FaunaHasPartner(uint4 partner)
{
    return partner.w != 0u;
}

float FaunaFuel(uint material, float4 vitals, uint stage)
{
    if (!IsFaunaMaterial(material))
        return 0.0;
    if (stage == FAUNA_STAGE_EMPTY || stage == FAUNA_STAGE_DEAD)
        return 0.0;
    return saturate(vitals.x);
}

uint PackFaunaCell(int2 cell, int2 size)
{
    return 1u + (uint)cell.y * (uint)max(1, size.x) + (uint)cell.x;
}

int2 UnpackFaunaCell(uint packed, int2 size)
{
    uint idx = packed - 1u;
    uint width = (uint)max(1, size.x);
    return int2((int)(idx % width), (int)(idx / width));
}

uint4 RandomFaunaGenome(uint2 cell, int seed, int tick)
{
    uint4 genome = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float h = Hash01(cell.x * 19349663u + cell.y * 73856093u + i * 83492791u + (uint)seed * 6151u + (uint)tick * 31337u);
        EncodeGene(genome, i, (uint)round(h * 255.0));
    }
    uint lineage = (uint)round(Hash01(cell.x * 73856093u + cell.y * 19349663u + (uint)seed * 9829u + (uint)tick) * 255.0);
    genome.w = PackFaunaMeta(FAUNA_STAGE_ADULT, FAUNA_BEHAVIOR_IDLE, 0u, lineage);
    return genome;
}

uint4 MutateFaunaGenome(uint4 genome, float baseMutationRate, uint salt)
{
    genome = SanitizeFaunaGenome(genome);
    float mutationGene = FaunaExpressFactor(DecodeGene(genome, FAUNA_GENE_MUTATION), 1.0);
    float rate = max(0.0, baseMutationRate) * mutationGene;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float unit = Hash01(salt + i * 83492791u + DecodeGene(genome, i) * 747796405u);
        int step = (int)round((unit * 2.0 - 1.0) * rate * 255.0);
        int next = clamp((int)DecodeGene(genome, i) + step, 0, 255);
        EncodeGene(genome, i, (uint)next);
    }
    return genome;
}

uint4 CombineFaunaGenomes(uint4 mother, uint4 partner, uint salt)
{
    uint4 child = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float unit = Hash01(salt + i * 83492791u);
        uint gene = unit < 0.5 ? DecodeGene(mother, i) : DecodeGene(partner, i);
        EncodeGene(child, i, gene);
    }
    return child;
}

uint4 RandomFloraGenome(uint2 cell, int seed, int tick)
{
    uint4 genome = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float h = Hash01(cell.x * 73856093u + cell.y * 19349663u + i * 83492791u + (uint)seed * 31337u + (uint)tick * 6151u);
        EncodeGene(genome, i, (uint)round(h * 255.0));
    }
    uint lineage = (uint)round(Hash01(cell.x * 19349663u + cell.y * 73856093u + (uint)seed * 7919u + (uint)tick) * 255.0);
    genome.w = PackFloraMeta(FLORA_STAGE_SPORE, 0u, lineage, 0u);
    return genome;
}

void ConsiderGenomeContribution(inout float bestMass, inout uint4 bestGenome, float mass, uint4 genome)
{
    if (mass > bestMass)
    {
        bestMass = mass;
        bestGenome = genome;
    }
}

float TraitModulatedIgnition(float ignitionTemperature, uint traits, float traitEffect)
{
    float threshold = ignitionTemperature;
    float effect = saturate(traitEffect);
    if ((traits & MYCO_HEAT_RESISTANT) != 0u)
        threshold += 28.0 * effect;
    if ((traits & MYCO_HEAT_PRONE) != 0u)
        threshold -= 22.0 * effect;
    return threshold;
}

float InfiltrationAmount(float sourceMass, float remainingCapacity, float absorbency, float porosity, float infiltrationRate, float dt)
{
    float rate = saturate(absorbency) * saturate(porosity) * max(0.0, infiltrationRate) * max(0.0, dt);
    return max(0.0, min(max(0.0, sourceMass), min(max(0.0, remainingCapacity), rate)));
}

float DrainableGroundwater(float groundwater, float capacity, float fieldCapacityFraction)
{
    float hold = max(0.0, capacity) * saturate(fieldCapacityFraction);
    return max(0.0, groundwater - hold);
}

float PercolationAmount(float upperGroundwater, float upperCapacity, float lowerGroundwater, float lowerCapacity,
    float upperPorosity, float lowerPorosity, float groundwaterRate, float fieldCapacityFraction, float dt)
{
    float drainable = DrainableGroundwater(upperGroundwater, upperCapacity, fieldCapacityFraction);
    float remaining = max(0.0, lowerCapacity - lowerGroundwater);
    float rate = max(0.0, groundwaterRate) * min(saturate(upperPorosity), saturate(lowerPorosity)) * max(0.0, dt);
    return max(0.0, min(drainable, min(remaining, rate)));
}

float MixTemperature(float destTemp, float destHeatCapacity, float sourceTemp, float transferredMass, float waterHeatCapacity)
{
    float destMassHeat = max(0.001, destHeatCapacity);
    float srcMassHeat = max(0.0, transferredMass) * max(0.001, waterHeatCapacity);
    return (destTemp * destMassHeat + sourceTemp * srcMassHeat) / (destMassHeat + srcMassHeat);
}

#define WATER_MELT_TEMP 0.0
#define WATER_BOIL_TEMP 100.0

float PressureEquilibriumAt(int radiusIndex, int radialSize, float gradient, float maximum)
{
    float radius = Radius01(radiusIndex, radialSize);
    return min(maximum, max(0.0, (1.0 - radius) * gradient));
}

float WaterPressureNorm(float pressure, float equilibrium)
{
    return saturate(pressure / max(0.05, equilibrium + 0.25));
}

float WaterBoilTemperature(float pressure, float equilibrium, float pressureResponse)
{
    float anomaly = pressure - equilibrium;
    float shift = clamp(anomaly * max(0.0, pressureResponse) * 25.0, -40.0, 60.0);
    return WATER_BOIL_TEMP + shift;
}

float WaterVaporCapacity(float temperature, float radius, float pressure, float atmosphereStartRadius, float saturationScale, float pressureResponse)
{
    float altitudeCooling = saturate((radius - atmosphereStartRadius) / max(0.01, 1.0 - atmosphereStartRadius));
    float thermal = saturate((temperature + 20.0) / 60.0);
    float pressureBoost = 1.0 + saturate(pressure) * (0.15 + 0.35 * saturate(pressureResponse));
    return max(0.01, max(0.01, saturationScale) * thermal * (1.0 - altitudeCooling * 0.65) * pressureBoost);
}

float WaterFrozenFraction(float temperature, float hysteresis)
{
    float h = max(0.001, hysteresis);
    return 1.0 - saturate((temperature - (WATER_MELT_TEMP - h)) / max(2.0 * h, 1e-4));
}

float WaterLiquidFraction(float temperature, float hysteresis)
{
    return 1.0 - WaterFrozenFraction(temperature, hysteresis);
}

uint LiquidPixelId(float temperature, float hysteresis)
{
    return temperature < WATER_MELT_TEMP - max(0.001, hysteresis) ? 10u : 9u;
}

uint WaterPhaseId(float temperature, uint current, float hysteresis)
{
    float h = max(0.001, hysteresis);
    if (current == 10u)
        return temperature > WATER_MELT_TEMP + h ? 9u : 10u;
    if (current == 9u)
        return temperature < WATER_MELT_TEMP - h ? 10u : 9u;
    return LiquidPixelId(temperature, hysteresis);
}

float WaterLatentHeatDelta(float mass, float latentScale, float strength)
{
    return clamp(max(0.0, mass) * max(0.0, latentScale) * strength, -8.0, 8.0);
}

#ifndef PRECIP_MIN_DROP
#define PRECIP_MIN_DROP 0.45
#endif

float PrecipitationMass(float cloud, float retain, float temperature, float pressure, float equilibrium, float radialFlow, float precipitationRate, float pressureResponse, float dt)
{
    float excess = max(0.0, cloud - max(0.01, retain));
    if (excess <= 1e-8 || precipitationRate <= 1e-8)
        return 0.0;
    float coldBoost = lerp(1.45, 0.7, saturate((temperature + 10.0) / 50.0));
    float pressureBoost = 1.0 + (WaterPressureNorm(pressure, equilibrium) - 0.5) * saturate(pressureResponse) * 0.6;
    float updraft = max(0.0, radialFlow);
    float downdraft = max(0.0, -radialFlow);
    float vertical = saturate(1.0 - updraft * 0.35) * (1.0 + saturate(downdraft) * 0.25);
    float efficiency = max(0.05, coldBoost * pressureBoost * vertical);
    return min(min(excess, 1.0), max(0.0, precipitationRate) * efficiency * max(0.0, dt));
}

// Rate is mean mass per tick. Airborne receivers wait for a real drop so falling
// pixels carry enough mass to pond instead of collapsing into invisible film.
float PrecipitationEmitMass(float ready, float excess, bool airReceiver, uint decisionHash)
{
    if (ready <= 1e-8)
        return 0.0;
    if (excess < PRECIP_MIN_DROP)
        return airReceiver ? 0.0 : ready;
    float drop = min(1.0, excess);
    float p = saturate(ready / PRECIP_MIN_DROP * 6.0);
    if (Hash01(decisionHash) < p)
        return drop;
    return airReceiver ? 0.0 : ready;
}

float4 SanitizeStorm(float4 storm)
{
    float4 value = SafeFinite4(storm, 0.0);
    value.x = clamp(value.x, -8.0, 8.0);
    value.y = saturate(value.y);
    value.z = saturate(value.z);
    value.w = clamp(value.w, -1.0, 1.0);
    return value;
}

// CFL-capped face fluxes. Angular and radial weights contribute independently so
// dominant angular wind cannot starve radial lofting.
float4 DonorFaceFlux(float2 flow, float mass, float advectionRate, float4 validFaces, float tangentialWeight, float dt, float cflLimit)
{
    if (mass <= 1e-8 || advectionRate <= 1e-8)
        return 0.0;

    float left = max(0.0, -flow.x) * validFaces.x * tangentialWeight;
    float right = max(0.0, flow.x) * validFaces.y * tangentialWeight;
    float down = max(0.0, -flow.y) * validFaces.z;
    float up = max(0.0, flow.y) * validFaces.w;
    float weightSum = left + right + down + up;
    if (weightSum <= 1e-8)
        return 0.0;

    float cfl = saturate(cflLimit);
    float moveFraction = min(cfl, weightSum * advectionRate * max(0.0, dt));
    float movable = mass * moveFraction;
    float inv = 1.0 / weightSum;
    return float4(movable * left * inv, movable * right * inv, movable * down * inv, movable * up * inv);
}

// Signed receive for the current cell from one neighbor. Positive means this cell gains mass.
float LateralGroundwaterReceive(float selfGroundwater, float neighborGroundwater, float selfCapacity, float neighborCapacity,
    float selfPorosity, float neighborPorosity, float rate, float dt)
{
    float delta = neighborGroundwater - selfGroundwater;
    float mag = abs(delta) * 0.5 * saturate(min(saturate(selfPorosity), saturate(neighborPorosity)) * max(0.0, rate) * max(0.0, dt));
    if (delta > 0.0)
        mag = min(mag, min(max(0.0, neighborGroundwater), max(0.0, selfCapacity - selfGroundwater)));
    else
        mag = min(mag, min(max(0.0, selfGroundwater), max(0.0, neighborCapacity - neighborGroundwater)));
    return delta > 0.0 ? mag : -mag;
}

uint PickRareMycologyTraits(uint2 cell, int seed)
{
    uint h = Hash(cell.x * 73856093u + cell.y * 19349663u + (uint)seed * 83492791u + 2707u);
    uint axis = h % 3u;
    uint resistant = (h >> 3) & 1u;
    uint traits = 0u;
    if (axis == 0u) traits = resistant != 0u ? MYCO_DROUGHT_RESISTANT : MYCO_DROUGHT_PRONE;
    else if (axis == 1u) traits = resistant != 0u ? MYCO_ELECTRIC_RESISTANT : MYCO_ELECTRIC_PRONE;
    else traits = resistant != 0u ? MYCO_HEAT_RESISTANT : MYCO_HEAT_PRONE;

    if (((h >> 8) & 7u) == 0u)
    {
        uint axis2 = (axis + 1u + ((h >> 11) & 1u)) % 3u;
        uint resistant2 = (h >> 12) & 1u;
        if (axis2 == 0u) traits |= resistant2 != 0u ? MYCO_DROUGHT_RESISTANT : MYCO_DROUGHT_PRONE;
        else if (axis2 == 1u) traits |= resistant2 != 0u ? MYCO_ELECTRIC_RESISTANT : MYCO_ELECTRIC_PRONE;
        else traits |= resistant2 != 0u ? MYCO_HEAT_RESISTANT : MYCO_HEAT_PRONE;
    }
    return SanitizeMycologyTraits(traits);
}

void ConsiderTraitContribution(inout float bestMass, inout uint bestTraits, float mass, uint traits)
{
    if (mass > bestMass)
    {
        bestMass = mass;
        bestTraits = traits;
    }
}

#define MATERIAL_DETRITUS 131u
#define GRASS_SLOT_COUNT 3
#define GRASS_SLICES_PER_SLOT 4
#define GRASS_SLICE_COUNT 12
#define GRASS_LIFE_OFFSET 0
#define GRASS_GENOME_OFFSET 1
#define GRASS_TIMING_OFFSET 2
#define GRASS_DONOR_OFFSET 3
#define GRASS_STAGE_EMPTY 0u
#define GRASS_STAGE_JUVENILE 1u
#define GRASS_STAGE_ADULT 2u
#define GRASS_GENE_TEMP_OPTIMUM 0u
#define GRASS_GENE_TEMP_TOLERANCE 1u
#define GRASS_GENE_MOISTURE_OPTIMUM 2u
#define GRASS_GENE_MOISTURE_TOLERANCE 3u
#define GRASS_GENE_LIGHT_AFFINITY 4u
#define GRASS_GENE_BLADE_HEIGHT 5u
#define GRASS_GENE_BLADE_COLOR 6u
#define GRASS_GENE_FLOWER_SIZE 7u
#define GRASS_GENE_FLOWER_COLOR 8u
#define GRASS_GENE_ROOT 9u
#define GRASS_GENE_CADENCE 10u
#define GRASS_GENE_MUTATION 11u
#define GRASS_FLOWERING_BIT 8u
#define GRASS_POLLINATED_BIT 16u
#define GRASS_RELEASED_BIT 32u
#define GRASS_ROOT_MASK 7u
#define GRASS_DONOR_VALID 1u
#define GRASS_PROPAGULE_LOAD 0
#define GRASS_PROPAGULE_MATERNAL 1
#define GRASS_PROPAGULE_DONOR 2
#define GRASS_CLAIM_EMPTY 0xffffffffu

bool IsDetritusMaterial(uint material)
{
    return material == MATERIAL_DETRITUS;
}

bool IsGrassSoilAnchor(uint material)
{
    return material == 7u;
}

int GrassSliceIndex(uint slot, uint field)
{
    return (int)((slot % 3u) * 4u + (field % 4u));
}

float4 SampleGrassLife(Texture2DArray<float4> tex, int2 cell, uint slot)
{
    return tex.Load(int4(cell, GrassSliceIndex(slot, GRASS_LIFE_OFFSET), 0));
}

uint4 SampleGrassGenome(Texture2DArray<float4> tex, int2 cell, uint slot)
{
    return asuint(tex.Load(int4(cell, GrassSliceIndex(slot, GRASS_GENOME_OFFSET), 0)));
}

float4 SampleGrassTiming(Texture2DArray<float4> tex, int2 cell, uint slot)
{
    return tex.Load(int4(cell, GrassSliceIndex(slot, GRASS_TIMING_OFFSET), 0));
}

uint4 SampleGrassDonor(Texture2DArray<float4> tex, int2 cell, uint slot)
{
    return asuint(tex.Load(int4(cell, GrassSliceIndex(slot, GRASS_DONOR_OFFSET), 0)));
}

float SampleGrassNectar(Texture2DArray<float4> tex, int2 cell, uint slot)
{
    return saturate(SampleGrassLife(tex, cell, slot).w);
}

uint GrassStage(uint4 genome)
{
    return genome.w & 255u;
}

uint GrassGeneration(uint4 genome)
{
    return (genome.w >> 8) & 255u;
}

uint GrassLineage(uint4 genome)
{
    return (genome.w >> 16) & 255u;
}

uint GrassToxinDose(uint4 genome)
{
    return (genome.w >> 24) & 255u;
}

uint PackGrassMeta(uint stage, uint generation, uint lineage, uint toxinDose)
{
    return (stage & 255u) | ((generation & 255u) << 8) | ((lineage & 255u) << 16) | ((toxinDose & 255u) << 24);
}

uint4 SanitizeGrassGenome(uint4 genome)
{
    uint stage = GrassStage(genome);
    if (stage > GRASS_STAGE_ADULT)
        stage = GRASS_STAGE_EMPTY;
    genome.w = PackGrassMeta(stage, GrassGeneration(genome), GrassLineage(genome), GrassToxinDose(genome));
    return genome;
}

float4 SanitizeGrassLife(float4 life)
{
    float4 value = max(SafeFinite4(life, 0.0), 0.0);
    value.x = saturate(value.x);
    value.y = saturate(value.y);
    value.z = saturate(value.z);
    value.w = saturate(value.w);
    return value;
}

float4 SanitizeGrassTiming(float4 timing)
{
    float4 value = max(SafeFinite4(timing, 0.0), 0.0);
    return value;
}

bool GrassIsLiving(uint stage)
{
    return stage == GRASS_STAGE_JUVENILE || stage == GRASS_STAGE_ADULT;
}

float GrassExpressFactor(uint gene, float range)
{
    return 1.0 + ((gene / 255.0) - 0.5) * 2.0 * saturate(range);
}

float GrassExpressShift(uint gene, float range)
{
    return ((gene / 255.0) - 0.5) * 2.0 * range;
}

uint GrassTimingFlags(float4 timing)
{
    return (uint)round(max(timing.w, 0.0));
}

uint GrassRootMask(float4 timing)
{
    return GrassTimingFlags(timing) & GRASS_ROOT_MASK;
}

bool GrassIsFlowering(float4 timing)
{
    return (GrassTimingFlags(timing) & GRASS_FLOWERING_BIT) != 0u;
}

bool GrassIsPollinated(float4 timing)
{
    return (GrassTimingFlags(timing) & GRASS_POLLINATED_BIT) != 0u;
}

bool GrassHasReleased(float4 timing)
{
    return (GrassTimingFlags(timing) & GRASS_RELEASED_BIT) != 0u;
}

bool GrassHasDonor(uint4 donor)
{
    return (donor.w & GRASS_DONOR_VALID) != 0u;
}

uint PackGrassTimingFlags(uint rootMask, bool flowering, bool pollinated, bool released)
{
    uint packed = rootMask & GRASS_ROOT_MASK;
    if (flowering) packed |= GRASS_FLOWERING_BIT;
    if (pollinated) packed |= GRASS_POLLINATED_BIT;
    if (released) packed |= GRASS_RELEASED_BIT;
    return packed;
}

uint SelectGrassRootMask(uint4 genome, int2 cell, uint slot)
{
    uint gene = DecodeGene(genome, GRASS_GENE_ROOT);
    uint count = 1u + (gene % 3u);
    uint start = (gene / 3u + (uint)cell.x * 3u + (uint)cell.y * 5u + slot * 7u) % 3u;
    uint mask = 0u;
    [unroll]
    for (uint i = 0u; i < 3u; i++)
    {
        if (i < count)
            mask |= 1u << ((start + i) % 3u);
    }
    return mask & GRASS_ROOT_MASK;
}

int2 GrassTapTarget(int2 cell, uint bit, int2 gridSize)
{
    int theta = bit == 0u ? -1 : (bit == 2u ? 1 : 0);
    return ClampCell(cell + int2(theta, -1), gridSize);
}

int2 GrassTapParent(int2 target, uint bit, int2 gridSize)
{
    int theta = bit == 0u ? 1 : (bit == 2u ? -1 : 0);
    return ClampCell(target + int2(theta, 1), gridSize);
}

uint GrassTapBitForParent(uint parentBit)
{
    // GrassTapParent(target, bit) already returns the organism whose tap `bit`
    // points at target, so the mask bit to test is the same index.
    return parentBit;
}

void WriteGrassSlot(RWTexture2DArray<float4> tex, int2 cell, uint slot, float4 life, uint4 genome, float4 timing, uint4 donor)
{
    tex[uint3((uint2)cell, GrassSliceIndex(slot, GRASS_LIFE_OFFSET))] = SanitizeGrassLife(life);
    tex[uint3((uint2)cell, GrassSliceIndex(slot, GRASS_GENOME_OFFSET))] = asfloat(SanitizeGrassGenome(genome));
    tex[uint3((uint2)cell, GrassSliceIndex(slot, GRASS_TIMING_OFFSET))] = SanitizeGrassTiming(timing);
    tex[uint3((uint2)cell, GrassSliceIndex(slot, GRASS_DONOR_OFFSET))] = asfloat(donor);
}

void ClearGrassSlot(RWTexture2DArray<float4> tex, int2 cell, uint slot)
{
    WriteGrassSlot(tex, cell, slot, 0.0, uint4(0u, 0u, 0u, 0u), 0.0, uint4(0u, 0u, 0u, 0u));
}

void CopyGrassSlot(Texture2DArray<float4> src, RWTexture2DArray<float4> dst, int2 cell, uint slot)
{
    WriteGrassSlot(dst, cell, slot,
        SampleGrassLife(src, cell, slot),
        SampleGrassGenome(src, cell, slot),
        SampleGrassTiming(src, cell, slot),
        SampleGrassDonor(src, cell, slot));
}

float4 SamplePropaguleLoad(Texture2DArray<float4> tex, int2 cell)
{
    float4 load = max(SafeFinite4(tex.Load(int4(cell, GRASS_PROPAGULE_LOAD, 0)), 0.0), 0.0);
    load.x = max(0.0, round(load.x));
    return load;
}

uint4 SamplePropaguleMaternal(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, GRASS_PROPAGULE_MATERNAL, 0)));
}

uint4 SamplePropaguleDonor(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, GRASS_PROPAGULE_DONOR, 0)));
}

void WritePropagule(RWTexture2DArray<float4> tex, int2 cell, float4 load, uint4 maternal, uint4 donor)
{
    load = max(SafeFinite4(load, 0.0), 0.0);
    load.x = max(0.0, round(load.x));
    if (load.x <= 0.5)
        maternal = uint4(0u, 0u, 0u, 0u);
    if (load.y <= 1e-8)
        donor = uint4(0u, 0u, 0u, 0u);
    tex[uint3((uint2)cell, GRASS_PROPAGULE_LOAD)] = load;
    tex[uint3((uint2)cell, GRASS_PROPAGULE_MATERNAL)] = asfloat(maternal);
    tex[uint3((uint2)cell, GRASS_PROPAGULE_DONOR)] = asfloat(donor);
}

float DetritusFuel(uint material, float4 aux, MaterialGpuData definition)
{
    if (!IsDetritusMaterial(material))
        return 0.0;
    return max(saturate(definition.biology.y), saturate(aux.z));
}

uint CountGrassRootTapsOnCell(Texture2DArray<float4> grass, int2 cell, int2 gridSize)
{
    uint taps = 0u;
    [unroll]
    for (uint bit = 0u; bit < 3u; bit++)
    {
        int2 parent = GrassTapParent(cell, bit, gridSize);
        if (parent.y >= gridSize.y)
            continue;
        uint expectedBit = GrassTapBitForParent(bit);
        [unroll]
        for (uint slot = 0u; slot < 3u; slot++)
        {
            uint4 genome = SampleGrassGenome(grass, parent, slot);
            if (!GrassIsLiving(GrassStage(genome)))
                continue;
            uint mask = GrassRootMask(SampleGrassTiming(grass, parent, slot));
            if ((mask & (1u << expectedBit)) != 0u)
                taps++;
        }
    }
    return taps;
}

float GrassCanopyOpacity(Texture2DArray<float4> grass, int2 cell, float scale)
{
    float opacity = 0.0;
    [unroll]
    for (uint slot = 0u; slot < 3u; slot++)
    {
        uint4 genome = SampleGrassGenome(grass, cell, slot);
        if (!GrassIsLiving(GrassStage(genome)))
            continue;
        float4 life = SampleGrassLife(grass, cell, slot);
        float height = GrassExpressFactor(DecodeGene(genome, GRASS_GENE_BLADE_HEIGHT), 0.45);
        opacity += saturate(life.x) * max(0.0, scale) * height;
    }
    return saturate(opacity);
}

uint4 RandomGrassGenome(uint2 cell, uint slot, int seed, int tick)
{
    uint4 genome = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float h = Hash01(cell.x * 73856093u + cell.y * 19349663u + slot * 6151u + i * 83492791u + (uint)seed * 31337u + (uint)tick * 2707u);
        EncodeGene(genome, i, (uint)round(h * 255.0));
    }
    uint lineage = 1u + (uint)round(Hash01(cell.x * 19349663u + cell.y * 73856093u + slot * 9829u + (uint)seed * 7919u + (uint)tick) * 254.0);
    genome.w = PackGrassMeta(GRASS_STAGE_ADULT, 0u, lineage, 0u);
    return genome;
}

uint4 MutateGrassGenome(uint4 genome, float baseMutationRate, float toxinMutationScale, uint salt)
{
    genome = SanitizeGrassGenome(genome);
    float toxin = GrassToxinDose(genome) / 255.0;
    float mutationGene = GrassExpressFactor(DecodeGene(genome, GRASS_GENE_MUTATION), 1.0);
    float rate = max(0.0, baseMutationRate) * (1.0 + toxin * max(0.0, toxinMutationScale)) * mutationGene;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float unit = Hash01(salt + i * 83492791u + DecodeGene(genome, i) * 747796405u);
        int step = (int)round((unit * 2.0 - 1.0) * rate * 255.0);
        int next = clamp((int)DecodeGene(genome, i) + step, 0, 255);
        EncodeGene(genome, i, (uint)next);
    }
    return genome;
}

uint4 CombineGrassGenomes(uint4 mother, uint4 partner, uint salt)
{
    uint4 child = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float unit = Hash01(salt + i * 83492791u);
        uint gene = unit < 0.5 ? DecodeGene(mother, i) : DecodeGene(partner, i);
        EncodeGene(child, i, gene);
    }
    return child;
}

uint4 InheritGrassGenome(uint4 mother, uint4 partner, bool hasPartner, float baseMutationRate, float toxinScale, uint salt)
{
    uint4 child = hasPartner ? CombineGrassGenomes(mother, partner, salt) : mother;
    child = MutateGrassGenome(child, baseMutationRate, toxinScale, salt + 17u);
    uint generation = min(255u, GrassGeneration(mother) + 1u);
    child.w = PackGrassMeta(GRASS_STAGE_JUVENILE, generation, GrassLineage(mother), GrassToxinDose(mother) / 2u);
    return child;
}

float GrassFloweringDays(uint4 genome)
{
    return 3.0 + DecodeGene(genome, GRASS_GENE_CADENCE) / 255.0;
}

float GrassSeedReleaseDays(uint4 genome)
{
    return 1.0 + DecodeGene(genome, GRASS_GENE_CADENCE) / 255.0;
}

#define WASP_STAGE_EMPTY 0u
#define WASP_STAGE_EGG 1u
#define WASP_STAGE_JUVENILE 2u
#define WASP_STAGE_ADULT 3u
#define WASP_STAGE_DEAD 4u
#define WASP_BEHAVIOR_IDLE 0u
#define WASP_BEHAVIOR_CRUISE 1u
#define WASP_BEHAVIOR_HUNT 2u
#define WASP_BEHAVIOR_NECTAR 3u
#define WASP_BEHAVIOR_SWOOP 4u
#define WASP_BEHAVIOR_FLEE 5u
#define WASP_GENE_FLIGHT_POWER 0u
#define WASP_GENE_BODY_MASS 1u
#define WASP_GENE_DRAG 2u
#define WASP_GENE_CRUISE_ALTITUDE 3u
#define WASP_GENE_HYDRATION_RETENTION 4u
#define WASP_GENE_METABOLISM 5u
#define WASP_GENE_CALORIE_CAPACITY 6u
#define WASP_GENE_PREY_SENSE 7u
#define WASP_GENE_HEARING 8u
#define WASP_GENE_AGGRESSION 9u
#define WASP_GENE_FERTILITY 10u
#define WASP_GENE_MUTATION 11u
#define WASP_VITALS_SLICE 0
#define WASP_MOTION_SLICE 1
#define WASP_GENOME_SLICE 2
#define WASP_PARTNER_SLICE 3
#define WASP_CARGO_SLICE 4
#define WASP_CARGO_SLOTS 3u
#define WASP_SLICE_COUNT 7
#define WASP_CLAIM_MOVE 0
#define WASP_CLAIM_PREY 1
#define WASP_CLAIM_MATE 2
#define WASP_CLAIM_EGG 3
#define WASP_CLAIM_FLOWER 4
#define WASP_CLAIM_COUNT 5
#define WASP_CLAIM_EMPTY 0xffffffffu
// Deferred wasp -> grass handoff. The wasp pass writes it, the grass pass consumes it
// on the following tick, which keeps the wasp pass out of the 12-slice grass array.
#define GRASS_VISIT_NECTAR 0
#define GRASS_VISIT_DONOR 1
#define GRASS_VISIT_SLICE_COUNT 2

uint WaspStage(uint4 genome)
{
    return genome.w & 255u;
}

uint WaspBehavior(uint4 genome)
{
    return (genome.w >> 8) & 255u;
}

uint WaspGeneration(uint4 genome)
{
    return (genome.w >> 16) & 255u;
}

uint WaspLineage(uint4 genome)
{
    return (genome.w >> 24) & 255u;
}

uint PackWaspMeta(uint stage, uint behavior, uint generation, uint lineage)
{
    return (stage & 255u) | ((behavior & 255u) << 8) | ((generation & 255u) << 16) | ((lineage & 255u) << 24);
}

uint4 SanitizeWaspGenome(uint4 genome)
{
    uint stage = WaspStage(genome);
    if (stage > WASP_STAGE_DEAD)
        stage = WASP_STAGE_EMPTY;
    uint behavior = WaspBehavior(genome);
    if (behavior > WASP_BEHAVIOR_FLEE)
        behavior = WASP_BEHAVIOR_IDLE;
    genome.w = PackWaspMeta(stage, behavior, WaspGeneration(genome), WaspLineage(genome));
    return genome;
}

float WaspExpressFactor(uint gene, float range)
{
    return 1.0 + ((gene / 255.0) - 0.5) * 2.0 * saturate(range);
}

float4 SampleWaspVitals(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, WASP_VITALS_SLICE, 0));
}

float4 SampleWaspMotion(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, WASP_MOTION_SLICE, 0));
}

uint4 SampleWaspGenome(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, WASP_GENOME_SLICE, 0)));
}

uint4 SampleWaspPartner(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, WASP_PARTNER_SLICE, 0)));
}

uint4 SampleWaspCargo(Texture2DArray<float4> tex, int2 cell, uint slot)
{
    return asuint(tex.Load(int4(cell, WASP_CARGO_SLICE + (int)min(slot, WASP_CARGO_SLOTS - 1u), 0)));
}

void WriteWaspState(RWTexture2DArray<float4> tex, int2 cell, float4 vitals, float4 motion, uint4 genome, uint4 partner, uint4 cargo0, uint4 cargo1, uint4 cargo2)
{
    tex[uint3((uint2)cell, WASP_VITALS_SLICE)] = SanitizeFaunaVitals(vitals);
    tex[uint3((uint2)cell, WASP_MOTION_SLICE)] = SanitizeFaunaMotion(motion);
    tex[uint3((uint2)cell, WASP_GENOME_SLICE)] = asfloat(SanitizeWaspGenome(genome));
    tex[uint3((uint2)cell, WASP_PARTNER_SLICE)] = asfloat(partner);
    tex[uint3((uint2)cell, WASP_CARGO_SLICE + 0)] = asfloat(cargo0);
    tex[uint3((uint2)cell, WASP_CARGO_SLICE + 1)] = asfloat(cargo1);
    tex[uint3((uint2)cell, WASP_CARGO_SLICE + 2)] = asfloat(cargo2);
}

void ClearWaspState(RWTexture2DArray<float4> tex, int2 cell)
{
    uint4 zero = uint4(0u, 0u, 0u, 0u);
    WriteWaspState(tex, cell, 0.0, 0.0, zero, zero, zero, zero, zero);
}

bool WaspHasPartner(uint4 partner)
{
    return partner.w != 0u;
}

// Pollen cargo reuses the grass donor convention: the low bit of .w marks a live
// sample, so a cargo entry can be handed straight to the grass donor slice.
bool WaspCargoValid(uint4 cargo)
{
    return (cargo.w & GRASS_DONOR_VALID) != 0u;
}

uint WaspCargoCount(uint4 cargo0, uint4 cargo1, uint4 cargo2)
{
    uint count = 0u;
    if (WaspCargoValid(cargo0)) count++;
    if (WaspCargoValid(cargo1)) count++;
    if (WaspCargoValid(cargo2)) count++;
    return count;
}

uint WaspPollenCapacity(float configured)
{
    return (uint)clamp((int)round(configured), 1, (int)WASP_CARGO_SLOTS);
}

// Oldest held sample whose lineage differs from the flower being visited, or -1.
int WaspCargoForeignIndex(uint4 cargo0, uint4 cargo1, uint4 cargo2, uint hostLineage)
{
    if (WaspCargoValid(cargo0) && GrassLineage(cargo0) != hostLineage) return 0;
    if (WaspCargoValid(cargo1) && GrassLineage(cargo1) != hostLineage) return 1;
    if (WaspCargoValid(cargo2) && GrassLineage(cargo2) != hostLineage) return 2;
    return -1;
}

// FIFO: removing shifts later samples forward so slot 0 is always the oldest.
void WaspCargoRemove(inout uint4 cargo0, inout uint4 cargo1, inout uint4 cargo2, int index)
{
    uint4 zero = uint4(0u, 0u, 0u, 0u);
    if (index == 0) { cargo0 = cargo1; cargo1 = cargo2; cargo2 = zero; }
    else if (index == 1) { cargo1 = cargo2; cargo2 = zero; }
    else if (index == 2) { cargo2 = zero; }
}

void WaspCargoPush(inout uint4 cargo0, inout uint4 cargo1, inout uint4 cargo2, uint4 sample, uint capacity)
{
    sample.w |= GRASS_DONOR_VALID;
    if (!WaspCargoValid(cargo0)) { cargo0 = sample; return; }
    if (capacity >= 2u && !WaspCargoValid(cargo1)) { cargo1 = sample; return; }
    if (capacity >= 3u && !WaspCargoValid(cargo2)) { cargo2 = sample; }
}

uint4 RandomWaspGenome(uint2 cell, int seed, int tick)
{
    uint4 genome = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float h = Hash01(cell.x * 40503u + cell.y * 22543u + i * 83492791u + (uint)seed * 5779u + (uint)tick * 26417u);
        EncodeGene(genome, i, (uint)round(h * 255.0));
    }
    uint lineage = (uint)round(Hash01(cell.x * 22543u + cell.y * 40503u + (uint)seed * 3323u + (uint)tick) * 255.0);
    genome.w = PackWaspMeta(WASP_STAGE_ADULT, WASP_BEHAVIOR_CRUISE, 0u, lineage);
    return genome;
}

uint4 MutateWaspGenome(uint4 genome, float baseMutationRate, uint salt)
{
    genome = SanitizeWaspGenome(genome);
    float mutationGene = WaspExpressFactor(DecodeGene(genome, WASP_GENE_MUTATION), 1.0);
    float rate = max(0.0, baseMutationRate) * mutationGene;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float unit = Hash01(salt + i * 83492791u + DecodeGene(genome, i) * 747796405u);
        int step = (int)round((unit * 2.0 - 1.0) * rate * 255.0);
        int next = clamp((int)DecodeGene(genome, i) + step, 0, 255);
        EncodeGene(genome, i, (uint)next);
    }
    return genome;
}

float WaspFuel(uint material, float4 vitals, uint stage)
{
    if (!IsWaspMaterial(material) && !IsWaspEggMaterial(material))
        return 0.0;
    if (stage == WASP_STAGE_EMPTY || stage == WASP_STAGE_DEAD)
        return 0.0;
    return saturate(vitals.x);
}

float4 SampleGrassVisitNectar(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, GRASS_VISIT_NECTAR, 0));
}

uint4 SampleGrassVisitDonor(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, GRASS_VISIT_DONOR, 0)));
}

uint DominantRareMycology(uint traitsA, uint traitsB, uint traitsC)
{
    uint combined = SanitizeMycologyTraits(traitsA | traitsB | traitsC);
    if (combined == 0u)
        return 0u;
    uint best = 0u;
    uint bestRank = 0u;
    uint flags[6] = { MYCO_DROUGHT_RESISTANT, MYCO_HEAT_RESISTANT, MYCO_ELECTRIC_RESISTANT, MYCO_DROUGHT_PRONE, MYCO_HEAT_PRONE, MYCO_ELECTRIC_PRONE };
    [unroll]
    for (uint i = 0u; i < 6u; i++)
    {
        if ((combined & flags[i]) != 0u && (6u - i) > bestRank)
        {
            best = flags[i];
            bestRank = 6u - i;
        }
    }
    return best;
}

#define TREE_SLICE_COUNT 3
#define TREE_PHYSIOLOGY_SLICE 0
#define TREE_TOPOLOGY_SLICE 1
#define TREE_GENOME_SLICE 2
#define TREE_STAGE_EMPTY 0u
#define TREE_STAGE_SPROUT 1u
#define TREE_STAGE_SAPLING 2u
#define TREE_STAGE_TREE 3u
#define TREE_STAGE_DEAD 4u
#define TREE_ROLE_NONE 0u
#define TREE_ROLE_ROOT 1u
#define TREE_ROLE_JUVENILE 2u
#define TREE_ROLE_TRUNK 3u
#define TREE_ROLE_BRANCH 4u
#define TREE_ROLE_STEM 5u
#define TREE_ROLE_LEAF 6u
#define TREE_GENE_TEMP_OPTIMUM 0u
#define TREE_GENE_TEMP_TOLERANCE 1u
#define TREE_GENE_MOISTURE_OPTIMUM 2u
#define TREE_GENE_MOISTURE_TOLERANCE 3u
#define TREE_GENE_LIGHT_AFFINITY 4u
#define TREE_GENE_METABOLIC 5u
#define TREE_GENE_ROOT_BRANCHING 6u
#define TREE_GENE_MATURE_HEIGHT 7u
#define TREE_GENE_BRANCH_CADENCE 8u
#define TREE_GENE_LEAF_LONGEVITY 9u
#define TREE_GENE_WIND_RESPONSE 10u
#define TREE_GENE_MUTATION 11u
#define TREE_FLAG_ANCHOR 1u
#define TREE_FLAG_SHED 2u
#define TREE_FLAG_CONVERT_WOOD 4u
#define TREE_FLAG_CONVERT_DETRITUS 8u
#define TREE_FLAG_CONVERT_ASH 16u
#define TREE_FLAG_EXPOSED 32u
#define TREE_CLAIM_EMPTY 0xffffffffu

bool IsLeafMaterial(uint material)
{
    return material == TREE_LEAF_ID;
}

bool IsWoodMaterial(uint material)
{
    return material == TREE_WOOD_ID;
}

bool IsTreeMaterial(uint material)
{
    return IsLeafMaterial(material) || IsWoodMaterial(material);
}

bool IsTreeHabitat(uint material)
{
    return material == 7u || material == 8u;
}

bool IsTreeOpen(uint material)
{
    return material == 0u || material == 1u || material == 11u;
}

float4 SampleTreePhysiology(Texture2DArray<float4> tex, int2 cell)
{
    return tex.Load(int4(cell, TREE_PHYSIOLOGY_SLICE, 0));
}

uint4 SampleTreeTopology(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, TREE_TOPOLOGY_SLICE, 0)));
}

uint4 SampleTreeGenome(Texture2DArray<float4> tex, int2 cell)
{
    return asuint(tex.Load(int4(cell, TREE_GENOME_SLICE, 0)));
}

uint TreeStage(uint4 genome)
{
    return genome.w & 255u;
}

uint TreeGeneration(uint4 genome)
{
    return (genome.w >> 8) & 255u;
}

uint TreeLineage(uint4 genome)
{
    return (genome.w >> 16) & 255u;
}

uint TreeToxinDose(uint4 genome)
{
    return (genome.w >> 24) & 255u;
}

uint PackTreeMeta(uint stage, uint generation, uint lineage, uint toxinDose)
{
    return (stage & 255u) | ((generation & 255u) << 8) | ((lineage & 255u) << 16) | ((toxinDose & 255u) << 24);
}

uint TreeRole(uint4 topology)
{
    return (topology.z >> 8) & 255u;
}

uint TreeTopoStage(uint4 topology)
{
    return topology.z & 255u;
}

uint TreeFlags(uint4 topology)
{
    return (topology.z >> 16) & 255u;
}

uint TreeCause(uint4 topology)
{
    return (topology.z >> 24) & 255u;
}

uint PackTreeTopologyMeta(uint stage, uint role, uint flags, uint cause)
{
    return (stage & 255u) | ((role & 255u) << 8) | ((flags & 255u) << 16) | ((cause & 255u) << 24);
}

bool TreeIsLiving(uint stage)
{
    return stage == TREE_STAGE_SPROUT || stage == TREE_STAGE_SAPLING || stage == TREE_STAGE_TREE;
}

bool TreeIsOccupied(uint4 topology)
{
    return topology.x != 0u;
}

uint4 SanitizeTreeGenome(uint4 genome)
{
    uint stage = TreeStage(genome);
    if (stage > TREE_STAGE_DEAD)
        stage = TREE_STAGE_EMPTY;
    genome.w = PackTreeMeta(stage, TreeGeneration(genome), TreeLineage(genome), TreeToxinDose(genome));
    return genome;
}

float4 SanitizeTreePhysiology(float4 phys)
{
    float4 value = max(SafeFinite4(phys, 0.0), 0.0);
    value.x = saturate(value.x);
    value.y = saturate(value.y);
    value.z = saturate(value.z);
    value.w = saturate(value.w);
    return value;
}

float TreeExpressFactor(uint gene, float range)
{
    return 1.0 + ((gene / 255.0) - 0.5) * 2.0 * saturate(range);
}

float TreeExpressShift(uint gene, float range)
{
    return ((gene / 255.0) - 0.5) * 2.0 * range;
}

int TreeSaplingBranchCount(uint4 genome, int configuredMin, int configuredMax)
{
    uint gene = DecodeGene(genome, TREE_GENE_BRANCH_CADENCE);
    int lo = clamp(configuredMin, 1, 8);
    int hi = clamp(configuredMax, lo, 8);
    return clamp(lo + (int)(gene % 3u), lo, hi);
}

float TreeMatureHeightScale(uint4 genome)
{
    return 0.75 + DecodeGene(genome, TREE_GENE_MATURE_HEIGHT) / 255.0 * 0.35;
}

float TreeLeafLifeScale(uint4 genome)
{
    return 0.5 + DecodeGene(genome, TREE_GENE_LEAF_LONGEVITY) / 255.0;
}

float TreeWindResponse(uint4 genome)
{
    return DecodeGene(genome, TREE_GENE_WIND_RESPONSE) / 255.0;
}

int TreeRootForkBudget(uint4 genome)
{
    return 1 + (int)(DecodeGene(genome, TREE_GENE_ROOT_BRANCHING) % 3u);
}

void WriteTreeState(RWTexture2DArray<float4> tex, int2 cell, float4 phys, uint4 topology, uint4 genome)
{
    tex[uint3((uint2)cell, TREE_PHYSIOLOGY_SLICE)] = SanitizeTreePhysiology(phys);
    tex[uint3((uint2)cell, TREE_TOPOLOGY_SLICE)] = asfloat(topology);
    tex[uint3((uint2)cell, TREE_GENOME_SLICE)] = asfloat(SanitizeTreeGenome(genome));
}

void ClearTreeState(RWTexture2DArray<float4> tex, int2 cell)
{
    WriteTreeState(tex, cell, 0.0, uint4(0u, 0u, 0u, 0u), uint4(0u, 0u, 0u, 0u));
}

void CopyTreeState(Texture2DArray<float4> src, RWTexture2DArray<float4> dst, int2 cell)
{
    WriteTreeState(dst, cell, SampleTreePhysiology(src, cell), SampleTreeTopology(src, cell), SampleTreeGenome(src, cell));
}

uint4 RandomTreeGenome(uint2 cell, int seed, int tick)
{
    uint4 genome = 0;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float h = Hash01(cell.x * 40503u + cell.y * 73856093u + i * 83492791u + (uint)seed * 5779u);
        EncodeGene(genome, i, (uint)round(h * 255.0));
    }
    uint lineage = 1u + (uint)round(Hash01(cell.x * 22543u + cell.y * 40503u + (uint)seed * 3323u) * 254.0);
    genome.w = PackTreeMeta(TREE_STAGE_SPROUT, 0u, lineage, 0u);
    return genome;
}

uint4 MutateTreeGenome(uint4 genome, float baseMutationRate, float toxinMutationScale, uint salt)
{
    genome = SanitizeTreeGenome(genome);
    float toxin = TreeToxinDose(genome) / 255.0;
    float mutationGene = TreeExpressFactor(DecodeGene(genome, TREE_GENE_MUTATION), 1.0);
    float rate = max(0.0, baseMutationRate) * (1.0 + toxin * max(0.0, toxinMutationScale)) * mutationGene;
    [unroll]
    for (uint i = 0u; i < 12u; i++)
    {
        float unit = Hash01(salt + i * 83492791u + DecodeGene(genome, i) * 747796405u);
        int step = (int)round((unit * 2.0 - 1.0) * rate * 255.0);
        int next = clamp((int)DecodeGene(genome, i) + step, 0, 255);
        EncodeGene(genome, i, (uint)next);
    }
    return genome;
}

float TreeFuel(uint material, float4 phys, uint stage)
{
    if (!IsTreeMaterial(material))
        return 0.0;
    float health = saturate(phys.w);
    if (stage == TREE_STAGE_EMPTY)
        return 0.0;
    if (stage == TREE_STAGE_DEAD)
        return health * 0.45;
    return health;
}

float TreeCanopyOpacity(uint material, float4 phys, float scale)
{
    if (IsLeafMaterial(material))
        return (0.18 + saturate(phys.w) * 0.22) * max(0.0, scale);
    if (IsWoodMaterial(material))
        return 0.4 * max(0.0, scale);
    return 0.0;
}

uint CountTreeRootsOnCell(Texture2DArray<float4> tree, int2 cell, int2 gridSize)
{
    uint taps = 0u;
    int2 offsets[4] = { int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        int2 n = cell + offsets[i];
        if (offsets[i].y != 0 && (n.y < 0 || n.y >= gridSize.y)) continue;
        int2 neighbor = ClampCell(n, gridSize);
        uint4 topo = SampleTreeTopology(tree, neighbor);
        if (!TreeIsOccupied(topo) || TreeRole(topo) != TREE_ROLE_ROOT) continue;
        uint stage = TreeStage(SampleTreeGenome(tree, neighbor));
        if (!TreeIsLiving(stage)) continue;
        taps++;
    }
    return taps;
}

#endif
