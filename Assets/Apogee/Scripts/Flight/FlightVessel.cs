using System;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rocket;
using Apogee.Simulation;
using UnityEngine;

namespace Apogee.Flight
{
    public enum FlightStatus { OnPad, Flying, Landed, Crashed }

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
                if (stack || d.isLeg)
                {
                    minY = Mathf.Min(minY, bottom);
                    maxY = Mathf.Max(maxY, top);
                }
                if (stack)
                {
                    maxD = Mathf.Max(maxD, Mathf.Abs(pose.position.x) * 2f + dims.diameter);
                    if (top > topY) { topY = top; cd = d.dragCoefficient; }
                }
                if (d.isLeg) hasLegs = true;
                if (d.isParachute) chuteCount++;
                if (d.isCommand) reactionTorque += d.crew >= 3 ? 60f : d.crew >= 1 ? 25f : 8f;
                if (d.isRcs) reactionTorque += 6f;
            }
            if (m <= 0f) m = 0.001f;
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
            nextStage++;
            if (log.Contains("Ignition")) enginesOn = true;
            if (log.Contains("Parachute")) chuteArmed = true;
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

            if (OnMessage != null) OnMessage(log.Count > 0 ? string.Join(" · ", log.ToArray()) : "Stage " + nextStage);
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
            double F = 0, gimbalTorque = 0;
            engineOutput.Clear();
            poolFlow.Clear();
            bool burning = false;
            foreach (int u in state.ignited)
            {
                if (!state.alive.Contains(u)) continue;
                var d = state.byUid[u].Def;
                float thr = d.solidFuel ? 1f : (enginesOn && throttle > 0.001f ? Mathf.Max(throttle, d.minThrottle) : 0f);
                if (thr <= 0f) continue;
                int key = state.PoolKey(u, fuelGroups);
                if (state.PoolFuel(key, fuelGroups) <= 1e-5f) continue;
                double flow = d.thrustVac / (d.ispVac * G0) * thr;
                double isp = d.ispVac + (d.ispSL - d.ispVac) * pr;
                double Fi = flow * isp * G0;
                F += Fi;
                double cur;
                poolFlow.TryGetValue(key, out cur);
                poolFlow[key] = cur + flow;
                var pose = Pose(u);
                double lever = Math.Sqrt(Math.Pow(pose.position.x - comLocal.x, 2) + Math.Pow(pose.position.y - comLocal.y, 2));
                gimbalTorque += Fi * Math.Sin(d.gimbalRange * Mathf.Deg2Rad) * lever;
                engineOutput[u] = thr;
                burning = true;
            }
            foreach (var kv in poolFlow) state.Drain(kv.Key, (float)(kv.Value * dt), fuelGroups);
            if (burning) Recalc();

            // ---- forces
            var gravity = up * (-MU / (r * r));
            double speed = vel.magnitude;
            double cdA = cd * frontalArea;
            if (chuteArmed && chuteCount > 0)
            {
                // A small drogue streams as soon as the chute is armed in the atmosphere; the main canopy
                // (~20 m) opens once the drogue has slowed the capsule below ~300 m/s.
                if (alt < 30000) cdA += chuteCount * 1.0 * Math.PI * 2.5 * 2.5;
                if (speed < 300 && alt < 25000) chuteOpen = Mathf.Min(1f, chuteOpen + (float)dt * 0.45f);
                cdA += chuteOpen * chuteCount * 1.3 * Math.PI * 100.0;
            }
            var dragAcc = Vector2d.zero;
            if (speed > 1e-3 && rho > 0)
                dragAcc = vel * (-(0.5 * rho * speed * cdA / 1000.0) / mass); // kN / t = m/s²
            var thrustAcc = f * (F / mass);
            var acc = thrustAcc + dragAcc + gravity;
            lastAcceleration = (thrustAcc + dragAcc).magnitude;

            // ---- rotation: reaction wheels + gimbal, steered by the player, damped when hands-off
            double maxAcc = (reactionTorque + gimbalTorque) / inertia;
            double maxRate = 0.9;
            if (turnInput != 0)
                angVel = Clamp(angVel + turnInput * maxAcc * dt, -maxRate, maxRate);
            else
                angVel = MoveTowards(angVel, 0, maxAcc * dt);

            // aerodynamic weathervaning: stable rockets turn nose-first into the airflow; chutes hang the capsule
            if (speed > 3 && rho > 1e-6 && status == FlightStatus.Flying)
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
                if (impact > tolerance || tilt > 40)
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
                if (p.Def.solidFuel) continue;
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
