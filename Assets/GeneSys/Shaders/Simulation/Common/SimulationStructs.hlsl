#ifndef GENESYS_SIMULATION_STRUCTS_INCLUDED
#define GENESYS_SIMULATION_STRUCTS_INCLUDED

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

#endif
