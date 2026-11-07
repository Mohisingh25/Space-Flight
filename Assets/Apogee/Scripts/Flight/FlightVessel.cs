using System;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rocket;
using Apogee.Simulation;
using UnityEngine;

namespace Apogee.Flight
{
    public enum FlightStatus { OnPad, Flying, Landed, Crashed }

    /// <summary>Optional cheats (Settings → Cheats). All off by default; tests run with real physics.</summary>
    public class FlightCheats
    {
        public bool infiniteFuel, noGravity, noDrag, unbreakable, superThrust;
        public bool Any { get { return infiniteFuel || noGravity || noDrag || unbreakable || superThrust; } }
    }

    /// <summary>A planar rigid body in planet-centred coordinates (metres, m/s, radians).</summary>
    public class Body2D
    {
        public Vector2d pos, vel;
        public double angle;   // direction of the body's +Y (nose) axis, radians CCW from +X
        public double angVel;  // rad/s

        public Vector2d Forward { get { return Vector2d.FromAngle(angle); } }

        /// <summary>Converts a rocket-space offset (x right, y towards the nose) into planet space.</summary>
        public Vector2d LocalToWorldVector(double lx, double ly) { return Vector2d.Rotate(new Vector2d(lx, ly), angle - Math.PI / 2); }
    }

    /// <summary>Separated parts falling on their own (spent boosters, stages, fairings).</summary>
    public class Debris : Body2D
    {
        public HashSet<int> parts;
        public float mass;
        public double cdA;
        public Vector2 comLocal; // rocket-space centre of mass of this piece
        public bool dead;
        public bool impacted;
        public double age;
    }

    /// <summary>
    /// The live flight simulation of one vehicle: thrust with pressure-dependent Isp and real mass flow,
    /// fuel crossfeed per stage, staging into debris, gravity (inverse square), aerodynamic drag and
    /// weathervane torque, reaction-wheel and gimbal steering, parachutes, landing and crashes.
    /// Pure C# — the flight scene renders it, tests can run it headless.
    /// </summary>
    public class FlightVessel : Body2D
    {
        public const double R = PhysicsConstants.EarthRadius;
        public const double MU = PhysicsConstants.EarthMu;
        public const double G0 = PhysicsConstants.G0;
        public const double PadTop = 1.5;          // metres above ground
        public const double PadHalfWidth = 35;     // metres

        public readonly RocketBlueprint bp;
        public readonly VehicleState state;
        public readonly Dictionary<int, PartPose> poses;

        public FlightStatus status = FlightStatus.OnPad;
        public float throttle = 1f;
        public bool enginesOn;
        public int turnInput;
        public int nextStage;
        public double missionTime;
        public double maxAltitude;
        public bool chuteArmed;
        public float chuteOpen;
        public bool everFlew;
        public readonly List<Debris> debris = new List<Debris>();
        public readonly Dictionary<int, float> engineOutput = new Dictionary<int, float>(); // 0..1 per firing engine
        public double lastAcceleration;           // m/s², for telemetry
        public FlightCheats cheats = new FlightCheats();

        // Per-part controls (tap a part in flight)
        public readonly HashSet<int> engineOff = new HashSet<int>();   // lit engines the player has shut down
        public readonly HashSet<int> folded = new HashSet<int>();      // folded landing legs / stowed solar arrays
        public readonly HashSet<int> chutesDeployed = new HashSet<int>();

        // RCS: translation in the vessel frame (x right, y towards the nose), each -1..1
        public bool rcsOn;
        public double rcsX, rcsY;
        public bool rcsFiring;
        public float chuteScaleSum;

        // mass properties (recomputed when parts or fuel change)
        public float mass;
        public Vector2 comLocal;
        public float inertia;
        public float length, frontalArea, cd, stability;
        public float lowY;
        public bool hasLegs;
        public int chuteCount;
        public float reactionTorque;
        Dictionary<int, int> fuelGroups;
        readonly Dictionary<int, double> poolFlow = new Dictionary<int, double>();

        public event Action<string> OnMessage;
        public event Action<Debris> OnDebris;
        public event Action OnCrash;
        public event Action OnLanded;

        public FlightVessel(RocketBlueprint blueprint)
        {
            bp = blueprint;
            if (bp.stageCount == 0) StagingSystem.Restage(bp);
            state = new VehicleState(bp, 0f);
            poses = RocketLayout.Compute(bp);
            fuelGroups = state.FuelGroups();
            foreach (var p in bp.parts) if (p.Def.solarKW > 0f) folded.Add(p.uid); // arrays launch stowed
            Recalc();
            pos = new Vector2d(0, R + PadTop + (comLocal.y - lowY));
            vel = Vector2d.zero;
            angle = Math.PI / 2;
        }

        public double Altitude { get { return pos.magnitude - R; } }
        public Vector2d Up { get { return pos.normalized; } }
        public bool HasNextStage { get { return nextStage < bp.stageCount; } }

        /// <summary>Height of the vehicle's lowest point above the terrain (what the HUD shows).</summary>
        public double TerrainHeight
        {
            get
            {
                var low = pos + LocalToWorldVector(0, lowY - comLocal.y);
                return low.magnitude - GroundRadiusAt(low);
            }
        }

        static double GroundRadiusAt(Vector2d p)
        {
            // Launch pad is centred at the planet's "top" (angle 90°).
            double ang = Math.Atan2(p.y, p.x) - Math.PI / 2;
            double arc = Math.Abs(ang) * R;
            return R + (arc < PadHalfWidth ? PadTop : 0.0);
        }

        // ------------------------------------------------------------------ mass properties

        PartPose Pose(int uid) { PartPose p; poses.TryGetValue(uid, out p); return p; }

        public void Recalc()
        {
            float m = 0f;
            Vector2 c = Vector2.zero;
            float minY = float.MaxValue, maxY = float.MinValue, maxD = 0.5f, topY = float.MinValue;
            cd = 0.8f;
            hasLegs = false;
            chuteCount = 0;
            float chuteScale = 0f;
            reactionTorque = 2f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                var d = p.Def;
                float pm = PartMass.Dry(p);
                float f;
                if (state.fuel.TryGetValue(u, out f)) pm += f;
                var pose = Pose(u);
                Vector2 pp = new Vector2(pose.position.x, pose.position.y);
                m += pm;
                c += pp * pm;
                var dims = PartDims.Of(p);
                bool stack = RocketAssembly.CanStack(d);
                float bottom = pose.position.y - dims.height * 0.5f, top = pose.position.y + dims.height * 0.5f;
                bool support = (d.isLeg && !folded.Contains(u)) || d.isWheel;
                if (d.isWheel) bottom = pose.position.y - dims.height * 0.675f;
                if (stack || support)
                {
                    minY = Mathf.Min(minY, bottom);
                    maxY = Mathf.Max(maxY, top);
                }
                if (stack)
                {
                    maxD = Mathf.Max(maxD, Mathf.Abs(pose.position.x) * 2f + dims.diameter);
                    if (top > topY) { topY = top; cd = d.dragCoefficient; }
                }
                if (support) hasLegs = true;
                if (d.isParachute && chutesDeployed.Contains(u)) { chuteCount++; chuteScale += d.chuteScale; }
                if (d.isCommand) reactionTorque += d.crew >= 3 ? 60f : d.crew >= 1 ? 25f : 8f;
                if (d.isRcs) reactionTorque += 6f;
            }
            if (m <= 0f) m = 0.001f;
            chuteScaleSum = chuteScale;
            mass = m;
            comLocal = c / m;
            if (minY == float.MaxValue) { minY = -1f; maxY = 1f; }
            lowY = minY;
            length = Mathf.Max(1f, maxY - minY);
            frontalArea = Mathf.PI * maxD * maxD * 0.25f;

            float I = 0f, areaSum = 0f, areaY = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                var d = p.Def;
                float pm = PartMass.Dry(p);
                float f;
                if (state.fuel.TryGetValue(u, out f)) pm += f;
                var pose = Pose(u);
                var dims = PartDims.Of(p);
                Vector2 rel = new Vector2(pose.position.x, pose.position.y) - comLocal;
                I += pm * (rel.sqrMagnitude + dims.height * dims.height / 12f);
                float lateral = d.finArea > 0f ? d.finArea * 8f : RocketAssembly.CanStack(d) ? 0.5f * (dims.diameter + dims.topDiameter) * dims.height : 0f;
                areaSum += lateral;
                areaY += lateral * pose.position.y;
            }
            inertia = Mathf.Max(0.05f, I);
            float cop = areaSum > 0f ? areaY / areaSum : comLocal.y;
            stability = Mathf.Clamp((comLocal.y - cop) / Mathf.Max(0.5f, maxD), -2f, 3f);
        }

        // ------------------------------------------------------------------ staging

        public void Stage()
        {
            if (status == FlightStatus.Crashed || !HasNextStage) return;
            var oldCom = comLocal;
            var log = new List<string>();
            var groups = state.ApplyStageCollect(nextStage, log);
            foreach (var p in bp.parts)
                if (p.stage == nextStage && p.Def.isParachute && state.alive.Contains(p.uid)) chutesDeployed.Add(p.uid);
            foreach (var p in bp.parts)
                if (p.stage == nextStage && p.Def.IsEngine) engineOff.Remove(p.uid);
            nextStage++;
            if (log.Contains("Ignition")) enginesOn = true;
            if (log.Contains("Parachute")) chuteArmed = true;
            SpawnDebris(groups, oldCom);
            if (OnMessage != null) OnMessage(log.Count > 0 ? string.Join(" · ", log.ToArray()) : "Stage " + nextStage);
        }

        /// <summary>Turns separated part groups into free-falling debris and re-centres the remaining vehicle.</summary>
        void SpawnDebris(List<HashSet<int>> groups, Vector2 oldCom)
        {
            fuelGroups = state.FuelGroups();
            Recalc();
            foreach (var g in groups)
            {
                var deb = new Debris { parts = g, angle = angle, angVel = angVel };
                float dm = 0f;
                Vector2 dc = Vector2.zero;
                float area = 0.5f;
                foreach (int u in g)
                {
                    var p = state.byUid[u];
                    float pm = PartMass.Dry(p);
                    float f;
                    if (state.fuel.TryGetValue(u, out f)) pm += f;
                    var pose = Pose(u);
                    dm += pm;
                    dc += new Vector2(pose.position.x, pose.position.y) * pm;
                    area = Mathf.Max(area, PartDims.Of(p).diameter);
                }
                if (dm <= 0f) dm = 0.01f;
                deb.mass = dm;
                deb.comLocal = dc / dm;
                deb.cdA = 0.9 * Math.PI * area * area * 0.25;
                // Position relative to the vessel's previous centre of mass
                var offset = new Vector2(deb.comLocal.x - oldCom.x, deb.comLocal.y - oldCom.y);
                deb.pos = pos + LocalToWorldVector(offset.x, offset.y);
                var push = new Vector2(deb.comLocal.x - comLocal.x, deb.comLocal.y - comLocal.y);
                var pushW = LocalToWorldVector(push.x, push.y).normalized;
                deb.vel = vel + pushW * 1.8;
                if (Mathf.Abs(push.x) > Mathf.Abs(push.y)) deb.angVel += push.x > 0 ? -0.12 : 0.12; // side boosters peel away
                debris.Add(deb);
                if (OnDebris != null) OnDebris(deb);
            }
            // Keep the remaining vehicle's centre-of-mass position consistent.
            var shift = new Vector2(comLocal.x - oldCom.x, comLocal.y - oldCom.y);
            pos = pos + LocalToWorldVector(shift.x, shift.y);
            if (status == FlightStatus.OnPad || status == FlightStatus.Landed) Reseat();
        }

        /// <summary>Keeps a grounded vehicle standing on its lowest point after its shape changes (legs folded, parts dropped).</summary>
        void Reseat()
        {
            var low = pos + LocalToWorldVector(0, lowY - comLocal.y);
            double groundR = GroundRadiusAt(low);
            pos = pos + pos.normalized * (groundR - low.magnitude);
        }

        // ------------------------------------------------------------------ tapping parts in flight

        /// <summary>
        /// The player tapped a part: engines start/stop individually, legs and solar arrays fold or unfold,
        /// parachutes deploy, decouplers and docking ports separate, fairings jettison, RCS blocks toggle RCS.
        /// Returns a short message for the HUD.
        /// </summary>
        public string TapPart(int uid)
        {
            if (status == FlightStatus.Crashed || !state.alive.Contains(uid)) return null;
            var p = state.byUid[uid];
            var d = p.Def;
            if (d.IsEngine)
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
                if (status == FlightStatus.OnPad || status == FlightStatus.Landed) Reseat();
                if (d.isLeg) return fold ? "Landing legs folded" : "Landing legs deployed";
                return fold ? "Solar arrays stowed" : "Solar arrays deployed";
            }
            if (d.isParachute)
            {
                if (chutesDeployed.Contains(uid)) return "Parachute already deployed";
                foreach (var m in bp.SymmetryMembers(p)) if (state.alive.Contains(m.uid)) chutesDeployed.Add(m.uid);
                chuteArmed = true;
                Recalc();
                return Altitude < 30000 ? "Parachute deployed" : "Parachute armed — opens in the atmosphere";
            }
            if (d.isDecoupler || d.isDockingPort || d.isFairing)
            {
                var oldCom = comLocal;
                HashSet<int> dropped;
                if (d.isFairing) dropped = new HashSet<int> { uid };
                else dropped = StagingSystem.Jettisoned(bp, uid, state.alive, state.controlUid);
                if (dropped.Count == 0) return null;
                if (dropped.Contains(state.controlUid)) return "That would cut off the command part";
                foreach (int u in dropped) state.alive.Remove(u);
                SpawnDebris(new List<HashSet<int>> { dropped }, oldCom);
                return d.isDockingPort ? "Undocked" : d.isFairing ? "Fairing jettisoned" : "Separated";
            }
            if (d.isRcs)
            {
                rcsOn = !rcsOn;
                return rcsOn ? "RCS on" : "RCS off";
            }
            float fuel;
            state.fuel.TryGetValue(uid, out fuel);
            float cap = PartMass.Fuel(p);
            return d.displayName + "  ·  " + PartDefinition.FormatMass(PartMass.Dry(p) + fuel) + (cap > 0f ? "  ·  fuel " + Mathf.RoundToInt(100f * fuel / cap) + "%" : "");
        }

        public bool HasRcs
        {
            get
            {
                foreach (int u in state.alive) if (state.byUid[u].Def.isRcs) return true;
                return false;
            }
        }

        public float RcsFuelFraction()
        {
            float cur = 0f, max = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                if (!p.Def.isRcs) continue;
                max += PartMass.Fuel(p);
                float f;
                if (state.fuel.TryGetValue(u, out f)) cur += f;
            }
            return max > 0f ? cur / max : -1f;
        }

        // ------------------------------------------------------------------ simulation

        public void Step(double dt)
        {
            if (status == FlightStatus.Crashed) { StepDebris(dt); return; }
            double r = pos.magnitude;
            var up = pos / r;
            double alt = r - R;
            double pr = EarthAtmosphere.PressureRatio(alt);
            double rho = EarthAtmosphere.Density(alt);
            var f = Forward;

            // ---- thrust and propellant flow
            double F = 0, gimbalTorque = 0, thrustTorque = 0;
            engineOutput.Clear();
            poolFlow.Clear();
            bool burning = false;
            foreach (int u in state.ignited)
            {
                if (!state.alive.Contains(u) || engineOff.Contains(u)) continue;
                var d = state.byUid[u].Def;
                float thr = d.solidFuel ? 1f : (enginesOn && throttle > 0.001f ? Mathf.Max(throttle, d.minThrottle) : 0f);
                if (thr <= 0f) continue;
                int key = state.PoolKey(u, fuelGroups);
                if (state.PoolFuel(key, fuelGroups) <= 1e-5f) continue;
                double flow = d.thrustVac / (d.ispVac * G0) * thr;
                double isp = d.ispVac + (d.ispSL - d.ispVac) * pr;
                double Fi = flow * isp * G0 * (cheats.superThrust ? 5.0 : 1.0);
                F += Fi;
                double cur;
                poolFlow.TryGetValue(key, out cur);
                poolFlow[key] = cur + flow;
                var pose = Pose(u);
                double lever = Math.Sqrt(Math.Pow(pose.position.x - comLocal.x, 2) + Math.Pow(pose.position.y - comLocal.y, 2));
                gimbalTorque += Fi * Math.Sin(d.gimbalRange * Mathf.Deg2Rad) * lever;
                thrustTorque += (pose.position.x - comLocal.x) * Fi; // off-centre engines turn the vehicle
                engineOutput[u] = thr;
                burning = true;
            }
            if (!cheats.infiniteFuel)
                foreach (var kv in poolFlow) state.Drain(kv.Key, (float)(kv.Value * dt), fuelGroups);

            // ---- RCS translation (monopropellant, 1 kN class thrusters: gentle, for docking and fine landing)
            var rcsAcc = Vector2d.zero;
            rcsFiring = false;
            if (rcsOn && (Math.Abs(rcsX) > 0.01 || Math.Abs(rcsY) > 0.01))
            {
                double rcsF = 0;
                var blocks = new List<int>();
                foreach (int u in state.alive)
                {
                    var d = state.byUid[u].Def;
                    float rf;
                    if (!d.isRcs || !state.fuel.TryGetValue(u, out rf) || rf <= 1e-6f) continue;
                    rcsF += (d.thrustVac + (d.thrustSL - d.thrustVac) * pr) * (cheats.superThrust ? 5.0 : 1.0);
                    blocks.Add(u);
                }
                if (rcsF > 0)
                {
                    var dir = LocalToWorldVector(rcsX, rcsY);
                    double mag = Math.Min(1.0, dir.magnitude);
                    rcsAcc = dir.normalized * (rcsF * mag / mass);
                    rcsFiring = true;
                    if (!cheats.infiniteFuel)
                    {
                        double used = rcsF * mag / (240.0 * G0) * dt / blocks.Count;
                        foreach (int u in blocks) state.fuel[u] = Mathf.Max(0f, state.fuel[u] - (float)used);
                    }
                    burning = true;
                }
            }
            if (burning) Recalc();

            // ---- forces
            var gravity = cheats.noGravity ? Vector2d.zero : up * (-MU / (r * r));
            double speed = vel.magnitude;
            double cdA = cd * frontalArea;
            if (chuteArmed && chuteCount > 0)
            {
                // A small drogue streams as soon as the chute is armed in the atmosphere; the main canopy
                // (~20 m) opens once the drogue has slowed the capsule below ~300 m/s.
                if (alt < 30000) cdA += chuteScaleSum * 1.0 * Math.PI * 2.5 * 2.5;
                if (speed < 300 && alt < 25000) chuteOpen = Mathf.Min(1f, chuteOpen + (float)dt * 0.45f);
                cdA += chuteOpen * chuteScaleSum * 1.3 * Math.PI * 100.0;
            }
            var dragAcc = Vector2d.zero;
            if (speed > 1e-3 && rho > 0 && !cheats.noDrag)
                dragAcc = vel * (-(0.5 * rho * speed * cdA / 1000.0) / mass); // kN / t = m/s²
            var thrustAcc = f * (F / mass);
            var acc = thrustAcc + dragAcc + gravity + rcsAcc;
            lastAcceleration = (thrustAcc + dragAcc + rcsAcc).magnitude;

            // ---- rotation: reaction wheels + gimbal, steered by the player, damped when hands-off
            double maxAcc = (reactionTorque + gimbalTorque) / inertia;
            double maxRate = 0.9;
            if (status == FlightStatus.Flying) angVel += thrustTorque / inertia * dt;
            if (turnInput != 0)
                angVel = Clamp(angVel + turnInput * maxAcc * dt, -maxRate, maxRate);
            else
                angVel = MoveTowards(angVel, 0, maxAcc * dt);

            // aerodynamic weathervaning: stable rockets turn nose-first into the airflow; chutes hang the capsule
            if (speed > 3 && rho > 1e-6 && status == FlightStatus.Flying && !cheats.noDrag)
            {
                double q = 0.5 * rho * speed * speed / 1000.0;
                var vdir = vel / speed;
                bool hanging = chuteOpen > 0.3f;
                var desired = hanging ? -vdir : vdir;
                double sinD = Vector2d.Cross(f, desired);
                double stiff = q * frontalArea * length * 0.03 * (hanging ? 6.0 : stability);
                double damp = q * frontalArea * length * length * 0.004;
                angVel += (stiff * sinD - damp * angVel) / inertia * dt;
                angVel = Clamp(angVel, -2.5, 2.5);
            }

            // ---- on the ground: wait for enough thrust to lift off
            if (status == FlightStatus.OnPad || status == FlightStatus.Landed)
            {
                if (Vector2d.Dot(acc, up) > 0.05)
                {
                    if (status == FlightStatus.OnPad && OnMessage != null) OnMessage("Liftoff!");
                    status = FlightStatus.Flying;
                }
                else
                {
                    vel = Vector2d.zero;
                    angVel = 0;
                    StepDebris(dt);
                    return;
                }
            }

            // ---- integrate (semi-implicit Euler: stable for orbits)
            vel = vel + acc * dt;
            pos = pos + vel * dt;
            angle += angVel * dt;
            missionTime += dt;
            if (alt > maxAltitude) maxAltitude = alt;
            if (alt > 200) everFlew = true;

            // ---- ground contact
            var low = pos + LocalToWorldVector(0, lowY - comLocal.y);
            double groundR = GroundRadiusAt(low);
            double lowR = low.magnitude;
            if (lowR < groundR)
            {
                double impact = vel.magnitude;
                double tilt = Math.Acos(Clamp(Vector2d.Dot(Forward, pos.normalized), -1, 1)) * Mathf.Rad2Deg;
                double tolerance = hasLegs ? 12.0 : 9.0;
                if ((impact > tolerance || tilt > 40) && !cheats.unbreakable)
                {
                    status = FlightStatus.Crashed;
                    vel = Vector2d.zero;
                    if (OnMessage != null) OnMessage("Impact at " + impact.ToString("0") + " m/s");
                    if (OnCrash != null) OnCrash();
                }
                else
                {
                    pos = pos + pos.normalized * (groundR - lowR);
                    vel = Vector2d.zero;
                    angVel = 0;
                    status = FlightStatus.Landed;
                    if (everFlew && OnLanded != null) OnLanded();
                }
            }
            StepDebris(dt);
        }

        void StepDebris(double dt)
        {
            for (int i = 0; i < debris.Count; i++)
            {
                var d = debris[i];
                if (d.dead) continue;
                d.age += dt;
                double r = d.pos.magnitude;
                double alt = r - R;
                var up = d.pos / r;
                double rho = EarthAtmosphere.Density(alt);
                double speed = d.vel.magnitude;
                var acc = up * (-MU / (r * r));
                if (speed > 1e-3 && rho > 0) acc = acc + d.vel * (-(0.5 * rho * speed * d.cdA / 1000.0) / d.mass);
                d.vel = d.vel + acc * dt;
                d.pos = d.pos + d.vel * dt;
                d.angle += d.angVel * dt;
                if (alt < 0.5)
                {
                    d.impacted = speed > 15;
                    d.dead = true;
                }
            }
        }

        public Orbit2D CurrentOrbit() { return Orbit2D.From(pos, vel, MU); }

        public float LiquidFuelFraction()
        {
            float cur = 0f, max = 0f;
            foreach (int u in state.alive)
            {
                var p = state.byUid[u];
                if (p.Def.solidFuel || p.Def.isRcs) continue;
                float cap = PartMass.Fuel(p);
                if (cap <= 0f) continue;
                max += cap;
                float f;
                if (state.fuel.TryGetValue(u, out f)) cur += f;
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
                float f;
                if (state.fuel.TryGetValue(u, out f)) cur += f;
            }
            return max > 0f ? cur / max : -1f;
        }

        public bool AnyEngineAvailable()
        {
            foreach (int u in state.ignited) if (state.alive.Contains(u)) return true;
            return false;
        }

        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double MoveTowards(double v, double target, double step) { return Math.Abs(target - v) <= step ? target : v + Math.Sign(target - v) * step; }
    }
}
