using System.Collections.Generic;
using Apogee.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Rocket
{
    /// <summary>Links a rendered part back to its blueprint entry.</summary>
    public class PartView : MonoBehaviour
    {
        public int uid;
        public string[] materialKeys;
        public Color[] baseColors;
        public MeshRenderer meshRenderer;
        public int paint = -1;
        public PartInstance part;
    }

    /// <summary>
    /// Renders a blueprint as 3D parts (with colliders for picking), plus highlight tinting,
    /// a translucent placement ghost and centre-of-mass/thrust/pressure markers.
    /// </summary>
    public class RocketView : MonoBehaviour
    {
        public const int PartLayer = 8;

        public Transform Root { get; private set; }
        public float baseHeight; // world Y the vehicle's lowest point should sit on
        public bool buildColliders = true;

        readonly Dictionary<int, PartView> views = new Dictionary<int, PartView>();
        Transform ghostRoot, markerRoot;
        Material ghostMat, ghostBadMat;
        Mesh sphere, arrowMesh;
        Material comMat, copMat, cotMat, nodeMat;
        Bounds bounds;
        MaterialPropertyBlock mpb;

        public Bounds WorldBounds { get { return bounds; } }

        void Awake()
        {
            Root = new GameObject("Vehicle").transform;
            Root.SetParent(transform, false);
            ghostRoot = new GameObject("Ghost").transform;
            ghostRoot.SetParent(transform, false);
            markerRoot = new GameObject("Markers").transform;
            markerRoot.SetParent(transform, false);
            ghostMat = MaterialLibrary.Custom("Ghost");
            ghostMat.SetColor("_Color", new Color(0.3f, 0.95f, 0.65f, 0.5f));
            ghostBadMat = MaterialLibrary.Custom("Ghost");
            ghostBadMat.SetColor("_Color", new Color(1f, 0.55f, 0.25f, 0.42f));
            sphere = PartMeshFactory.UVSphere(12, 16, 0.5f);
            mpb = new MaterialPropertyBlock();
            comMat = MarkerMat(new Color(1f, 0.85f, 0.1f, 0.95f));
            copMat = MarkerMat(new Color(0.3f, 0.85f, 1f, 0.9f));
            cotMat = MarkerMat(new Color(1f, 0.35f, 0.9f, 0.9f));
            nodeMat = MarkerMat(new Color(0.35f, 1f, 0.55f, 0.75f));
            var amb = new MeshBuilder();
            amb.Cylinder(0.12f, -2.2f, 0f, 10, 0);
            amb.Lathe(new[] { new Vector2(0f, -3.2f), new Vector2(0.35f, -2.2f), new Vector2(0f, -2.2f) }, 12, 0);
            arrowMesh = amb.ToMesh("ThrustArrow");
        }

        static Material MarkerMat(Color c)
        {
            var m = MaterialLibrary.Custom("Marker");
            m.SetColor("_Color", c);
            return m;
        }

        public Vector3 ToWorld(Vector3 rocketSpace) { return Root.TransformPoint(rocketSpace); }
        public Vector3 ToRocket(Vector3 world) { return Root.InverseTransformPoint(world); }

        // ------------------------------------------------------------------ vehicle

        public void Rebuild(RocketBlueprint bp)
        {
            foreach (var v in views.Values) if (v != null) Destroy(v.gameObject);
            views.Clear();
            var poses = RocketLayout.Compute(bp);
            if (poses.Count == 0)
            {
                Root.localPosition = new Vector3(0f, baseHeight, 0f);
                bounds = new Bounds(new Vector3(0f, baseHeight + 4f, 0f), new Vector3(4f, 8f, 4f));
                return;
            }
            var local = RocketLayout.ComputeBounds(bp, poses);
            Root.localPosition = new Vector3(0f, baseHeight - local.min.y, 0f);
            foreach (var p in bp.parts)
            {
                PartPose pose;
                if (!poses.TryGetValue(p.uid, out pose)) continue;
                views[p.uid] = CreatePart(p, pose, Root, false, null);
            }
            bounds = new Bounds(Root.TransformPoint(local.center), local.size);
        }

        PartView CreatePart(PartInstance p, PartPose pose, Transform parent, bool ghost, Material ghostMaterial)
        {
            var model = PartMeshFactory.Get(p);
            var go = new GameObject((ghost ? "Ghost_" : "Part_") + p.partId + "_" + p.uid);
            go.layer = ghost ? 0 : PartLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pose.position;
            go.transform.localRotation = pose.rotation;
            go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (ghost)
            {
                var mats = new Material[model.materials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            else
            {
                r.sharedMaterials = model.materials;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (buildColliders)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = model.mesh;
                }
            }
            var view = go.AddComponent<PartView>();
            view.uid = p.uid;
            view.part = p;
            view.materialKeys = model.materialKeys;
            view.meshRenderer = r;
            if (!ghost)
            {
                view.paint = p.paint;
                view.baseColors = PartPaint.Apply(r, model.materialKeys, p.paint, mpb);
                if (xray) XRay.Apply(go, p, true);
            }
            return view;
        }

        bool xray;

        /// <summary>See-through "inside view": hulls turn to glass and the internals (propellant, engines, crew seats) show.</summary>
        public void SetXRay(bool on)
        {
            xray = on;
            foreach (var v in views.Values)
                if (v != null) XRay.Apply(v.gameObject, v.part, on);
        }

        public bool XRayOn { get { return xray; } }

        public PartView ViewOf(int uid)
        {
            PartView v;
            return views.TryGetValue(uid, out v) ? v : null;
        }

        /// <summary>Tints the given parts (selection, staging highlight). Pass an empty set to clear.</summary>
        public void Highlight(ICollection<int> uids, Color tint, float amount = 0.45f)
        {
            foreach (var kv in views)
            {
                var v = kv.Value;
                if (v == null) continue;
                bool on = uids != null && uids.Contains(kv.Key);
                for (int i = 0; i < v.materialKeys.Length; i++)
                {
                    mpb.Clear();
                    Color baseCol = v.baseColors != null && i < v.baseColors.Length ? v.baseColors[i] : MaterialLibrary.BaseColor(v.materialKeys[i]);
                    if (on || (v.paint >= 0 && PartPaint.IsBodyKey(v.materialKeys[i])))
                    {
                        Color c = on ? Color.Lerp(baseCol, tint, amount) : baseCol;
                        mpb.SetColor("_BaseColor", c);
                        mpb.SetColor("_Color", c);
                    }
                    if (!XRay.IsOn(v.gameObject)) v.meshRenderer.SetPropertyBlock(mpb, i);
                }
            }
        }

        // ------------------------------------------------------------------ ghost

        public void ClearGhost()
        {
            for (int i = ghostRoot.childCount - 1; i >= 0; i--) Destroy(ghostRoot.GetChild(i).gameObject);
        }

        /// <summary>Shows a set of parts as a ghost using rocket-space poses (snapped preview).</summary>
        public void ShowGhostSnapped(RocketBlueprint previewBp, ICollection<int> ghostUids)
        {
            ClearGhost();
            ghostRoot.position = Root.position;
            ghostRoot.rotation = Root.rotation;
            var poses = RocketLayout.Compute(previewBp);
            foreach (var p in previewBp.parts)
            {
                if (!ghostUids.Contains(p.uid)) continue;
                PartPose pose;
                if (poses.TryGetValue(p.uid, out pose)) CreatePart(p, pose, ghostRoot, true, ghostMat);
            }
        }

        public void MoveGhost(Vector3 worldPos) { ghostRoot.position = worldPos; }

        /// <summary>Shows a held assembly floating at a world position (no valid attach point).</summary>
        public void ShowGhostFree(HeldAssembly held, Vector3 worldPos)
        {
            ClearGhost();
            var bp = held.AsBlueprint();
            var poses = RocketLayout.Compute(bp);
            ghostRoot.position = worldPos;
            ghostRoot.rotation = Quaternion.identity;
            foreach (var p in bp.parts)
            {
                PartPose pose;
                if (poses.TryGetValue(p.uid, out pose)) CreatePart(p, pose, ghostRoot, true, ghostBadMat);
            }
        }

        // ------------------------------------------------------------------ markers

        public void ShowMarkers(RocketStats s, bool visible)
        {
            for (int i = markerRoot.childCount - 1; i >= 0; i--) Destroy(markerRoot.GetChild(i).gameObject);
            if (!visible || s == null || s.partCount == 0) return;
            float size = Mathf.Clamp(bounds.size.y * 0.035f, 0.35f, 1.6f);
            Marker(s.centerOfMass, comMat, size, "CoM");
            Marker(s.centerOfPressure, copMat, size * 0.85f, "CoP");
            if (s.hasThrust)
            {
                Marker(s.centerOfThrust, cotMat, size * 0.85f, "CoT");
                var arrow = new GameObject("Thrust Arrow");
                arrow.transform.SetParent(markerRoot, false);
                arrow.transform.position = ToWorld(s.centerOfThrust);
                arrow.transform.localScale = Vector3.one * size;
                arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                arrow.AddComponent<MeshRenderer>().sharedMaterial = cotMat;
            }
        }

        void Marker(Vector3 rocketPos, Material mat, float size, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(markerRoot, false);
            go.transform.position = ToWorld(rocketPos);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Small glowing dots at every free attach node the held part can use.</summary>
        public void ShowNodes(List<Vector3> rocketSpaceNodes, float size)
        {
            for (int i = markerRoot.childCount - 1; i >= 0; i--)
            {
                var c = markerRoot.GetChild(i);
                if (c.name == "Node") Destroy(c.gameObject);
            }
            if (rocketSpaceNodes == null) return;
            foreach (var n in rocketSpaceNodes)
            {
                var go = new GameObject("Node");
                go.transform.SetParent(markerRoot, false);
                go.transform.position = ToWorld(n);
                go.transform.localScale = Vector3.one * size;
                go.AddComponent<MeshFilter>().sharedMesh = sphere;
                go.AddComponent<MeshRenderer>().sharedMaterial = nodeMat;
            }
        }
    }
}
