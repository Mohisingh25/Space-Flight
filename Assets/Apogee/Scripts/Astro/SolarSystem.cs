using System;
using System.Collections.Generic;
using Apogee.Simulation;
using UnityEngine;

namespace Apogee.Astro
{
    /// <summary>Exponential atmosphere with a hard top (Earth uses the detailed standard-atmosphere model).</summary>
    public class AtmosphereModel
    {
        public double height;        // top of the atmosphere, m
        public double p0;            // surface pressure, Pa
        public double rho0;          // surface density, kg/m³
        public double scaleHeight;   // m
        public double heatK = 1.83e-4; // Sutton–Graves constant for the gas mix
        public Color skyColor = new Color(0.4f, 0.64f, 0.95f);
        public bool earth;

        public double Density(double alt)
        {
            if (alt >= height) return 0;
            if (earth) return EarthAtmosphere.Density(Math.Max(0, alt));
            return rho0 * Math.Exp(-Math.Max(0, alt) / scaleHeight);
        }

        public double Pressure(double alt)
        {
            if (alt >= height) return 0;
            if (earth) return EarthAtmosphere.PressureKPa(Math.Max(0, alt)) * 1000.0;
            return p0 * Math.Exp(-Math.Max(0, alt) / scaleHeight);
        }

        /// <summary>Pressure relative to Earth sea level (what engine Isp curves are rated against).</summary>
        public double PressureAtm(double alt) { return Pressure(alt) / 101325.0; }
    }

    /// <summary>A star, planet or moon moving on fixed Keplerian rails around its parent.</summary>
    public class CelestialBody
    {
        public string name;
        public CelestialBody parent;
        public readonly List<CelestialBody> children = new List<CelestialBody>();
        public double mu, radius, rotationPeriod, soi, rotation0;
        public AtmosphereModel atmosphere;
        public string texture;           // Resources/Planets/<texture>
        public Color tint = Color.white;  // used for bodies without their own map
        public Color atmoColor = new Color(0.35f, 0.6f, 1f);
        public bool star, gasGiant, hasRings;
        public string description;
        public int index;
        public Orbit orbit;

        double cacheT = double.NaN;
        Vector3d cacheR, cacheV;

        public bool HasAtmosphere { get { return atmosphere != null; } }
        public double SurfaceGravity { get { return mu / (radius * radius); } }

        /// <summary>Position and velocity relative to the parent at time t.</summary>
        public void LocalState(double t, out Vector3d r, out Vector3d v)
        {
            if (orbit == null) { r = v = Vector3d.zero; return; }
            if (t != cacheT) { orbit.StateAt(t, out cacheR, out cacheV); cacheT = t; }
            r = cacheR; v = cacheV;
        }

        public Vector3d LocalPosition(double t) { Vector3d r, v; LocalState(t, out r, out v); return r; }

        /// <summary>Heliocentric position.</summary>
        public Vector3d AbsolutePosition(double t)
        {
            var p = Vector3d.zero;
            for (var b = this; b != null && b.orbit != null; b = b.parent) p = p + b.LocalPosition(t);
            return p;
        }

        public Vector3d AbsoluteVelocity(double t)
        {
            var vel = Vector3d.zero;
            for (var b = this; b != null && b.orbit != null; b = b.parent) { Vector3d r, v; b.LocalState(t, out r, out v); vel = vel + v; }
            return vel;
        }

        /// <summary>Spin angle (rad) about the body's north (+y) axis; positive = eastward.</summary>
        public double SpinAngle(double t) { return rotationPeriod == 0 ? 0 : rotation0 + 2 * Math.PI * t / rotationPeriod; }

        /// <summary>Unity rotation of the body at time t (east = counter-clockwise seen from north).</summary>
        public Quaternion Spin(double t) { return Quaternion.Euler(0f, (float)(-SpinAngle(t) * Mathf.Rad2Deg % 360.0), 0f); }

        public Vector3d SpinVector { get { return rotationPeriod == 0 ? Vector3d.zero : new Vector3d(0, -2 * Math.PI / rotationPeriod, 0); } }

        /// <summary>Velocity of the surface / co-rotating air at body-relative position r (Unity handedness).</summary>
        public Vector3d SurfaceVelocity(Vector3d r)
        {
            if (rotationPeriod == 0) return Vector3d.zero;
            double w = 2 * Math.PI / rotationPeriod;
            // eastward = counter-clockwise from north: d/dt (cos a, sin a) = w(-sin a, cos a) in (x, z)
            return new Vector3d(-r.z * w, 0, r.x * w);
        }

        /// <summary>Body-fixed direction for a latitude/longitude (degrees) at time t.</summary>
        public Vector3d SurfaceDirection(double latDeg, double lonDeg, double t)
        {
            double lat = latDeg * Math.PI / 180, lon = lonDeg * Math.PI / 180 + SpinAngle(t);
            return new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
        }

        /// <summary>Body-fixed unit vector (no spin applied) for a latitude/longitude — the frame position is Spin(t) × this.</summary>
        public Vector3d FixedDirection(double latDeg, double lonDeg)
        {
            double lat = latDeg * Math.PI / 180, lon = lonDeg * Math.PI / 180;
            return new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
        }

        public void LatLon(Vector3d r, double t, out double latDeg, out double lonDeg)
        {
            var n = r.normalized;
            latDeg = Math.Asin(Math.Max(-1, Math.Min(1, n.y))) * 180 / Math.PI;
            double lon = Math.Atan2(n.z, n.x) - SpinAngle(t);
            lon = lon % (2 * Math.PI);
            if (lon > Math.PI) lon -= 2 * Math.PI;
            if (lon < -Math.PI) lon += 2 * Math.PI;
            lonDeg = lon * 180 / Math.PI;
        }

        /// <summary>Terrain height above the reference sphere (m). Gentle procedural relief on airless worlds; Earth's launch region is flat.</summary>
        public double TerrainHeight(Vector3d bodyFixedDir)
        {
            if (star || gasGiant) return 0;
            if (name == "Earth") return 0;
            double h = 0, amp = radius * 0.0012, f = 3.0;
            for (int o = 0; o < 4; o++)
            {
                h += amp * (Math.Sin(bodyFixedDir.x * f + o * 1.7) * Math.Sin(bodyFixedDir.y * f * 1.3 + o) * Math.Sin(bodyFixedDir.z * f * 0.9 + o * 2.3));
                amp *= 0.45; f *= 2.3;
            }
            return Math.Max(0, h);
        }

        public override string ToString() { return name; }
    }

    /// <summary>A named place to land (historic landing sites, the launch complex).</summary>
    public class LandingSite
    {
        public string name;
        public string body;
        public double lat, lon;
        public LandingSite(string name, string body, double lat, double lon) { this.name = name; this.body = body; this.lat = lat; this.lon = lon; }

        public static readonly List<LandingSite> All = new List<LandingSite>
        {
            new LandingSite("Equatorial Launch Complex", "Earth", 0.0, -51.07),
            new LandingSite("Tranquility Base (Apollo 11)", "Moon", 0.674, 23.473),
            new LandingSite("Shackleton crater rim (south pole)", "Moon", -89.67, -129.2),
            new LandingSite("Copernicus crater", "Moon", 9.62, -20.08),
            new LandingSite("Jezero Crater (Perseverance)", "Mars", 18.38, 77.58),
            new LandingSite("Gale Crater (Curiosity)", "Mars", -5.4, 137.8),
            new LandingSite("Huygens landing site", "Titan", -10.25, 192.3 - 360.0),
        };

        /// <summary>Great-circle distance (m) and initial bearing (deg from north) from (lat1,lon1) to this site on a body of radius R.</summary>
        public void DistanceFrom(double lat1, double lon1, double R, out double dist, out double bearing)
        {
            double p1 = lat1 * Math.PI / 180, p2 = lat * Math.PI / 180, dl = (lon - lon1) * Math.PI / 180;
            double c = Math.Sin(p1) * Math.Sin(p2) + Math.Cos(p1) * Math.Cos(p2) * Math.Cos(dl);
            dist = R * Math.Acos(Math.Max(-1, Math.Min(1, c)));
            bearing = (Math.Atan2(Math.Sin(dl) * Math.Cos(p2), Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl)) * 180 / Math.PI + 360) % 360;
        }

        public static LandingSite Nearest(CelestialBody b, double lat, double lon, out double dist, out double bearing)
        {
            LandingSite best = null; dist = double.MaxValue; bearing = 0;
            foreach (var s in All)
            {
                if (s.body != b.name) continue;
                double d, br;
                s.DistanceFrom(lat, lon, b.radius, out d, out br);
                if (d < dist) { dist = d; bearing = br; best = s; }
            }
            return best;
        }
    }

    /// <summary>
    /// The real Solar System at real scale: the Sun, eight planets and the largest moons, on Keplerian
    /// orbits from J2000 mean elements. Game time 0 = 8 Nov 2026 (so planets are roughly where they are today).
    /// </summary>
    public static class SolarSystem
    {
        public const double AU = 1.495978707e11;
        public const double DaysFromJ2000 = 9807.5;
        public static readonly List<CelestialBody> Bodies = new List<CelestialBody>();
        public static CelestialBody Sun, Earth, Moon, Mars;
        static bool built;

        public static CelestialBody Get(string name)
        {
            Build();
            foreach (var b in Bodies) if (b.name == name) return b;
            return null;
        }

        public static void Build()
        {
            if (built) return;
            built = true;
            Sun = Add(new CelestialBody
            {
                name = "Sun", mu = 1.32712440018e20, radius = 6.957e8, rotationPeriod = 2192832, star = true, texture = "sun",
                description = "Our star. Nothing survives a landing here."
            }, null);
            Sun.soi = double.PositiveInfinity;

            // name, a(AU), e, i, Ω, ϖ (longitude of perihelion), L (mean longitude) [deg], mu, radius(km), sidereal day(s)
            var mercury = Planet("Mercury", 0.387098, 0.205630, 7.005, 48.331, 77.456, 252.251, 2.2032e13, 2439.7, 5067014, "mercury");
            var venus = Planet("Venus", 0.723332, 0.006772, 3.39458, 76.680, 131.533, 181.980, 3.24859e14, 6051.8, -20997360, "venus");
            venus.atmosphere = new AtmosphereModel { height = 250000, p0 = 9.2e6, rho0 = 65, scaleHeight = 15900, heatK = 1.9e-4, skyColor = new Color(0.85f, 0.72f, 0.45f) };
            venus.atmoColor = new Color(0.95f, 0.8f, 0.5f);
            Earth = Planet("Earth", 1.000000, 0.016709, 0.0, -11.26064, 102.947, 100.464, 3.986004418e14, 6371.0, 86164.0905, "earth");
            Earth.atmosphere = new AtmosphereModel { height = 140000, p0 = 101325, rho0 = 1.225, scaleHeight = 8500, earth = true };
            Mars = Planet("Mars", 1.523679, 0.0934, 1.850, 49.558, 336.04, 355.453, 4.282837e13, 3389.5, 88642.66, "mars");
            Mars.atmosphere = new AtmosphereModel { height = 125000, p0 = 610, rho0 = 0.020, scaleHeight = 11100, heatK = 1.9e-4, skyColor = new Color(0.78f, 0.6f, 0.45f) };
            Mars.atmoColor = new Color(0.85f, 0.55f, 0.35f);
            var jupiter = Planet("Jupiter", 5.2026, 0.0484, 1.303, 100.464, 14.331, 34.351, 1.26686534e17, 69911, 35730, "jupiter");
            jupiter.gasGiant = true;
            jupiter.atmosphere = new AtmosphereModel { height = 1000000, p0 = 1e7, rho0 = 0.16, scaleHeight = 27000, heatK = 6e-5, skyColor = new Color(0.75f, 0.65f, 0.5f) };
            var saturn = Planet("Saturn", 9.5549, 0.0555, 2.489, 113.666, 93.057, 50.077, 3.7931187e16, 58232, 38362, "saturn");
            saturn.gasGiant = true; saturn.hasRings = true;
            saturn.atmosphere = new AtmosphereModel { height = 1000000, p0 = 1e7, rho0 = 0.19, scaleHeight = 59500, heatK = 6e-5, skyColor = new Color(0.8f, 0.72f, 0.55f) };
            var uranus = Planet("Uranus", 19.2184, 0.0463, 0.773, 74.006, 173.005, 314.055, 5.793939e15, 25362, -62064, "uranus");
            uranus.gasGiant = true;
            uranus.atmosphere = new AtmosphereModel { height = 1000000, p0 = 1e7, rho0 = 0.42, scaleHeight = 27700, heatK = 6e-5, skyColor = new Color(0.6f, 0.85f, 0.9f) };
            var neptune = Planet("Neptune", 30.110, 0.0090, 1.770, 131.784, 48.124, 304.349, 6.836529e15, 24622, 57996, "neptune");
            neptune.gasGiant = true;
            neptune.atmosphere = new AtmosphereModel { height = 1000000, p0 = 1e7, rho0 = 0.45, scaleHeight = 19700, heatK = 6e-5, skyColor = new Color(0.4f, 0.55f, 0.95f) };
            mercury.description = "Smallest planet, closest to the Sun. Airless; a 3 km/s landing.";
            venus.description = "Crushing 92-bar CO₂ atmosphere at 460 °C.";
            Earth.description = "Home. Launch site on the equator.";
            Mars.description = "Thin CO₂ air: parachutes help, but you'll still need engines to land.";
            jupiter.description = "Gas giant. No surface — the 'surface' altitude is where the pressure crushes your craft.";
            saturn.description = "Gas giant with spectacular rings.";
            uranus.description = "Ice giant, rolling on its side.";
            neptune.description = "The windiest world, 30 AU out.";

            // Moons: name, parent, a(km), e, i(deg), mu, radius(km), start angle(deg); tidally locked
            Moon = MoonOf("Moon", Earth, 384400, 0.0549, 5.145, 4.9048695e12, 1737.4, 125, "moon", Color.white);
            Moon.description = "Airless, 1/6 g. 384,000 km away — about 3 days on a transfer orbit.";
            MoonOf("Phobos", Mars, 9376, 0.0151, 1.08, 7.087e5, 11.27, 30, "moon", new Color(0.55f, 0.5f, 0.45f)).description = "Tiny potato-shaped moon of Mars.";
            MoonOf("Deimos", Mars, 23463, 0.0003, 1.79, 9.8e4, 6.2, 200, "moon", new Color(0.6f, 0.55f, 0.5f)).description = "Mars' smaller, outer moon.";
            MoonOf("Io", jupiter, 421700, 0.0041, 0.05, 5.959916e12, 1821.6, 10, "moon", new Color(1.0f, 0.88f, 0.45f)).description = "Volcanic sulphur world.";
            MoonOf("Europa", jupiter, 671034, 0.009, 0.47, 3.202739e12, 1560.8, 100, "moon", new Color(0.92f, 0.86f, 0.78f)).description = "Ice shell over a hidden ocean.";
            MoonOf("Ganymede", jupiter, 1070412, 0.0013, 0.2, 9.887834e12, 2634.1, 200, "moon", new Color(0.7f, 0.66f, 0.6f)).description = "Largest moon in the Solar System.";
            MoonOf("Callisto", jupiter, 1882709, 0.0074, 0.19, 7.179289e12, 2410.3, 300, "moon", new Color(0.45f, 0.42f, 0.38f)).description = "Ancient, heavily cratered.";
            var titan = MoonOf("Titan", saturn, 1221870, 0.0288, 0.35, 8.978e12, 2574.7, 50, "moon", new Color(0.85f, 0.62f, 0.3f));
            titan.atmosphere = new AtmosphereModel { height = 600000, p0 = 146700, rho0 = 5.4, scaleHeight = 40000, heatK = 1.7e-4, skyColor = new Color(0.85f, 0.6f, 0.3f) };
            titan.atmoColor = new Color(0.9f, 0.6f, 0.25f);
            titan.description = "Thick orange haze: you can glide down under a parachute.";

            foreach (var b in Bodies)
            {
                if (b.parent == null) continue;
                double m = b.mu, M = b.parent.mu;
                b.soi = b.orbit.a * Math.Pow(m / M, 0.4);
            }
        }

        static CelestialBody Add(CelestialBody b, CelestialBody parent)
        {
            b.parent = parent;
            b.index = Bodies.Count;
            if (parent != null) parent.children.Add(b);
            Bodies.Add(b);
            return b;
        }

        /// <summary>Converts right-handed ecliptic coordinates (z = north) to Unity's frame (y = north).</summary>
        static Vector3d FromEcliptic(double X, double Y, double Z) { return new Vector3d(X, Z, Y); }

        static Orbit OrbitFromElements(double mu, double a, double e, double iDeg, double lanDeg, double argPeDeg, double meanAnomalyDeg)
        {
            double d2r = Math.PI / 180;
            double i = iDeg * d2r, O = lanDeg * d2r, w = argPeDeg * d2r, M = meanAnomalyDeg * d2r;
            double E = M;
            for (int k = 0; k < 50; k++) E -= (E - e * Math.Sin(E) - M) / (1 - e * Math.Cos(E));
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2));
            double p = a * (1 - e * e), r = p / (1 + e * Math.Cos(nu));
            double xp = r * Math.Cos(nu), yp = r * Math.Sin(nu);
            double vxp = -Math.Sqrt(mu / p) * Math.Sin(nu), vyp = Math.Sqrt(mu / p) * (e + Math.Cos(nu));
            double cO = Math.Cos(O), sO = Math.Sin(O), cw = Math.Cos(w), sw = Math.Sin(w), ci = Math.Cos(i), si = Math.Sin(i);
            double r11 = cO * cw - sO * sw * ci, r12 = -cO * sw - sO * cw * ci;
            double r21 = sO * cw + cO * sw * ci, r22 = -sO * sw + cO * cw * ci;
            double r31 = sw * si, r32 = cw * si;
            var pos = FromEcliptic(r11 * xp + r12 * yp, r21 * xp + r22 * yp, r31 * xp + r32 * yp);
            var vel = FromEcliptic(r11 * vxp + r12 * vyp, r21 * vxp + r22 * vyp, r31 * vxp + r32 * vyp);
            return new Orbit(mu, pos, vel, 0);
        }

        static CelestialBody Planet(string name, double aAU, double e, double i, double lan, double lonPeri, double meanLon, double mu, double rKm, double day, string tex)
        {
            var b = Add(new CelestialBody { name = name, mu = mu, radius = rKm * 1000, rotationPeriod = day, texture = tex }, Sun);
            double a = aAU * AU;
            double n = Math.Sqrt(Sun.mu / (a * a * a)) * 86400 * 180 / Math.PI; // deg/day
            double meanAnomaly = (meanLon - lonPeri + n * DaysFromJ2000) % 360;
            b.orbit = OrbitFromElements(Sun.mu + mu, a, e, i, lan, lonPeri - lan, meanAnomaly);
            b.rotation0 = (meanLon * 7.3) % 360 * Math.PI / 180;
            return b;
        }

        static CelestialBody MoonOf(string name, CelestialBody parent, double aKm, double e, double i, double mu, double rKm, double startDeg, string tex, Color tint)
        {
            var b = Add(new CelestialBody { name = name, mu = mu, radius = rKm * 1000, texture = tex, tint = tint }, parent);
            b.orbit = OrbitFromElements(parent.mu + mu, aKm * 1000, e, i, 0, 0, startDeg);
            b.rotationPeriod = b.orbit.Period; // tidally locked
            b.rotation0 = startDeg * Math.PI / 180 + Math.PI;
            return b;
        }
    }
}
