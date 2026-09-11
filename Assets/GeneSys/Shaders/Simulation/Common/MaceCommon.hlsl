#ifndef GENESYS_MACE_COMMON_INCLUDED
#define GENESYS_MACE_COMMON_INCLUDED

#define MACE_SLICE_COARSE 0
#define MACE_SLICE_FINE 1
#define MACE_SLICE_SOLUTE 2
#define MACE_SLICE_MAGMA 3
#define MACE_SLICE_ASH 4
#define MACE_SLICE_STRUCTURAL 5
#define MACE_SLICE_COUNT 6

#define MACE_FLAG_SEDIMENT 0
#define MACE_FLAG_ENTRAIN 1
#define MACE_FLAG_SHORE 2
#define MACE_FLAG_SOLUTE 3

static const int2 MaceMoore[9] =
{
    int2(0, 0), int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1),
    int2(-1, -1), int2(1, -1), int2(-1, 1), int2(1, 1)
};

float SampleMobileSlice(Texture2DArray<float> mobile, int2 cell, int slice)
{
    cell = ClampCell(cell, _GridSize);
    return max(0.0, mobile.Load(int4(cell, slice, 0)));
}

void WriteMobileSlice(RWTexture2DArray<float> mobile, int2 cell, int slice, float value)
{
    mobile[int3(cell, slice)] = max(0.0, SafeFinite(value, 0.0));
}

float MobileSedimentFillFrom(Texture2DArray<float> mobile, int2 cell)
{
    return SampleMobileSlice(mobile, cell, MACE_SLICE_COARSE) + SampleMobileSlice(mobile, cell, MACE_SLICE_FINE);
}

float MobileOccupiedExcept(Texture2DArray<float> mobile, int2 cell, int exceptSlice)
{
    float occupied = 0.0;
    [unroll]
    for (int slice = 0; slice < MACE_SLICE_COUNT; slice++)
    {
        if (slice == exceptSlice) continue;
        occupied += SampleMobileSlice(mobile, cell, slice);
    }
    return occupied;
}

float MobileChannelCapacity(Texture2DArray<float> mobile, int2 cell, int slice)
{
    return max(0.0, 1.0 - MobileOccupiedExcept(mobile, cell, slice));
}

bool MaceRadialValid(int2 cell, int2 offset)
{
    int ny = cell.y + offset.y;
    return ny >= 0 && ny < _GridSize.y;
}

int2 MaceNeighbor(int2 cell, int2 offset)
{
    return ClampCell(cell + offset, _GridSize);
}

bool IsMaceAtmosphereClass(uint material, int radiusIndex)
{
    if (material == 1u) return true;
    if (material == 0u) return Radius01(radiusIndex, _GridSize.y) >= _PlayableInnerRadius;
    return false;
}

bool IsLooseSedimentCarrier(uint material)
{
    return material == 0u || material == 1u || material == 8u;
}

bool IsStructuralHostId(uint material)
{
    return material == 2u || material == 3u || material == 4u || material == 5u
        || material == 7u || material == 13u || material == 14u || material == 15u
        || material == 135u;
}

bool IsSedimentEligibleDest(uint material, float structural)
{
    if (IsLooseSedimentCarrier(material)) return true;
    if ((material == 7u || material == 15u) && structural < 0.999)
        return true;
    return false;
}

bool IsSedimentEligibleSource(uint material, float rho)
{
    if (IsLooseSedimentCarrier(material)) return rho > 0.0;
    return (material == 7u || material == 15u) && rho > 0.0;
}

bool IsSoluteEligible(uint material, MaterialGpuData definition, float groundwater)
{
    return IsGroundwaterHost(material, definition) && groundwater > 1e-5;
}

bool IsAshEligibleDest(uint material)
{
    return material == 0u || material == 1u || material == 8u || material == 12u || material == 7u;
}

bool IsMagmaEligibleDest(uint material)
{
    return material == 0u || material == 1u || material == 6u || IsSoftCrustMaterial(material);
}

float MacePhysicalArc(int radiusIndex)
{
    return 6.28318530718 * max(Radius01(radiusIndex, _GridSize.y), 1e-4) / max(1.0, (float)_GridSize.x);
}

float MacePhysicalRadial()
{
    return RadialDelta(_GridSize.y);
}

float MaceTanRepose(uint material)
{
    float repose = Mat(material).physical.z;
    if (repose <= 1e-4) repose = 0.436;
    return tan(clamp(repose, 0.05, 1.4));
}

float MaceSupportBelow(Texture2DArray<float> mobile, int2 cell)
{
    if (cell.y <= 0) return 1.0;
    int2 below = int2(cell.x, cell.y - 1);
    uint belowMat = _MaterialRead.Load(int3(below, 0));
    if (IsHardCrustMaterial(belowMat) || belowMat == 2u || belowMat == 3u || belowMat == 13u)
        return 1.0;
    float structural = SampleMobileSlice(mobile, below, MACE_SLICE_STRUCTURAL);
    float fill = MobileSedimentFillFrom(mobile, below);
    float magma = SampleMobileSlice(mobile, below, MACE_SLICE_MAGMA);
    return saturate(structural + fill + magma + (belowMat == 8u || belowMat == 6u ? 0.35 : 0.0));
}

float MaceLateralSlope(Texture2DArray<float> mobile, int2 cell, int dx)
{
    int2 neighbor = MaceNeighbor(cell, int2(dx, 0));
    float selfH = (float)cell.y + MobileSedimentFillFrom(mobile, cell);
    float nH = (float)neighbor.y + MobileSedimentFillFrom(mobile, neighbor);
    float dxPhys = max(MacePhysicalArc(cell.y), 1e-5);
    float dyPhys = (selfH - nH) * MacePhysicalRadial();
    return dyPhys / dxPhys;
}

bool MaceFlag(float4 flags, int index)
{
    return flags[index] > 0.5;
}

float MaceLogSumExpWeight(float affinity, float maxAffinity, float beta)
{
    return exp(clamp(beta * (affinity - maxAffinity), -40.0, 40.0));
}

#endif
