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

bool IsMargolusOpenCarrier(uint material, MaterialGpuData def)
{
    if (material == 0u || material == 1u) return true;
    return def.metadata.x == 1.0;
}

bool IsRockMaterial(uint material)
{
    return material == 4u || material == 5u || material == 13u || material == 14u;
}

bool IsMargolusPinned(MargolusCell c, MaterialGpuData def)
{
    if (IsMargolusOpenCarrier(c.material, def)) return false;

    // Override table: dedicated organism solvers and planetary basement.
    if (IsTreeMaterial(c.material) || IsWaspMaterial(c.material)) return true;
    if (c.material == 2u || c.material == 3u) return true;

    // Algae films settle with Margolus even though the asset rigidity is solid-ish.
    if (c.material == FLORA_ALGAE_ID) return false;

    // Calved or fractured rock (stress >= 1.0) loses structural cohesion and falls.
    if (IsRockMaterial(c.material) && c.aux.w >= 1.0) return false;

    // Rigid solids (category Solid or high rigidity) stay put by default.
    // Spatial attachment and unsupported gravity falls are checked in MargolusTransport.
    if (def.physical.y >= 0.75) return true;
    if (def.metadata.x == 4.0 && IsRockMaterial(c.material))
        return true;

    return false;
}

bool IsMargolusFluid(uint material, MaterialGpuData def)
{
    float cat = def.metadata.x;
    if (cat == 2.0 || cat == 5.0) return true;
    return material == 9u || material == 6u;
}

bool IsMargolusGranular(uint material, MaterialGpuData def)
{
    if (def.metadata.x == 3.0) return true;
    // Landed Ice piles at its repose instead of sitting as an unowned 1-wide tower.
    // Airborne Ice still uses AirborneIceCanSlide (unrestricted fall/slide).
    return material == 8u || material == 7u || material == 15u || material == 12u
        || material == 10u || material == FLORA_ALGAE_ID;
}

float MargolusEffectiveDensity(MargolusCell c, MaterialGpuData def)
{
    if (c.material == 0u) return 0.0;
    float d = max(0.0001, def.physical.x);
    if (c.aux.y > 0.0) d += 0.25 * saturate(c.aux.y);
    return d;
}

float MargolusTanRepose(uint material, MaterialGpuData def)
{
    float reposeRad = def.physical.z;
    if (reposeRad <= 1e-4)
        reposeRad = 0.523;
    return tan(clamp(reposeRad, 0.05, 1.45));
}

#endif // GENESYS_MARGOLUS_COMMON_INCLUDED
