#ifndef GENESYS_SURFACE_WATER_INCLUDED
#define GENESYS_SURFACE_WATER_INCLUDED

#ifndef PRECIP_MIN_DROP
#define PRECIP_MIN_DROP 0.45
#endif

// Shared surface-water profiling for the hydrostatic column solver. The flux pass and
// the apply pass must agree cell for cell on where a column starts and how much water
// it holds, or the face exchange stops conserving mass. Include this after the file's
// C/S/M/D accessors so both compute shaders resolve to the exact same code.

bool IsAtmosphereCell(int2 cell, uint material)
{
    float category = D(material).metadata.x;
    if (category == 1.0) return true;
    return material == 0u && Radius01(cell.y, _GridSize.y) >= _AtmosphereStartRadius;
}

bool IsOpenCarrier(uint material)
{
    return material == 0u || material == 1u || D(material).metadata.x == 1.0;
}

// A cell that carries surface film and can hand it to an open cell above.
// Both sides of a materialization pair must agree, so they share this predicate.
bool IsFilmSubstrate(int2 cell, uint material)
{
    if (IsAtmosphereCell(cell, material)) return false;
    return material != 0u && material != 9u && material != 10u;
}

// A liquid pixel is airborne when the cell beneath its whole contiguous liquid run is
// open. Testing only the cell directly below misreads the upper members of a falling
// clump as standing water, which hands the column an air cell for a bed.
bool IsAirborneLiquid(int2 cell, uint material)
{
    if (material != 9u && material != 10u) return false;
    int y = cell.y;
    [loop]
    while (y > 0)
    {
        uint below = M(int2(cell.x, y - 1));
        if (below != 9u && below != 10u)
            return IsOpenCarrier(below);
        y--;
    }
    return false;
}

// Falling rain/hail. Hydrostatic must not profile, flux, or rewrite these cells.
bool IsHydrometeor(int2 cell, uint material)
{
    return IsAirborneLiquid(cell, material);
}

// A lone Water pixel on a non-liquid bed is pondable rain. Groundwater soaks stacked
// standing water, not this drop, while ponding is on so the pixel can stay visible.
bool IsPondableRainPixel(int2 cell, uint material)
{
    if (material != 9u) return false;
    if (IsAirborneLiquid(cell, material)) return true;
    if (cell.y + 1 < _GridSize.y)
    {
        uint above = M(int2(cell.x, cell.y + 1));
        if (above == 9u || above == 10u) return false;
    }
    if (cell.y > 0)
    {
        uint below = M(int2(cell.x, cell.y - 1));
        if (below == 9u || below == 10u) return false;
    }
    return true;
}

// Atmosphere-connected surface reservoir for one angular column. Ice lids and
// enclosed cave water are excluded so only the free surface participates.
// Landed Ice on rock is that lid (volume 0) until PhaseChange thaws it to Water;
// hydrostatic never levels Ice, which is why hail/snow sits while rain should pond.
// Falling precipitation is skipped (not absorbed) so Margolus owns the fall.
void ProfileSurfaceColumn(int x, out int bedY, out float volume, out float temperature, out float head, out int waterTop, out bool hadPixels)
{
    bedY = -1;
    volume = 0.0;
    temperature = 15.0;
    head = 0.0;
    waterTop = -1;
    hadPixels = false;

    int y = _GridSize.y - 1;
    int runTop = -1;
    [loop]
    while (y >= 0)
    {
        int2 cell = int2(x, y);
        uint material = M(cell);
        if (material == 9u || material == 10u)
        {
            // Remember where this contiguous liquid run starts. Whether it is standing
            // water or precipitation depends on what the whole run turns out to rest on.
            if (runTop < 0) runTop = y;
            y--;
            continue;
        }
        if (IsAtmosphereCell(cell, material) || (runTop >= 0 && IsOpenCarrier(material)))
        {
            // Open air under a liquid run means the entire clump is still falling, so
            // Margolus owns the fall and the free surface lies further down.
            runTop = -1;
            y--;
            continue;
        }
        break;
    }
    if (runTop >= 0)
        y = runTop;
    else if (y < 0)
        return;

    uint surfaceMat = M(int2(x, y));
    if (surfaceMat == 10u)
    {
        int probeY = y;
        [loop]
        while (probeY >= 0 && M(int2(x, probeY)) == 10u)
        {
            probeY--;
        }
        if (probeY >= 0 && M(int2(x, probeY)) == 9u)
        {
            surfaceMat = 9u;
            y = probeY;
        }
        else
        {
            bedY = y;
            waterTop = y;
            head = (float)(bedY + 1);
            return;
        }
    }

    if (surfaceMat == 9u)
    {
        waterTop = y;
        hadPixels = true;
        float heat = 0.0;
        [loop]
        while (y >= 0 && M(int2(x, y)) == 9u)
        {
            float4 state = S(int2(x, y));
            float mass = max(0.0, state.z);
            volume += mass;
            heat += mass * state.x;
            y--;
        }
        bedY = y;
        if (bedY >= 0)
        {
            int2 bed = int2(x, bedY);
            uint bedMat = M(bed);
            if (IsFilmSubstrate(bed, bedMat))
            {
                float4 bedState = S(bed);
                float film = max(0.0, bedState.z);
                volume += film;
                heat += film * bedState.x;
            }
        }
        temperature = volume > 1e-6 ? heat / volume : 15.0;
        head = (float)(bedY + 1) + volume;
        return;
    }

    bedY = y;
    waterTop = y;
    int2 bed = int2(x, bedY);
    if (IsFilmSubstrate(bed, surfaceMat))
    {
        float4 state = S(bed);
        volume = max(0.0, state.z);
        temperature = state.x;
    }
    head = (float)(bedY + 1) + volume;
}

void PartitionSurfaceVolume(float volume, bool canFilm, out int cells, out float remainder)
{
    volume = max(0.0, volume);
    if (canFilm)
    {
        cells = (int)floor(volume + 1e-6);
        remainder = max(0.0, volume - (float)cells);
        if (remainder >= 1.0 - 1e-6)
        {
            cells += 1;
            remainder = max(0.0, remainder - 1.0);
        }
    }
    else
    {
        cells = volume > 1e-6 ? (int)ceil(volume - 1e-6) : 0;
        remainder = 0.0;
    }
}

// A landed rain pixel should stay visible until the column shrinks below a drop.
// Film-only beds (no pixel yet) still use exact floor/remainder so thin wetting
// does not spawn a standing cell.
void KeepLandedRainPixel(bool hadPixels, float volume, inout int cells, inout float remainder)
{
    if (!hadPixels || cells > 0 || volume < PRECIP_MIN_DROP)
        return;
    cells = 1;
    remainder = 0.0;
}

#endif
