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
// Milestone 1 fuel is ecology.y on Soil/Sediment. Burning consumes that biomass
// locally and writes heat/pressure/updraft/steam into the existing weather chain.
// Combustion never invents water: flashpoint vaporization only moves state.z -> aux.x.
// Atmosphere representation:
//   Air (ID 1) is the permanent atmospheric carrier. Vapor (ID 11) is a phase descriptor only;
//   runtime boiling / legacy cells normalize to Air while keeping vapor mass in aux.x.
//   Clouds are atmospheric state.z; rain drains that condensate onto the surface below.
//   WaterMaterialization converts dense cloud (rainPixelFormationThreshold) or pooled
//   surface film (surfaceWaterPixelThreshold) into Water/Ice pixels. Zero disables.
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

#endif
