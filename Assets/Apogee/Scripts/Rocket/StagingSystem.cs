using System.Collections.Generic;
using UnityEngine;

namespace Apogee.Rocket
{
    /// <summary>
    /// Builds and edits the activation sequence. Stage 0 fires at launch, then 1, 2, ...
    /// A stage can ignite engines, fire decouplers, jettison fairings and deploy parachutes.
    /// The same data drives the flight stage sequencer from Phase 2.
    /// </summary>
    public static class StagingSystem
    {
        /// <summary>The part that "is" the vehicle after separation: first command part, else the root.</summary>
        public static PartInstance ControlPart(RocketBlueprint bp)
        {
            var root = bp.Root;
            if (root != null && root.Def.isCommand) return root;
            foreach (var p in bp.parts) if (p.Def.isCommand) return p;
            return root;
        }

        public static Dictionary<int, List<int>> Adjacency(RocketBlueprint bp, HashSet<int> alive)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (var p in bp.parts)
            {
                if (alive != null && !alive.Contains(p.uid)) continue;
                if (!adj.ContainsKey(p.uid)) adj[p.uid] = new List<int>();
            }
            foreach (var p in bp.parts)
            {
                if (p.mode == AttachMode.Root) continue;
                if (!adj.ContainsKey(p.uid) || !adj.ContainsKey(p.parentUid)) continue;
                adj[p.uid].Add(p.parentUid);
                adj[p.parentUid].Add(p.uid);
            }
            return adj;
        }

        /// <summary>Parts that fall away when the decoupler fires: everything not connected to the control part, plus the decoupler.</summary>
        public static HashSet<int> Jettisoned(RocketBlueprint bp, int decouplerUid, HashSet<int> alive, int controlUid)
        {
            var dropped = new HashSet<int>();
            if (!alive.Contains(decouplerUid)) return dropped;
            var adj = Adjacency(bp, alive);
            var reached = new HashSet<int>();
            if (alive.Contains(controlUid) && controlUid != decouplerUid)
            {
                var q = new Queue<int>();
                q.Enqueue(controlUid);
                reached.Add(controlUid);
                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    foreach (int v in adj[u])
                    {
                        if (v == decouplerUid || reached.Contains(v)) continue;
                        reached.Add(v);
                        q.Enqueue(v);
                    }
                }
            }
            foreach (int u in alive) if (!reached.Contains(u)) dropped.Add(u);
            return dropped;
        }

        /// <summary>The decoupler an engine sits on (walking down its bottom node chain), or null if it can fire at launch.</summary>
        public static PartInstance BlockingDecoupler(RocketBlueprint bp, PartInstance engine)
        {
            var cur = engine;
            for (int i = 0; i < 64; i++)
            {
                var below = RocketAssembly.PartBelow(bp, cur);
                if (below == null) return null;
                if (below.Def.isDecoupler) return below;
                cur = below;
            }
            return null;
        }

        /// <summary>Computes the automatic stage of every staged part. Returns uid → stage and the stage count.</summary>
        public static Dictionary<int, int> AutoStages(RocketBlueprint bp, out int count)
        {
            var result = new Dictionary<int, int>();
            count = 0;
            var control = ControlPart(bp);
            if (control == null) return result;

            // Depth of each part = number of decouplers on the path from the control part.
            var adj = Adjacency(bp, null);
            var depth = new Dictionary<int, int>();
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            q.Enqueue(control.uid);
            depth[control.uid] = 0;
            dist[control.uid] = 0;
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                var up = bp.Find(u);
                int passDepth = depth[u] + (up != null && up.Def.isDecoupler && u != control.uid ? 1 : 0);
                foreach (int v in adj[u])
                {
                    if (depth.ContainsKey(v)) continue;
                    depth[v] = passDepth;
                    dist[v] = dist[u] + 1;
                    q.Enqueue(v);
                }
            }

            // Group decouplers that should fire together (symmetry counterparts).
            var groups = new Dictionary<int, List<PartInstance>>();
            foreach (var p in bp.parts)
            {
                if (!p.Def.isDecoupler || !depth.ContainsKey(p.uid)) continue;
                int key = p.symmetryGroup != 0 ? p.symmetryGroup : -p.uid;
                List<PartInstance> list;
                if (!groups.TryGetValue(key, out list)) { list = new List<PartInstance>(); groups[key] = list; }
                list.Add(p);
            }
            var ordered = new List<List<PartInstance>>(groups.Values);
            ordered.Sort((a, b) =>
            {
                int da = MaxOf(a, depth), db = MaxOf(b, depth);
                if (da != db) return db.CompareTo(da);        // deepest separations first
                return MaxOf(b, dist).CompareTo(MaxOf(a, dist)); // then farthest from the capsule
            });

            int stage = 0;
            var decouplerStage = new Dictionary<int, int>();
            foreach (var g in ordered)
            {
                stage++;
                foreach (var d in g) { result[d.uid] = stage; decouplerStage[d.uid] = stage; }
            }

            foreach (var p in bp.parts)
            {
                if (!p.Def.IsEngine || !depth.ContainsKey(p.uid)) continue;
                var blocker = BlockingDecoupler(bp, p);
                int s;
                result[p.uid] = blocker != null && decouplerStage.TryGetValue(blocker.uid, out s) ? s : 0;
            }

            int fairingStage = ordered.Count > 0 ? stage : 1;
            int maxStage = Mathf.Max(stage, 0);
            bool anyFairing = false;
            foreach (var p in bp.parts)
                if (p.Def.isFairing && depth.ContainsKey(p.uid)) { result[p.uid] = fairingStage; anyFairing = true; }
            if (anyFairing) maxStage = Mathf.Max(maxStage, fairingStage);

            bool anyChute = false;
            foreach (var p in bp.parts) if (p.Def.isParachute && depth.ContainsKey(p.uid)) anyChute = true;
            if (anyChute)
            {
                int chuteStage = maxStage + 1;
                foreach (var p in bp.parts) if (p.Def.isParachute && depth.ContainsKey(p.uid)) result[p.uid] = chuteStage;
            }

            count = Compact(result);
            return result;
        }

        static int MaxOf(List<PartInstance> list, Dictionary<int, int> map)
        {
            int m = 0;
            foreach (var p in list) { int v; if (map.TryGetValue(p.uid, out v)) m = Mathf.Max(m, v); }
            return m;
        }

        /// <summary>Renumbers used stages to 0..n-1 keeping their order. Returns n.</summary>
        static int Compact(Dictionary<int, int> assign)
        {
            var used = new SortedSet<int>(assign.Values);
            var remap = new Dictionary<int, int>();
            int i = 0;
            foreach (int s in used) remap[s] = i++;
            var keys = new List<int>(assign.Keys);
            foreach (int k in keys) assign[k] = remap[assign[k]];
            return i;
        }

        /// <summary>Re-applies staging after the vehicle changed. Custom edits are kept; new parts get automatic stages.</summary>
        public static void Restage(RocketBlueprint bp)
        {
            int autoCount;
            var auto = AutoStages(bp, out autoCount);
            var assign = new Dictionary<int, int>();
            foreach (var p in bp.parts)
            {
                if (!p.Def.IsStaged) { p.stage = -1; continue; }
                int a;
                if (!auto.TryGetValue(p.uid, out a)) a = 0;
                if (bp.customStaging && p.stage >= 0) assign[p.uid] = p.stage;
                else assign[p.uid] = bp.customStaging ? Mathf.Min(a, Mathf.Max(bp.stageCount, 0)) : a;
            }
            bp.stageCount = Compact(assign);
            foreach (var kv in assign) bp.Find(kv.Key).stage = kv.Value;
        }

        public static void ResetToAuto(RocketBlueprint bp)
        {
            bp.customStaging = false;
            foreach (var p in bp.parts) p.stage = -1;
            Restage(bp);
        }

        /// <summary>Moves a part (and its symmetry counterparts) one stage earlier (-1) or later (+1).
        /// Moving past either end creates a new stage there.</summary>
        public static void MovePart(RocketBlueprint bp, int uid, int delta)
        {
            var p = bp.Find(uid);
            if (p == null || p.stage < 0) return;
            bp.customStaging = true;
            int target = p.stage + delta;
            var members = bp.SymmetryMembers(p);
            if (target < 0)
            {
                foreach (var q in bp.parts) if (q.stage >= 0) q.stage += 1;
                target = 0;
            }
            foreach (var m in members) if (m.stage >= 0) m.stage = target;
            var assign = new Dictionary<int, int>();
            foreach (var q in bp.parts) if (q.stage >= 0) assign[q.uid] = q.stage;
            bp.stageCount = Compact(assign);
            foreach (var kv in assign) bp.Find(kv.Key).stage = kv.Value;
        }

        public static List<PartInstance> PartsInStage(RocketBlueprint bp, int stage)
        {
            var list = new List<PartInstance>();
            foreach (var p in bp.parts) if (p.stage == stage) list.Add(p);
            return list;
        }

        public static string ActionVerb(PartInstance p)
        {
            var d = p.Def;
            if (d.IsEngine) return "Ignite";
            if (d.isDecoupler) return "Separate";
            if (d.isFairing) return "Jettison";
            if (d.isParachute) return "Deploy";
            return "Activate";
        }
    }
}
