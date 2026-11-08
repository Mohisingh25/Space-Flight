using System;

namespace Apogee.Simulation
{
    /// <summary>
    /// Keplerian orbit around one body (two-body problem), built from a state vector and propagated
    /// analytically to any time. Handles circular, elliptic and hyperbolic orbits in 3D.
    /// Frame: body-centred, non-rotating, Unity axes (y = north of the ecliptic).
    /// </summary>
    public class Orbit
    {
        public double mu;
        public double a;         // semi-major axis (negative for hyperbolic)
        public double e;         // eccentricity
        public double p;         // semi-latus rectum
        public double epoch;     // time of the state the orbit was built from
        public double M0;        // mean anomaly (or hyperbolic mean anomaly) at epoch
        public Vector3d P, Q;    // perifocal unit vectors: towards periapsis, and 90° ahead in the orbital plane
        public Vector3d H;       // angular momentum unit vector
        public double hMag;
        public bool radial;      // degenerate straight-line orbit (falls straight down / up)
        Vector3d r0, v0;

        public Orbit(double mu, Vector3d r, Vector3d v, double time)
        {
            this.mu = mu;
            epoch = time;
            r0 = r;
            v0 = v;
            var h = Vector3d.Cross(r, v);
            hMag = h.magnitude;
            double rm = r.magnitude, v2 = v.sqrMagnitude;
            var eVec = (Vector3d.Cross(v, h) / mu) - r / rm;
            e = eVec.magnitude;
            double energy = v2 / 2 - mu / rm;
            a = Math.Abs(energy) > 1e-12 ? -mu / (2 * energy) : 1e30;
            p = hMag * hMag / mu;
            radial = hMag < 1e-6 * rm * Math.Sqrt(v2 + 1e-9) || hMag < 1e-3;
            if (radial) { H = Vector3d.up; P = r.normalized; Q = Vector3d.Cross(H, P).normalized; return; }
            H = h / hMag;
            P = e > 1e-9 ? eVec / e : r / rm;
            Q = Vector3d.Cross(H, P);
            double nu = Math.Atan2(Vector3d.Dot(r, Q), Vector3d.Dot(r, P));
            M0 = MeanFromTrue(nu);
        }

        public bool Hyperbolic { get { return e >= 1.0; } }
        public double Periapsis { get { return p / (1 + e); } }
        public double Apoapsis { get { return e < 1 ? p / (1 - e) : double.PositiveInfinity; } }
        public double Period { get { return e < 1 ? 2 * Math.PI * Math.Sqrt(a * a * a / mu) : double.PositiveInfinity; } }
        public double MeanMotion { get { return Math.Sqrt(mu / Math.Abs(a * a * a)); } }
        public double Inclination { get { return Math.Acos(Math.Max(-1, Math.Min(1, H.y))); } }

        double MeanFromTrue(double nu)
        {
            if (e < 1)
            {
                double E = 2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
                return E - e * Math.Sin(E);
            }
            double F = 2 * Atanh(Math.Sqrt((e - 1) / (e + 1)) * Math.Tan(nu / 2));
            return e * Math.Sinh(F) - F;
        }

        static double Atanh(double x) { x = Math.Max(-0.999999999999, Math.Min(0.999999999999, x)); return 0.5 * Math.Log((1 + x) / (1 - x)); }

        public double TrueAnomalyAt(double t)
        {
            double M = M0 + MeanMotion * (t - epoch);
            if (e < 1)
            {
                M = M % (2 * Math.PI);
                double E = e < 0.8 ? M : Math.PI;
                for (int i = 0; i < 40; i++)
                {
                    double f = E - e * Math.Sin(E) - M;
                    double d = f / (1 - e * Math.Cos(E));
                    E -= d;
                    if (Math.Abs(d) < 1e-13) break;
                }
                return 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2));
            }
            double F = Math.Asinh(M / e);
            for (int i = 0; i < 60; i++)
            {
                double f = e * Math.Sinh(F) - F - M;
                double d = f / (e * Math.Cosh(F) - 1);
                F -= d;
                if (Math.Abs(d) < 1e-13) break;
            }
            return 2 * Math.Atan(Math.Sqrt((e + 1) / (e - 1)) * Math.Tanh(F / 2));
        }

        public Vector3d PositionAtTrue(double nu)
        {
            double r = p / (1 + e * Math.Cos(nu));
            return (P * Math.Cos(nu) + Q * Math.Sin(nu)) * r;
        }

        public Vector3d VelocityAtTrue(double nu)
        {
            double k = Math.Sqrt(mu / p);
            return (P * -Math.Sin(nu) + Q * (e + Math.Cos(nu))) * k;
        }

        /// <summary>State at time t. Radial (no angular momentum) trajectories are integrated numerically.</summary>
        public void StateAt(double t, out Vector3d r, out Vector3d v)
        {
            if (radial)
            {
                r = r0; v = v0;
                double dt = t - epoch, step = Math.Sign(dt) * Math.Max(0.5, Math.Abs(dt) / 2000);
                for (double s = 0; Math.Abs(s) < Math.Abs(dt) - 1e-9;)
                {
                    double h = Math.Abs(dt - s) < Math.Abs(step) ? dt - s : step;
                    var g = r * (-mu / Math.Pow(Math.Max(1, r.sqrMagnitude), 1.5));
                    v = v + g * h;
                    r = r + v * h;
                    s += h;
                }
                return;
            }
            double nu = TrueAnomalyAt(t);
            r = PositionAtTrue(nu);
            v = VelocityAtTrue(nu);
        }

        public Vector3d PositionAt(double t) { Vector3d r, v; StateAt(t, out r, out v); return r; }

        /// <summary>Time from 'from' until the orbit next reaches radius 'radius' (outbound if 'outbound'), or -1.</summary>
        public double TimeToRadius(double radius, double from, bool outbound)
        {
            if (radial) return -1;
            double cosNu = (p / radius - 1) / e;
            if (e < 1e-9 || cosNu > 1 || cosNu < -1) return -1;
            double nu = Math.Acos(cosNu);
            if (!outbound) nu = -nu;
            double M = MeanFromTrue(nu);
            double n = MeanMotion;
            double Mnow = M0 + n * (from - epoch);
            double dM = M - Mnow;
            if (e < 1) { dM %= 2 * Math.PI; if (dM < 0) dM += 2 * Math.PI; }
            else if (dM < 0) return -1;
            return dM / n;
        }
    }
}
