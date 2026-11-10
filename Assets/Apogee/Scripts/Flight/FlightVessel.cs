using System;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rocket;
using Apogee.Simulation;
using Apogee.Astro;
using UnityEngine;

namespace Apogee.Flight
{
    public enum FlightStatus { Rollout, OnPad, Flying, Landed, Crashed }

    public enum SasMode { Off, Stability, Prograde, Retrograde, RadialOut, RadialIn, Normal, AntiNormal, Target, AntiTarget }

    /// <summary>Optional cheats (Settings → Cheats). All off by default; tests run with real physics.</summary>
    public class FlightCheats
    {
        public bool infiniteFuel, noGravity, noDrag, unbreakable, superThrust, noHeat, infinitePower, noGLimit;
        public bool Any { get { return infiniteFuel || noGravity || noDrag || unbreakable || superThrust || noHeat || infinitePower || noGLimit; } }
    }

    /// <summary>A separated piece: falls, drags and burns up or crashes on its own.</summary>
    public class Debris
    {
        public CelestialBody body;
        public Vector3d pos, vel;
        public Quaternion rot;
        public Vector3 spin;          // rad/s, body frame
        public HashSet<int> parts;
        public Vector3 comLocal;
        public float mass;
        public double cdA;
        public bool dead, impacted, landed;
        public Vector3d landedFixed;
        public double age;
    }

    /// <summary>
    /// Full 3D flight simulation of one vehicle in the real Solar System (patched conics):
    /// every engine pushes from where it is mounted (off-centre engines twist the craft), Isp varies with
    /// ambient pressure, propellant drains through crossfeed groups, drag and weathervaning come from the
    /// shape, re-entry heating raises each part's temperature (parts melt above their limit), joints break
    /// when loads exceed their strength, parachutes, landing legs, RCS, SAS, staging into debris, landing
    /// and crashing on any body, and sphere-of-influence changes between planets and moons.
    /// Pure C#: the flight scene renders it and the tests run it headless.
    /// </summary>
    public class FlightVessel
    {
        public const double G0 = 9.80665;
        public const double PadTop = 1.5;
        const double Sigma = 5.670374e-8;

        // ---- blueprint & parts
        public readonly RocketBlueprint bp;
        public readonly VehicleState state;
        public Dictionary<int, PartPose> poses;
        Dictionary<int, int> fuelGroups;

        // ---- orbital state (body-centred, non-rotating frame, Unity axes)
        public CelestialBody body;
        public Vector3d pos, vel;
        public Quaternion rot = Quaternion.identity;   // vessel local → frame
        public Vector3 angVel;                         // rad/s, vessel local axes
        public double time;
        public FlightStatus status = FlightStatus.OnPad;
        public Orbit orbit;
        bool orbitValid;

        // Surface attachment (pad, landed): body-fixed position and attitude.
        Vector3d fixedPos;
        Quaternion fixedRot;

        // ---- mass properties (vessel local, root part at origin, +y = nose)
        public float mass, length, maxRadius;
        public Vector3 comLocal;
        public Vector3 inertia = Vector3.one;          // t·m² about x (pitch), y (roll), z (yaw)
        public float lowY;
        public bool hasLegs;
        public float reactionTorque, rcsThrust, rcsTorque;
        float cdAxial, areaFront, areaSide, copY, finArea;
        public float chuteScaleSum;
        readonly List<Vector3> supports = new List<Vector3>();

        // ---- controls
        public float throttle = 1f;
        public bool enginesOn;
        public float pitchInput, yawInput, rollInput;   // -1..1
        public SasMode sas = SasMode.Stability;
        public CelestialBody target;
        public bool rcsOn;
        public Vector3 rcsInput;                        // x right, y nose, z "top" (vessel local), -1..1
        public bool rcsFiring;
        public int nextStage;
        public bool chuteArmed;
        public bool holdDown;
        public Vector3 gimbalCmd;

        // ---- electrical system (kWh / kW)
        public double charge = -1, capacity, powerIn, powerOut;
        public bool sunlit = true, powerOut0;
        bool lowPowerWarned;        // -1..1 per axis: how hard the engines are steering (x pitch, z yaw)           // launch clamps hold the rocket until T-0
        public float chuteOpen;
        public FlightCheats cheats = new FlightCheats();
        public Station station;                         // space station to rendezvous and dock with (Station mode)

        public bool HasCommand
        {
            get { foreach (int u in state.alive) if (state.byUid[u].Def.isCommand) return true; return false; }
        }

        // ---- per-part state
        public readonly HashSet<int> engineOff = new HashSet<int>();
        public readonly HashSet<int> folded = new HashSet<int>();
        public readonly HashSet<int> chutesDeployed = new HashSet<int>();
        public readonly Dictionary<int, float> temperature = new Dictionary<int, float>();
        public readonly Dictionary<int, float> engineOutput = new Dictionary<int, float>();
        public readonly Dictionary<int, float> spool = new Dictionary<int, float>();        // 0..1 turbopump spool-up per engine
        public readonly Dictionary<int, float> thrustLimit = new Dictionary<int, float>();  // per-engine thrust limiter 0.2..1

        public float ThrustLimit(int uid) { float l; return thrustLimit.TryGetValue(uid, out l) ? l : 1f; }
        public readonly Dictionary<int, float> rcsOutput = new Dictionary<int, float>();
        readonly Dictionary<int, double> poolFlow = new Dictionary<int, double>();
        readonly Dictionary<int, double> engineFlow = new Dictionary<int, double>();

        // ---- fuel transfer between tanks (tap one tank, then another)
        public int transferPick = -1, transferFrom = -1, transferTo = -1;
        public float transferRate;   // t/s currently pumping

        // ---- telemetry
        public double lastAcceleration, dynamicPressure, heatFlux, machLike;
        public float maxTempFraction;
        public Vector3d airVelocity;
        public readonly List<Debris> debris = new List<Debris>();

        public event Action<string> OnMessage;
        public event Action<Debris> OnDebris;
        public event Action<Vector3d, float> OnExplosion;   // frame position, size
        public event Action OnCrash, OnLanded;
        public event Action<CelestialBody> OnSoiChange;

        // =====================================================================================
        public FlightVessel(RocketBlueprint blueprint) : this(blueprint, null, 0, 0) { }

        /// <summary>Creates the vessel standing on 'site' (default: Earth's equatorial launch complex, facing the Sun).</summary>
        public FlightVessel(RocketBlueprint blueprint, CelestialBody site, double lat, double lon)
        {
            SolarSystem.Build();
            bp = blueprint;
            RocketAssembly.AutoTaper(bp);
            if (!bp.customStaging || bp.stageCount == 0) StagingSystem.Restage(bp);
            state = new VehicleState(bp, 0f);
            poses = RocketLayout.Compute(bp);
            fuelGroups = state.FuelGroups();
            foreach (var p in bp.parts) if (p.Def.solarKW > 0f) folded.Add(p.uid);
            foreach (var p in bp.parts) temperature[p.uid] = 290f;
            Recalc();
            body = site ?? SolarSystem.Earth;
            if (site == null) lon = SunFacingLongitude(body, 0);
            PlaceOnSurface(lat, lon, PadTop);
        }

        /// <summary>Longitude (deg) where it is noon at time t — the default launch site, so first launches are in daylight.</summary>
        public static double SunFacingLongitude(CelestialBody b, double t)
        {
            var toSun = (-b.AbsolutePosition(t)).normalized;
            double lon = Math.Atan2(toSun.z, toSun.x) - b.SpinAngle(t);
            return lon * 180 / Math.PI;
        }

        public double LaunchLat { get; private set; }
        public double LaunchLon { get; private set; }

        /// <summary>Stands the vessel upright on the surface at lat/lon with its lowest point 'height' above the ground.</summary>
        public void PlaceOnSurface(double lat, double lon, double height)
        {
            LaunchLat = lat; LaunchLon = lon;
            var dir = body.FixedDirection(lat, lon);           // body-fixed (spin angle 0)
            var up = dir.ToFloat();
            double ground = body.radius + body.TerrainHeight(dir);
            fixedPos = dir * (ground + height + (comLocal.y - lowY));
            // Vessel +y up, +z pointing north (so the camera's default view looks east along the surface).
            var north = Vector3.ProjectOnPlane(Vector3.up, up);
            if (north.sqrMagnitude < 1e-6f) north = Vector3.forward;
            fixedRot = Quaternion.LookRotation(north.normalized, up);
            SyncToSurface();
        }

        /// <summary>Raises/lowers a grounded vessel (rollout: crawler → pad), keeping it upright at the same spot.</summary>
        public void SetSurfaceHeight(double lat, double lon, double height) { PlaceOnSurface(lat, lon, height); }

        /// <summary>Offsets the grounded vessel 'east' metres east of the given site (crawler rollout).</summary>
        public void PlaceEastOf(double lat, double lon, double east, double height)
        {
            double dLon = east / (body.radius * Math.Max(0.1, Math.Cos(lat * Math.PI / 180))) * 180 / Math.PI;
            PlaceOnSurface(lat, lon + dLon, height);
            LaunchLon = lon;
        }

        /// <summary>Puts the vessel in a circular equatorial orbit (cheat start), nose pointing prograde.</summary>
        public void PlaceInOrbit(CelestialBody b, double altitude)
        {
            body = b;
            double r = b.radius + altitude;
            pos = new Vector3d(r, 0, 0);
            vel = new Vector3d(0, 0, Math.Sqrt(b.mu / r));
            rot = Quaternion.LookRotation(Vector3.right, Vector3.forward);
            angVel = Vector3.zero;
            status = FlightStatus.Flying;
            orbitValid = false;
        }

        void SyncToSurface()
        {
            var spin = body.Spin(time);
            pos = Vector3d.Rotate(spin, fixedPos);
            rot = spin * fixedRot;
            vel = body.SurfaceVelocity(pos);
            angVel = Vector3.zero;
            orbitValid = false;
        }

        void CaptureSurface()
        {
            var inv = Quaternion.Inverse(body.Spin(time));
            fixedPos = Vector3d.Rotate(inv, pos);
            fixedRot = inv * rot;
        }

        // =====================================================================================
        // Mass properties

        PartPose Pose(int uid) { PartPose p; poses.TryGetValue(uid, out p); return p; }

        public static float MaxTemp(PartDefinition d)
        {
            if (d.shape == PartShape.HeatShield) return 3300f;
            if (d.IsEngine) return 2100f;
            if (d.shape == PartShape.NoseCone || d.isFairing) return 1700f;
            if (d.isCommand && d.crew > 0) return 1350f;
            if (d.isParachute) return 900f;
            if (d.solarKW > 0f || d.shape == PartShape.Antenna) return 900f;
            return 1100f;
        }

        /// <summary>Sustained acceleration a part survives (g). Full tanks buckle under their own propellant's weight.</summary>
        public static float MaxG(PartDefinition d)
        {
            if (d.isCommand && d.crew > 0) return 25f;
            if (d.fuelMass > 0f && !d.solidFuel && !d.isRcs) return 25f;
            if (d.isCommand) return 40f;
            if (d.IsEngine) return 60f;
            if (d.solarKW > 0f || d.shape == PartShape.Antenna) return 12f;
            return 30f;
        }

        float overG;

        public void Recalc()
        {
            float m = 0f;
            var com = Vector3.zero;
            float minY = float.MaxValue, maxY = float.MinValue;
            maxRadius = 0.3f;
            hasLegs = false;
            reactionTorque = 3f;
            rcsThrust = 0f;
            rcsTorque = 0f;
            chuteScaleSum = 0f;
            finArea = 0f;
            float topY = float.MinValue;
            cdAxial = 0.8f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                var d = p.Def;
                var pose = Pose(u);
                var dims = PartDims.Of(p);
                float fm;
                state.fuel.TryGetValue(u, out fm);
                float pm = PartMass.Dry(p) + fm + (d.isPayload ? p.payloadMass : 0f);
                m += pm;
                com += pose.position * pm;
                bool stack = RocketAssembly.CanStack(d) && !d.radialMount;
                bool support = (d.isLeg && !folded.Contains(u)) || d.isWheel;
                float bottom = pose.position.y - dims.height * 0.5f, top = pose.position.y + dims.height * 0.5f;
                if (d.isWheel) bottom = pose.position.y - dims.height * 0.675f;
                if (stack || support) { minY = Mathf.Min(minY, bottom); maxY = Mathf.Max(maxY, top); }
                if (stack) maxRadius = Mathf.Max(maxRadius, Mathf.Abs(pose.position.x) + dims.Radius, Mathf.Abs(pose.position.z) + dims.Radius);
                if (stack && top > topY) { topY = top; cdAxial = d.dragCoefficient > 0f ? d.dragCoefficient : 0.8f; }
                if (support) hasLegs = true;
                if (d.isParachute && chutesDeployed.Contains(u)) chuteScaleSum += d.chuteScale;
                if (d.isCommand) reactionTorque += d.crew > 0 ? 6f * dims.diameter : 4f;
                if (d.shape == PartShape.NavComputer) reactionTorque += 12f;
                if (d.isRcs) rcsThrust += d.thrustVac;
                finArea += d.finArea;
            }
            if (station != null && station.docked)
            {
                var sc = station.dockOffset + station.dockRot * station.com;
                m += station.mass;
                com += sc * station.mass;
            }
            if (m <= 0f) m = 0.001f;
            mass = m;
            comLocal = com / m;
            if (minY == float.MaxValue) { minY = -1f; maxY = 1f; }
            lowY = minY;
            length = maxY - minY;

            // inertia, aerodynamic areas and centre of pressure, RCS lever arms, support points
            var I = Vector3.zero;
            float sideSum = 0f, sideY = 0f;
            supports.Clear();
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                var d = p.Def;
                var pose = Pose(u);
                var dims = PartDims.Of(p);
                float fm; state.fuel.TryGetValue(u, out fm);
                float pm = PartMass.Dry(p) + fm + (d.isPayload ? p.payloadMass : 0f);
                var rp = pose.position - comLocal;
                float r = dims.Radius, h = dims.height;
                I.x += pm * (rp.y * rp.y + rp.z * rp.z) + pm * (3 * r * r + h * h) / 12f;
                I.y += pm * (rp.x * rp.x + rp.z * rp.z) + pm * r * r * 0.5f;
                I.z += pm * (rp.x * rp.x + rp.y * rp.y) + pm * (3 * r * r + h * h) / 12f;
                bool stack = RocketAssembly.CanStack(d) && !d.radialMount;
                float lateral = stack ? 2f * r * h : 0f;
                if (d.finArea > 0f) lateral += d.finArea * 6f;
                if (d.solarKW > 0f && !folded.Contains(u)) lateral += 1.5f;
                sideSum += lateral;
                sideY += lateral * pose.position.y;
                if (d.isRcs) rcsTorque += d.thrustVac * (Mathf.Abs(rp.y) + maxRadius);
                // contact points
                if (stack)
                {
                    float by = pose.position.y - h * 0.5f;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI * 0.5f;
                        supports.Add(new Vector3(pose.position.x + Mathf.Cos(a) * r * 0.9f, by, pose.position.z + Mathf.Sin(a) * r * 0.9f));
                    }
                }
                if (d.isLeg && !folded.Contains(u)) supports.Add(pose.position + pose.rotation * new Vector3(0.95f * h / 2f, -h * 0.5f, 0f));
                if (d.isWheel) supports.Add(pose.position + pose.rotation * new Vector3(0.18f + h * 0.375f, -h * 0.675f, 0f));
            }
            childMap.Clear();
            foreach (int u in state.alive)
            {
                int par = state.byUid[u].parentUid;
                if (par < 0 || !state.alive.Contains(par)) continue;
                List<int> l;
                if (!childMap.TryGetValue(par, out l)) { l = new List<int>(); childMap[par] = l; }
                l.Add(u);
            }
            if (station != null && station.docked)
            {
                var rs = station.dockOffset + station.dockRot * station.com - comLocal;
                float sm = station.mass, own = sm * 12f;   // station's own inertia (~ 6 m radius of gyration)
                I.x += sm * (rs.y * rs.y + rs.z * rs.z) + own;
                I.y += sm * (rs.x * rs.x + rs.z * rs.z) + own;
                I.z += sm * (rs.x * rs.x + rs.y * rs.y) + own;
            }
            inertia = new Vector3(Mathf.Max(0.05f, I.x), Mathf.Max(0.02f, I.y), Mathf.Max(0.05f, I.z));
            copY = sideSum > 0f ? sideY / sideSum : comLocal.y;
            areaFront = Mathf.PI * maxRadius * maxRadius;
            areaSide = Mathf.Max(0.5f, sideSum);
            orbitValid = false;
        }

        // =====================================================================================
        // Derived values

        public double Radius { get { return pos.magnitude; } }
        public double Altitude { get { return pos.magnitude - body.radius; } }
        public Vector3d Up { get { return pos.normalized; } }
        public Vector3d Forward { get { return Vector3d.Rotate(rot, Vector3d.up); } }
        public double TerrainAltitude
        {
            get
            {
                var inv = Quaternion.Inverse(body.Spin(time));
                var dir = Vector3d.Rotate(inv, pos).normalized;
                return Altitude - body.TerrainHeight(dir) - (comLocal.y - lowY);
            }
        }
        public Vector3d SurfaceVelocity { get { return vel - body.SurfaceVelocity(pos); } }
        public double VerticalSpeed { get { return Vector3d.Dot(vel, Up); } }
        public double HorizontalSurfaceSpeed
        {
            get { var sv = SurfaceVelocity; return (sv - Up * Vector3d.Dot(sv, Up)).magnitude; }
        }
        public bool Grounded { get { return status == FlightStatus.OnPad || status == FlightStatus.Landed || status == FlightStatus.Rollout; } }
        public bool InAtmosphere { get { return body.HasAtmosphere && Altitude < body.atmosphere.height; } }

        public Orbit CurrentOrbit
        {
            get
            {
                if (!orbitValid || orbit == null) { orbit = new Orbit(body.mu, pos, vel, time); orbitValid = true; }
                return orbit;
            }
        }

        public double Apoapsis { get { var o = CurrentOrbit; return o.e < 1 ? o.Apoapsis - body.radius : double.PositiveInfinity; } }
        public double Periapsis { get { return CurrentOrbit.Periapsis - body.radius; } }

        /// <summary>Tilt from local vertical (deg).</summary>
        public double Tilt { get { return Vector3d.Angle(Forward, Up) * 180 / Math.PI; } }

        public float LiquidFuelFraction()
        {
            float cur = 0f, max = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                if (p.Def.solidFuel || p.Def.isRcs || p.Def.monoprop) continue;
                float cap = PartMass.Fuel(p);
                if (cap <= 0f) continue;
                max += cap;
                float f; state.fuel.TryGetValue(u, out f); cur += f;
            }
            return max > 0f ? cur / max : 0f;
        }

        public float SolidFuelFraction()
        {
            float cur = 0f, max = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                if (!p.Def.solidFuel) continue;
                max += PartMass.Fuel(p);
                float f; state.fuel.TryGetValue(u, out f); cur += f;
            }
            return max > 0f ? cur / max : -1f;
        }

        public float RcsFuelFraction()
        {
            float cur = 0f, max = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                if (!p.Def.isRcs && !p.Def.monoprop) continue;
                max += PartMass.Fuel(p);
                float f; if (state.fuel.TryGetValue(u, out f)) cur += f;
            }
            return max > 0f ? cur / max : -1f;
        }

        /// <summary>Speed of sound (m/s) at an altitude: Earth's standard atmosphere temperature profile, generic elsewhere.</summary>
        public static double SpeedOfSound(CelestialBody b, double alt)
        {
            if (b == null || !b.HasAtmosphere) return 0;
            double T;
            if (b.name == "Earth")
            {
                double km = alt / 1000.0;
                if (km < 11) T = 288.15 - 6.5 * km;
                else if (km < 20) T = 216.65;
                else if (km < 32) T = 216.65 + (km - 20);
                else if (km < 47) T = 228.65 + 2.8 * (km - 32);
                else if (km < 51) T = 270.65;
                else if (km < 71) T = 270.65 - 2.8 * (km - 51);
                else T = Math.Max(186.9, 214.65 - 2.0 * (km - 71));
                return Math.Sqrt(1.4 * 287.05 * T);
            }
            if (b.name == "Mars") return 240;
            if (b.name == "Venus") return 410;
            if (b.name == "Titan") return 195;
            return 600;
        }

        public double Mach
        {
            get
            {
                double a = SpeedOfSound(body, Math.Max(0, Altitude));
                return a > 0 && InAtmosphere ? SurfaceVelocity.magnitude / a : 0;
            }
        }

        /// <summary>Main engines in a stable order: by stage, then around the rocket. Number = index + 1.</summary>
        public List<int> EngineList()
        {
            var list = new List<int>();
            foreach (var p in bp.parts) if (p.Def.IsEngine && !p.Def.isRcs) list.Add(p.uid);
            list.Sort((x, y) =>
            {
                var a = state.byUid[x]; var b = state.byUid[y];
                int sa = a.stage < 0 ? 999 : a.stage, sb = b.stage < 0 ? 999 : b.stage;
                if (sa != sb) return sa.CompareTo(sb);
                var pa = Pose(x).position; var pb = Pose(y).position;
                float aa = Mathf.Atan2(pa.z, pa.x), ab = Mathf.Atan2(pb.z, pb.x);
                if (Mathf.Abs(pa.x) + Mathf.Abs(pa.z) < 0.05f) aa = -10f;
                if (Mathf.Abs(pb.x) + Mathf.Abs(pb.z) < 0.05f) ab = -10f;
                if (Mathf.Abs(aa - ab) > 1e-3f) return aa.CompareTo(ab);
                return x.CompareTo(y);
            });
            return list;
        }

        public enum EngineState { Ready, Running, Off, NoFuel, Gone }

        public EngineState GetEngineState(int uid)
        {
            if (!state.alive.Contains(uid)) return EngineState.Gone;
            if (state.PoolFuel(state.PoolKey(uid, fuelGroups), fuelGroups) <= 1e-5f) return EngineState.NoFuel;
            if (!state.ignited.Contains(uid)) return EngineState.Ready;
            return engineOff.Contains(uid) ? EngineState.Off : EngineState.Running;
        }

        /// <summary>Starts or stops one engine (solid motors can only be lit).</summary>
        public string SetEngine(int uid, bool on)
        {
            if (!state.alive.Contains(uid) || status == FlightStatus.Crashed) return null;
            var d = state.byUid[uid].Def;
            if (on)
            {
                state.ignited.Add(uid);
                engineOff.Remove(uid);
                enginesOn = true;
                return d.displayName + " on";
            }
            if (d.solidFuel && state.ignited.Contains(uid)) return "Solid boosters can't be shut down";
            if (state.ignited.Contains(uid)) engineOff.Add(uid);
            return d.displayName + " off";
        }

        public bool HasRcs
        {
            get { foreach (int u in state.alive) if (state.byUid[u].Def.isRcs) return true; return false; }
        }

        public bool StagesRemaining { get { return nextStage < bp.stageCount; } }

        /// <summary>Δv left in the currently lit engines' tanks (vacuum Isp), m/s.</summary>
        public double StageDeltaV()
        {
            double fuel = 0, ispSum = 0, thrSum = 0;
            var pools = new HashSet<int>();
            foreach (int u in state.ignited)
            {
                if (!state.alive.Contains(u) || engineOff.Contains(u)) continue;
                var d = state.byUid[u].Def;
                int key = state.PoolKey(u, fuelGroups);
                if (pools.Add(key)) fuel += state.PoolFuel(key, fuelGroups);
                ispSum += d.thrustVac; thrSum += d.thrustVac / d.ispVac;
            }
            if (thrSum <= 0 || fuel <= 0) return 0;
            double isp = ispSum / thrSum;
            return isp * G0 * Math.Log(mass / Math.Max(0.001, mass - fuel));
        }

        public double MaxThrustNow()
        {
            double F = 0, pr = body.HasAtmosphere ? body.atmosphere.PressureAtm(Math.Max(0, Altitude)) : 0;
            foreach (int u in state.alive)
            {
                var d = state.byUid[u].Def;
                if (!d.IsEngine || d.isRcs || engineOff.Contains(u)) continue;
                if (!state.ignited.Contains(u)) continue;
                if (state.PoolFuel(state.PoolKey(u, fuelGroups), fuelGroups) <= 1e-5f) continue;
                F += d.thrustVac * (d.solidFuel ? 1f : ThrustLimit(u)) * Math.Max(0.05, (d.ispVac + (d.ispSL - d.ispVac) * pr) / d.ispVac);
            }
            return F;
        }

        // =====================================================================================
        // Staging & part actions

        public void Stage()
        {
            if (status == FlightStatus.Crashed || nextStage >= bp.stageCount) return;
            var log = new List<string>();
            var oldCom = comLocal;
            var groups = state.ApplyStageCollect(nextStage, log);
            foreach (var p in bp.parts)
            {
                if (p.stage != nextStage) continue;
                if (p.Def.isParachute && state.alive.Contains(p.uid)) chutesDeployed.Add(p.uid);
                if (p.Def.IsEngine) engineOff.Remove(p.uid);
            }
            nextStage++;
            if (log.Contains("Ignition")) enginesOn = true;
            if (log.Contains("Parachute")) chuteArmed = true;
            SpawnDebris(groups, oldCom, 0.4);
            Say(log.Count > 0 ? string.Join(" · ", log.ToArray()) : "Stage " + nextStage);
        }

        void Say(string s) { if (OnMessage != null) OnMessage(s); }

        /// <summary>Turns separated groups into debris (pushed apart gently) and re-centres the vehicle.</summary>
        void SpawnDebris(List<HashSet<int>> groups, Vector3 oldCom, double separationSpeed)
        {
            fuelGroups = state.FuelGroups();
            Recalc();
            foreach (var g in groups)
            {
                if (g.Count == 0) continue;
                float dm = 0f;
                var dc = Vector3.zero;
                foreach (int u in g)
                {
                    var p = state.byUid[u];
                    float fm; state.fuel.TryGetValue(u, out fm);
                    float pm = PartMass.Dry(p) + fm + (p.Def.isPayload ? p.payloadMass : 0f);
                    dm += pm;
                    dc += Pose(u).position * pm;
                }
                if (dm <= 0f) dm = 0.01f;
                dc /= dm;
                var away = (dc - comLocal).normalized;
                var piece = new Debris
                {
                    body = body,
                    pos = pos + Vector3d.Rotate(rot, (Vector3d)(dc - oldCom)),
                    vel = vel + Vector3d.Rotate(rot, (Vector3d)away) * separationSpeed,
                    rot = rot,
                    spin = angVel + new Vector3(UnityEngine.Random.Range(-0.05f, 0.05f), 0f, UnityEngine.Random.Range(-0.05f, 0.05f)),
                    parts = g,
                    comLocal = dc,
                    mass = dm,
                    cdA = 0.8 * Math.PI * 1.5 * 1.5
                };
                if (Grounded) { piece.landed = true; piece.landedFixed = Vector3d.Rotate(Quaternion.Inverse(body.Spin(time)), piece.pos); }
                debris.Add(piece);
                if (OnDebris != null) OnDebris(piece);
            }
            var shift = comLocal - oldCom;
            pos = pos + Vector3d.Rotate(rot, (Vector3d)shift);
            if (Grounded) { CaptureSurface(); }
            orbitValid = false;
        }

        /// <summary>
        /// Tap a part in flight: engines start/stop individually, legs/solar arrays fold, chutes deploy,
        /// decouplers / docking ports / fairings separate, RCS blocks toggle RCS. Returns a HUD message.
        /// </summary>
        public string TapPart(int uid)
        {
            if (status == FlightStatus.Crashed || !state.alive.Contains(uid)) return null;
            var p = state.byUid[uid];
            var d = p.Def;
            if (station != null && station.docked && station.dockedPortUid == uid) return Undock();
            if (d.IsEngine && !d.isRcs)
            {
                if (!state.ignited.Contains(uid))
                {
                    state.ignited.Add(uid);
                    engineOff.Remove(uid);
                    enginesOn = true;
                    if (state.PoolFuel(state.PoolKey(uid, fuelGroups), fuelGroups) <= 1e-5f) return d.displayName + ": no fuel reaches this engine";
                    return d.displayName + " ignited";
                }
                if (d.solidFuel) return "Solid boosters burn until empty — they can't be shut down";
                if (engineOff.Remove(uid)) { enginesOn = true; return d.displayName + " on"; }
                engineOff.Add(uid);
                return d.displayName + " off";
            }
            if (d.Foldable)
            {
                bool fold = !folded.Contains(uid);
                foreach (var m in bp.SymmetryMembers(p)) { if (fold) folded.Add(m.uid); else folded.Remove(m.uid); }
                Recalc();
                if (Grounded) Reseat();
                if (d.isLeg) return fold ? "Landing legs retracting" : "Landing legs deploying";
                return fold ? "Solar arrays stowed" : "Solar arrays deployed";
            }
            if (d.isParachute)
            {
                if (chutesDeployed.Contains(uid)) return "Parachute already deployed";
                foreach (var m in bp.SymmetryMembers(p)) if (state.alive.Contains(m.uid)) chutesDeployed.Add(m.uid);
                chuteArmed = true;
                Recalc();
                return InAtmosphere ? "Parachute deployed" : "Parachute armed — opens in the atmosphere";
            }
            if (d.isDecoupler || d.isDockingPort || d.isFairing)
            {
                var oldCom = comLocal;
                HashSet<int> dropped = d.isFairing ? new HashSet<int> { uid } : StagingSystem.Jettisoned(bp, uid, state.alive, state.controlUid);
                if (dropped.Count == 0) return null;
                if (dropped.Contains(state.controlUid)) return "That would cut off the command part";
                foreach (int u in dropped) state.alive.Remove(u);
                SpawnDebris(new List<HashSet<int>> { dropped }, oldCom, d.isDockingPort ? 0.3 : 0.6);
                return d.isDockingPort ? "Undocked" : d.isFairing ? "Fairing jettisoned" : "Separated";
            }
            if (d.isRcs) { rcsOn = !rcsOn; return rcsOn ? "RCS on" : "RCS off"; }
            if (d.fuelMass > 0f && !d.solidFuel) return TapTank(uid);
            float fuel; state.fuel.TryGetValue(uid, out fuel);
            float cap = PartMass.Fuel(p);
            float temp; temperature.TryGetValue(uid, out temp);
            return d.displayName + "  ·  " + PartDefinition.FormatMass(PartMass.Dry(p) + fuel) +
                (cap > 0f ? "  ·  fuel " + Mathf.RoundToInt(100f * fuel / cap) + "%" : "") + "  ·  " + Mathf.RoundToInt(temp - 273f) + " °C";
        }

        /// <summary>True when the body blocks the line from the vessel to the Sun (orbital night).</summary>
        public bool InShadow()
        {
            var sunRel = -(body.AbsolutePosition(time) + pos);   // vessel → Sun
            var dir = sunRel.normalized;
            double along = Vector3d.Dot(-pos, dir);               // closest approach of the ray to the body's centre
            if (along <= 0) return false;
            var closest = pos + dir * along;
            return closest.magnitude < body.radius;
        }

        /// <summary>
        /// Batteries charge from deployed solar arrays (scaled by 1/distance² from the Sun, nothing in the shadow
        /// of a planet) and RTGs; flight computers, life support and reaction wheels drain them.
        /// </summary>
        void UpdatePower(double dt)
        {
            capacity = 0;
            double solar = 0, gen = 0, load = 0;
            foreach (int u in state.alive)
            {
                var d = state.byUid[u].Def;
                capacity += d.batteryKWh;
                if (d.solarKW > 0f && !folded.Contains(u)) solar += d.solarKW;
                gen += d.generatorKW;
                if (d.isCommand) load += d.crew > 0 ? 0.15 * d.crew + 0.1 : 0.04;
            }
            if (charge < 0) charge = capacity;
            sunlit = !InShadow();
            double au = (body.AbsolutePosition(time) + pos).magnitude / SolarSystem.AU;
            powerIn = (sunlit ? solar / Math.Max(0.05, au * au) : 0) + gen;
            // reaction wheels and SAS draw power while they work
            load += (Mathf.Abs(gimbalCmd.x) + Mathf.Abs(gimbalCmd.y) + Mathf.Abs(gimbalCmd.z)) * 0.25 * reactionTorque / 10.0 + (sas != SasMode.Off ? 0.02 : 0);
            powerOut = load;
            charge = Math.Max(0, Math.Min(capacity, charge + (powerIn - powerOut) * dt / 3600.0));
            if (cheats.infinitePower) charge = Math.Max(capacity, 1);
            bool dead = !cheats.infinitePower && (capacity <= 0 || charge <= 1e-6);
            if (dead && !powerOut0) Say("Batteries flat — SAS and reaction wheels offline. Unfold solar panels or add batteries/RTG.");
            if (!dead && charge / Math.Max(1e-6, capacity) < 0.1 && !lowPowerWarned) { lowPowerWarned = true; Say("Low power: batteries below 10%"); }
            if (charge / Math.Max(1e-6, capacity) > 0.2) lowPowerWarned = false;
            powerOut0 = dead;
        }

        public bool HasPower { get { return !powerOut0; } }

        bool SameFuel(int a, int b)
        {
            var da = state.byUid[a].Def; var db = state.byUid[b].Def;
            return da.monoprop == db.monoprop && da.isRcs == db.isRcs && !da.solidFuel && !db.solidFuel;
        }

        string TankInfo(int uid)
        {
            var p = state.byUid[uid];
            float cap = PartMass.Fuel(p);
            return p.Def.displayName + " " + Mathf.RoundToInt(100f * Fuel(uid) / Mathf.Max(1e-5f, cap)) + "% (" + Fuel(uid).ToString("0.0") + " t)";
        }

        /// <summary>Tap a tank to pick it, tap a second tank to pump fuel from the first into it; tap again to stop.</summary>
        string TapTank(int uid)
        {
            if (transferFrom >= 0 && (uid == transferFrom || uid == transferTo))
            {
                transferFrom = transferTo = -1; transferPick = -1; transferRate = 0f;
                return "Fuel transfer stopped";
            }
            if (transferPick < 0 || !state.alive.Contains(transferPick))
            {
                transferPick = uid;
                return "Selected " + TankInfo(uid) + " — tap another tank to pump fuel into it";
            }
            if (transferPick == uid) { transferPick = -1; return "Tank deselected"; }
            if (!SameFuel(transferPick, uid)) { transferPick = -1; return "Those tanks hold different propellants"; }
            if (Fuel(transferPick) <= 1e-4f) { transferPick = -1; return "That tank is empty"; }
            transferFrom = transferPick;
            transferTo = uid;
            transferPick = -1;
            return "Pumping fuel: " + state.byUid[transferFrom].Def.displayName + " → " + state.byUid[transferTo].Def.displayName + " (tap either to stop)";
        }

        void UpdateTransfer(double dt)
        {
            transferRate = 0f;
            if (transferFrom < 0) return;
            if (!state.alive.Contains(transferFrom) || !state.alive.Contains(transferTo)) { transferFrom = transferTo = -1; return; }
            float capTo = PartMass.Fuel(state.byUid[transferTo]);
            float room = capTo - Fuel(transferTo);
            float rate = Mathf.Max(0.2f, capTo * 0.04f);                 // pumps: ~4% of the receiving tank per second
            float amount = Mathf.Min((float)(rate * dt), Mathf.Min(Fuel(transferFrom), room));
            if (amount <= 1e-6f)
            {
                Say(room <= 1e-4f ? "Transfer complete — receiving tank full" : "Transfer complete — source tank empty");
                transferFrom = transferTo = -1;
                return;
            }
            state.fuel[transferFrom] -= amount;
            state.fuel[transferTo] = Fuel(transferTo) + amount;
            transferRate = rate;
            Recalc();
        }

        void Reseat()
        {
            double minH = double.MaxValue;
            foreach (var s in supports)
            {
                var wp = pos + Vector3d.Rotate(rot, (Vector3d)(s - comLocal));
                minH = Math.Min(minH, wp.magnitude - GroundRadius(wp));
            }
            if (minH == double.MaxValue) return;
            double target = status == FlightStatus.OnPad ? PadTop : 0;
            pos = pos + Up * (target - minH);
            CaptureSurface();
        }

        double GroundRadius(Vector3d framePos)
        {
            var dir = Vector3d.Rotate(Quaternion.Inverse(body.Spin(time)), framePos).normalized;
            return body.radius + body.TerrainHeight(dir);
        }

        // =====================================================================================
        // Simulation step

        /// <summary>True when nothing but gravity acts (coasting in vacuum): time warp can run on rails.</summary>
        public bool CanRails
        {
            get
            {
                if (Grounded) return true;
                if (status != FlightStatus.Flying) return true;
                if (InAtmosphere) return false;
                if (rcsFiring || pitchInput != 0 || yawInput != 0 || rollInput != 0) return false;
                if (enginesOn && throttle > 0.001f && AnyEngineBurning()) return false;
                foreach (int u in state.ignited) if (state.alive.Contains(u) && state.byUid[u].Def.solidFuel && Fuel(u) > 0) return false;
                return true;
            }
        }

        float Fuel(int u) { float f; state.fuel.TryGetValue(u, out f); return f; }

        bool AnySpooling() { foreach (var v in spool.Values) if (v > 1e-3f) return true; return false; }

        bool AnyEngineBurning()
        {
            foreach (int u in state.ignited)
            {
                if (!state.alive.Contains(u) || engineOff.Contains(u)) continue;
                var d = state.byUid[u].Def;
                if (!d.solidFuel && (!enginesOn || throttle <= 0.001f || ThrustLimit(u) <= 0f)) continue;
                if (state.PoolFuel(state.PoolKey(u, fuelGroups), fuelGroups) > 1e-5f) return true;
            }
            return false;
        }

        /// <summary>Advances time by dt seconds. Uses full physics when anything acts, Kepler rails otherwise.</summary>
        public void Advance(double dt)
        {
            UpdateTransfer(dt);
            UpdatePower(dt);
            if (status == FlightStatus.Crashed) { time += dt; StepDebris(dt); if (station != null) station.Propagate(time); return; }
            if (Grounded && !(enginesOn && AnyEngineBurning() && status != FlightStatus.Rollout) && !AnySpooling())
            {
                engineOutput.Clear();
                spool.Clear();
                time += dt;
                SyncToSurface();
                StepDebris(dt);
                if (station != null) { station.Propagate(time); SyncStation(); }
                return;
            }
            if (CanRails && status == FlightStatus.Flying && dt > 0.05)
            {
                // let engines finish winding down with real physics, then go on rails
                while (AnySpooling() && dt > 0.02 && status == FlightStatus.Flying) { Step(0.02); dt -= 0.02; }
                engineOutput.Clear();
                spool.Clear();
                Rails(dt);
                StepDebris(dt);
                if (station != null) { station.Propagate(time); SyncStation(); }
                return;
            }
            double rem = dt;
            while (rem > 1e-9)
            {
                double h = Math.Min(0.02, rem);
                Step(h);
                rem -= h;
                if (status == FlightStatus.Crashed) break;
            }
            SyncStation();
            StepDebris(dt);
        }

        /// <summary>Propagates the orbit analytically, sub-stepping to catch sphere-of-influence changes and atmosphere entry.</summary>
        void Rails(double dt)
        {
            double rem = dt;
            int guard = 0;
            while (rem > 1e-6 && guard++ < 20000)
            {
                var o = CurrentOrbit;
                // time until the next event that needs physics: entering the atmosphere / nearing the ground
                // (only when actually descending) or crossing a sphere-of-influence boundary
                double tEvent = double.MaxValue;
                double r = pos.magnitude, vr = Vector3d.Dot(vel, pos) / r;
                double floor = body.radius + (body.HasAtmosphere ? body.atmosphere.height : 2000);
                if (vr < 0) tEvent = Math.Min(tEvent, (r - floor) / -vr);
                if (body.parent != null && vr > 0) tEvent = Math.Min(tEvent, (body.soi - r) / vr);
                foreach (var c in body.children)
                {
                    Vector3d cr, cv;
                    c.LocalState(time, out cr, out cv);
                    var rel = pos - cr;
                    double closing = -Vector3d.Dot(vel - cv, rel) / rel.magnitude;
                    double gap = rel.magnitude - c.soi;
                    tEvent = Math.Min(tEvent, closing > 0 ? gap / closing : gap / ((vel - cv).magnitude + 1) * 4);
                }
                double h = Math.Min(rem, Math.Max(1.0, 0.3 * tEvent));
                time += h;
                rem -= h;
                o.StateAt(time, out pos, out vel);
                rot = rot * Quaternion.Euler(angVel * (float)(h * Mathf.Rad2Deg));
                CheckSoi();
                double fl = body.radius + (body.HasAtmosphere ? body.atmosphere.height : 2000);
                if (pos.magnitude < fl) break; // entering atmosphere/near ground: hand back to physics
            }
            if (rem > 1e-6) { double r = rem; while (r > 1e-9) { double hh = Math.Min(0.02, r); Step(hh); r -= hh; } }
        }

        void CheckSoi()
        {
            if (body.parent != null && pos.magnitude > body.soi)
            {
                Vector3d br, bv;
                body.LocalState(time, out br, out bv);
                pos = pos + br;
                vel = vel + bv;
                var old = body;
                body = body.parent;
                orbitValid = false;
                Say("Leaving " + old.name + "'s sphere of influence — now orbiting " + body.name);
                if (OnSoiChange != null) OnSoiChange(body);
                return;
            }
            foreach (var c in body.children)
            {
                Vector3d cr, cv;
                c.LocalState(time, out cr, out cv);
                if ((pos - cr).magnitude < c.soi)
                {
                    pos = pos - cr;
                    vel = vel - cv;
                    body = c;
                    orbitValid = false;
                    Say("Entering " + c.name + "'s sphere of influence");
                    if (OnSoiChange != null) OnSoiChange(body);
                    return;
                }
            }
        }

        public void Step(double dt)
        {
            if (status == FlightStatus.Crashed) return;
            time += dt;
            orbitValid = false;
            double rm = pos.magnitude, alt = rm - body.radius;
            var atmo = body.atmosphere;
            double pr = atmo != null ? atmo.PressureAtm(Math.Max(0, alt)) : 0;
            double rho = atmo != null ? atmo.Density(Math.Max(0, alt)) : 0;
            var vAir = vel - body.SurfaceVelocity(pos);
            airVelocity = vAir;
            double speed = vAir.magnitude;
            var invRot = Quaternion.Inverse(rot);

            // ---------------- engines
            var thrustBody = Vector3.zero;     // kN, vessel local
            var torqueBody = Vector3.zero;     // kN·m, vessel local
            double gimbalTorque = 0;
            engineOutput.Clear();
            poolFlow.Clear();
            engineFlow.Clear();
            bool burning = false;
            double tMul = cheats.superThrust ? 5.0 : 1.0;
            foreach (int u in state.ignited)
            {
                if (!state.alive.Contains(u)) continue;
                var d = state.byUid[u].Def;
                if (d.isRcs) continue;
                int key = state.PoolKey(u, fuelGroups);
                bool fed = state.PoolFuel(key, fuelGroups) > 1e-5f;
                float want = !fed || engineOff.Contains(u) ? 0f : d.solidFuel ? 1f : (enginesOn && throttle > 0.001f ? Mathf.Max(throttle, d.minThrottle) : 0f);
                if (!d.solidFuel) want *= ThrustLimit(u);
                // Real engines take ~2 s to spool up through their start sequence and a fraction of a second to cut off.
                float sp; spool.TryGetValue(u, out sp);
                float rate = d.solidFuel ? 20f : want > sp ? 0.55f : 7f;
                sp = Mathf.MoveTowards(sp, want, (float)(rate * dt));
                spool[u] = sp;
                if (sp <= 1e-3f || !fed) continue;
                float thr = sp;
                double flow = d.thrustVac / (d.ispVac * G0) * thr;
                double isp = Math.Max(d.ispVac * 0.05, d.ispVac + (d.ispSL - d.ispVac) * pr);
                double Fi = flow * isp * G0 * tMul;
                double cur; poolFlow.TryGetValue(key, out cur); poolFlow[key] = cur + flow;
                engineFlow[u] = flow;
                var pose = Pose(u);
                var dir = pose.rotation * Vector3.up;
                var f = dir * (float)Fi;
                thrustBody += f;
                var lever = pose.position - comLocal;
                torqueBody += Vector3.Cross(lever, f);
                gimbalTorque += Fi * Math.Sin(d.gimbalRange * Mathf.Deg2Rad) * Math.Max(0.5, Math.Abs(lever.y));
                engineOutput[u] = thr;
                burning = true;
            }
            if (!cheats.infiniteFuel)
                foreach (var kv in engineFlow) state.DrainFrom(kv.Key, (float)(kv.Value * dt), fuelGroups);

            // ---------------- RCS translation
            rcsFiring = false;
            rcsOutput.Clear();
            if (rcsOn && rcsInput.sqrMagnitude > 1e-4f && rcsThrust > 0f && RcsFuelFraction() > 0f)
            {
                var want = Vector3.ClampMagnitude(rcsInput, 1f);
                float F = rcsThrust * 0.5f * want.magnitude * (float)tMul;
                thrustBody += want.normalized * F;
                rcsFiring = true;
                ConsumeRcs(F, dt);
                foreach (int u in state.alive) if (state.byUid[u].Def.isRcs) rcsOutput[u] = want.magnitude;
            }

            // ---------------- aerodynamics
            var dragBody = Vector3.zero;
            dynamicPressure = 0.5 * rho * speed * speed;
            if (speed > 0.5 && rho > 0 && !cheats.noDrag)
            {
                var vb = (Vector3)Vector3d.Rotate(invRot, vAir / speed).ToFloat();
                float cosA = vb.y, sin2 = 1f - cosA * cosA;
                float cdFront = cosA >= 0f ? cdAxial : 1.15f;   // tail-first: blunt engine end
                double cdA = cdFront * areaFront * cosA * cosA + 1.0f * areaSide * sin2;
                // parachutes: drogue first, main canopy once slow enough
                if (chuteArmed && chuteScaleSum > 0f)
                {
                    if (speed < 320 && dynamicPressure < 40000) chuteOpen = Mathf.Min(1f, chuteOpen + (float)dt * 0.35f);
                    cdA += chuteScaleSum * (1.0 * Math.PI * 2.5 * 2.5 + chuteOpen * 1.3 * Math.PI * 100.0);
                }
                var drag = -vb * (float)(dynamicPressure * cdA / 1000.0);   // kN
                dragBody += drag;
                // side force acts at the centre of pressure (fins at the back make it stable)
                var side = drag - Vector3.up * Vector3.Dot(drag, Vector3.up);
                float chuteArm = chuteOpen > 0.05f ? 0f : 1f;
                torqueBody += Vector3.Cross(new Vector3(0f, (copY - comLocal.y) * chuteArm, 0f), side);
                if (chuteOpen > 0.05f)
                {
                    // chutes hang above the capsule: strong stabilising torque towards nose-up into the flow
                    torqueBody += Vector3.Cross(new Vector3(0f, length * 0.6f, 0f), -vb * (float)(dynamicPressure * chuteScaleSum * 20.0 / 1000.0));
                }
            }

            // ---------------- heating
            UpdateHeating(dt, rho, speed, invRot, vAir, atmo);
            if (status == FlightStatus.Crashed) return;

            // ---------------- forces → acceleration
            var forceBody = thrustBody + dragBody;
            var accFrame = Vector3d.Rotate(rot, (Vector3d)forceBody) / mass;  // kN / t = m/s²
            lastAcceleration = accFrame.magnitude;
            var gravity = cheats.noGravity ? Vector3d.zero : pos * (-body.mu / (rm * rm * rm));

            // ---------------- attitude control
            ControlAttitude(dt, torqueBody, gimbalTorque, rho, speed);

            // ---------------- g-load: sustained high acceleration crushes the weakest part
            if (!cheats.unbreakable && !cheats.noGLimit)
            {
                double gNow = forceBody.magnitude / mass / G0;
                int weakest = -1; float lim = float.MaxValue;
                foreach (int u in state.alive)
                {
                    var pd = state.byUid[u].Def;
                    float m = MaxG(pd);
                    // propellant head pressure is what buckles a tank: an emptying tank tolerates far more g
                    if (pd.fuelMass > 0f && !pd.solidFuel && !pd.isRcs)
                        m += 35f * (1f - Mathf.Clamp01(Fuel(u) / Mathf.Max(1e-4f, PartMass.Fuel(state.byUid[u]))));
                    if (m < lim) { lim = m; weakest = u; }
                }
                if (gNow > lim) overG += (float)dt; else overG = 0f;
                if (overG > 1.5f && weakest >= 0)
                {
                    overG = 0f;
                    DestroyPart(weakest, state.byUid[weakest].Def.displayName + " crushed at " + gNow.ToString("0") + " g (limit " + lim.ToString("0") + " g)");
                    if (status == FlightStatus.Crashed) return;
                }
            }

            // ---------------- structure
            CheckStructure(forceBody, dt);
            if (status == FlightStatus.Crashed) return;

            if (burning || rcsFiring) Recalc();

            // ---------------- grounded: hold-down clamps / landed until thrust lifts us
            if (Grounded)
            {
                var upF = Vector3d.Dot(accFrame + gravity, Up);
                if (upF > 0.05 && status != FlightStatus.Rollout && !holdDown)
                {
                    status = FlightStatus.Flying;
                    Say(body == SolarSystem.Earth && time < 3600 * 6 ? "Liftoff!" : "Lift-off from " + body.name);
                }
                else { SyncToSurface(); return; }
            }

            // ---------------- integrate (semi-implicit Euler)
            vel = vel + (accFrame + gravity) * dt;
            pos = pos + vel * dt;
            var w = angVel;
            if (w.sqrMagnitude > 1e-12f) rot = rot * Quaternion.AngleAxis((float)(w.magnitude * dt * Mathf.Rad2Deg), w.normalized);
            rot = Normalize(rot);

            if (chuteArmed && (!InAtmosphere)) chuteOpen = 0f;

            GroundContact();
            CheckSoi();
            if (station != null) { station.StepPhysics(dt); CheckDocking(); }
        }

        // ------------------------------------------------------------------ rendezvous & docking

        double dockWarnAt = -1e9;

        public bool PortFace(int u, out Vector3 point, out Vector3 axis)
        {
            var p = state.byUid[u];
            var pose = Pose(u);
            axis = pose.rotation * (p.mode == AttachMode.Below ? Vector3.down : Vector3.up);
            point = pose.position + axis * PartDims.Of(p).height * 0.5f;
            return true;
        }

        void CheckDocking()
        {
            if (station.docked || station.body != body || time - station.undockedAt < 5) return;
            var rel = station.pos - pos;
            if (rel.magnitude > 60) return;
            foreach (int u in state.alive)
            {
                if (!state.byUid[u].Def.isDockingPort) continue;
                Vector3 pl, al;
                PortFace(u, out pl, out al);
                var pw = pos + (Vector3d)(rot * (pl - comLocal));
                double d = (pw - station.PortWorld).magnitude;
                if (d > 1.0) continue;
                double rv = (vel - station.vel).magnitude;
                float align = Vector3.Dot(rot * al, station.PortAxisWorld);
                if (rv > 0.6 || align > -0.94f)
                {
                    if (time - dockWarnAt > 3)
                    {
                        dockWarnAt = time;
                        Say(rv > 0.6 ? "Bump! Too fast to dock (" + rv.ToString("0.0") + " m/s) — approach below 0.6 m/s" : "Ports not lined up — point the docking port straight at the station's port");
                    }
                    // gentle contact: bounce apart
                    var n = (pw - station.PortWorld).normalized;
                    double into = Vector3d.Dot(vel - station.vel, -n);
                    if (into > 0) vel += n * (into * 1.5);
                    continue;
                }
                Dock(u, pl, al);
                return;
            }
        }

        void Dock(int u, Vector3 pl, Vector3 al)
        {
            // snap the station so the two docking faces meet, axes opposed
            var vAxisW = rot * al;
            var stRot = Quaternion.FromToRotation(station.PortAxisWorld, -vAxisW) * station.rot;
            var pw = pos + (Vector3d)(rot * (pl - comLocal));
            var stOrigin = pw - (Vector3d)(stRot * station.portPoint);
            // momentum is conserved
            double m1 = mass, m2 = station.mass;
            vel = (vel * m1 + station.vel * m2) / (m1 + m2);
            angVel *= (float)(m1 / (m1 + m2));
            station.docked = true;
            station.dockedPortUid = u;
            station.dockRot = Quaternion.Inverse(rot) * stRot;
            station.dockOffset = comLocal + Quaternion.Inverse(rot) * (stOrigin - pos).ToFloat();
            station.rot = stRot;
            var oldCom = comLocal;
            Recalc();
            pos += (Vector3d)(rot * (comLocal - oldCom));
            SyncStation();
            Say("Docked with the station! Tap your docking port to undock.");
        }

        string Undock()
        {
            Vector3 pl, al;
            PortFace(station.dockedPortUid, out pl, out al);
            var axisW = (Vector3d)(rot * al);
            SyncStation();
            double m1 = mass - station.mass, m2 = station.mass;
            station.Release(station.pos, vel + axisW * (0.3 * m1 / (m1 + m2)), time);
            vel -= axisW * (0.3 * m2 / (m1 + m2));
            station.dockedPortUid = -1;
            var oldCom = comLocal;
            Recalc();
            pos += (Vector3d)(rot * (comLocal - oldCom));
            return "Undocked from the station";
        }

        void SyncStation()
        {
            if (station == null || !station.docked) return;
            station.body = body;
            station.rot = rot * station.dockRot;
            station.pos = pos + (Vector3d)(rot * (station.dockOffset - comLocal));
            station.vel = vel;
        }

        public double StationDistance { get { return station != null && station.body == body ? (station.pos - pos).magnitude : -1; } }

        static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }

        void ConsumeRcs(float F, double dt)
        {
            if (cheats.infiniteFuel) return;
            double used = F / (240.0 * G0) * dt;
            var blocks = new List<int>();
            foreach (int u in state.alive) { var dd = state.byUid[u].Def; if ((dd.isRcs || dd.monoprop) && Fuel(u) > 1e-6f) blocks.Add(u); }
            if (blocks.Count == 0) return;
            foreach (int u in blocks) state.fuel[u] = Mathf.Max(0f, state.fuel[u] - (float)(used / blocks.Count));
        }

        /// <summary>Direction (frame) the SAS should point the nose at, or null for none.</summary>
        public Vector3d? SasDirection()
        {
            bool surface = Altitude < (body.HasAtmosphere ? body.atmosphere.height : body.radius * 0.05);
            var v = surface ? SurfaceVelocity : vel;
            // near the station, prograde/retrograde follow the velocity relative to it (for matching speed)
            if (station != null && !station.docked && station.body == body && (station.pos - pos).magnitude < 5000) v = vel - station.vel;
            if (v.magnitude < 0.5 && (sas == SasMode.Prograde || sas == SasMode.Retrograde)) return Up;
            var up = Up;
            var n = Vector3d.Cross(pos, vel).normalized;
            switch (sas)
            {
                case SasMode.Prograde: return v.normalized;
                case SasMode.Retrograde: return -v.normalized;
                case SasMode.RadialOut: return up;
                case SasMode.RadialIn: return -up;
                case SasMode.Normal: return n;
                case SasMode.AntiNormal: return -n;
                case SasMode.Target:
                case SasMode.AntiTarget:
                    if (target == null && station != null && !station.docked && station.body == body)
                    {
                        var sp = station.pos - pos;
                        return sas == SasMode.Target ? sp.normalized : -sp.normalized;
                    }
                    if (target == null) return null;
                    var tp = TargetRelativePosition();
                    return sas == SasMode.Target ? tp.normalized : -tp.normalized;
            }
            return null;
        }

        /// <summary>Target position relative to the vessel, in the current frame.</summary>
        public Vector3d TargetRelativePosition()
        {
            if (target == null) return Vector3d.zero;
            return target.AbsolutePosition(time) - (body.AbsolutePosition(time) + pos);
        }

        public Vector3d TargetRelativeVelocity()
        {
            if (target == null) return Vector3d.zero;
            return target.AbsoluteVelocity(time) - (body.AbsoluteVelocity(time) + vel);
        }

        void ControlAttitude(double dt, Vector3 extTorque, double gimbalTorque, double rho, double speed)
        {
            // Control authority: reaction wheels + gimbals + RCS + fins in the airflow (kN·m)
            float aero = (float)(0.5 * rho * speed * speed * finArea * 0.25 * Math.Max(1.0, length * 0.4) / 1000.0);
            float wheels = HasPower ? reactionTorque : 0f;
            float ctl = wheels + (float)gimbalTorque + (rcsOn ? rcsTorque : 0f) + aero;
            if (ctl < 1e-3f) ctl = 1e-3f;
            var maxAcc = new Vector3(ctl / inertia.x, ctl * 0.6f / inertia.y, ctl / inertia.z);
            float maxRate = 0.6f;

            // External torque (off-centre thrust, aerodynamics)
            angVel += new Vector3(extTorque.x / inertia.x, extTorque.y / inertia.y, extTorque.z / inertia.z) * (float)dt;

            Vector3 desired;
            bool manual = Mathf.Abs(pitchInput) > 0.01f || Mathf.Abs(yawInput) > 0.01f || Mathf.Abs(rollInput) > 0.01f;
            if (manual)
                desired = new Vector3(pitchInput, rollInput, yawInput) * maxRate;
            else if (sas == SasMode.Off || !HasPower)
                desired = angVel;
            else
            {
                desired = Vector3.zero;
                var dir = SasDirection();
                if (dir.HasValue && sas != SasMode.Stability)
                {
                    var db = Vector3d.Rotate(Quaternion.Inverse(rot), dir.Value).ToFloat().normalized;
                    var axis = Vector3.Cross(Vector3.up, db);
                    float sin = axis.magnitude;
                    float ang = Mathf.Atan2(sin, Vector3.Dot(Vector3.up, db));
                    if (sin > 1e-5f)
                    {
                        axis /= sin;
                        float a = Mathf.Min(maxAcc.x, maxAcc.z);
                        float rate = Mathf.Min(maxRate, Mathf.Sqrt(2f * a * ang) * 0.7f + ang * 0.2f);
                        desired = axis * rate;
                        desired.y = 0f;
                    }
                }
            }
            var cmd = Vector3.zero;
            for (int k = 0; k < 3; k++)
            {
                float err = desired[k] - angVel[k];
                float step = maxAcc[k] * (float)dt;
                angVel[k] += Mathf.Clamp(err, -step, step);
                cmd[k] = Mathf.Clamp(err / Mathf.Max(1e-4f, maxAcc[k] * 0.25f), -1f, 1f);
            }
            // engine bells swivel to make the turn (also counter off-centre thrust)
            gimbalCmd = Vector3.Lerp(gimbalCmd, cmd, Mathf.Clamp01((float)dt * 8f));
        }

        // =====================================================================================
        // Heating

        void UpdateHeating(double dt, double rho, double speed, Quaternion invRot, Vector3d vAir, AtmosphereModel atmo)
        {
            heatFlux = 0;
            maxTempFraction = 0f;
            double ambient = 290 - Math.Min(200, Altitude / 400);
            if (atmo == null || rho <= 0 || speed < 300)
            {
                // radiate towards ambient / space
                foreach (int u in state.alive)
                {
                    float T = temperature[u];
                    if (T > ambient) temperature[u] = (float)Math.Max(ambient, T - (T - ambient) * 0.02 * dt);
                }
                return;
            }
            double rn = Math.Max(0.3, maxRadius);
            heatFlux = atmo.heatK * Math.Sqrt(rho / rn) * speed * speed * speed;     // W/m²
            var flowB = Vector3d.Rotate(invRot, vAir / speed).ToFloat();
            // Which parts face the airflow? A part is shadowed when a wider part sits ahead of it in the flow
            // (a heat shield in front of a capsule, a nose cone in front of tanks).
            var alive = new List<int>(state.alive);
            int n = alive.Count;
            var sAlong = new float[n];
            var lateral = new Vector3[n];
            var rad = new float[n];
            for (int i = 0; i < n; i++)
            {
                var pp = Pose(alive[i]).position - comLocal;
                var dm = PartDims.Of(state.byUid[alive[i]]);
                sAlong[i] = Vector3.Dot(pp, flowB);
                lateral[i] = pp - flowB * sAlong[i];
                rad[i] = state.byUid[alive[i]].Def.radialMount ? 0.3f : Mathf.Max(dm.Radius, dm.TopRadius);
            }
            int worst = -1;
            for (int i = 0; i < n; i++)
            {
                int u = alive[i];
                var p = state.byUid[u];
                var d = p.Def;
                var dims = PartDims.Of(p);
                float exposure = 1f;
                for (int k = 0; k < n; k++)
                {
                    if (k == i || sAlong[k] <= sAlong[i] + 0.05f) continue;
                    float off = (lateral[k] - lateral[i]).magnitude;
                    if (off + rad[i] * 0.95f <= rad[k] * 1.05f) { exposure = 0.07f; break; }   // fully behind a wider part
                    if (off < rad[k] + rad[i]) exposure = Mathf.Min(exposure, 0.45f);      // partly covered
                }
                float aFront = Mathf.PI * Mathf.Max(0.05f, dims.Radius * dims.Radius);
                float aSurf = 2f * Mathf.PI * dims.Radius * dims.height + 2f * aFront;
                float kg = Mathf.Max(5f, PartMass.Dry(p) * 1000f);
                float cap = kg * 900f * (d.shape == PartShape.HeatShield ? 1.6f : 0.12f);
                double T = temperature[u];
                double qIn = heatFlux * aFront * exposure;
                double qOut = 0.8 * Sigma * (T * T * T * T - ambient * ambient * ambient * ambient) * aSurf;
                T += (qIn - qOut) / cap * dt;
                T = Math.Max(ambient, Math.Min(5000, T));
                temperature[u] = (float)T;
                float frac = (float)(T / MaxTemp(d));
                if (frac > maxTempFraction) maxTempFraction = frac;
                if (frac > 1f && worst < 0) worst = u;
            }
            if (worst >= 0 && !cheats.unbreakable && !cheats.noHeat) DestroyPart(worst, state.byUid[worst].Def.displayName + " burned up in re-entry heating");
        }

        // =====================================================================================
        // Structure

        /// <summary>
        /// Each joint carries the force needed to accelerate everything beyond it, minus what pushes on that side
        /// itself. Joint strength scales with the contact area (diameter²). A heavy engine on a tiny tank, or a
        /// rocket turning sideways at high speed, tears joints apart.
        /// </summary>
        void CheckStructure(Vector3 forceBody, double dt)
        {
            if (cheats.unbreakable || mass <= 0f) return;
            var aBody = forceBody / mass;   // proper acceleration felt by the structure (m/s²)
            if (aBody.sqrMagnitude < 4f && dynamicPressure < 5000) return;
            var dragShare = (forceBody - ThrustOnly()) / mass;
            breakCandidate = null;
            breakLoad = breakStrength = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                if (p.parentUid < 0 || !state.alive.Contains(p.parentUid)) Accumulate(u, aBody, dragShare);
            }
            if (breakCandidate == null) { overload = 0f; return; }
            overload += (float)dt;
            if (overload < 0.25f) return;   // momentary spikes (ignition, staging) don't snap joints
            overload = 0f;
            if (breakCandidate != null)
                BreakJoint(breakCandidate, "Structural failure: " + breakCandidate.Def.displayName + " tore off (" + Mathf.RoundToInt(breakLoad) + " kN on a " + Mathf.RoundToInt(breakStrength) + " kN joint)");
        }

        PartInstance breakCandidate;
        float overload;
        float breakLoad, breakStrength;
        Dictionary<int, List<int>> childMap = new Dictionary<int, List<int>>();

        /// <summary>Post-order walk: returns subtree mass and the external force acting on it; checks each joint.</summary>
        float Accumulate(int u, Vector3 aBody, Vector3 dragPerTonne)
        {
            Vector3 applied;
            return Accumulate(u, aBody, dragPerTonne, out applied);
        }

        float Accumulate(int u, Vector3 aBody, Vector3 dragPerTonne, out Vector3 applied)
        {
            var q = state.byUid[u];
            float fm; state.fuel.TryGetValue(u, out fm);
            float m = PartMass.Dry(q) + fm + (q.Def.isPayload ? q.payloadMass : 0f);
            applied = dragPerTonne * m;
            float o;
            if (engineOutput.TryGetValue(u, out o))
                applied += Pose(u).rotation * Vector3.up * (float)(q.Def.thrustVac * o * (cheats.superThrust ? 5 : 1));
            List<int> kids;
            if (childMap.TryGetValue(u, out kids))
                foreach (int c in kids)
                {
                    Vector3 ca;
                    float cm = Accumulate(c, aBody, dragPerTonne, out ca);
                    var child = state.byUid[c];
                    float load = (aBody * cm - ca).magnitude;
                    float strength = JointStrength(child);
                    if (load > strength && load / strength > breakLoad / Mathf.Max(1f, breakStrength))
                    {
                        breakCandidate = child; breakLoad = load; breakStrength = strength;
                    }
                    m += cm;
                    applied += ca;
                }
            return m;
        }

        Vector3 ThrustOnly()
        {
            var t = Vector3.zero;
            foreach (var kv in engineOutput)
            {
                var d = state.byUid[kv.Key].Def;
                t += Pose(kv.Key).rotation * Vector3.up * (float)(d.thrustVac * kv.Value * (cheats.superThrust ? 5 : 1));
            }
            return t;
        }

        public float JointStrength(PartInstance child)
        {
            var parent = state.byUid[child.parentUid];
            var cd = PartDims.Of(child);
            var pd = PartDims.Of(parent);
            float dc = child.mode == AttachMode.Above ? cd.bottomEnd : cd.topEnd;
            float dp = child.mode == AttachMode.Above ? pd.topEnd : pd.bottomEnd;
            float d = Mathf.Max(0.3f, Mathf.Min(dc, dp));
            float geo;
            switch (child.mode)
            {
                case AttachMode.Surface: geo = 120f + 400f * cd.diameter * cd.diameter; break;
                case AttachMode.Flank: geo = 900f * d * d; break;
                case AttachMode.Side: geo = 1100f * d * d; break;
                default: geo = 1500f * d * d; break;
            }
            // Engine and booster mounts are built for the thrust they carry (thrust structure, ~1.5× safety factor).
            // Only a sensibly sized mount gets that rating: a huge engine bolted under a tiny tank still tears off.
            float rated = 0f;
            bool radial = child.mode == AttachMode.Surface || child.mode == AttachMode.Side || child.mode == AttachMode.Flank;
            if (child.Def.IsEngine && (radial || cd.diameter <= pd.diameter * 1.3f)) rated = Mathf.Max(rated, child.Def.thrustVac);
            if (parent.Def.IsEngine && pd.diameter <= cd.diameter * 1.3f) rated = Mathf.Max(rated, parent.Def.thrustVac);
            return Mathf.Max(geo, rated * 1.5f);
        }

        HashSet<int> Subtree(int uid)
        {
            var set = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(uid);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                if (!state.alive.Contains(u) || !set.Add(u)) continue;
                List<int> kids;
                if (childMap.TryGetValue(u, out kids)) foreach (int c in kids) stack.Push(c);
            }
            return set;
        }

        void BreakJoint(PartInstance child, string why)
        {
            var sub = Subtree(child.uid);
            if (sub.Contains(state.controlUid))
            {
                var rest = new HashSet<int>(state.alive);
                rest.ExceptWith(sub);
                sub = rest;
            }
            if (sub.Count == 0) return;
            var oldCom = comLocal;
            var at = pos + Vector3d.Rotate(rot, (Vector3d)(Pose(child.uid).position - comLocal));
            foreach (int u in sub) state.alive.Remove(u);
            if (OnExplosion != null) OnExplosion(at, 4f + PartDims.Of(child).diameter * 3f);
            SpawnDebris(new List<HashSet<int>> { sub }, oldCom, 3.0);
            Say(why);
        }

        void DestroyPart(int uid, string why)
        {
            var p = state.byUid[uid];
            var at = pos + Vector3d.Rotate(rot, (Vector3d)(Pose(uid).position - comLocal));
            if (OnExplosion != null) OnExplosion(at, 3f + PartDims.Of(p).diameter * 2f);
            if (uid == state.controlUid)
            {
                Crash(why);
                return;
            }
            var oldCom = comLocal;
            state.alive.Remove(uid);
            // anything only connected through this part comes off too
            var connected = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(state.controlUid);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                if (!state.alive.Contains(u) || !connected.Add(u)) continue;
                var pu = state.byUid[u];
                if (pu.parentUid >= 0) stack.Push(pu.parentUid);
                List<int> kids;
                if (childMap.TryGetValue(u, out kids)) foreach (int c in kids) stack.Push(c);
            }
            var loose = new HashSet<int>(state.alive);
            loose.ExceptWith(connected);
            foreach (int u in loose) state.alive.Remove(u);
            if (loose.Count > 0) SpawnDebris(new List<HashSet<int>> { loose }, oldCom, 2.0);
            else { fuelGroups = state.FuelGroups(); Recalc(); }
            Say(why);
        }

        // =====================================================================================
        // Ground

        void GroundContact()
        {
            double minH = double.MaxValue;
            foreach (var s in supports)
            {
                var wp = pos + Vector3d.Rotate(rot, (Vector3d)(s - comLocal));
                double h = wp.magnitude - GroundRadius(wp);
                if (h < minH) minH = h;
            }
            if (supports.Count == 0) minH = TerrainAltitude;
            if (minH > 0) return;

            var sv = SurfaceVelocity;
            double vDown = -Vector3d.Dot(sv, Up);
            double impact = sv.magnitude;
            double tolerance = hasLegs ? 12.0 : 7.0;
            double tilt = Tilt;
            if (body.gasGiant || body.star)
            {
                Crash(body.star ? "Vaporised by the Sun" : "Crushed in " + body.name + "'s deep atmosphere");
                return;
            }
            if ((impact > tolerance || tilt > 35) && !cheats.unbreakable)
            {
                Crash("Crashed into " + body.name + " at " + impact.ToString("0") + " m/s" + (tilt > 35 ? " (tipped over)" : ""));
                return;
            }
            // Settle upright and stick to the surface.
            pos = pos + Up * (-minH);
            var up = Up.ToFloat();
            rot = Quaternion.FromToRotation(rot * Vector3.up, up) * rot;
            status = FlightStatus.Landed;
            chuteOpen = 0f;
            CaptureSurface();
            SyncToSurface();
            Say("Landed on " + body.name + " at " + vDown.ToString("0.0") + " m/s");
            if (OnLanded != null) OnLanded();
        }

        void Crash(string why)
        {
            if (status == FlightStatus.Crashed) return;
            status = FlightStatus.Crashed;
            enginesOn = false;
            if (OnExplosion != null) OnExplosion(pos, 10f + length);
            Say(why);
            if (OnCrash != null) OnCrash();
        }

        // =====================================================================================
        // Debris

        void StepDebris(double dt)
        {
            foreach (var d in debris)
            {
                if (d.dead) continue;
                d.age += dt;
                if (d.landed)
                {
                    var spin = d.body.Spin(time);
                    d.pos = Vector3d.Rotate(spin, d.landedFixed);
                    d.vel = d.body.SurfaceVelocity(d.pos);
                    continue;
                }
                double rem = dt;
                while (rem > 0)
                {
                    double h = Math.Min(rem, dt > 1 ? 5 : 0.05);
                    rem -= h;
                    double rm = d.pos.magnitude;
                    var g = d.pos * (-d.body.mu / (rm * rm * rm));
                    var acc = g;
                    if (d.body.HasAtmosphere)
                    {
                        double alt = rm - d.body.radius;
                        double rho = d.body.atmosphere.Density(Math.Max(0, alt));
                        var va = d.vel - d.body.SurfaceVelocity(d.pos);
                        double sp = va.magnitude;
                        if (rho > 0 && sp > 0.1) acc = acc - va * (0.5 * rho * sp * d.cdA / (d.mass * 1000.0));
                    }
                    d.vel = d.vel + acc * h;
                    d.pos = d.pos + d.vel * h;
                    d.rot = d.rot * Quaternion.Euler(d.spin * (float)(h * Mathf.Rad2Deg));
                    double ground = d.body.radius + d.body.TerrainHeight(Vector3d.Rotate(Quaternion.Inverse(d.body.Spin(time)), d.pos).normalized);
                    if (rm < ground + 1)
                    {
                        double sp = (d.vel - d.body.SurfaceVelocity(d.pos)).magnitude;
                        if (sp > 15) { d.dead = true; d.impacted = true; if (OnExplosion != null && d.body == body) OnExplosion(d.pos, 5f); }
                        else { d.landed = true; d.landedFixed = Vector3d.Rotate(Quaternion.Inverse(d.body.Spin(time)), d.pos.normalized * (ground + 0.5)); }
                        break;
                    }
                }
                if (d.body != body || (d.pos - pos).magnitude > 2.5e6) d.dead = true;
            }
            debris.RemoveAll(x => x.dead && x.age > 0.5);
        }
    }
}
