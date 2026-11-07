using UnityEngine;

namespace Apogee.Simulation
{
    /// <summary>
    /// Physical constants. Units used across the vehicle model:
    /// mass in tonnes (t), force in kilonewtons (kN), length in metres, time in seconds.
    /// Because 1 kN = 1 t·m/s², F[kN] / m[t] gives acceleration directly in m/s².
    /// </summary>
    public static class PhysicsConstants
    {
        public const double G0 = 9.80665;                 // standard gravity, m/s²
        public const double EarthRadius = 6371000.0;      // m (mean)
        public const double EarthMu = 3.986004418e14;     // m³/s²
        public const double LeoDeltaV = 9300.0;           // typical ascent to LEO incl. gravity + drag losses, m/s
    }

    /// <summary>
    /// Simple exponential model of Earth's atmosphere (good to a few % below ~80 km).
    /// Used by the builder's performance estimator and, from Phase 2, by flight physics.
    /// </summary>
    public static class EarthAtmosphere
    {
        public const double SeaLevelPressureKPa = 101.325;
        public const double SeaLevelDensity = 1.225;      // kg/m³
        public const double PressureScaleHeight = 7640.0; // m
        public const double DensityScaleHeight = 8500.0;  // m
        public const double Ceiling = 140000.0;           // m — above this we treat it as vacuum

        public static double PressureKPa(double altitude)
        {
            if (altitude >= Ceiling) return 0.0;
            if (altitude < 0) altitude = 0;
            return SeaLevelPressureKPa * System.Math.Exp(-altitude / PressureScaleHeight);
        }

        /// <summary>Pressure as a fraction of sea level (1 at the pad, 0 in vacuum).</summary>
        public static double PressureRatio(double altitude)
        {
            return PressureKPa(altitude) / SeaLevelPressureKPa;
        }

        public static double Density(double altitude)
        {
            if (altitude >= Ceiling) return 0.0;
            if (altitude < 0) altitude = 0;
            return SeaLevelDensity * System.Math.Exp(-altitude / DensityScaleHeight);
        }

        public static double Gravity(double altitude)
        {
            double r = PhysicsConstants.EarthRadius + altitude;
            return PhysicsConstants.EarthMu / (r * r);
        }
    }
}
