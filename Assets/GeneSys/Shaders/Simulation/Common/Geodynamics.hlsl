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
float4 _GeodynamicsL; // coseismicScale, unused
#ifndef GENESYS_VOLCANIC_DECLARED
#define GENESYS_VOLCANIC_DECLARED
float4 _Volcanic; // extrusionRate, coolingRate, magmaViscosity, volcanicSurfaceCoupling
#endif
#ifndef GENESYS_HYDROTHERMAL_DECLARED
#define GENESYS_HYDROTHERMAL_DECLARED
float4 _Hydrothermal; // heatTransfer, nutrientYield, releaseThreshold, surfaceCoupling
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
    int a0 = GeodynamicsAngularBin(theta);
    int r0 = GeodynamicsRadialBin(radius01);
    int aBins = GeodynamicsAngularBins();
    int rBins = GeodynamicsRadialBins();
    float footprint = max(0.01, _GeodynamicsC.z);
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
            float ang = GeodynamicsAngularDistance(a0, ab);
            float rad = abs((float)(r0 - rb)) / max(1.0, (float)rBins);
            float falloff = saturate(1.0 - ang / footprint) * saturate(1.0 - rad / 0.35);
            best = max(best, saturate(ev.y) * falloff);
        }
    }
    return best;
}

float GeodynamicsOverpressure(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsReservoir(GeodynamicsAngularBin(theta), GeodynamicsRadialBin(radius01)).y);
}

float GeodynamicsStrain(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsReservoir(GeodynamicsAngularBin(theta), GeodynamicsRadialBin(radius01)).z);
}

float GeodynamicsMeltFraction(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsReservoir(GeodynamicsAngularBin(theta), GeodynamicsRadialBin(radius01)).w);
}

float GeodynamicsThermalAnomaly(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return GeodynamicsReservoir(GeodynamicsAngularBin(theta), GeodynamicsRadialBin(radius01)).x;
}

float GeodynamicsFaultWeakness(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return saturate(GeodynamicsKinematics(GeodynamicsAngularBin(theta), GeodynamicsRadialBin(radius01)).z);
}

float2 GeodynamicsFlow(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    return GeodynamicsKinematics(GeodynamicsAngularBin(theta), GeodynamicsRadialBin(radius01)).xy;
}

float GeodynamicsVolcanicEnvelope(int theta, float radius01)
{
    return GeodynamicsEnvelopeAt(theta, radius01, 2);
}

float GeodynamicsDikeNucleation(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    float weakness = GeodynamicsFaultWeakness(theta, radius01);
    if (weakness < 0.35)
        return 0.0;
    int aBins = GeodynamicsAngularBins();
    int width = max(1, _GridSize.x);
    int bin = GeodynamicsAngularBin(theta);
    int start = (int)((uint)bin * (uint)width / (uint)aBins);
    int endExclusive = (int)((uint)(bin + 1) * (uint)width / (uint)aBins);
    int span = max(1, endExclusive - start);
    float u = ((float)(theta - start) + 0.5) / (float)span;
    uint radKey = (uint)floor(radius01 * 36.0);
    float meander = sin(radius01 * 22.0 + Hash01((uint)bin * 509u + (uint)_Seed) * 6.28318530718) * 0.32;
    meander += (Hash01((uint)bin * 374761u + radKey * 127u + (uint)_Seed) - 0.5) * 0.22;
    float center = saturate(0.5 + meander);
    float filament = saturate((0.24 - abs(u - center)) / 0.24);
    float gap = Hash01((uint)bin * 19349663u + radKey * 83492791u + (uint)_Seed);
    if (gap < 0.3)
        filament = 0.0;
    float pocket = Hash01((uint)theta * 73856093u + radKey * 6151u + (uint)_Seed);
    filament = max(filament, step(0.97, pocket) * 0.7);
    return saturate(weakness * filament) * step(0.1, radius01) * step(radius01, _AtmosphereStartRadius - 0.03);
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

float GeodynamicsVerticalDrive(int theta, float radius01)
{
    if (_GeodynamicsFlags.x < 0.5)
        return 0.0;
    float2 flow = GeodynamicsFlow(theta, radius01);
    float convergence = GeodynamicsConvergence(theta, radius01);
    float overpressure = GeodynamicsOverpressure(theta, radius01);
    float seismic = GeodynamicsSeismicEnvelope(theta, radius01);
    float upliftScale = max(0.0, _GeodynamicsK.y);
    float convergenceScale = max(0.0, _GeodynamicsK.z);
    float coseismic = max(0.0, _GeodynamicsL.x);
    float drive = upliftScale * (flow.y + overpressure)
        + convergenceScale * convergence
        + coseismic * seismic;
    return clamp(drive, -8.0, 8.0);
}

float GeodynamicsKinematicChance(float drive)
{
    float coupling = saturate(_GeodynamicsK.x);
    float strength = abs(drive) * coupling;
    if (strength < 0.25)
        return 0.0;
    return saturate((strength - 0.25) * 2.0 + 1e-4);
}

float GeodynamicsKinematicGate(int theta)
{
    int bin = GeodynamicsAngularBin(theta);
    return Hash01((uint)bin * 73856093u + (uint)_Tick * 19349663u + (uint)_Seed);
}

#endif
