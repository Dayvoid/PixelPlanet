#ifndef GENESYS_SIMULATION_STRUCTS_INCLUDED
#define GENESYS_SIMULATION_STRUCTS_INCLUDED

// Water mass contract:
//   state.z  = canonical surface liquid/ice mass (never created implicitly by material ID alone)
//   aux.x    = atmospheric vapor mass
//   aux.y    = subsurface groundwater mass
//   aux.z    = nutrient / fertility
//   aux.w    = shared fault / erosion stress
//              Rock, Basalt, and Soil recover linearly via stressDecayRate (_Erosion.w)
//              in ErosionAndCollapse; Mantle/Magma keep tectonic fault semantics without decay.
// Moisture-aware soil erosion:
//   Exposed soil only. Local moisture (state.z + aux.y) raises cohesion and suppresses
//   erosion-stress gain; dryness enables wind/runoff erosion but never converts alone.
//   Sediment does not auto-revert to soil; ash fertilization remains the pedogenesis path.
// Every transfer must subtract from a source reservoir before adding to a destination.
//
// Pressure (state.y):
//   Local sources/sinks (thermal expansion, mantle feed, vapor, brushes) still write absolute
//   pressure. PressureDiffusion transports the anomaly relative to a radial equilibrium
//   profile min(pressureEquilibriumMaximum, (1 - radius) * pressureEquilibriumGradient).
//   Edge conductance is category-weighted (gas / fluid / porous / rigid) so highs and lows
//   persist for material-appropriate durations while fields eventually equilibrate.

struct MaterialGpuData
{
    float4 color;
    float4 physical;   // density, rigidity, repose radians, grain size
    float4 transport;  // thermal conductivity, heat capacity, electrical conductivity, absorbency
    float4 phase;      // melt point, boil point, thermal expansion, electric expansion
    float4 biology;    // toxicity, calories, porosity, buoyancy bias
    float4 metadata;   // category, packed phase IDs, bio-modifiable, stable ID
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
