using System.Collections.Generic;
using Apogee.Parts;
using UnityEngine;

namespace Apogee.Rocket
{
    /// <summary>
    /// Which parts are still attached, how much propellant each tank holds and which engines are lit.
    /// Shared by the builder's performance estimator and the live flight simulation.
    /// </summary>
    public class VehicleState
    {
        public RocketBlueprint bp;
        public int controlUid;
        public float extraPayload;
        public HashSet<int> alive = new HashSet<int>();
        public Dictionary<int, float> fuel = new Dictionary<int, float>();
        public HashSet<int> ignited = new HashSet<int>();
        public Dictionary<int, PartInstance> byUid = new Dictionary<int, PartInstance>();

        public VehicleState(RocketBlueprint bp, float extraPayload)
        {
            this.bp = bp;
            this.extraPayload = extraPayload;
            var c = StagingSystem.ControlPart(bp);
            controlUid = c != null ? c.uid : -1;
            foreach (var p in bp.parts)
            {
                alive.Add(p.uid);
                byUid[p.uid] = p;
                float f = PartMass.Fuel(p);
                if (f > 0f) fuel[p.uid] = f;
            }
        }

        public float Mass()
        {
            float m = extraPayload;
            foreach (int u in alive)
            {
                m += PartMass.Dry(byUid[u]);
                float f;
                if (fuel.TryGetValue(u, out f)) m += f;
            }
            return m;
        }

        public void ApplyStage(int stage, List<string> log)
        {
            foreach (var p in bp.parts)
            {
                if (p.stage != stage || !alive.Contains(p.uid)) continue;
                var d = p.Def;
                if (d.isDecoupler)
                {
                    var dropped = StagingSystem.Jettisoned(bp, p.uid, alive, controlUid);
                    foreach (int u in dropped) alive.Remove(u);
                    if (log != null) log.Add("Separate " + d.displayName);
                }
                else if (d.isFairing)
                {
                    alive.Remove(p.uid);
                    if (log != null) log.Add("Jettison " + d.displayName);
                }
                else if (d.IsEngine)
                {
                    ignited.Add(p.uid);
                    if (log != null) log.Add("Ignite " + d.displayName);
                }
                else if (d.isParachute && log != null) log.Add("Deploy " + d.displayName);
            }
        }

        /// <summary>Crossfeed groups: parts connected without passing through a decoupler.</summary>
        public Dictionary<int, int> FuelGroups()
        {
            var group = new Dictionary<int, int>();
            var adj = StagingSystem.Adjacency(bp, alive);
            int next = 0;
            foreach (int start in alive)
            {
                if (group.ContainsKey(start)) continue;
                int g = next++;
                var q = new Queue<int>();
                q.Enqueue(start);
                group[start] = g;
                if (byUid[start].Def.isDecoupler) continue; // decouplers are their own island
                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    foreach (int v in adj[u])
                    {
                        if (group.ContainsKey(v) || byUid[v].Def.isDecoupler) continue;
                        group[v] = g;
                        q.Enqueue(v);
                    }
                }
            }
            return group;
        }

        /// <summary>Key of the propellant pool an engine draws from (solids use their own casing).</summary>
        public int PoolKey(int engineUid, Dictionary<int, int> groups)
        {
            return byUid[engineUid].Def.solidFuel ? -(engineUid + 1) : groups[engineUid];
        }

        public float PoolFuel(int key, Dictionary<int, int> groups)
        {
            if (key < 0)
            {
                float f;
                return fuel.TryGetValue(-key - 1, out f) ? f : 0f;
            }
            float sum = 0f;
            foreach (var kv in fuel)
                if (alive.Contains(kv.Key) && !byUid[kv.Key].Def.solidFuel && groups[kv.Key] == key) sum += kv.Value;
            return sum;
        }

        public void Drain(int key, float amount, Dictionary<int, int> groups)
        {
            if (amount <= 0f) return;
            if (key < 0)
            {
                int u = -key - 1;
                fuel[u] = Mathf.Max(0f, fuel[u] - amount);
                return;
            }
            float total = PoolFuel(key, groups);
            if (total <= 0f) return;
            var keys = new List<int>(fuel.Keys);
            foreach (int u in keys)
            {
                if (!alive.Contains(u) || byUid[u].Def.solidFuel || groups[u] != key) continue;
                fuel[u] = Mathf.Max(0f, fuel[u] - amount * fuel[u] / total);
            }
        }

        public List<int> ActiveEngines(Dictionary<int, int> groups)
        {
            var list = new List<int>();
            foreach (int u in ignited)
                if (alive.Contains(u) && PoolFuel(PoolKey(u, groups), groups) > 1e-5f) list.Add(u);
            return list;
        }

        /// <summary>Parts the next stage would remove (used to decide when to stage).</summary>
        /// <summary>
        /// Flight version of ApplyStage: performs a stage and returns each separated group of parts
        /// (one set per decoupler or fairing) so the flight scene can turn them into falling debris.
        /// </summary>
        public List<HashSet<int>> ApplyStageCollect(int stage, List<string> log)
        {
            var groups = new List<HashSet<int>>();
            foreach (var p in bp.parts)
            {
                if (p.stage != stage || !alive.Contains(p.uid)) continue;
                var d = p.Def;
                if (d.isDecoupler)
                {
                    var dropped = StagingSystem.Jettisoned(bp, p.uid, alive, controlUid);
                    foreach (int u in dropped) alive.Remove(u);
                    if (dropped.Count > 0) groups.Add(dropped);
                    if (log != null) log.Add("Separation");
                }
                else if (d.isFairing)
                {
                    alive.Remove(p.uid);
                    groups.Add(new HashSet<int> { p.uid });
                    if (log != null) log.Add("Fairing jettison");
                }
                else if (d.IsEngine)
                {
                    ignited.Add(p.uid);
                    if (log != null && !log.Contains("Ignition")) log.Add("Ignition");
                }
                else if (d.isParachute && log != null && !log.Contains("Parachute")) log.Add("Parachute");
            }
            return groups;
        }

        public HashSet<int> WouldDrop(int stage)
        {
            var set = new HashSet<int>();
            var tmpAlive = new HashSet<int>(alive);
            foreach (var p in bp.parts)
            {
                if (p.stage != stage || !tmpAlive.Contains(p.uid)) continue;
                if (p.Def.isDecoupler)
                    foreach (int u in StagingSystem.Jettisoned(bp, p.uid, tmpAlive, controlUid)) set.Add(u);
                else if (p.Def.isFairing) set.Add(p.uid);
            }
            return set;
        }
    }
}
