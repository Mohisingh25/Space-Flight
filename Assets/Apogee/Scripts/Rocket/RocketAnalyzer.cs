using System;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Simulation;
using UnityEngine;

namespace Apogee.Rocket
{
    public class StageStats
    {
        public int index;
        public float dvVac, dvAsl;
        public float twrAsl, twrVac;
        public float burnTime;
        public float startMass, endMass;
        public float thrustVac;
        public List<string> actions = new List<string>();
    }

    public enum Stability { Unknown, Unstable, Marginal, Stable }

    public class RocketStats
    {
        public int partCount;
        public float totalMass, dryMass, fuelMass;
        public float height, maxDiameter;
        public float launchThrustAsl, launchThrustVac, launchTwr;
        public float dvVacTotal, dvAslTotal, effectiveDv;
        public double maxAltitude; // m
        public bool escapes;
        public float payloadToLeo = -1f; // t, -1 = cannot reach orbit
        public Vector3 centerOfMass, centerOfThrust, centerOfPressure;
        public bool hasThrust;
        public float stabilityCalibers;
        public Stability stability;
        public int crew;
        public float batteryKWh, solarKW;
        public List<StageStats> stages = new List<StageStats>();
        public List<string> warnings = new List<string>();
    }

    /// <summary>
    /// Real-time engineering analysis shown in the builder: mass properties, thrust, stage-by-stage
    /// delta-v (Tsiolkovsky, integrated across engines that burn out at different times), an ascent
    /// simulation for maximum altitude, and a payload-to-orbit estimate.
    /// </summary>
    public static class RocketAnalyzer
    {
        const double G0 = PhysicsConstants.G0;

        enum Env { Vacuum, SeaLevel }

        // ---------------------------------------------------------------- delta-v

        static List<StageStats> StageDeltaV(RocketBlueprint bp, float extraPayload, Env env)
        {
            var result = new List<StageStats>();
            var v = new VehicleState(bp, extraPayload);
            int stages = bp.stageCount;
            for (int s = 0; s < stages; s++)
            {
                var st = new StageStats { index = s };
                v.ApplyStage(s, st.actions);
                st.startMass = v.Mass();

                var groups = v.FuelGroups();
                var dropNext = s + 1 < stages ? v.WouldDrop(s + 1) : new HashSet<int>();
                var active0 = v.ActiveEngines(groups);
                float thrust0 = 0f;
                foreach (int u in active0) thrust0 += Thrust(v.byUid[u].Def, env);
                st.thrustVac = 0f;
                foreach (int u in active0) st.thrustVac += v.byUid[u].Def.thrustVac;
                st.twrAsl = st.startMass > 0 ? (float)(SumThrust(v, active0, Env.SeaLevel) / (st.startMass * G0)) : 0f;
                st.twrVac = st.startMass > 0 ? (float)(SumThrust(v, active0, Env.Vacuum) / (st.startMass * G0)) : 0f;

                bool waitForSubset = false;
                foreach (int u in active0) if (dropNext.Contains(u)) waitForSubset = true;

                double dv = 0, time = 0;
                for (int seg = 0; seg < 64; seg++)
                {
                    var active = v.ActiveEngines(groups);
                    if (active.Count == 0) break;
                    if (waitForSubset)
                    {
                        bool any = false;
                        foreach (int u in active) if (dropNext.Contains(u)) any = true;
                        if (!any) break;
                    }
                    // mass flow per pool
                    var poolFlow = new Dictionary<int, double>();
                    double F = 0, mdot = 0;
                    foreach (int u in active)
                    {
                        var d = v.byUid[u].Def;
                        double f = Thrust(d, env);
                        double flow = d.thrustVac / (d.ispVac * G0); // t/s — constant regardless of ambient pressure
                        F += f;
                        mdot += flow;
                        int key = v.PoolKey(u, groups);
                        double cur;
                        poolFlow.TryGetValue(key, out cur);
                        poolFlow[key] = cur + flow;
                    }
                    if (mdot <= 0) break;
                    double dt = double.MaxValue;
                    foreach (var kv in poolFlow) dt = Math.Min(dt, v.PoolFuel(kv.Key, groups) / kv.Value);
                    if (dt <= 0 || double.IsInfinity(dt)) break;
                    double m0 = v.Mass();
                    double m1 = m0 - mdot * dt;
                    if (m1 <= 0) break;
                    dv += (F / mdot) * Math.Log(m0 / m1);
                    time += dt;
                    foreach (var kv in poolFlow) v.Drain(kv.Key, (float)(kv.Value * dt), groups);
                }
                st.burnTime = (float)time;
                st.endMass = v.Mass();
                if (env == Env.Vacuum) st.dvVac = (float)dv; else st.dvAsl = (float)dv;
                result.Add(st);
            }
            return result;
        }

        static double SumThrust(VehicleState v, List<int> engines, Env env)
        {
            double f = 0;
            foreach (int u in engines) f += Thrust(v.byUid[u].Def, env);
            return f;
        }

        static float Thrust(PartDefinition d, Env env) { return env == Env.Vacuum ? d.thrustVac : d.thrustSL; }

        // ---------------------------------------------------------------- ascent simulation

        /// <summary>
        /// Vertical ascent at full throttle with gravity falling off with altitude, exponential atmosphere
        /// drag and pressure-dependent engine performance. Stages automatically when the parts it would
        /// drop have burned out. Returns apex altitude (metres) or +∞ if the vehicle reaches escape velocity.
        /// </summary>
        public static double SimulateVerticalAscent(RocketBlueprint bp, out bool escapes)
        {
            escapes = false;
            if (bp.stageCount == 0) return 0;
            var v = new VehicleState(bp, 0f);
            int stage = 0;
            v.ApplyStage(0, null);
            var groups = v.FuelGroups();

            // Drag: frontal area from the widest part, Cd from the topmost part (nose cone vs blunt capsule).
            float maxD = 0f, topY = float.MinValue, cd = 0.8f;
            var poses = RocketLayout.Compute(bp);
            foreach (var p in bp.parts)
            {
                var dims = PartDims.Of(p);
                if (RocketAssembly.CanStack(p.Def)) maxD = Mathf.Max(maxD, dims.diameter);
                PartPose pose;
                if (poses.TryGetValue(p.uid, out pose) && RocketAssembly.CanStack(p.Def))
                {
                    float ty = pose.position.y + dims.height * 0.5f;
                    if (ty > topY) { topY = ty; cd = p.Def.dragCoefficient; }
                }
            }
            double area = Math.PI * maxD * maxD * 0.25;

            double h = 0, vel = 0, t = 0;
            double apex = 0;
            HashSet<int> nextDrop = stage + 1 < bp.stageCount ? v.WouldDrop(stage + 1) : null;
            for (int step = 0; step < 400000; step++)
            {
                var active = v.ActiveEngines(groups);

                // Stage when everything the next stage would drop has burned out (or nothing is burning).
                while (nextDrop != null)
                {
                    bool burningInDrop = false, spentInDrop = false;
                    foreach (int u in active) if (nextDrop.Contains(u)) burningInDrop = true;
                    foreach (int u in v.ignited) if (nextDrop.Contains(u) && !active.Contains(u)) spentInDrop = true;
                    bool shouldStage = !burningInDrop && (spentInDrop || active.Count == 0);
                    if (!shouldStage) break;
                    stage++;
                    v.ApplyStage(stage, null);
                    groups = v.FuelGroups();
                    active = v.ActiveEngines(groups);
                    nextDrop = stage + 1 < bp.stageCount ? v.WouldDrop(stage + 1) : null;
                }

                double m = v.Mass();
                if (m <= 0) break;
                double pr = EarthAtmosphere.PressureRatio(h);
                double F = 0, mdot = 0;
                var poolFlow = new Dictionary<int, double>();
                foreach (int u in active)
                {
                    var d = v.byUid[u].Def;
                    double flow = d.thrustVac / (d.ispVac * G0);
                    double isp = d.ispVac + (d.ispSL - d.ispVac) * pr;
                    F += flow * isp * G0;
                    mdot += flow;
                    int key = v.PoolKey(u, groups);
                    double cur;
                    poolFlow.TryGetValue(key, out cur);
                    poolFlow[key] = cur + flow;
                }

                double rho = EarthAtmosphere.Density(h);
                double drag = 0.5 * rho * vel * vel * cd * area / 1000.0; // kN
                double g = EarthAtmosphere.Gravity(h);
                double a = F / m - g - Math.Sign(vel) * drag / m;

                // Coasting above the atmosphere: finish analytically with energy conservation.
                if (active.Count == 0 && h > EarthAtmosphere.Ceiling)
                {
                    double r = PhysicsConstants.EarthRadius + h;
                    double energy = 0.5 * vel * vel - PhysicsConstants.EarthMu / r;
                    if (energy >= 0 && vel > 0) { escapes = true; return double.PositiveInfinity; }
                    if (vel > 0) apex = Math.Max(apex, PhysicsConstants.EarthMu / (-energy) - PhysicsConstants.EarthRadius);
                    else apex = Math.Max(apex, h);
                    return apex;
                }

                double dt = h > 100000 ? 1.0 : 0.2;
                if (h <= 0 && a < 0 && vel <= 0) { if (active.Count == 0 || t > 30) break; vel = 0; a = 0; }

                vel += a * dt;
                h += vel * dt;
                t += dt;
                if (h < 0) { h = 0; if (vel < 0) vel = 0; }
                apex = Math.Max(apex, h);

                foreach (var kv in poolFlow) v.Drain(kv.Key, (float)(kv.Value * dt), groups);

                if (vel < 0 && active.Count == 0 && t > 1) break; // falling with engines out
                if (vel > 0)
                {
                    double esc = Math.Sqrt(2 * PhysicsConstants.EarthMu / (PhysicsConstants.EarthRadius + h));
                    if (vel >= esc && h > EarthAtmosphere.Ceiling && active.Count == 0) { escapes = true; return double.PositiveInfinity; }
                }
                if (t > 4 * 3600) break;
            }
            return apex;
        }

        // ---------------------------------------------------------------- full report

        public static RocketStats Analyze(RocketBlueprint bp)
        {
            var s = new RocketStats();
            s.partCount = bp.parts.Count;
            if (bp.IsEmpty) { s.stability = Stability.Unknown; return s; }

            var poses = RocketLayout.Compute(bp);
            var bounds = RocketLayout.ComputeBounds(bp, poses);
            s.height = bounds.size.y;

            Vector3 comSum = Vector3.zero;
            float areaSum = 0f, areaY = 0f;
            foreach (var p in bp.parts)
            {
                var d = p.Def;
                float dry = PartMass.Dry(p), fuel = PartMass.Fuel(p);
                s.dryMass += dry;
                s.fuelMass += fuel;
                s.crew += d.crew;
                s.batteryKWh += d.batteryKWh;
                s.solarKW += d.solarKW;
                PartPose pose;
                if (!poses.TryGetValue(p.uid, out pose)) continue;
                comSum += pose.position * (dry + fuel);
                var dims = PartDims.Of(p);
                if (RocketAssembly.CanStack(d)) s.maxDiameter = Mathf.Max(s.maxDiameter, dims.diameter);
                float lateral = 0f;
                if (d.finArea > 0f) lateral = d.finArea * 8f; // lifting surfaces dominate the centre of pressure
                else if (RocketAssembly.CanStack(d)) lateral = 0.5f * (dims.diameter + dims.topDiameter) * dims.height;
                areaSum += lateral;
                areaY += lateral * pose.position.y;
            }
            s.totalMass = s.dryMass + s.fuelMass;
            s.centerOfMass = s.totalMass > 0 ? comSum / s.totalMass : Vector3.zero;
            s.centerOfPressure = new Vector3(0f, areaSum > 0 ? areaY / areaSum : 0f, 0f);

            // Launch thrust and centre of thrust (stage-0 engines).
            Vector3 cotSum = Vector3.zero;
            foreach (var p in bp.parts)
            {
                if (!p.Def.IsEngine || p.stage != 0) continue;
                s.launchThrustAsl += p.Def.thrustSL;
                s.launchThrustVac += p.Def.thrustVac;
                PartPose pose;
                if (poses.TryGetValue(p.uid, out pose)) cotSum += RocketLayout.BottomNode(p, pose) * p.Def.thrustSL;
            }
            s.hasThrust = s.launchThrustAsl > 0f;
            s.centerOfThrust = s.hasThrust ? cotSum / s.launchThrustAsl : Vector3.zero;
            s.launchTwr = s.totalMass > 0 ? (float)(s.launchThrustAsl / (s.totalMass * G0)) : 0f;

            // Stage-by-stage delta-v in vacuum and at sea level.
            var vac = StageDeltaV(bp, 0f, Env.Vacuum);
            var asl = StageDeltaV(bp, 0f, Env.SeaLevel);
            for (int i = 0; i < vac.Count; i++)
            {
                vac[i].dvAsl = i < asl.Count ? asl[i].dvAsl : 0f;
                s.dvVacTotal += vac[i].dvVac;
                s.dvAslTotal += vac[i].dvAsl;
            }
            s.stages = vac;
            s.effectiveDv = EffectiveDv(vac);

            // Stability: calibers between centre of mass and centre of pressure.
            if (s.maxDiameter > 0 && areaSum > 0)
            {
                s.stabilityCalibers = (s.centerOfMass.y - s.centerOfPressure.y) / s.maxDiameter;
                s.stability = s.stabilityCalibers >= 1f ? Stability.Stable : s.stabilityCalibers >= 0f ? Stability.Marginal : Stability.Unstable;
            }

            bool esc;
            s.maxAltitude = s.launchTwr > 1f ? SimulateVerticalAscent(bp, out esc) : 0;
            s.escapes = s.launchTwr > 1f && double.IsPositiveInfinity(s.maxAltitude);
            s.payloadToLeo = EstimatePayloadToLeo(bp);

            BuildWarnings(bp, s);
            return s;
        }

        static float EffectiveDv(List<StageStats> stages)
        {
            float sum = 0f;
            for (int i = 0; i < stages.Count; i++)
            {
                var st = stages[i];
                // Stages burning in the lower atmosphere get a blend of sea-level and vacuum performance.
                sum += i == 0 ? 0.4f * st.dvAsl + 0.6f * st.dvVac : st.dvVac;
            }
            return sum;
        }

        static bool ReachesOrbit(RocketBlueprint bp, float payload)
        {
            var vac = StageDeltaV(bp, payload, Env.Vacuum);
            if (vac.Count == 0 || vac[0].twrAsl < 1.15f) return false;
            var asl = StageDeltaV(bp, payload, Env.SeaLevel);
            for (int i = 0; i < vac.Count && i < asl.Count; i++) vac[i].dvAsl = asl[i].dvAsl;
            return EffectiveDv(vac) >= PhysicsConstants.LeoDeltaV;
        }

        /// <summary>Largest extra mass on the final stage that still reaches low Earth orbit, or -1.</summary>
        public static float EstimatePayloadToLeo(RocketBlueprint bp)
        {
            if (bp.stageCount == 0 || !ReachesOrbit(bp, 0f)) return -1f;
            float lo = 0f, hi = 1f;
            while (hi < 2000f && ReachesOrbit(bp, hi)) { lo = hi; hi *= 2f; }
            for (int i = 0; i < 20; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (ReachesOrbit(bp, mid)) lo = mid; else hi = mid;
            }
            return lo;
        }

        static void BuildWarnings(RocketBlueprint bp, RocketStats s)
        {
            bool hasCommand = false, hasChute = false, hasEngine = false;
            foreach (var p in bp.parts)
            {
                if (p.Def.isCommand) hasCommand = true;
                if (p.Def.isParachute) hasChute = true;
                if (p.Def.IsEngine) hasEngine = true;
            }
            if (!hasCommand) s.warnings.Add("No command part — add a capsule or probe core to control the vehicle.");
            if (!hasEngine) s.warnings.Add("No engines.");
            else if (!s.hasThrust) s.warnings.Add("No engine fires at launch (stage 1).");
            else if (s.launchTwr < 1f) s.warnings.Add("Launch TWR below 1 — the rocket cannot lift off.");
            else if (s.launchTwr < 1.2f) s.warnings.Add("Launch TWR under 1.2 — slow, wasteful climb.");
            if (s.stability == Stability.Unstable) s.warnings.Add("Aerodynamically unstable — centre of pressure is above the centre of mass. Add fins low on the rocket or a nose cone.");
            if (s.crew > 0 && !hasChute) s.warnings.Add("Crewed vehicle without a parachute — the crew cannot land safely.");

            // Engines with no propellant source.
            var v = new VehicleState(bp, 0f);
            var groups = v.FuelGroups();
            foreach (var p in bp.parts)
            {
                if (!p.Def.IsEngine) continue;
                if (v.PoolFuel(v.PoolKey(p.uid, groups), groups) <= 0f)
                {
                    s.warnings.Add(p.Def.displayName + " has no fuel tank connected.");
                    break;
                }
            }
        }
    }
}
