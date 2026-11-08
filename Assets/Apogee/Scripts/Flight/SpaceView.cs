using System.Collections.Generic;
using Apogee.Rendering;
using Apogee.Simulation;
using Apogee.Astro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Flight
{
    /// <summary>
    /// "Scaled space": the whole Solar System drawn by a background camera at a tiny scale, positioned
    /// relative to the viewer so float precision never runs out. The flight camera renders the rocket,
    /// launch complex and nearby terrain on top of it. The map view reuses it with its own zoom.
    /// Planet maps: Solar System Scope (CC BY 4.0), based on NASA imagery.
    /// </summary>
    public class SpaceView : MonoBehaviour
    {
        public const int Layer = 30;
        const float MaxDistance = 80000f;

        class BodyVis
        {
            public CelestialBody body;
            public Transform root, sphere, atmo, rings, glow;
            public Material mat, atmoMat, ringMat;
        }

        public Camera cam;
        readonly List<BodyVis> bodies = new List<BodyVis>();
        Transform sky;
        Material skyMat;
        float cloudShift;

        class Line
        {
            public LineRenderer lr;
            public CelestialBody frame;      // points are relative to this body (null = heliocentric)
            public List<Vector3d> points = new List<Vector3d>();
        }
        readonly Dictionary<string, Line> lines = new Dictionary<string, Line>();
        Material lineMat;

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static Mesh sphereMesh, ringMesh;

        public static Texture2D Tex(string name)
        {
            Texture2D t;
            if (texCache.TryGetValue(name, out t)) return t;
            t = Resources.Load<Texture2D>("Planets/" + name);
            texCache[name] = t;
            return t;
        }

        public void Init()
        {
            SolarSystem.Build();
            var go = new GameObject("Space Camera");
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120000f;
            cam.depth = -10;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            // Milky Way backdrop
            skyMat = MaterialLibrary.Custom("Sky");
            skyMat.SetTexture("_MainTex", Tex("starfield"));
            sky = Make("Sky", Sphere(), skyMat).transform;
            sky.localScale = Vector3.one * 100000f;

            lineMat = MaterialLibrary.Custom("Line");

            foreach (var b in SolarSystem.Bodies) bodies.Add(BuildBody(b));
        }

        GameObject Make(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        BodyVis BuildBody(CelestialBody b)
        {
            var v = new BodyVis { body = b };
            v.root = new GameObject(b.name).transform;
            v.root.SetParent(transform, false);
            v.root.gameObject.layer = Layer;
            Texture2D tex = Tex(b.texture);
            if (b.star)
            {
                v.mat = MaterialLibrary.Custom("Unlit");
                v.mat.SetTexture("_MainTex", tex);
                v.mat.SetFloat("_Brightness", 1.6f);
                var glowMat = MaterialLibrary.Custom("Stars");
                glowMat.SetTexture("_MainTex", ProceduralTextures.Glow(128));
                glowMat.SetFloat("_Brightness", 2.2f);
                v.glow = Make("Glow", PlanetFactory.Quad(), glowMat).transform;
                v.glow.SetParent(v.root, false);
            }
            else
            {
                v.mat = MaterialLibrary.Custom("Planet");
                v.mat.SetTexture("_MainTex", tex);
                bool earth = b.name == "Earth";
                v.mat.SetFloat("_Specular", earth ? 1f : 0f);
                v.mat.SetFloat("_NightLights", earth ? 0.6f : 0f);
                v.mat.SetFloat("_CloudStrength", earth ? 1f : 0f);
                v.mat.SetFloat("_Ambient", 0.012f);
                if (earth) v.mat.SetTexture("_CloudTex", Tex("earth_clouds"));
                v.mat.SetColor("_AtmoColor", b.atmoColor);
                v.mat.SetFloat("_AtmoStrength", b.HasAtmosphere ? (earth ? 1f : 0.6f) : 0f);
                v.mat.color = b.tint;
                if (b.tint != Color.white) TintMaterial(v.mat, b);
            }
            v.sphere = Make("Surface", Sphere(), v.mat).transform;
            v.sphere.SetParent(v.root, false);
            if (b.HasAtmosphere && !b.star)
            {
                v.atmoMat = MaterialLibrary.Custom("Atmosphere");
                v.atmoMat.SetColor("_Color", b.atmoColor);
                v.atmoMat.SetFloat("_Intensity", b.name == "Earth" ? 1.3f : 0.9f);
                v.atmo = Make("Atmosphere", Sphere(), v.atmoMat).transform;
                v.atmo.SetParent(v.root, false);
                v.atmo.localScale = Vector3.one * 1.025f;
            }
            if (b.hasRings)
            {
                v.ringMat = MaterialLibrary.Custom("Ring");
                v.ringMat.SetTexture("_MainTex", Tex("saturn_ring"));
                v.rings = Make("Rings", Ring(), v.ringMat).transform;
                v.rings.SetParent(v.root, false);
                v.rings.localScale = Vector3.one * (float)(140000e3 / b.radius);
            }
            return v;
        }

        /// <summary>Moons without their own map reuse the Moon's, coloured for that world.</summary>
        static void TintMaterial(Material m, CelestialBody b)
        {
            var baseTex = Tex(b.texture);
            if (baseTex == null || !baseTex.isReadable) return;
            var px = baseTex.GetPixels32();
            var tint = b.tint;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float l = (c.r + c.g + c.b) / 765f;
                px[i] = new Color32((byte)(Mathf.Clamp01(l * tint.r * 1.3f) * 255), (byte)(Mathf.Clamp01(l * tint.g * 1.3f) * 255), (byte)(Mathf.Clamp01(l * tint.b * 1.3f) * 255), 0);
            }
            var t = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGBA32, true);
            t.SetPixels32(px);
            t.Apply(true, true);
            m.SetTexture("_MainTex", t);
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Unit sphere whose UVs follow the planet maps: u = longitude east from -180°, v = latitude.</summary>
        public static Mesh Sphere()
        {
            if (sphereMesh != null) return sphereMesh;
            int lon = 96, lat = 48;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float v = j / (float)lat;
                float phi = (v - 0.5f) * Mathf.PI;
                for (int i = 0; i <= lon; i++)
                {
                    float u = i / (float)lon;
                    float L = (u * 360f - 180f) * Mathf.Deg2Rad;
                    verts.Add(new Vector3(Mathf.Cos(phi) * Mathf.Cos(L), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(L)));
                    uvs.Add(new Vector2(u, v));
                }
            }
            int row = lon + 1;
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row + 1, d = a + row;
                    tris.Add(a); tris.Add(d); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
            var m = new Mesh { name = "PlanetSphere" };
            m.SetVertices(verts);
            m.SetNormals(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            sphereMesh = m;
            return m;
        }

        static Mesh Ring()
        {
            if (ringMesh != null) return ringMesh;
            var m = new Mesh { name = "Ring" };
            m.SetVertices(new List<Vector3> { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) });
            m.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new List<int> { 0, 3, 2, 0, 2, 1 }, 0);
            m.RecalculateBounds();
            ringMesh = m;
            return m;
        }

        // ------------------------------------------------------------------ lines (orbits)

        public void SetLine(string id, CelestialBody frame, List<Vector3d> points, Color color)
        {
            Line l;
            if (!lines.TryGetValue(id, out l))
            {
                var go = new GameObject("Line " + id);
                go.layer = Layer;
                go.transform.SetParent(transform, false);
                l = new Line { lr = go.AddComponent<LineRenderer>() };
                l.lr.sharedMaterial = lineMat;
                l.lr.useWorldSpace = true;
                l.lr.shadowCastingMode = ShadowCastingMode.Off;
                l.lr.receiveShadows = false;
                l.lr.numCapVertices = 2;
                lines[id] = l;
            }
            l.frame = frame;
            l.points.Clear();
            l.points.AddRange(points);
            l.lr.startColor = l.lr.endColor = color;
            l.lr.positionCount = points.Count;
            l.lr.enabled = points.Count > 1;
        }

        public void HideLine(string id) { Line l; if (lines.TryGetValue(id, out l)) l.lr.enabled = false; }
        public void HideAllLines() { foreach (var l in lines.Values) l.lr.enabled = false; }

        // ------------------------------------------------------------------ per frame

        Vector3d camAbs;
        double scale;
        public double Scale { get { return scale; } }

        /// <summary>Scaled-space position of a heliocentric point, as seen from the current camera.</summary>
        public Vector3 ToScaled(Vector3d abs)
        {
            var d = (abs - camAbs) * scale;
            double m = d.magnitude;
            if (m > MaxDistance) d = d * (MaxDistance / m);
            return d.ToFloat();
        }

        /// <summary>
        /// Draws the Solar System from viewpoint 'viewerAbs' (heliocentric, m) with camera rotation 'rotation'.
        /// 'metersPerUnit' sets the scale (1e5 in flight: 1 unit = 100 km).
        /// </summary>
        /// <summary>Sky dome parameters for the viewer's location (set by the flight renderer; map view leaves it in space).</summary>
        public void SetSky(Vector3 up, Vector3 sunDir, Color zenith, Color horizon, Color ground, Color sunGlow, float atmosphere)
        {
            skyMat.SetVector("_Up", up);
            skyMat.SetVector("_SunDir", sunDir);
            skyMat.SetColor("_Zenith", zenith);
            skyMat.SetColor("_Horizon", horizon);
            skyMat.SetColor("_Ground", ground);
            skyMat.SetColor("_SunGlow", sunGlow);
            skyMat.SetFloat("_AtmoAmount", atmosphere);
        }

        public void Render(Vector3d viewerAbs, Quaternion rotation, float fov, double t, double metersPerUnit, float starBrightness, Color background, float lineWidthAtUnit)
        {
            camAbs = viewerAbs;
            scale = 1.0 / metersPerUnit;
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = rotation;
            cam.fieldOfView = fov;
            cam.backgroundColor = background;
            sky.position = Vector3.zero;
            skyMat.SetFloat("_Brightness", starBrightness);
            cloudShift += Time.deltaTime * 0.0004f;

            var sunAbs = Vector3d.zero;
            foreach (var v in bodies)
            {
                var b = v.body;
                var abs = b.AbsolutePosition(t);
                var d = (abs - camAbs) * scale;
                double dist = d.magnitude;
                double k = dist > MaxDistance ? MaxDistance / dist : 1.0;
                v.root.position = (d * k).ToFloat();
                float size = (float)(b.radius * scale * k);
                v.root.localScale = Vector3.one * size;
                v.root.gameObject.SetActive(size > 1e-6f || b.star);
                v.sphere.localRotation = b.Spin(t);
                var sunDir = (sunAbs - abs).normalized.ToFloat();
                if (!b.star)
                {
                    v.mat.SetVector("_SunDir", sunDir);
                    v.mat.SetFloat("_CloudShift", cloudShift);
                    if (v.atmoMat != null) v.atmoMat.SetVector("_SunDir", sunDir);
                    if (v.ringMat != null) v.ringMat.SetVector("_SunDir", sunDir);
                }
                else if (v.glow != null)
                {
                    v.glow.rotation = rotation;
                    v.glow.localScale = Vector3.one * 7f;
                }
            }

            foreach (var l in lines.Values)
            {
                if (!l.lr.enabled) continue;
                var origin = l.frame != null ? l.frame.AbsolutePosition(t) : Vector3d.zero;
                for (int i = 0; i < l.points.Count; i++) l.lr.SetPosition(i, ToScaled(origin + l.points[i]));
                l.lr.widthMultiplier = lineWidthAtUnit;
            }
        }

        public Transform BodyTransform(CelestialBody b)
        {
            foreach (var v in bodies) if (v.body == b) return v.root;
            return null;
        }
    }
}
