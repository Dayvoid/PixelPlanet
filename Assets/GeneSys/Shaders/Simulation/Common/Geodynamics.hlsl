#ifndef GENESYS_GEODYNAMICS_HLSL
#define GENESYS_GEODYNAMICS_HLSL

#ifdef GENESYS_GEODYNAMICS_STATE_RW
RWStructuredBuffer<float4> _GeodynamicsState;
#else
StructuredBuffer<float4> _GeodynamicsState;
#endif

#ifdef GENESYS_GEODYNAMICS_EVENTS_RW
RWStructuredBuffer<float4> _GeodynamicsEvents;
#else
StructuredBuffer<float4> _GeodynamicsEvents;
#endif

float4 _GeodynamicsFlags; // enable, init, angularBins, radialBins
float4 _GeodynamicsA; // period, convection, pressureBuild, pressureLeakage
float4 _GeodynamicsB; // heatCoupling, strainGain, strainTransfer, faultHealing
float4 _GeodynamicsC; // earthquakeThreshold, releaseFraction, footprint, cooldownTicks
float4 _GeodynamicsD; // maxConcurrent, surfaceCoupling, volcanicThreshold, volcanicReleaseFraction
float4 _GeodynamicsK; // kinematicCoupling, upliftScale, convergenceScale, displacementScale
float4 _GeodynamicsL; // coseismicScale, crustRatio, isostasyScale, magmaFractionLimit
#ifndef GENESYS_VOLCANIC_DECLARED
#define GENESYS_VOLCANIC_DECLARED
float4 _Volcanic; // extrusionRate, coolingRate, magmaViscosity, meltRate
#endif
#ifndef GENESYS_HYDROTHERMAL_DECLARED
#define GENESYS_HYDROTHERMAL_DECLARED
float4 _Hydrothermal; // heatTransfer, nutrientYield, releaseThreshold, surfaceCoupling
#endif
#ifndef GENESYS_GEODYNAMICS_STATS_DECLARED
#define GENESYS_GEODYNAMICS_STATS_DECLARED
#ifdef GENESYS_GEODYNAMICS_STATS_RW
RWStructuredBuffer<float4> _GeodynamicsEventCounter;
#else
StructuredBuffer<float4> _GeodynamicsEventCounter;
#endif
#endif

int GeodynamicsAngularBins()
{
    return clamp((int)_GeodynamicsFlags.z, 16, 128);
}

int GeodynamicsRadialBins()
{
    return clamp((int)_GeodynamicsFlags.w, 8, 32);
}

int GeodynamicsWrapBin(int bin, int bins)
{
    bins = max(1, bins);
    int wrapped = bin % bins;
    return wrapped < 0 ? wrapped + bins : wrapped;
}

int GeodynamicsAngularBin(int theta)
{
    int bins = GeodynamicsAngularBins();
    int width = max(1, _GridSize.x);
    theta = WrapTheta(theta, width);
    return (int)((uint)theta * (uint)bins / (uint)width);
}

int GeodynamicsRadialBin(float radius01)
{
    int bins = GeodynamicsRadialBins();
    float outer = max(0.05, _AtmosphereStartRadius);
    float t = saturate(radius01 / outer);
    return clamp((int)(t * bins), 0, bins - 1);
}

int GeodynamicsStateIndex(int angularBin, int radialBin, int slot)
{
    int aBins = GeodynamicsAngularBins();
    int rBins = GeodynamicsRadialBins();
    angularBin = GeodynamicsWrapBin(angularBin, aBins);
    radialBin = clamp(radialBin, 0, rBins - 1);
    slot = clamp(slot, 0, 1);
    return ((angularBin * rBins) + radialBin) * 2 + slot;
}

int GeodynamicsEventIndex(int angularBin, int radialBin)
{
    int aBins = GeodynamicsAngularBins();
    int rBins = GeodynamicsRadialBins();
    angularBin = GeodynamicsWrapBin(angularBin, aBins);
    radialBin = clamp(radialBin, 0, rBins - 1);
    return angularBin * rBins + radialBin;
}

float4 GeodynamicsReservoir(int angularBin, int radialBin)
{
    return _GeodynamicsState[GeodynamicsStateIndex(angularBin, radialBin, 0)];
}

float4 GeodynamicsKinematics(int angularBin, int radialBin)
{
    return _GeodynamicsState[GeodynamicsStateIndex(angularBin, radialBin, 1)];
}

float4 GeodynamicsEvent(int angularBin, int radialBin)
{
    return _GeodynamicsEvents[GeodynamicsEventIndex(angularBin, radialBin)];
}

float GeodynamicsAngularCoord(int theta)
{
    int bins = GeodynamicsAngularBins();
    int width = max(1, _GridSize.x);
    theta = WrapTheta(theta, width);
    return ((float)theta + 0.5) * (float)bins / (float)width;
}

float GeodynamicsRadialCoord(float radius01)
{
    int bins = GeodynamicsRadialBins();
    float outer = max(0.05, _AtmosphereStartRadius);
    float t = saturate(radius01 / outer);
    return clamp(t * (float)bins, 0.0, (float)bins - 1e-4);
}

float4 GeodynamicsSampleLattice(int slot, int theta, float radius01)
{
    float af = GeodynamicsAngularCoord(theta);
    float rf = GeodynamicsRadialCoord(radius01);
    int a0 = (int)floor(af);
    int r0 = (int)floor(rf);
    float ta = frac(af);
    float tr = saturate(rf - (float)r0);
    int r1 = min(r0 + 1, GeodynamicsRadialBins() - 1);
    float4 v00 = slot == 0 ? GeodynamicsReservoir(a0, r0) : GeodynamicsKinematics(a0, r0);
    float4 v10 = slot == 0 ? GeodynamicsReservoir(a0 + 1, r0) : GeodynamicsKinematics(a0 + 1, r0);
    float4 v01 = slot == 0 ? GeodynamicsReservoir(a0, r1) : GeodynamicsKinematics(a0, r1);
    float4 v11 = slot == 0 ? GeodynamicsReservoir(a0 + 1, r1) : GeodynamicsKinematics(a0 + 1, r1);
    return lerp(lerp(v00, v10, ta), lerp(v01, v11, ta), tr);
}

float4 GeodynamicsSampleReservoir(int theta, float radius01)
{
    return GeodynamicsSampleLattice(0, theta, radius01);
}

float4 GeodynamicsSampleKinematics(int theta, float radius01)
{
    return GeodynamicsSampleLattice(1, theta, radius01);
}

float GeodynamicsSafetyThrottle()
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(_GeodynamicsEventCounter[0].w);
}

float GeodynamicsVolcanoScore(float4 res, float4 kin)
{
    float heatDrive = saturate(res.x * 0.25 + max(0.0, kin.y) * 0.5);
    return saturate(res.y) * (0.35 + heatDrive) * (0.25 + saturate(kin.z) + saturate(res.w));
}

float GeodynamicsVolcanoScoreAt(float4 res, float4 kin, int radialBin)
{
    int rBins = GeodynamicsRadialBins();
    float r = ((float)clamp(radialBin, 0, rBins - 1) + 0.5) / max(1.0, (float)rBins);
    // Prefer asthenosphere / upper mantle so vents are not locked to the core.
    float window = saturate(1.0 - abs(r - 0.62) * 2.4);
    float coreCut = saturate((r - 0.10) / 0.22);
    return saturate(GeodynamicsVolcanoScore(res, kin) * (0.16 + 0.84 * window) * (0.20 + 0.80 * coreCut));
}

void GeodynamicsNearestVolcanic(int theta, float radius01, out int bestA, out int bestR, out float bestY)
{
    int aBins = GeodynamicsAngularBins();
    int rBins = GeodynamicsRadialBins();
    float af = GeodynamicsAngularCoord(theta);
    int a0 = (int)floor(af);
    float footprint = max(0.01, _GeodynamicsC.z);
    float angSpan = max(0.45, footprint * (float)aBins);
    bestA = GeodynamicsWrapBin(a0, aBins);
    bestR = GeodynamicsRadialBin(radius01);
    bestY = 0.0;
    [unroll]
    for (int da = -2; da <= 2; da++)
    {
        int ab = GeodynamicsWrapBin(a0 + da, aBins);
        [loop]
        for (int rb = 0; rb < rBins; rb++)
        {
            float4 ev = GeodynamicsEvent(ab, rb);
            if ((int)round(ev.x) != 2)
                continue;
            float dang = abs(af - ((float)ab + 0.5));
            dang = min(dang, (float)aBins - dang);
            float score = saturate(ev.y) * saturate(1.0 - dang / angSpan);
            if (score > bestY)
            {
                bestY = score;
                bestA = ab;
                bestR = rb;
            }
        }
    }
}

float GeodynamicsRisingColumn(float rf, float eventRf, int rBins)
{
    float below = eventRf - rf;
    if (below > 1.15)
        return 0.0;
    float crustRf = max(eventRf + 0.5, (float)rBins - 1.35);
    float above = rf - eventRf;
    float toCrust = saturate(1.02 - max(0.0, rf - crustRf) * 1.8);
    float fromSource = saturate((rf - (eventRf - 1.15)) / 1.15);
    return saturate(toCrust * lerp(fromSource, 1.0, step(0.0, above)));
}

float GeodynamicsEnabled()
{
    return _GeodynamicsFlags.x;
}

float GeodynamicsAngularDistance(int a, int b)
{
    int bins = GeodynamicsAngularBins();
    int d = abs(GeodynamicsWrapBin(a, bins) - GeodynamicsWrapBin(b, bins));
    return min(d, bins - d) / max(1.0, (float)bins);
}

float GeodynamicsEnvelopeAt(int theta, float radius01, int eventType)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    float af = GeodynamicsAngularCoord(theta);
    float rf = GeodynamicsRadialCoord(radius01);
    int a0 = (int)floor(af);
    int r0 = clamp((int)floor(rf), 0, GeodynamicsRadialBins() - 1);
    int aBins = GeodynamicsAngularBins();
    int rBins = GeodynamicsRadialBins();
    float footprint = max(0.01, _GeodynamicsC.z);
    float angSpan = max(0.45, footprint * (float)aBins);
    float radSpan = max(0.65, 0.28 * (float)rBins);
    float best = 0.0;
    [unroll]
    for (int da = -2; da <= 2; da++)
    {
        for (int dr = -1; dr <= 1; dr++)
        {
            int ab = GeodynamicsWrapBin(a0 + da, aBins);
            int rb = clamp(r0 + dr, 0, rBins - 1);
            float4 ev = GeodynamicsEvent(ab, rb);
            if ((int)round(ev.x) != eventType)
                continue;
            float dang = abs(af - ((float)ab + 0.5));
            dang = min(dang, (float)aBins - dang);
            float drad = abs(rf - ((float)rb + 0.5));
            float falloff = saturate(1.0 - dang / angSpan) * saturate(1.0 - drad / radSpan);
            best = max(best, saturate(ev.y) * falloff);
        }
    }
    return best;
}

float GeodynamicsOverpressure(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsSampleReservoir(theta, radius01).y);
}

float GeodynamicsStrain(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsSampleReservoir(theta, radius01).z);
}

float GeodynamicsMeltFraction(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsSampleReservoir(theta, radius01).w);
}

float GeodynamicsThermalAnomaly(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return GeodynamicsSampleReservoir(theta, radius01).x;
}

float GeodynamicsFaultWeakness(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsSampleKinematics(theta, radius01).z);
}

float2 GeodynamicsFlow(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return GeodynamicsSampleKinematics(theta, radius01).xy;
}

float GeodynamicsVolcanicEnvelope(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    int bestA, bestR;
    float bestY;
    GeodynamicsNearestVolcanic(theta, radius01, bestA, bestR, bestY);
    if (bestY < 0.02)
        return 0.0;
    float af = GeodynamicsAngularCoord(theta);
    float rf = GeodynamicsRadialCoord(radius01);
    float dang = abs(af - ((float)bestA + 0.5));
    int aBins = GeodynamicsAngularBins();
    dang = min(dang, (float)aBins - dang);
    float footprint = max(0.01, _GeodynamicsC.z);
    float angSpan = max(0.45, footprint * (float)aBins);
    float column = GeodynamicsRisingColumn(rf, (float)bestR + 0.5, GeodynamicsRadialBins());
    return saturate(bestY * saturate(1.0 - dang / angSpan) * column);
}

float GeodynamicsDikeNucleation(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    int bestA, bestR;
    float bestY;
    GeodynamicsNearestVolcanic(theta, radius01, bestA, bestR, bestY);
    float weakness = GeodynamicsFaultWeakness(theta, radius01);
    if (bestY < 0.04 && weakness < 0.18)
        return 0.0;

    int aBins = GeodynamicsAngularBins();
    float af = GeodynamicsAngularCoord(theta);
    float rf = GeodynamicsRadialCoord(radius01);
    float eventRf = (float)bestR + 0.5;
    float column = GeodynamicsRisingColumn(rf, eventRf, GeodynamicsRadialBins());
    if (column < 0.04 && weakness < 0.18)
        return 0.0;

    float meander = sin(radius01 * 18.0 + Hash01((uint)bestA * 509u + (uint)_Seed) * 6.28318530718) * 0.28;
    meander += sin(radius01 * 41.0 + Hash01((uint)bestA * 374761u + (uint)_Seed) * 6.28318530718) * 0.10;
    float centerAf = (float)bestA + 0.5 + meander;
    float dang = abs(af - centerAf);
    dang = min(dang, (float)aBins - dang);
    float halfWidth = 0.22 + bestY * 0.08;
    float filament = saturate((halfWidth - dang) / max(0.04, halfWidth));
    float edge = Hash01((uint)theta * 73856093u + (uint)floor(radius01 * 96.0) * 19349663u + (uint)_Seed);
    filament *= lerp(0.78, 1.0, edge);
    float active = max(weakness * 0.55 * column, bestY * column);
    return saturate(active * filament) * step(0.04, radius01) * step(radius01, _AtmosphereStartRadius - 0.03);
}

float GeodynamicsSeismicEnvelope(int theta, float radius01)
{
    return GeodynamicsEnvelopeAt(theta, radius01, 1);
}

float GeodynamicsHydrothermalEnvelope(int theta, float radius01)
{
    float eventGain = GeodynamicsEnvelopeAt(theta, radius01, 3);
    float seep = GeodynamicsFaultWeakness(theta, radius01) * saturate(GeodynamicsThermalAnomaly(theta, radius01) * 0.02);
    return saturate(max(eventGain, seep));
}

float GeodynamicsSeedFault(int theta, float radius01, float faultCount)
{
    float count = max(1.0, faultCount);
    float angular = (theta + 0.5) / max(1.0, (float)_GridSize.x);
    float wander = (Hash01((uint)floor(radius01 * 64.0) * 374761u + (uint)_Seed) - 0.5) * 0.045;
    float wave = abs(frac(angular * count + wander + Hash01((uint)theta * 509u + (uint)_Seed) * 0.15) - 0.5);
    float band = saturate((0.035 - wave) * 28.0);
    return band * step(0.08, radius01) * step(radius01, _AtmosphereStartRadius + 0.02);
}

float GeodynamicsAngularDrive(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    float2 flow = GeodynamicsFlow(theta, radius01);
    float seismic = GeodynamicsSeismicEnvelope(theta, radius01);
    float weakness = GeodynamicsFaultWeakness(theta, radius01);
    float slip = seismic * weakness * max(0.0, _GeodynamicsL.x);
    float polarity = flow.x == 0.0 ? 1.0 : sign(flow.x);
    return clamp(flow.x + slip * polarity, -4.0, 4.0);
}

float GeodynamicsConvergence(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    int a0 = GeodynamicsAngularBin(theta);
    int r0 = GeodynamicsRadialBin(radius01);
    float left = GeodynamicsKinematics(a0 - 1, r0).x;
    float right = GeodynamicsKinematics(a0 + 1, r0).x;
    return clamp(left - right, -4.0, 4.0);
}

float GeodynamicsRelativeRadialFlow(int theta, float radius01)
{
    int a0 = GeodynamicsAngularBin(theta);
    int r0 = GeodynamicsRadialBin(radius01);
    float self = GeodynamicsKinematics(a0, r0).y;
    float mean = 0.0;
    [unroll]
    for (int da = -4; da <= 4; da++)
        mean += GeodynamicsKinematics(a0 + da, r0).y;
    return self - mean * (1.0 / 9.0);
}

float GeodynamicsVerticalDrive(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    float relativeUpwell = GeodynamicsRelativeRadialFlow(theta, radius01);
    float convergence = GeodynamicsConvergence(theta, radius01);
    float overpressure = GeodynamicsOverpressure(theta, radius01);
    float seismic = GeodynamicsSeismicEnvelope(theta, radius01);
    float upliftScale = max(0.0, _GeodynamicsK.y);
    float convergenceScale = max(0.0, _GeodynamicsK.z);
    float coseismic = max(0.0, _GeodynamicsL.x);
    float drive = upliftScale * (relativeUpwell + overpressure)
        + convergenceScale * convergence
        + coseismic * seismic;
    return clamp(drive, -8.0, 8.0);
}

float GeodynamicsKinematicChance(float drive)
{
    float coupling = saturate(_GeodynamicsK.x);
    float excess = abs(drive) - 0.08;
    if (excess <= 0.0 || coupling <= 1e-5)
        return 0.0;
    // coupling=1, |drive|=2 saturates so forced fixtures step every tick.
    // coupling=0.15, |drive|=1 yields ~0.33, about one cell per 10–30 geo ticks
    // once spacing is applied.
    return saturate(coupling * excess * 2.4);
}

float GeodynamicsKinematicGate(int theta)
{
    int bin = GeodynamicsAngularBin(theta);
    return Hash01((uint)bin * 73856093u + (uint)_Tick * 19349663u + (uint)_Seed);
}

int GeodynamicsKinematicSpacing()
{
    float coupling = saturate(_GeodynamicsK.x);
    float hold = 1.0 - coupling;
    return max(1, (int)round(1.0 + 4.0 * hold * hold));
}

bool GeodynamicsKinematicDue(int theta)
{
    int spacing = GeodynamicsKinematicSpacing();
    if (spacing <= 1)
        return true;
    int period = max(1, (int)_GeodynamicsA.x);
    int geoTick = _Tick / period;
    int bin = GeodynamicsAngularBin(theta);
    int phase = (int)(Hash01((uint)bin * 374761u + (uint)_Seed) * (float)spacing);
    int slot = geoTick - phase;
    int wrapped = slot % spacing;
    if (wrapped < 0) wrapped += spacing;
    return wrapped == 0;
}

bool GeodynamicsKinematicTriggered(int theta, float drive)
{
    float chance = GeodynamicsKinematicChance(drive);
    if (chance <= 0.0 || !GeodynamicsKinematicDue(theta))
        return false;
    return chance >= 0.999 || GeodynamicsKinematicGate(theta) < chance;
}

#endif
