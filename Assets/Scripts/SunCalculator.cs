using System;
using UnityEngine;


///Computes solar position (azimuth, altitude) and clear-sky beam irradiance
// using the Blanco-Muriel / NOAA algorithm (~0.01° accuracy).

// "Beam irradiance" is the illuminance on a surface perpendicular to the sun's rays.
// Lambert's cosine law (dot(normal, sunDir)) is applied separately in the compute shader.

// REFERENCE — Main solar position algorithm:
//   Blanco-Muriel et al. (2001). "Computing the solar vector."
//   Solar Energy, 70(5), pp. 431-441. https://doi.org/10.1016/S0038-092X(00)00156-0

// REFERENCE — NOAA implementation / formula cross-check:
//   NOAA Solar Calculator spreadsheet (2023).
//   https://gml.noaa.gov/grad/solcalc/calcdetails.html
public static class SunCalculator
{

    public struct SunPosition
    {
        // Degrees from North, clockwise (0 = North, 90 = East).
        public float AzimuthDeg;

        // Degrees above the horizon. Negative means the sun is below the horizon.
        public float AltitudeDeg;


        // Estimated clear-sky beam irradiance in Lux —
        // i.e. the illuminance on a surface facing directly toward the sun.
        // Multiply by dot(surfaceNormal, sunDirection) to get the actual irradiance on a tilted surface.
        public float BeamLux;

        public bool IsAboveHorizon => AltitudeDeg > 0f;
    }


    // Calculate sun position for a given location and local date/time.
    public static SunPosition Calculate(double latitude, double longitude,
                                        double utcOffsetHours, DateTime localTime)
    {
        // Convert local time to UTC
        DateTime utc = localTime.AddHours(-utcOffsetHours);
        double jd = ToJulianDay(utc);

        // Julian centuries from J2000.0 epoch
        double T = (jd - 2451545.0) / 36525.0;

        //  Ecliptic coordinates (NOAA algorithm) 

        // Geometric mean longitude of the Sun (degrees, mod 360)
        double L0 = (280.46646 + T * (36000.76983 + T * 0.0003032)) % 360.0;

        // Geometric mean anomaly of the Sun (degrees)
        double M = 357.52911 + T * (35999.05029 - 0.0001537 * T);
        double Mrad = M * Math.PI / 180.0;

        // Equation of center — accounts for the eccentricity of Earth's orbit
        double C = Math.Sin(Mrad) * (1.914602 - T * (0.004817 + 0.000014 * T))
                 + Math.Sin(2 * Mrad) * (0.019993 - 0.000101 * T)
                 + Math.Sin(3 * Mrad) * 0.000289;

        // Sun's apparent longitude (corrects for nutation and aberration)
        double sunLon = L0 + C;
        double omega = 125.04 - 1934.136 * T;
        double omRad = omega * Math.PI / 180.0;
        double appLon = sunLon - 0.00569 - 0.00478 * Math.Sin(omRad);
        double appLonR = appLon * Math.PI / 180.0;

        // Mean obliquity of the ecliptic (degrees → radians)
        double eps0 = 23.0
                    + (26.0 + (21.448 - T * (46.8150 + T * (0.00059 - T * 0.001813))) / 60.0) / 60.0;
        double epsR = (eps0 + 0.00256 * Math.Cos(omRad)) * Math.PI / 180.0;

        // Solar declination δ — how far the sun is above/below the celestial equator
        double decl = Math.Asin(Math.Sin(epsR) * Math.Sin(appLonR));

        // Equation of Time (converts between solar and clock time) 
        double e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T); // orbital eccentricity
        double L0rad = L0 * Math.PI / 180.0;
        double varY = Math.Tan(epsR / 2.0) * Math.Tan(epsR / 2.0);

        // Result in degrees, multiply by 4 for minutes
        double eqT = 4.0 * (varY * Math.Sin(2 * L0rad)
                             - 2 * e * Math.Sin(Mrad)
                             + 4 * e * varY * Math.Sin(Mrad) * Math.Cos(2 * L0rad)
                             - 0.5 * varY * varY * Math.Sin(4 * L0rad)
                             - 1.25 * e * e * Math.Sin(2 * Mrad))
                   * (180.0 / Math.PI); // radians → degrees; *4 converts degrees to minutes of time

        // True Solar Time (minutes) 
        // timeMin is UTC minutes-of-day; add longitude correction (4 min/degree) and equation of time
        double timeMin = utc.TimeOfDay.TotalMinutes;
        double trueSolar = (timeMin + 4.0 * longitude + eqT + utcOffsetHours * 60.0) % 1440.0;
        if (trueSolar < 0) trueSolar += 1440.0;

        // Hour Angle H — distance from solar noon in degrees 
        double HA = trueSolar / 4.0 - 180.0; // 0 at noon, ±180 at midnight
        double HArad = HA * Math.PI / 180.0;
        double latR = latitude * Math.PI / 180.0;

        // Solar Zenith Angle 
        double cosZen = Math.Sin(latR) * Math.Sin(decl)
                      + Math.Cos(latR) * Math.Cos(decl) * Math.Cos(HArad);
        cosZen = Math.Max(-1.0, Math.Min(1.0, cosZen)); // clamp to [-1,1] for acos
        double zenRad = Math.Acos(cosZen);
        double zenDeg = zenRad * 180.0 / Math.PI;
        double altDeg = 90.0 - zenDeg;

        // Solar Azimuth (from North, clockwise) 
        double az;
        double sinZen = Math.Sin(zenRad);
        if (sinZen < 1e-9)
        {
            az = 0.0; // sun is at the zenith
        }
        else
        {
            double cosAz = (Math.Sin(latR) * cosZen - Math.Sin(decl)) / (Math.Cos(latR) * sinZen);
            cosAz = Math.Max(-1.0, Math.Min(1.0, cosAz));
            az = Math.Acos(cosAz) * 180.0 / Math.PI;
            if (HA > 0.0) az = 360.0 - az; // afternoon: sun is west of south
        }

        return new SunPosition
        {
            AzimuthDeg = (float)az,
            AltitudeDeg = (float)altDeg,
            BeamLux = ClearSkyBeamLux((float)altDeg)
        };
    }


    // Convert a SunPosition to a Unity world-space unit vector pointing TOWARD the sun.
    // Convention: North = +Z, East = +X, Up = +Y (standard Unity world space).
    public static Vector3 ToWorldDirection(SunPosition sun)
    {
        float azRad = sun.AzimuthDeg * Mathf.Deg2Rad;
        float altRad = sun.AltitudeDeg * Mathf.Deg2Rad;
        float cosAlt = Mathf.Cos(altRad);
        return new Vector3(
             Mathf.Sin(azRad) * cosAlt,   // East  (+X)
             Mathf.Sin(altRad),            // Up    (+Y)
             Mathf.Cos(azRad) * cosAlt    // North (+Z)
        );
    }


    // REFERENCE — Optical air mass (Kasten-Young formula):
    //   Kasten, F., Young, A.T. (1989). "Revised optical air mass tables and approximation formula."
    //   Applied Optics, 28(22), pp. 4735-4738. https://doi.org/10.1364/AO.28.004735

    // REFERENCE — Clear-sky transmittance model (E = E0 * 0.7^(AM^0.678)):
    //   Meinel, A.B., Meinel, M.P. (1976). "Applied Solar Energy: An Introduction."
    //   Addison-Wesley. Further tabulated in: Iqbal, M. (1983). "An Introduction to Solar Radiation."
    //   Academic Press, Chapter 10.

    // REFERENCE — Solar constant 127,500 Lux:
    //   CIE Publication 85 (1989). "Solar spectral irradiance."
    //   Solar constant ~1361 W/m²; luminous efficacy of solar radiation ~93 lm/W → ~127,500 lux.
    //   See also: Darula & Kittler (2002). Building Research Journal, 50(3).

    // Estimated clear-sky beam irradiance (Lux) on a surface facing directly toward the sun.
    // Uses Meinel's transmittance model: E = E0 * 0.7^(AM^0.678)
    // Air mass is computed via the Kasten-Young formula (avoids division-by-zero near horizon).
    private static float ClearSkyBeamLux(float altitudeDeg)
    {
        if (altitudeDeg <= 0f) return 0f;

        // Kasten-Young formula for optical air mass
        float airMass = 1f / (Mathf.Sin(altitudeDeg * Mathf.Deg2Rad)
                             + 0.50572f * Mathf.Pow(altitudeDeg + 6.07995f, -1.6364f));

        // Beam irradiance perpendicular to sun rays
        // E0 = 127,500 Lux (solar constant in luminous flux terms)
        return 127500f * Mathf.Pow(0.7f, Mathf.Pow(airMass, 0.678f));
    }

    // REFERENCE — Julian Day Number formula (Chapter 7, pp. 60-61):
    //   Meeus, J. (1998). "Astronomical Algorithms," 2nd ed.
    //   Willmann-Bell, Richmond VA. ISBN: 978-0943396613

    // Convert a UTC DateTime to Julian Day Number.
    private static double ToJulianDay(DateTime utc)
    {
        int y = utc.Year;
        int m = utc.Month;
        double d = utc.Day + utc.TimeOfDay.TotalSeconds / 86400.0;

        if (m <= 2) { y--; m += 12; }

        int A = y / 100;
        int B = 2 - A + A / 4;

        return Math.Floor(365.25 * (y + 4716))
             + Math.Floor(30.6001 * (m + 1))
             + d + B - 1524.5;
    }
}
