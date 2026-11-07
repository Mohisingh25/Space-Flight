using System;
using System.Collections.Generic;
using Apogee.Parts;
using UnityEngine;

namespace Apogee.Rocket
{
    public enum AttachMode
    {
        Root = 0,
        Below = 1,    // child's top node joins the parent's bottom node
        Above = 2,    // child's bottom node joins the parent's top node
        Surface = 3,  // radial part glued to the parent's surface
        Side = 4,     // stack part hung off a radial decoupler's outer node
        Flank = 5     // stack part (tank, engine, booster) glued upright to the side of another part
    }

    /// <summary>One placed part. Position is derived from the attachment description by RocketLayout.</summary>
    [Serializable]
    public class PartInstance
    {
        public int uid;
        public string partId;
        public int parentUid = -1;
        public AttachMode mode;
        public float angle;         // Surface: degrees around parent's axis
        public float heightFrac;    // Surface: -0.5..0.5 along parent; Side: point on the child that touches the node
        public float yaw;           // extra rotation (stack: about own axis; radial: roll about outward axis)
        public int sizeStep = -1;   // index into PartCatalog.DiameterSteps, -1 = default size
        public int lengthStep = -1; // index into PartCatalog.LengthSteps, -1 = 1x
        public int symmetryGroup;   // parts placed together in symmetry share a non-zero id
        public float payloadMass;   // t, for payload containers / cargo modules
        public int stage = -1;      // activation stage (0 = launch) for staged parts
        public int paint = -1;      // index into PaintPalette, -1 = the part's factory finish

        public PartDefinition Def { get { return PartCatalog.Get(partId); } }

        public PartInstance Clone()
        {
            return (PartInstance)MemberwiseClone();
        }
    }

    [Serializable]
    public class RocketBlueprint
    {
        public string name = "Untitled Rocket";
        public int version = 1;
        public int nextUid = 1;
        public int nextSymmetryGroup = 1;
        public bool customStaging;
        public int stageCount;
        public long savedAtTicks;
        public List<PartInstance> parts = new List<PartInstance>();

        public bool IsEmpty { get { return parts.Count == 0; } }

        public PartInstance Find(int uid)
        {
            for (int i = 0; i < parts.Count; i++) if (parts[i].uid == uid) return parts[i];
            return null;
        }

        public PartInstance Root
        {
            get
            {
                for (int i = 0; i < parts.Count; i++) if (parts[i].mode == AttachMode.Root) return parts[i];
                return null;
            }
        }

        public List<PartInstance> ChildrenOf(int uid)
        {
            var list = new List<PartInstance>();
            for (int i = 0; i < parts.Count; i++) if (parts[i].parentUid == uid && parts[i].mode != AttachMode.Root) list.Add(parts[i]);
            return list;
        }

        /// <summary>The part itself plus all its descendants (in tree order).</summary>
        public List<PartInstance> Subtree(int uid)
        {
            var result = new List<PartInstance>();
            var start = Find(uid);
            if (start == null) return result;
            var queue = new Queue<PartInstance>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                result.Add(p);
                foreach (var c in ChildrenOf(p.uid)) queue.Enqueue(c);
            }
            return result;
        }

        public List<PartInstance> SymmetryMembers(PartInstance p)
        {
            var list = new List<PartInstance>();
            if (p.symmetryGroup == 0) { list.Add(p); return list; }
            foreach (var q in parts) if (q.symmetryGroup == p.symmetryGroup) list.Add(q);
            return list;
        }

        public RocketBlueprint DeepCopy()
        {
            return JsonUtility.FromJson<RocketBlueprint>(JsonUtility.ToJson(this));
        }

        public string ToJson(bool pretty = false) { return JsonUtility.ToJson(this, pretty); }

        public static RocketBlueprint FromJson(string json)
        {
            var bp = JsonUtility.FromJson<RocketBlueprint>(json);
            if (bp == null) return null;
            if (bp.parts == null) bp.parts = new List<PartInstance>();
            // Drop parts whose definitions no longer exist (keeps old saves loadable).
            bp.parts.RemoveAll(p => PartCatalog.Get(p.partId) == null);
            int maxUid = 0;
            foreach (var p in bp.parts) maxUid = Mathf.Max(maxUid, p.uid);
            bp.nextUid = Mathf.Max(bp.nextUid, maxUid + 1);
            return bp;
        }
    }

    /// <summary>Effective dimensions of a placed part after resizing.</summary>
    public struct PartDims
    {
        public float diameter;
        public float topDiameter;
        public float height;
        public float diameterScale;
        public float lengthScale;

        public float Radius { get { return diameter * 0.5f; } }
        public float TopRadius { get { return topDiameter * 0.5f; } }

        public static PartDims Of(PartInstance p)
        {
            var def = p.Def;
            var d = new PartDims();
            float ds = 1f;
            if (def.resizableDiameter && p.sizeStep >= 0 && p.sizeStep < PartCatalog.DiameterSteps.Length)
                ds = PartCatalog.DiameterSteps[p.sizeStep] / def.diameter;
            float ls = 1f;
            if (def.resizableLength && p.lengthStep >= 0 && p.lengthStep < PartCatalog.LengthSteps.Length)
                ls = PartCatalog.LengthSteps[p.lengthStep];
            d.diameterScale = ds;
            d.lengthScale = ls;
            d.diameter = def.diameter * ds;
            d.topDiameter = def.topDiameter * ds;
            d.height = def.height * ls * (def.scaleHeightWithDiameter ? ds : 1f);
            return d;
        }

        /// <summary>Radius of the part's outer surface at a height fraction (-0.5 bottom .. 0.5 top).</summary>
        public float RadiusAt(float frac)
        {
            return Mathf.Lerp(Radius, TopRadius, Mathf.Clamp01(frac + 0.5f));
        }
    }

    /// <summary>Mass properties of a placed part, accounting for resizing and payload.</summary>
    public static class PartMass
    {
        public static float VolumeScale(PartInstance p)
        {
            var d = PartDims.Of(p);
            return d.diameterScale * d.diameterScale * d.lengthScale;
        }

        public static float Dry(PartInstance p)
        {
            var def = p.Def;
            float s = def.resizableDiameter || def.resizableLength ? VolumeScale(p) : 1f;
            return def.dryMass * s + (def.isPayload ? p.payloadMass : 0f);
        }

        public static float Fuel(PartInstance p)
        {
            var def = p.Def;
            if (def.fuelMass <= 0f) return 0f;
            return def.fuelMass * (def.resizableDiameter || def.resizableLength ? VolumeScale(p) : 1f);
        }

        public static float Wet(PartInstance p) { return Dry(p) + Fuel(p); }
    }
}
