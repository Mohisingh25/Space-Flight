using System;
using UnityEngine;

namespace Apogee.Simulation
{
    /// <summary>Double-precision 2D vector for planet-scale positions (metres from the planet's centre).</summary>
    [Serializable]
    public struct Vector2d
    {
        public double x, y;

        public Vector2d(double x, double y) { this.x = x; this.y = y; }

        public static Vector2d zero { get { return new Vector2d(0, 0); } }

        public double magnitude { get { return Math.Sqrt(x * x + y * y); } }
        public double sqrMagnitude { get { return x * x + y * y; } }
        public Vector2d normalized
        {
            get { double m = magnitude; return m > 1e-12 ? new Vector2d(x / m, y / m) : zero; }
        }

        /// <summary>Rotated 90° counter-clockwise.</summary>
        public Vector2d Perp { get { return new Vector2d(-y, x); } }

        public static Vector2d operator +(Vector2d a, Vector2d b) { return new Vector2d(a.x + b.x, a.y + b.y); }
        public static Vector2d operator -(Vector2d a, Vector2d b) { return new Vector2d(a.x - b.x, a.y - b.y); }
        public static Vector2d operator -(Vector2d a) { return new Vector2d(-a.x, -a.y); }
        public static Vector2d operator *(Vector2d a, double d) { return new Vector2d(a.x * d, a.y * d); }
        public static Vector2d operator *(double d, Vector2d a) { return new Vector2d(a.x * d, a.y * d); }
        public static Vector2d operator /(Vector2d a, double d) { return new Vector2d(a.x / d, a.y / d); }

        public static double Dot(Vector2d a, Vector2d b) { return a.x * b.x + a.y * b.y; }
        public static double Cross(Vector2d a, Vector2d b) { return a.x * b.y - a.y * b.x; }

        public static Vector2d FromAngle(double radians) { return new Vector2d(Math.Cos(radians), Math.Sin(radians)); }

        /// <summary>Rotates a local vector by an angle (radians, counter-clockwise).</summary>
        public static Vector2d Rotate(Vector2d v, double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            return new Vector2d(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public Vector3 ToVector3() { return new Vector3((float)x, (float)y, 0f); }

        public override string ToString() { return "(" + x.ToString("0.0") + ", " + y.ToString("0.0") + ")"; }
    }

    /// <summary>Two-body orbit derived from a position/velocity state (2D).</summary>
    public struct Orbit2D
    {
        public double semiMajorAxis, eccentricity, argumentOfPeriapsis;
        public double apoapsis, periapsis;   // radii (m)
        public bool clockwise, bound;

        public static Orbit2D From(Vector2d r, Vector2d v, double mu)
        {
            var o = new Orbit2D();
            double rm = r.magnitude, v2 = v.sqrMagnitude;
            double h = Vector2d.Cross(r, v);
            var e = (r * (v2 - mu / rm) - v * Vector2d.Dot(r, v)) / mu;
            o.eccentricity = e.magnitude;
            double energy = v2 / 2 - mu / rm;
            o.bound = energy < 0;
            o.semiMajorAxis = Math.Abs(energy) > 1e-9 ? -mu / (2 * energy) : double.PositiveInfinity;
            o.argumentOfPeriapsis = o.eccentricity > 1e-9 ? Math.Atan2(e.y, e.x) : Math.Atan2(r.y, r.x);
            o.clockwise = h < 0;
            if (o.bound)
            {
                o.apoapsis = o.semiMajorAxis * (1 + o.eccentricity);
                o.periapsis = o.semiMajorAxis * (1 - o.eccentricity);
            }
            else
            {
                o.periapsis = (h * h / mu) / (1 + o.eccentricity);
                o.apoapsis = double.PositiveInfinity;
            }
            return o;
        }

        /// <summary>Radius at a true anomaly.</summary>
        public double RadiusAt(double trueAnomaly, double mu, double h2)
        {
            return (h2 / mu) / (1 + eccentricity * Math.Cos(trueAnomaly));
        }
    }
}
