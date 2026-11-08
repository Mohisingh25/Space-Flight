using System.Collections.Generic;
using Apogee.Parts;
using UnityEngine;

namespace Apogee.Rocket
{
    /// <summary>A detached group of parts being dragged around in the builder.</summary>
    public class HeldAssembly
    {
        public List<PartInstance> parts = new List<PartInstance>(); // parts[0] is the root
        public int symmetry = 1;
        public bool mirror;      // also place a mirror image (reflected left↔right) of radial / side parts

        public PartInstance Root { get { return parts.Count > 0 ? parts[0] : null; } }

        public RocketBlueprint AsBlueprint()
        {
            var bp = new RocketBlueprint();
            foreach (var p in parts) bp.parts.Add(p.Clone());
            bp.parts[0].mode = AttachMode.Root;
            bp.parts[0].parentUid = -1;
            return bp;
        }

        public static HeldAssembly ForNewPart(PartDefinition def)
        {
            var h = new HeldAssembly();
            h.parts.Add(new PartInstance { uid = 1, partId = def.id, mode = AttachMode.Root, parentUid = -1 });
            return h;
        }
    }

    public struct AttachCandidate
    {
        public bool valid;
        public int parentUid;
        public AttachMode mode;
        public float angle;
        public float heightFrac;
        public Vector3 nodePosition; // rocket space
    }

    /// <summary>
    /// All editing operations on a blueprint: attaching with symmetry, removing, moving,
    /// duplicating, rotating and resizing. Pure data — the builder UI and views call into this.
    /// </summary>
    public static class RocketAssembly
    {
        // ------------------------------------------------------------------ queries

        public static bool CanStack(PartDefinition d) { return d.hasTopNode || d.hasBottomNode; }

        public static bool TopOccupied(RocketBlueprint bp, PartInstance p)
        {
            if (p.mode == AttachMode.Below) return true; // our top touches our parent
            foreach (var c in bp.ChildrenOf(p.uid)) if (c.mode == AttachMode.Above) return true;
            return false;
        }

        public static bool BottomOccupied(RocketBlueprint bp, PartInstance p)
        {
            if (p.mode == AttachMode.Above) return true; // our bottom sits on our parent
            foreach (var c in bp.ChildrenOf(p.uid)) if (c.mode == AttachMode.Below) return true;
            return false;
        }

        public static bool OuterOccupied(RocketBlueprint bp, PartInstance p)
        {
            foreach (var c in bp.ChildrenOf(p.uid)) if (c.mode == AttachMode.Side) return true;
            return false;
        }

        /// <summary>The part attached at p's bottom node, if any.</summary>
        public static PartInstance PartBelow(RocketBlueprint bp, PartInstance p)
        {
            foreach (var c in bp.ChildrenOf(p.uid)) if (c.mode == AttachMode.Below) return c;
            if (p.mode == AttachMode.Above) return bp.Find(p.parentUid);
            return null;
        }

        /// <summary>Every free node the held part's root could snap to.</summary>
        public static List<AttachCandidate> StackCandidates(RocketBlueprint bp, Dictionary<int, PartPose> poses, PartDefinition held)
        {
            var list = new List<AttachCandidate>();
            foreach (var p in bp.parts)
            {
                PartPose pose;
                if (!poses.TryGetValue(p.uid, out pose)) continue;
                var def = p.Def;
                if (held.hasTopNode && def.hasBottomNode && !BottomOccupied(bp, p))
                    list.Add(new AttachCandidate { valid = true, parentUid = p.uid, mode = AttachMode.Below, nodePosition = RocketLayout.BottomNode(p, pose) });
                if (held.hasBottomNode && def.hasTopNode && !TopOccupied(bp, p))
                    list.Add(new AttachCandidate { valid = true, parentUid = p.uid, mode = AttachMode.Above, nodePosition = RocketLayout.TopNode(p, pose) });
                if (def.hasOuterNode && CanStack(held) && !OuterOccupied(bp, p))
                    list.Add(new AttachCandidate { valid = true, parentUid = p.uid, mode = AttachMode.Side, heightFrac = 0.3f, nodePosition = RocketLayout.OuterNode(p, pose) });
            }
            return list;
        }

        /// <summary>Converts a surface hit (rocket space) on a part into a radial attach candidate.</summary>
        public static AttachCandidate SurfaceCandidate(RocketBlueprint bp, Dictionary<int, PartPose> poses, int parentUid, Vector3 hitPoint)
        {
            var c = new AttachCandidate();
            var parent = bp.Find(parentUid);
            PartPose pose;
            if (parent == null || !parent.Def.acceptsRadial || !poses.TryGetValue(parentUid, out pose)) return c;
            var dims = PartDims.Of(parent);
            Vector3 local = Quaternion.Inverse(pose.rotation) * (hitPoint - pose.position);
            float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 5f) * 5f;
            float hf = dims.height > 0.01f ? local.y / dims.height : 0f;
            hf = Mathf.Clamp(Mathf.Round(hf * 20f) / 20f, -0.45f, 0.45f);
            c.valid = true;
            c.parentUid = parentUid;
            c.mode = AttachMode.Surface;
            c.angle = angle;
            c.heightFrac = hf;
            float a = angle * Mathf.Deg2Rad;
            c.nodePosition = pose.position + pose.rotation * new Vector3(Mathf.Cos(a) * dims.RadiusAt(hf), hf * dims.height, Mathf.Sin(a) * dims.RadiusAt(hf));
            return c;
        }

        /// <summary>
        /// Side-by-side placement for stack parts (tanks, engines, boosters, girders): finds the part whose side the
        /// held part's centre (rocket space) is beside, and returns a Flank candidate that stands it upright
        /// against that side. maxGap is how far from touching the held part may still be and snap.
        /// </summary>
        public static AttachCandidate FlankCandidate(RocketBlueprint bp, Dictionary<int, PartPose> poses, PartDims held, Vector3 point, float maxGap)
        {
            var best = new AttachCandidate();
            float bestGap = maxGap;
            float hr = Mathf.Max(held.Radius, held.TopRadius);
            foreach (var p in bp.parts)
            {
                var def = p.Def;
                if (!def.acceptsRadial || !CanStack(def)) continue;
                PartPose pose;
                if (!poses.TryGetValue(p.uid, out pose)) continue;
                var dims = PartDims.Of(p);
                Vector3 local = Quaternion.Inverse(pose.rotation) * (point - pose.position);
                float hf = dims.height > 0.01f ? local.y / dims.height : 0f;
                if (hf < -0.75f || hf > 0.75f) continue;
                hf = Mathf.Clamp(Mathf.Round(hf * 20f) / 20f, -0.5f, 0.5f);
                float radial = new Vector2(local.x, local.z).magnitude;
                float gap = Mathf.Abs(radial - (dims.RadiusAt(hf) + hr));
                if (gap >= bestGap) continue;
                float angle = Mathf.Round(Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg / 5f) * 5f;
                bestGap = gap;
                best.valid = true;
                best.parentUid = p.uid;
                best.mode = AttachMode.Flank;
                best.angle = angle;
                best.heightFrac = hf;
                float a = angle * Mathf.Deg2Rad;
                best.nodePosition = pose.position + pose.rotation * new Vector3(Mathf.Cos(a) * dims.RadiusAt(hf), hf * dims.height, Mathf.Sin(a) * dims.RadiusAt(hf));
            }
            return best;
        }

        /// <summary>
        /// Auto-shaping: where two stacked parts have different widths, the wider fuel tank gets a conical
        /// end that meets the narrower part, so stages blend into one smooth hull instead of a step.
        /// </summary>
        public static void AutoTaper(RocketBlueprint bp)
        {
            foreach (var p in bp.parts) { p.taperBottom = 0f; p.taperTop = 0f; }
            foreach (var c in bp.parts)
            {
                if (c.parentUid < 0 || (c.mode != AttachMode.Below && c.mode != AttachMode.Above)) continue;
                var parent = bp.Find(c.parentUid);
                if (parent == null) continue;
                var upper = c.mode == AttachMode.Below ? parent : c;
                var lower = c.mode == AttachMode.Below ? c : parent;
                float du = PartDims.Of(upper).diameter, dl = PartDims.Of(lower).topDiameter;
                if (Mathf.Abs(du - dl) < 0.05f) continue;
                if (du > dl && upper.Def.shape == PartShape.Tank) upper.taperBottom = dl;
                else if (dl > du && lower.Def.shape == PartShape.Tank) lower.taperTop = du;
            }
        }

        public static bool IsRadialMode(AttachMode m) { return m == AttachMode.Surface || m == AttachMode.Flank; }

        // ------------------------------------------------------------------ edits

        /// <summary>Places a held assembly as the first (root) part of an empty blueprint.</summary>
        public static void PlaceAsRoot(RocketBlueprint bp, HeldAssembly held)
        {
            if (!bp.IsEmpty || held.Root == null) return;
            var map = new Dictionary<int, int>();
            var groupMap = new Dictionary<string, int>();
            foreach (var src in held.parts)
            {
                var p = src.Clone();
                map[src.uid] = bp.nextUid++;
                p.uid = map[src.uid];
                bp.parts.Add(p);
            }
            for (int i = 0; i < held.parts.Count; i++)
            {
                var p = bp.parts[bp.parts.Count - held.parts.Count + i];
                var src = held.parts[i];
                if (i == 0) { p.mode = AttachMode.Root; p.parentUid = -1; }
                else p.parentUid = map[src.parentUid];
                if (src.symmetryGroup != 0) p.symmetryGroup = GroupFor(bp, groupMap, "g" + src.symmetryGroup);
                p.stage = -1;
            }
            StagingSystem.Restage(bp);
        }

        /// <summary>
        /// Attaches a held assembly at a candidate. With symmetry, copies go around the parent
        /// (radial) and onto every symmetry counterpart of the parent. Returns the new root uids.
        /// </summary>
        public static List<int> Attach(RocketBlueprint bp, HeldAssembly held, AttachCandidate cand)
        {
            var newRoots = new List<int>();
            var target = bp.Find(cand.parentUid);
            if (target == null || held.Root == null || !cand.valid) return newRoots;

            var targets = bp.SymmetryMembers(target);
            // angles (and yaw sign) of the copies placed on each target
            var angles = new List<float>();
            var yawSign = new List<float>();
            if (IsRadialMode(cand.mode))
            {
                int sym = Mathf.Max(1, held.symmetry);
                for (int k = 0; k < sym; k++) { angles.Add(cand.angle + k * 360f / sym); yawSign.Add(1f); }
                if (held.mirror)
                {
                    int n0 = angles.Count;
                    for (int k = 0; k < n0; k++)
                    {
                        float m = Mathf.Repeat(180f - angles[k], 360f);
                        bool dup = false;
                        foreach (var a in angles) if (Mathf.Abs(Mathf.DeltaAngle(a, m)) < 1f) dup = true;
                        if (!dup) { angles.Add(m); yawSign.Add(-1f); }
                    }
                }
            }
            else { angles.Add(cand.angle); yawSign.Add(1f); }
            int perTarget = angles.Count;
            int total = 0;
            foreach (var t in targets) if (NodeFree(bp, t, cand.mode)) total += perTarget;
            if (total == 0) return newRoots;

            var groupMap = new Dictionary<string, int>();
            foreach (var t in targets)
            {
                if (!NodeFree(bp, t, cand.mode)) continue;
                for (int k = 0; k < perTarget; k++)
                {
                    var map = new Dictionary<int, int>();
                    foreach (var src in held.parts) map[src.uid] = bp.nextUid++;
                    for (int i = 0; i < held.parts.Count; i++)
                    {
                        var src = held.parts[i];
                        var p = src.Clone();
                        p.uid = map[src.uid];
                        p.stage = -1;
                        if (i == 0)
                        {
                            p.parentUid = t.uid;
                            p.mode = cand.mode;
                            p.angle = angles[k];
                            p.yaw *= yawSign[k];
                            p.heightFrac = cand.heightFrac;
                            p.symmetryGroup = total > 1 ? GroupFor(bp, groupMap, "root") : 0;
                            newRoots.Add(p.uid);
                        }
                        else
                        {
                            p.parentUid = map[src.parentUid];
                            if (total > 1)
                                p.symmetryGroup = GroupFor(bp, groupMap, src.symmetryGroup != 0 ? "g" + src.symmetryGroup : "u" + src.uid);
                            else if (src.symmetryGroup != 0)
                                p.symmetryGroup = GroupFor(bp, groupMap, "g" + src.symmetryGroup);
                            else p.symmetryGroup = 0;
                        }
                        bp.parts.Add(p);
                    }
                }
            }
            StagingSystem.Restage(bp);
            return newRoots;
        }

        static bool NodeFree(RocketBlueprint bp, PartInstance t, AttachMode mode)
        {
            switch (mode)
            {
                case AttachMode.Below: return !BottomOccupied(bp, t);
                case AttachMode.Above: return !TopOccupied(bp, t);
                case AttachMode.Side: return !OuterOccupied(bp, t);
                default: return true;
            }
        }

        static int GroupFor(RocketBlueprint bp, Dictionary<string, int> map, string key)
        {
            int g;
            if (!map.TryGetValue(key, out g)) { g = bp.nextSymmetryGroup++; map[key] = g; }
            return g;
        }

        /// <summary>Deletes a part, its symmetry counterparts and everything attached to them.</summary>
        public static int Remove(RocketBlueprint bp, int uid)
        {
            var p = bp.Find(uid);
            if (p == null) return 0;
            var doomed = new HashSet<int>();
            foreach (var m in bp.SymmetryMembers(p))
                foreach (var s in bp.Subtree(m.uid)) doomed.Add(s.uid);
            int n = bp.parts.RemoveAll(q => doomed.Contains(q.uid));
            StagingSystem.Restage(bp);
            return n;
        }

        /// <summary>Copies a part's subtree into a new held assembly (for duplicate / move).</summary>
        public static HeldAssembly CopySubtree(RocketBlueprint bp, int uid)
        {
            var held = new HeldAssembly();
            var p = bp.Find(uid);
            if (p == null) return held;
            foreach (var s in bp.Subtree(uid)) held.parts.Add(s.Clone());
            held.parts[0].mode = AttachMode.Root;
            held.parts[0].parentUid = -1;
            int members = bp.SymmetryMembers(p).Count;
            held.symmetry = IsRadialMode(p.mode) ? Mathf.Max(1, CountSameParent(bp, p)) : 1;
            if (members <= 1) held.symmetry = 1;
            return held;
        }

        static int CountSameParent(RocketBlueprint bp, PartInstance p)
        {
            int n = 0;
            foreach (var m in bp.SymmetryMembers(p)) if (m.parentUid == p.parentUid) n++;
            return n;
        }

        /// <summary>Pulls a part (and counterparts) off the rocket so it can be dragged elsewhere.</summary>
        public static HeldAssembly Detach(RocketBlueprint bp, int uid, out string error)
        {
            error = null;
            var p = bp.Find(uid);
            if (p == null) { error = "Part not found."; return null; }
            if (p.mode == AttachMode.Root && bp.parts.Count > 1)
            {
                error = "The root part holds the rocket together. Move the parts attached to it instead.";
                return null;
            }
            var held = CopySubtree(bp, uid);
            Remove(bp, uid);
            return held;
        }

        public static void Rotate(RocketBlueprint bp, int uid)
        {
            var p = bp.Find(uid);
            if (p == null) return;
            bool radial = IsRadialMode(p.mode);
            foreach (var m in bp.SymmetryMembers(p)) m.yaw = Mathf.Repeat(m.yaw + (radial ? 90f : 45f), 360f);
        }

        public static int CurrentSizeStep(PartInstance p)
        {
            if (p.sizeStep >= 0) return p.sizeStep;
            float d = p.Def.diameter;
            int best = 0;
            for (int i = 0; i < PartCatalog.DiameterSteps.Length; i++)
                if (Mathf.Abs(PartCatalog.DiameterSteps[i] - d) < Mathf.Abs(PartCatalog.DiameterSteps[best] - d)) best = i;
            return best;
        }

        public static int CurrentLengthStep(PartInstance p)
        {
            return p.lengthStep >= 0 ? p.lengthStep : 2; // index of 1x
        }

        /// <summary>Changes diameter by +/- one step. Returns false if not resizable or at the limit.</summary>
        public static bool Resize(RocketBlueprint bp, int uid, int delta)
        {
            var p = bp.Find(uid);
            if (p == null || !p.Def.resizableDiameter) return false;
            int next = CurrentSizeStep(p) + delta;
            if (next < 0 || next >= PartCatalog.DiameterSteps.Length) return false;
            foreach (var m in bp.SymmetryMembers(p)) m.sizeStep = next;
            return true;
        }

        public static bool Stretch(RocketBlueprint bp, int uid, int delta)
        {
            var p = bp.Find(uid);
            if (p == null || !p.Def.resizableLength) return false;
            int next = CurrentLengthStep(p) + delta;
            if (next < 0 || next >= PartCatalog.LengthSteps.Length) return false;
            foreach (var m in bp.SymmetryMembers(p)) m.lengthStep = next;
            return true;
        }

        public static void SetPayload(RocketBlueprint bp, int uid, float tonnes)
        {
            var p = bp.Find(uid);
            if (p == null || !p.Def.isPayload) return;
            foreach (var m in bp.SymmetryMembers(p)) m.payloadMass = Mathf.Max(0f, tonnes);
        }

        // ------------------------------------------------------------------ scripted building (examples, tests)

        public static int AddRoot(RocketBlueprint bp, string partId)
        {
            var p = new PartInstance { uid = bp.nextUid++, partId = partId, mode = AttachMode.Root };
            bp.parts.Add(p);
            return p.uid;
        }

        public static int AddChild(RocketBlueprint bp, int parentUid, string partId, AttachMode mode, float heightFrac = 0f)
        {
            var held = HeldAssembly.ForNewPart(PartCatalog.Get(partId));
            var r = Attach(bp, held, new AttachCandidate { valid = true, parentUid = parentUid, mode = mode, heightFrac = mode == AttachMode.Side && heightFrac == 0f ? 0.3f : heightFrac });
            return r.Count > 0 ? r[0] : -1;
        }

        public static List<int> AddRadial(RocketBlueprint bp, int parentUid, string partId, int symmetry, float heightFrac, float angle = 0f)
        {
            var held = HeldAssembly.ForNewPart(PartCatalog.Get(partId));
            held.symmetry = symmetry;
            return Attach(bp, held, new AttachCandidate { valid = true, parentUid = parentUid, mode = AttachMode.Surface, heightFrac = heightFrac, angle = angle });
        }
    }
}
