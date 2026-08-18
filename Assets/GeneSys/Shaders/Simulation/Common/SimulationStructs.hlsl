#ifndef GENESYS_SIMULATION_STRUCTS_INCLUDED
#define GENESYS_SIMULATION_STRUCTS_INCLUDED

// Water mass contract (canonical Water texture):
//   water.x = surface liquid mass
//   water.y = subsurface groundwater mass
//   water.z = ice mass
//   water.w = vapor mass
// Material IDs 9/10/11 are presentation/host state only.
// Every transfer must subtract from a source reservoir before adding to a destination.
// Total pressure = hydrostatic (derived column) + dynamic overpressure (state.y).

struct MaterialGpuData
{
    float4 color;
    float4 physical;   // density, rigidity, repose radians, grain size
    float4 transport;  // thermal conductivity, heat capacity, electrical conductivity, absorbency
    float4 phase;      // melt point, boil point, thermal expansion, electric expansion
    float4 biology;    // toxicity, calories, porosity, buoyancy bias
    float4 metadata;   // category, packed phase IDs, bio-modifiable, stable ID
    float4 mechanics;  // latent fusion, latent vapor, permeability, yield strength
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

bool InRadialBounds(int y, int radialSize)
{
    return y >= 0 && y < radialSize;
}

int2 NeighborCell(int2 cell, int2 offset, int2 size)
{
    return int2((int)WrapTheta(cell.x + offset.x, size.x), cell.y + offset.y);
}

float Radius01(int radiusIndex, int radialSize)
{
    return (radiusIndex + 0.5) / max(1.0, (float)radialSize);
}

float CellAreaWeight(int radiusIndex, int radialSize)
{
    return max(0.5 / max(1.0, (float)radialSize), Radius01(radiusIndex, radialSize));
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

uint GeneSysHash(uint value)
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
    return (GeneSysHash(value) & 0x00ffffff) / 16777215.0;
}

uint HashCell(int2 cell, int tick)
{
    return GeneSysHash((uint)(cell.x * 1973 + cell.y * 9277 + tick * 53 + 101));
}

int PackCell(int2 cell, int width)
{
    return cell.y * width + cell.x;
}

int2 UnpackCell(int packed, int width)
{
    width = max(1, width);
    return int2(packed % width, packed / width);
}

float WaterTotal(float4 water)
{
    return max(0.0, water.x) + max(0.0, water.y) + max(0.0, water.z) + max(0.0, water.w);
}

float EffectivePermeability(MaterialGpuData definition, float fault)
{
    return saturate(definition.mechanics.z + saturate(fault) * 0.45);
}

bool IsGasCategory(MaterialGpuData definition)
{
    return definition.metadata.x == 1.0;
}

bool IsOpenMaterial(uint material, MaterialGpuData definition)
{
    return material == 0u || definition.metadata.x == 1.0;
}

bool IsGranularCategory(MaterialGpuData definition)
{
    return definition.metadata.x == 3.0;
}

bool IsSolidCategory(MaterialGpuData definition)
{
    return definition.metadata.x == 4.0;
}

bool IsMagmaCategory(MaterialGpuData definition)
{
    return definition.metadata.x == 5.0;
}

bool CanStoreGroundwater(uint material, MaterialGpuData definition, float fault)
{
    if (material == 2u) return false;
    float perm = EffectivePermeability(definition, fault);
    if (perm < 0.02) return false;
    if (material == 3u && perm < 0.08) return false;
    return true;
}

float PressureAdjustedMelt(MaterialGpuData definition, float totalPressure, float meltSlope)
{
    return definition.phase.x + max(0.0, meltSlope) * max(0.0, totalPressure);
}

float PressureAdjustedBoil(float baseBoil, float totalPressure, float boilSlope)
{
    return baseBoil + max(0.0, boilSlope) * max(0.0, totalPressure);
}

#endif
