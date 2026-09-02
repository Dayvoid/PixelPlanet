#ifndef GENESYS_SOLAR_INSOLATION_INCLUDED
#define GENESYS_SOLAR_INSOLATION_INCLUDED

// Shortest wrapped angular distance in [0, 0.5]. Terminator is at 0.25 (90 deg).
// Peak 1.0 is directly under the solar body; falloff is linear to 0 at the terminators.
float SolarInsolation(float theta01, float solarAngle01)
{
    float wrapped = frac(theta01 - solarAngle01);
    float dist = min(wrapped, 1.0 - wrapped);
    return saturate(1.0 - dist * 4.0);
}

#endif
