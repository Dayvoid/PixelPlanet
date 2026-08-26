#define FLORA_MOVE_EMPTY 0xffffffffu

uint FloraClaimIndex(int2 cell)
{
    return (uint)cell.y * (uint)_GridSize.x + (uint)cell.x;
}

uint PackFloraCell(int2 cell)
{
    return 1u + (uint)cell.y * (uint)_GridSize.x + (uint)cell.x;
}

int2 UnpackFloraCell(uint packed)
{
    uint idx = packed - 1u;
    uint width = (uint)max(1, _GridSize.x);
    return int2((int)(idx % width), (int)(idx / width));
}

bool IsStackedFlora(int2 cell)
{
    if (!IsFloraMaterial(M(cell)) || cell.y <= 0) return false;
    return IsFloraMaterial(M(cell + int2(0, -1)));
}

float TangentialForcing(int2 cell)
{
    float tangential = F(cell).x;
    int2 offsets[3] = { int2(-1, 0), int2(1, 0), int2(0, 1) };
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        int2 offset = offsets[i];
        if (offset.y != 0)
        {
            int neighborY = cell.y + offset.y;
            if (neighborY < 0 || neighborY >= _GridSize.y) continue;
        }
        int2 neighbor = C(cell + offset);
        uint neighborMaterial = M(neighbor);
        if (!IsSporeAirCarrier(neighborMaterial) && !IsSporeWaterCarrier(neighborMaterial)) continue;
        float fx = F(neighbor).x;
        if (abs(fx) > abs(tangential))
            tangential = fx;
    }
    return tangential;
}

float RainImpact(int2 cell)
{
    float impact = max(0.0, S(cell).z);
    if (cell.y < _GridSize.y - 1)
    {
        int2 above = cell + int2(0, 1);
        uint aboveMaterial = M(above);
        if (IsSporeAirCarrier(aboveMaterial) || aboveMaterial == 0u)
            impact += max(0.0, S(above).z);
    }
    return impact;
}

int PolewardDir(int2 cell)
{
    float poleAngle = Hash01((uint)_Seed * 9829u);
    float angular = (cell.x + 0.5) / max(1.0, (float)_GridSize.x);
    float pole0 = poleAngle;
    float pole1 = frac(poleAngle + 0.5);
    float target = AngularDistance01(angular, pole0) <= AngularDistance01(angular, pole1) ? pole0 : pole1;
    float delta = target - angular;
    if (delta > 0.5) delta -= 1.0;
    if (delta < -0.5) delta += 1.0;
    if (abs(delta) <= 1e-6) return 0;
    return delta > 0.0 ? 1 : -1;
}

int2 AdjacentOpen(int2 cell, int dir)
{
    if (dir == 0) return cell;
    int2 dest = C(cell + int2(dir, 0));
    if (IsFloraOpenHabitat(M(dest))) return dest;
    return cell;
}

int2 ScanPolewardFreeCell(int2 cell, int dir)
{
    if (dir == 0) return cell;
    [loop]
    for (int step = 1; step < _GridSize.x; step++)
    {
        int2 candidate = C(cell + int2(dir * step, 0));
        if (all(candidate == cell)) break;
        uint mat = M(candidate);
        if (IsFloraOpenHabitat(mat))
        {
            uint below = candidate.y > 0 ? M(candidate + int2(0, -1)) : 0xffffffffu;
            if (!IsFloraMaterial(below))
                return candidate;
            continue;
        }
        if (IsFloraMaterial(mat))
            continue;
        break;
    }
    return cell;
}

int2 FloraMoveDestination(int2 cell)
{
    if (!IsFloraMaterial(M(cell))) return cell;

    float windRate = max(0.0, _FloraH.y);
    float rainRate = max(0.0, _FloraH.z);
    float stackRate = max(0.0, _FloraH.w);
    float tangential = TangentialForcing(cell);

    float windChance = saturate(windRate * abs(tangential) * _DeltaTime);
    uint windHash = (uint)cell.x * 73856093u + (uint)cell.y * 19349663u + (uint)_Seed * 31337u + (uint)_Tick * 2707u;
    if (windChance > 1e-8 && Hash01(windHash) < windChance)
    {
        int dir = tangential >= 0.0 ? 1 : -1;
        int2 dest = AdjacentOpen(cell, dir);
        if (!all(dest == cell)) return dest;
    }

    float rain = RainImpact(cell);
    float rainChance = saturate(rainRate * rain * _DeltaTime);
    uint rainHash = (uint)cell.x * 83492791u + (uint)cell.y * 6151u + (uint)_Seed * 7919u + (uint)_Tick * 9829u;
    if (rainChance > 1e-8 && Hash01(rainHash) < rainChance)
    {
        int dir = abs(tangential) > 1e-5
            ? (tangential >= 0.0 ? 1 : -1)
            : (Hash01(rainHash + 17u) < 0.5 ? -1 : 1);
        int2 dest = AdjacentOpen(cell, dir);
        if (!all(dest == cell)) return dest;
    }

    if (!IsStackedFlora(cell)) return cell;
    float motility = DecodeGene(G(cell), FLORA_GENE_STACK_MOTILITY) / 255.0;
    float migrateChance = saturate(stackRate * motility * _DeltaTime);
    if (migrateChance <= 1e-8) return cell;
    uint migHash = (uint)cell.x * 19349663u + (uint)cell.y * 73856093u + (uint)_Seed * 17u + (uint)_Tick * 31337u;
    if (Hash01(migHash) >= migrateChance) return cell;
    return ScanPolewardFreeCell(cell, PolewardDir(cell));
}
