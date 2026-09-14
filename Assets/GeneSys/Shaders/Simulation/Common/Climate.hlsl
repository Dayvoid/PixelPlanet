#ifndef GENESYS_CLIMATE_HLSL
#define GENESYS_CLIMATE_HLSL

#ifdef GENESYS_CLIMATE_STATE_RW
RWStructuredBuffer<float4> _ClimateState;
#else
StructuredBuffer<float4> _ClimateState;
#endif

float4 _ClimateFlags; // enable, init, prevailingInject, albedoFeedback
float4 _ClimateA; // binCount, couplePeriod, slabHeatCapacity, heatTransport
float4 _ClimateB; // seasonLengthDays, seasonalAmplitude, thermalWindGain, iceAlbedo
float4 _ClimateC; // canopyAlbedoDrop, roughnessGain, bucketGain, memoryRate
float4 _ClimateD; // biomeFeedback, baseAlbedo, ashAlbedo, burnBucketPenalty
float4 _ClimateE; // slabRadiativeCooling, unused, unused, unused

#ifndef GENESYS_TICKS_PER_DAY_DECLARED
#define GENESYS_TICKS_PER_DAY_DECLARED
float _TicksPerDay;
#endif

int ClimateBinCount()
{
    return clamp((int)_ClimateA.x, 8, 128);
}

int ClimateBin(int theta)
{
    int bins = ClimateBinCount();
    int width = max(1, _GridSize.x);
    theta = WrapTheta(theta, width);
    return (int)((uint)theta * (uint)bins / (uint)width);
}

int ClimateShellOf(float radius01)
{
    if (radius01 < _AtmosphereStartRadius)
        return 0;
    float mid = (_AtmosphereStartRadius + 1.0) * 0.5;
    return radius01 < mid ? 1 : 2;
}

float4 ClimateMemoryOf(int bin)
{
    return _ClimateState[bin * 3];
}

float4 ClimateLandOf(int bin)
{
    return _ClimateState[bin * 3 + 1];
}

float4 ClimateSurfaceOf(int bin)
{
    return ClimateMemoryOf(bin);
}

float ClimateSeasonScale()
{
    float days = (float)_Tick / max(1.0, _TicksPerDay);
    float period = max(1.0, _ClimateB.x);
    return 1.0 + _ClimateB.y * cos(6.28318530718 * days / period);
}

float ClimateInsolationScale(int theta)
{
    if (_ClimateFlags.x < 0.5 || _ClimateFlags.z < 0.5)
        return 1.0;
    return ClimateSeasonScale();
}

float ClimateSurfaceAbsorb(int theta)
{
    if (_ClimateFlags.x < 0.5 || _ClimateFlags.w < 0.5)
        return 1.0;
    float albedo = saturate(ClimateMemoryOf(ClimateBin(theta)).w);
    return saturate(1.0 - albedo);
}

float ClimateWindBias(int theta, int shell)
{
    if (_ClimateFlags.x < 0.5 || _ClimateFlags.z < 0.5)
        return _WeatherI.z;
    float u = ClimateLandOf(ClimateBin(theta)).x;
    if (shell >= 2)
        u = -u;
    return u;
}

float ClimateRoughness(int theta)
{
    if (_ClimateFlags.x < 0.5 || _ClimateD.x < 0.5)
        return 1.0;
    return max(1.0, ClimateLandOf(ClimateBin(theta)).y);
}

float ClimateBucketScale(int theta)
{
    if (_ClimateFlags.x < 0.5 || _ClimateD.x < 0.5)
        return 1.0;
    return max(0.05, ClimateLandOf(ClimateBin(theta)).z);
}

float ClimateBucketScaleFace(int thetaA, int thetaB)
{
    return 0.5 * (ClimateBucketScale(thetaA) + ClimateBucketScale(thetaB));
}

#endif
