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
// Atmosphere representation:
//   Air (ID 1) is the permanent atmospheric carrier. Vapor (ID 11) is a phase descriptor only;
//   runtime boiling / legacy cells normalize to Air while keeping vapor mass in aux.x.
//   Clouds are atmospheric state.z; rain drains that condensate into negative flow.y.
// Moisture-aware soil erosion:
//   Exposed soil only. Local moisture (state.z + aux.y) raises cohesion and suppresses
//   erosion-stress gain; dryness enables wind/runoff erosion but never converts alone.
//   Sediment does not auto-revert to soil; ash fertilization remains the pedogenesis path.
// Every transfer must subtract from a source reservoir before adding to a destination.
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

#endif
