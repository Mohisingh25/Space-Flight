using System.Collections.Generic;
using UnityEngine;

namespace Apogee.Rocket
{
    public struct PartPose
    {
        public Vector3 position;
        public Quaternion rotation;

        public PartPose(Vector3 p, Quaternion r) { position = p; rotation = r; }

        public Vector3 Up { get { return rotation * Vector3.up; } }
        public Vector3 Outward { get { return rotation * Vector3.right; } }
    }

    /// <summary>
    /// Turns a blueprint's attachment tree into positions/rotations in "rocket space"
    /// (root part at the origin, +Y up). Because positions are derived rather than stored,
    /// resizing a part automatically moves everything attached to it.
    /// </summary>
    public static class RocketLayout
    {
        public static Dictionary<int, PartPose> Compute(RocketBlueprint bp)
        {
            var poses = new Dictionary<int, PartPose>();
            var root = bp.Root;
            if (root == null) return poses;

            poses[root.uid] = new PartPose(Vector3.zero, Quaternion.Euler(0f, root.yaw, 0f));
            var queue = new Queue<PartInstance>();
            queue.Enqueue(root);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 10000)
            {
                var parent = queue.Dequeue();
                var parentPose = poses[parent.uid];
                foreach (var child in bp.ChildrenOf(parent.uid))
                {
                    poses[child.uid] = ChildPose(parent, parentPose, child);
                    queue.Enqueue(child);
                }
            }
            return poses;
        }

        public static PartPose ChildPose(PartInstance parent, PartPose pp, PartInstance child)
        {
            var pd = PartDims.Of(parent);
            var cd = PartDims.Of(child);
            Quaternion rot;
            Vector3 pos;
            switch (child.mode)
            {
                case AttachMode.Below:
                {
                    rot = pp.rotation * Quaternion.Euler(0f, child.yaw, 0f);
                    Vector3 node = pp.position - pp.Up * (pd.height * 0.5f);
                    pos = node - (rot * Vector3.up) * (cd.height * 0.5f);
                    break;
                }
                case AttachMode.Above:
                {
                    rot = pp.rotation * Quaternion.Euler(0f, child.yaw, 0f);
                    Vector3 node = pp.position + pp.Up * (pd.height * 0.5f);
                    pos = node + (rot * Vector3.up) * (cd.height * 0.5f);
                    break;
                }
                case AttachMode.Surface:
                {
                    // Radial parts are modelled extending along their local +X from the attach point.
                    float r = pd.RadiusAt(child.heightFrac);
                    rot = pp.rotation * Quaternion.Euler(0f, -child.angle, 0f) * Quaternion.Euler(child.yaw, 0f, 0f);
                    float a = child.angle * Mathf.Deg2Rad;
                    Vector3 local = new Vector3(Mathf.Cos(a) * r, child.heightFrac * pd.height, Mathf.Sin(a) * r);
                    pos = pp.position + pp.rotation * local;
                    break;
                }
                case AttachMode.Side:
                {
                    // Parent is a radial decoupler; its outer face sits at local +X = its depth.
                    float depth = pd.diameter;
                    rot = pp.rotation * Quaternion.Euler(0f, child.yaw, 0f);
                    Vector3 node = pp.position + pp.Outward * depth;
                    pos = node + pp.Outward * cd.Radius - (rot * Vector3.up) * (child.heightFrac * cd.height);
                    break;
                }
                default:
                    rot = pp.rotation;
                    pos = pp.position;
                    break;
            }
            return new PartPose(pos, rot);
        }

        public static Vector3 TopNode(PartInstance p, PartPose pose)
        {
            return pose.position + pose.Up * (PartDims.Of(p).height * 0.5f);
        }

        public static Vector3 BottomNode(PartInstance p, PartPose pose)
        {
            return pose.position - pose.Up * (PartDims.Of(p).height * 0.5f);
        }

        public static Vector3 OuterNode(PartInstance p, PartPose pose)
        {
            return pose.position + pose.Outward * PartDims.Of(p).diameter;
        }

        /// <summary>Approximate axis-aligned bounds of the whole vehicle in rocket space.</summary>
        public static Bounds ComputeBounds(RocketBlueprint bp, Dictionary<int, PartPose> poses)
        {
            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var p in bp.parts)
            {
                PartPose pose;
                if (!poses.TryGetValue(p.uid, out pose)) continue;
                var d = PartDims.Of(p);
                float r = Mathf.Max(d.Radius, d.TopRadius);
                if (p.Def.hasTopNode || p.Def.hasBottomNode)
                {
                    var pb = new Bounds(pose.position, Vector3.zero);
                    pb.Encapsulate(pose.position + pose.rotation * new Vector3(r, d.height * 0.5f, r));
                    pb.Encapsulate(pose.position + pose.rotation * new Vector3(-r, -d.height * 0.5f, -r));
                    pb.Encapsulate(pose.position + pose.rotation * new Vector3(-r, d.height * 0.5f, r));
                    pb.Encapsulate(pose.position + pose.rotation * new Vector3(r, -d.height * 0.5f, -r));
                    if (!any) { b = pb; any = true; } else b.Encapsulate(pb);
                }
                else
                {
                    // Radial part: extends outward from the attach point.
                    float len = Mathf.Max(d.diameter, 0.6f);
                    var pb = new Bounds(pose.position, Vector3.zero);
                    pb.Encapsulate(pose.position + pose.Outward * len + pose.Up * d.height * 0.5f);
                    pb.Encapsulate(pose.position - pose.Up * d.height * 0.5f);
                    if (!any) { b = pb; any = true; } else b.Encapsulate(pb);
                }
            }
            return b;
        }
    }
}
