#ifndef GENESYS_MARGOLUS_COMMON_INCLUDED
#define GENESYS_MARGOLUS_COMMON_INCLUDED

#include "SimulationStructs.hlsl"

struct MargolusCell
{
    uint material;
    float4 state;
    float2 flow;
    float4 aux;
    uint shade;
    float4 ecology;
    float4 combustion;
    float4 life;
    uint4 genome;
};

// Radial aspect ratio for polar grid compensation:
// MetricRatio = ArcLength / RadialHeight = (2*PI * Radius(y) / Nx) / (1 / Ny)
float MargolusMetricRatio(int y, int2 gridSize)
{
    float r = max(0.04, Radius01(y, gridSize.y));
    return (6.28318530718 * r * (float)gridSize.y) / max(1.0, (float)gridSize.x);
}

bool IsMargolusOpenCarrier(uint material)
{
    return material == 0u || material == 1u;
}

bool IsMargolusPinned(MargolusCell c, MaterialGpuData def)
{
    // Void and Air are open carriers
    if (IsMargolusOpenCarrier(c.material)) return false;

    // Planetary Core and Mantle are pinned basement geology
    if (c.material == 2u || c.material == 3u) return true;

    // Rigid crystalline bedrock (Granite 4, Basalt 5, Metal 13, Limestone 14) is pinned
    if (c.material == 4u || c.material == 5u || c.material == 13u || c.material == 14u) return true;
    if (def.physical.y >= 0.75) return true;

    // Tree trunks/canopy and wasps are governed by dedicated biological solvers
    if (IsTreeMaterial(c.material) || IsWaspMaterial(c.material)) return true;

    return false;
}

bool IsMargolusFluid(uint material, MaterialGpuData def)
{
    return def.metadata.x == 2.0 || material == 9u || material == 6u;
}

bool IsMargolusGranular(uint material, MaterialGpuData def)
{
    return def.metadata.x == 3.0 || material == 8u || material == 7u || material == 15u || material == 12u;
}

float MargolusEffectiveDensity(MargolusCell c, MaterialGpuData def)
{
    if (c.material == 0u) return 0.0;
    if (c.material == 1u) return 0.0012; // Air density
    float d = max(0.01, def.physical.x);
    // Extra density weight from groundwater saturation in porous host
    if (c.aux.y > 0.0) d += 0.25 * saturate(c.aux.y);
    return d;
}

float MargolusTanRepose(uint material, MaterialGpuData def)
{
    float reposeRad = def.physical.z;
    if (reposeRad <= 1e-4)
    {
        if (material == 8u) reposeRad = 0.436; // ~25 deg
        else if (material == 7u) reposeRad = 0.593; // ~34 deg
        else if (material == 15u) reposeRad = 0.489; // ~28 deg
        else reposeRad = 0.523; // ~30 deg
    }
    return tan(clamp(reposeRad, 0.05, 1.45));
}

#endif // GENESYS_MARGOLUS_COMMON_INCLUDED
