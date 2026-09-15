#ifndef GENESYS_ROCK_CHUNKS_INCLUDED
#define GENESYS_ROCK_CHUNKS_INCLUDED

#include "MargolusCommon.hlsl"

#define ROCK_CHUNK_MAX_SLOTS 32u
#define ROCK_CHUNK_MAX_CELLS 96u
#define ROCK_CHUNK_HOPS_MAX 255u
#define ROCK_CHUNK_EMPTY_PACKED 0xFFFFFFFFu

#define ROCK_STATUS_IDLE 0u
#define ROCK_STATUS_SEARCHING 1u
#define ROCK_STATUS_INFLIGHT 2u
#define ROCK_STATUS_STABLEHOLD 3u
#define ROCK_STATUS_CAPTURE 4u

#define ROCK_FLAG_STABLE 1u
#define ROCK_FLAG_DETACHED 2u
#define ROCK_FLAG_HINGED 4u
#define ROCK_FLAG_SETTLING 8u

#define ROCK_HEADER_EMPTY 0u
#define ROCK_HEADER_SEARCHING 1u
#define ROCK_HEADER_INFLIGHT 2u
#define ROCK_HEADER_CAPTURE 4u

#define ROCK_TAU 6.28318530718

struct RockChunkHeader
{
    uint status;
    uint memberCount;
    uint prevMemberCount;
    uint ageTicks;
    uint flags;
    uint contactCount;
    uint heelPacked;
    uint seedPacked;
    uint sumX;
    uint sumY;
    uint contactSumX;
    uint contactSumY;
    uint contactMinOff;
    uint contactMaxOff;
    uint massU;
    uint maxRadiusU;
    float pivotX;
    float pivotY;
    float angle;
    float omega;
    float restPivotX;
    float restPivotY;
    float lastDAngle;
    float pad0;
    int biasSign;
    uint collision;
    uint halfStepUsed;
    uint ageInFlight;
    uint pad1;
    uint pad2;
    uint pad3;
    uint pad4;
};

struct RockChunkMember
{
    uint packedXY;
    uint chunkId;
    float restOx;
    float restOy;
};

uint RockPackXY(int2 cell)
{
    return ((uint)cell.y << 16) | ((uint)cell.x & 0xFFFFu);
}

int2 RockUnpackXY(uint packed)
{
    return int2((int)(packed & 0xFFFFu), (int)(packed >> 16));
}

uint RockPackSupport(uint hops, uint status, uint slot, uint seedHops)
{
    return (hops & 255u)
        | ((status & 3u) << 8)
        | ((slot & 63u) << 10)
        | ((seedHops & 255u) << 16);
}

uint RockHops(uint packed) { return packed & 255u; }
uint RockStatus(uint packed) { return (packed >> 8) & 3u; }
uint RockSlot(uint packed) { return (packed >> 10) & 63u; }
uint RockSeedHops(uint packed) { return (packed >> 16) & 255u; }

bool IsRockChunkOpen(uint material)
{
    return material == 0u || material == 1u || material == 9u;
}

bool IsRockBasement(uint material)
{
    return material == 2u || material == 3u;
}

bool IsRockChunkSeedMaterial(uint material)
{
    return material == 4u || material == 14u;
}

int RockWrapTheta(int theta, int width)
{
    int w = max(1, width);
    int t = theta % w;
    if (t < 0) t += w;
    return t;
}

int2 RockClampCell(int2 cell, int2 size)
{
    return int2(RockWrapTheta(cell.x, size.x), clamp(cell.y, 0, size.y - 1));
}

int RockAngularOff(int x, int origin, int width)
{
    int w = max(1, width);
    int off = x - origin;
    int half = w >> 1;
    if (off > half) off -= w;
    if (off < -half) off += w;
    return off;
}

float2 RockCellToCartesian(int2 cell, int2 size)
{
    float theta = ((RockWrapTheta(cell.x, size.x) + 0.5) / max(1.0, (float)size.x)) * ROCK_TAU;
    float r = Radius01(cell.y, size.y);
    return float2(cos(theta), sin(theta)) * r;
}

int2 RockCartesianToCell(float2 p, int2 size)
{
    float theta = atan2(p.y, p.x);
    if (theta < 0.0) theta += ROCK_TAU;
    int angular = (int)floor(theta / ROCK_TAU * max(1.0, (float)size.x));
    int radial = (int)floor(length(p) * max(1.0, (float)size.y));
    return RockClampCell(int2(angular, radial), size);
}

float2 RockRotate(float2 v, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

uint RockEncodeCoord(float v)
{
    return (uint)round(clamp(v + 2.0, 0.0, 4.0) * 65536.0);
}

float RockDecodeCoord(uint v, uint count)
{
    if (count == 0u) return 0.0;
    return (v / (float)count) / 65536.0 - 2.0;
}

bool HasRockSurfaceAir(int2 cell, int2 size, Texture2D<uint> materials)
{
    int2 left = RockClampCell(cell + int2(-1, 0), size);
    int2 right = RockClampCell(cell + int2(1, 0), size);
    int2 up = RockClampCell(cell + int2(0, 1), size);
    uint ml = materials.Load(int3(left, 0));
    uint mr = materials.Load(int3(right, 0));
    uint mu = cell.y < size.y - 1 ? materials.Load(int3(up, 0)) : 0u;
    return IsRockChunkOpen(ml) || IsRockChunkOpen(mr) || IsRockChunkOpen(mu);
}

bool IsRockOpenBelow(int2 cell, int2 size, Texture2D<uint> materials)
{
    if (cell.y <= 0) return false;
    uint below = materials.Load(int3(int2(cell.x, cell.y - 1), 0));
    return IsRockChunkOpen(below);
}

bool HasRockNeighbor(int2 cell, int2 size, Texture2D<uint> materials)
{
    int2 n0 = RockClampCell(cell + int2(-1, 0), size);
    int2 n1 = RockClampCell(cell + int2(1, 0), size);
    int2 n2 = RockClampCell(cell + int2(0, -1), size);
    int2 n3 = RockClampCell(cell + int2(0, 1), size);
    if (IsRockMaterial(materials.Load(int3(n0, 0)))) return true;
    if (IsRockMaterial(materials.Load(int3(n1, 0)))) return true;
    if (cell.y > 0 && IsRockMaterial(materials.Load(int3(n2, 0)))) return true;
    if (cell.y < size.y - 1 && IsRockMaterial(materials.Load(int3(n3, 0)))) return true;
    return false;
}

bool RockShouldHoldForChunk(int2 cell, uint material, float auxW, uint packed, int2 size, Texture2D<uint> materials)
{
    if (!IsRockMaterial(material)) return false;
    uint status = RockStatus(packed);
    if (status == ROCK_STATUS_SEARCHING || status == ROCK_STATUS_INFLIGHT) return true;
    if (auxW >= 1.0) return false;
    if (!IsRockChunkSeedMaterial(material)) return false;
    if (!IsRockOpenBelow(cell, size, materials)) return false;
    if (!HasRockSurfaceAir(cell, size, materials)) return false;
    return HasRockNeighbor(cell, size, materials);
}

#endif
