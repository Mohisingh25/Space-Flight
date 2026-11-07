using System.Collections;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rendering;
using Apogee.Rocket;
using Apogee.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Flight
{
    /// <summary>Colours of the planet's surface along the flight plane (sampled from the generated Earth map).</summary>
    public class EquatorBand
    {
        const int N = 2048;
        readonly Color[] colors = new Color[N];
        readonly bool[] ocean = new bool[N];
        double offset; // fraction of a turn, chosen so the launch site sits on land

        public EquatorBand()
        {
            var tex = PlanetFactory.LoadCachedEarthAlbedo();
            if (tex == null)
            {
                for (int i = 0; i < N; i++)
                {
                    float n = Mathf.Sin(i * 0.011f) * 0.5f + Mathf.Sin(i * 0.0037f + 1.3f) * 0.5f;
                    ocean[i] = n < -0.25f;
                    colors[i] = ocean[i] ? new Color(0.05f, 0.2f, 0.38f) : Color.Lerp(new Color(0.3f, 0.38f, 0.17f), new Color(0.55f, 0.48f, 0.32f), n * 0.5f + 0.5f);
                }
            }
            else
            {
                for (int i = 0; i < N; i++)
                {
                    var c = tex.GetPixelBilinear((float)i / N, 0.5f);
                    ocean[i] = c.a > 0.5f;
                    c.a = 1f;
                    colors[i] = ocean[i] ? new Color(c.r * 1.2f + 0.02f, c.g * 1.25f + 0.05f, c.b * 1.2f + 0.08f) : c * 1.15f;
                }
            }
            // Find the longest stretch of land and put the launch site (angle 90°) in its middle.
            int bestStart = 0, bestLen = 0;
            for (int s = 0; s < N; s++)
            {
                if (ocean[s] || !ocean[(s - 1 + N) % N]) continue;
                int len = 0;
                while (len < N && !ocean[(s + len) % N]) len++;
                if (len > bestLen) { bestLen = len; bestStart = s; }
            }
            if (bestLen == 0) bestStart = 0;
            double centre = (bestStart + bestLen * 0.5) / N;
            offset = centre - 0.25; // phi = 90° is a quarter turn
        }

        int Index(double phi)
        {
            double f = phi / (2 * System.Math.PI) + offset;
            f -= System.Math.Floor(f);
            return Mathf.Clamp((int)(f * N), 0, N - 1);
        }

        public Color Sample(double phi) { return colors[Index(phi)]; }
        public bool IsOcean(double phi) { return ocean[Index(phi)]; }
    }

    class FlameFx
    {
        public Transform t;
        public Material mat;
        public float exitDiameter;
        public int uid;
        public float current;
    }

    /// <summary>
    /// Renders the flight: the vehicle and its exhaust, separated debris, the curved ground and ocean
    /// along the flight plane, the launch complex, clouds, sky colour by altitude, stars and the
    /// atmosphere glow seen from orbit. Uses a floating origin centred on the vehicle so it stays
    /// precise anywhere in the solar system.
    /// </summary>
    public class FlightWorld : MonoBehaviour
    {
        public Vector2d origin;
        public EquatorBand band;

        FlightVessel vessel;
        Camera cam;
        Transform vesselRoot;
        readonly Dictionary<int, GameObject> partObjects = new Dictionary<int, GameObject>();
        readonly List<FlameFx> flames = new List<FlameFx>();
        readonly Dictionary<Debris, Transform> debrisRoots = new Dictionary<Debris, Transform>();
        readonly Dictionary<int, float> foldAnim = new Dictionary<int, float>();   // 0 = deployed, 1 = folded
        readonly HashSet<int> foldApplied = new HashSet<int>();
        readonly Dictionary<int, Transform> canopies = new Dictionary<int, Transform>();
        Mesh canopyMesh;
        bool xray;
        public bool XRayOn { get { return xray; } }

        Mesh groundMesh, atmoMesh;
        Material groundMat, atmoMat, starMat;
        Transform ground, atmo, stars, pad, padAnchor;
        readonly List<Transform> clouds = new List<Transform>();
        readonly List<Vector2d> cloudPos = new List<Vector2d>();
        ParticleSystem smoke, blast;
        Light sun;

        const int Segments = 320;
        readonly List<Vector3> gv = new List<Vector3>();
        readonly List<Color> gc = new List<Color>();
        readonly List<Vector2> guv = new List<Vector2>();
        readonly List<int> gt = new List<int>();
        readonly List<Vector3> av = new List<Vector3>();
        readonly List<Color> ac = new List<Color>();
        readonly List<int> at = new List<int>();

        public Vector3 ToRender(Vector2d p) { return new Vector3((float)(p.x - origin.x), (float)(p.y - origin.y), 0f); }

        public static Quaternion RotationFor(double angle) { return Quaternion.Euler(0f, 0f, (float)(angle * Mathf.Rad2Deg) - 90f); }

        public void Init(FlightVessel v, Camera camera)
        {
            vessel = v;
            cam = camera;
            band = new EquatorBand();

            var sunGo = new GameObject("Sun");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.LookRotation(new Vector3(-0.55f, -0.45f, 0.7f));
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.18f);
            DynamicGI.UpdateEnvironment();

            // Ground and atmosphere meshes are rebuilt every frame around the vehicle.
            groundMesh = new Mesh { name = "Ground Arc" };
            groundMesh.MarkDynamic();
            groundMat = MaterialLibrary.Custom("Ground");
            groundMat.SetTexture("_MainTex", ProceduralTextures.Get("soil"));
            ground = MakeObject("Ground", groundMesh, groundMat).transform;
            atmoMesh = new Mesh { name = "Atmosphere Arc" };
            atmoMesh.MarkDynamic();
            atmoMat = MaterialLibrary.Custom("VertexColor");
            atmo = MakeObject("Atmosphere", atmoMesh, atmoMat).transform;

            starMat = MaterialLibrary.Custom("Stars");
            starMat.SetTexture("_MainTex", ProceduralTextures.Glow(64));
            stars = MakeObject("Stars", PlanetFactory.StarField(3500, 9000f, 11), starMat).transform;

            BuildPad();
            BuildClouds();
            BuildVessel();
            BuildParticles();
        }

        static GameObject MakeObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ------------------------------------------------------------------ construction

        void BuildPad()
        {
            var mb = new MeshBuilder();
            float top = (float)FlightVessel.PadTop;
            float half = (float)FlightVessel.PadHalfWidth;
            mb.Box(new Vector3(0f, top * 0.5f, 0f), new Vector3(half * 2f, top, 40f), 0);                   // concrete pad
            mb.Box(new Vector3(0f, top + 0.02f, -6f), new Vector3(10f, 0.05f, 1f), 2);                        // hazard line
            mb.Box(new Vector3(0f, top * 0.5f, 0f), new Vector3(9f, top + 0.05f, 9f), 1);                     // flame deflector
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1f : 1f) * 3.2f, z = (i % 2 == 0 ? -1f : 1f) * 3.2f;
                mb.Box(new Vector3(x, top + 0.6f, z), new Vector3(0.8f, 1.2f, 0.8f), 1);                   // hold-down clamps
            }
            // Service tower with access arms
            float tx = -17f, th = 72f, tw = 2f;
            Vector3[] c = { new Vector3(tx - tw, 0, -tw), new Vector3(tx + tw, 0, -tw), new Vector3(tx + tw, 0, tw), new Vector3(tx - tw, 0, tw) };
            foreach (var p in c) mb.Box(p + Vector3.up * (top + th * 0.5f), new Vector3(0.4f, th, 0.4f), 3);
            for (float y = top; y <= top + th; y += 4f)
                for (int i = 0; i < 4; i++)
                {
                    var a = c[i] + Vector3.up * y;
                    var b = c[(i + 1) % 4] + Vector3.up * y;
                    mb.Box((a + b) * 0.5f, new Vector3(Mathf.Abs(b.x - a.x) + 0.3f, 0.25f, Mathf.Abs(b.z - a.z) + 0.3f), 3);
                    if (y + 4f <= top + th) mb.Tube(a, c[(i + 1) % 4] + Vector3.up * (y + 4f), 0.08f, 6, 3);
                }
            foreach (float y in new[] { 16f, 30f, 46f })
                mb.Box(new Vector3(tx + tw + 3.2f, top + y, 0f), new Vector3(6.4f, 1.2f, 1.6f), 4);
            mb.Box(new Vector3(tx, top + th + 0.6f, 0f), new Vector3(1f, 1.2f, 1f), 5); // beacon
            // Lightning masts and floodlight poles
            foreach (float x in new[] { -half + 2f, half - 2f })
            {
                mb.Box(new Vector3(x, top + 12f, 12f), new Vector3(0.3f, 24f, 0.3f), 4);
                mb.Box(new Vector3(x, top + 24f, 12f), new Vector3(2f, 0.6f, 0.6f), 6);
            }
            var go = new GameObject("Launch Complex");
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("Pad");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[]
            {
                MaterialLibrary.Get("pad_steel"), MaterialLibrary.Get("dark_metal"), MaterialLibrary.Get("hazard"),
                MaterialLibrary.Get("tower_steel"), MaterialLibrary.Get("aluminium"), MaterialLibrary.Get("light_red"), MaterialLibrary.Get("light_strip")
            };
            r.shadowCastingMode = ShadowCastingMode.On;
            pad = go.transform;
            padAnchor = new GameObject("Pad Anchor").transform;
        }

        void BuildClouds()
        {
            var rng = new System.Random(7);
            var quad = PlanetFactory.Quad();
            var mat = MaterialLibrary.Custom("Smoke");
            mat.SetColor("_Color", new Color(1f, 1f, 1f, 0.62f));
            mat.SetFloat("_Softness", 1.3f);
            for (int i = 0; i < 26; i++)
            {
                double phi = System.Math.PI / 2 + (rng.NextDouble() - 0.5) * 0.06;
                double alt = 1500 + rng.NextDouble() * 5500;
                float w = 500f + (float)rng.NextDouble() * 1400f;
                for (int k = 0; k < 4; k++)
                {
                    double dphi = (rng.NextDouble() - 0.5) * w / FlightVessel.R;
                    double dalt = (rng.NextDouble() - 0.5) * w * 0.15;
                    var go = MakeObject("Cloud", quad, mat);
                    float s = w * (0.45f + (float)rng.NextDouble() * 0.4f);
                    go.transform.localScale = new Vector3(s, s * 0.45f, 1f);
                    clouds.Add(go.transform);
                    double r = FlightVessel.R + alt + dalt;
                    cloudPos.Add(new Vector2d(System.Math.Cos(phi + dphi) * r, System.Math.Sin(phi + dphi) * r));
                }
            }
        }

        void BuildVessel()
        {
            vesselRoot = new GameObject("Vessel").transform;
            var flameBase = MaterialLibrary.Custom("Flame");
            foreach (var p in vessel.bp.parts)
            {
                PartPose pose;
                if (!vessel.poses.TryGetValue(p.uid, out pose)) continue;
                var model = PartMeshFactory.Get(p);
                var go = new GameObject("Part_" + p.partId);
                go.transform.SetParent(vesselRoot, false);
                go.transform.localPosition = pose.position;
                go.transform.localRotation = pose.rotation;
                go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = model.materials;
                r.shadowCastingMode = ShadowCastingMode.On;
                PartPaint.Apply(r, model.materialKeys, p.paint, new MaterialPropertyBlock());
                // Tappable in flight: engines on/off, fold legs and arrays, deploy chutes, separate...
                go.layer = RocketView.PartLayer;
                go.AddComponent<MeshCollider>().sharedMesh = model.mesh;
                var pv = go.AddComponent<PartView>();
                pv.uid = p.uid;
                pv.part = p;
                partObjects[p.uid] = go;
                if (p.Def.Foldable) foldAnim[p.uid] = vessel.folded.Contains(p.uid) ? 1f : 0f;

                var d = p.Def;
                if (d.IsEngine)
                {
                    var dims = PartDims.Of(p);
                    float exit = d.shape == PartShape.VacuumEngine ? dims.Radius * 2.64f : d.shape == PartShape.SolidBooster ? dims.Radius * 1.2f : dims.Radius * (d.minThrottle <= 0.15f ? 1.64f : 1.76f);
                    var fgo = MakeObject("Flame", FlameQuad(), new Material(flameBase));
                    fgo.transform.SetParent(go.transform, false);
                    fgo.transform.localPosition = new Vector3(0f, -dims.height * 0.5f + 0.05f, 0f);
                    var fx = new FlameFx { t = fgo.transform, mat = fgo.GetComponent<MeshRenderer>().sharedMaterial, exitDiameter = exit, uid = p.uid };
                    // Colour by propellant: kerolox orange, methalox blue-white, solids bright yellow-white
                    Color col = d.solidFuel ? new Color(1f, 0.72f, 0.35f) : d.shape == PartShape.Engine && d.finish == Finish.Stainless ? new Color(0.55f, 0.6f, 1f) : new Color(1f, 0.5f, 0.18f);
                    fx.mat.SetColor("_Color", col);
                    fgo.SetActive(false);
                    flames.Add(fx);
                }
            }
        }

        static Mesh flameQuad;
        static Mesh FlameQuad()
        {
            if (flameQuad != null) return flameQuad;
            var m = new Mesh { name = "FlameQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, -1f, 0f), new Vector3(-0.5f, -1f, 0f) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateBounds();
            m.bounds = new Bounds(new Vector3(0, -0.5f, 0), new Vector3(2, 2, 2));
            flameQuad = m;
            return m;
        }

        void BuildParticles()
        {
            var smat = MaterialLibrary.Custom("Smoke");
            smoke = MakeSystem("Exhaust Smoke", smat, 800, false);
            var main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 22f);
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.9f, 0.88f, 0.55f), new Color(0.78f, 0.76f, 0.74f, 0.45f));
            main.gravityModifier = -0.01f;
            var shape = smoke.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.6f;
            var sol = smoke.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 5f));
            var col = smoke.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
                      new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var vel = smoke.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.limit = new ParticleSystem.MinMaxCurve(6f);
            vel.dampen = 0.08f;

            blast = MakeSystem("Explosions", smat, 600, true);
            var bm = blast.main;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(8f, 45f);
            bm.startSize = new ParticleSystem.MinMaxCurve(3f, 12f);
            bm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f, 0.9f), new Color(0.35f, 0.32f, 0.3f, 0.8f));
            var bs = blast.shape;
            bs.enabled = true;
            bs.shapeType = ParticleSystemShapeType.Sphere;
            bs.radius = 2f;
            var bsol = blast.sizeOverLifetime;
            bsol.enabled = true;
            bsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 3f));
            var bcol = blast.colorOverLifetime;
            bcol.enabled = true;
            var bg = new Gradient();
            bg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(0.25f, 0.23f, 0.22f), 0.35f), new GradientColorKey(new Color(0.2f, 0.2f, 0.2f), 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            bcol.color = new ParticleSystem.MinMaxGradient(bg);
        }

        ParticleSystem MakeSystem(string name, Material mat, int max, bool burstOnly)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = padAnchor;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ debris & effects

        /// <summary>Moves separated parts into their own object that follows the debris body.</summary>
        public void AddDebris(Debris d)
        {
            UpdateVesselTransform();
            var root = new GameObject("Debris").transform;
            root.position = ToRender(d.pos);
            root.rotation = RotationFor(d.angle);
            foreach (int u in d.parts)
            {
                GameObject go;
                if (partObjects.TryGetValue(u, out go) && go != null)
                {
                    go.transform.SetParent(root, true);
                    partObjects.Remove(u);
                    Transform cnp;
                    if (canopies.TryGetValue(u, out cnp)) { Destroy(cnp.gameObject); canopies.Remove(u); }
                }
            }
            debrisRoots[d] = root;
        }

        public void Explode(Vector3 renderPos, float size)
        {
            padAnchor.position = ToRender(new Vector2d(0, FlightVessel.R));
            var ep = new ParticleSystem.EmitParams();
            ep.position = padAnchor.InverseTransformPoint(renderPos);
            ep.applyShapeToPosition = true;
            int n = Mathf.Clamp((int)(size * 8f), 20, 160);
            blast.Emit(ep, n);
            StartCoroutine(Flash(renderPos, size));
        }

        IEnumerator Flash(Vector3 pos, float size)
        {
            var mat = MaterialLibrary.Custom("Stars");
            mat.SetTexture("_MainTex", ProceduralTextures.Glow(128));
            mat.SetFloat("_Brightness", 3f);
            var go = MakeObject("Flash", PlanetFactory.Quad(), mat);
            var t = go.transform;
            for (float a = 0f; a < 1.2f; a += Time.deltaTime)
            {
                float k = a / 1.2f;
                t.position = pos + Vector3.back * 2f;
                t.localScale = Vector3.one * size * (2f + k * 6f);
                mat.SetFloat("_Brightness", 3f * (1f - k) * (1f - k));
                yield return null;
            }
            Destroy(go);
        }

        public void SetXRay(bool on)
        {
            xray = on;
            foreach (var kv in partObjects) if (kv.Value != null) XRay.Apply(kv.Value, vessel.state.byUid[kv.Key], on);
        }

        /// <summary>Animates folding legs / solar arrays, chute canopies and the inside-view fuel levels.</summary>
        void UpdateParts()
        {
            foreach (var kv in partObjects)
            {
                int uid = kv.Key;
                var go = kv.Value;
                if (go == null) continue;
                var p = vessel.state.byUid[uid];
                var d = p.Def;
                float f;
                if (foldAnim.TryGetValue(uid, out f))
                {
                    float target = vessel.folded.Contains(uid) ? 1f : 0f;
                    float nf = Mathf.MoveTowards(f, target, Time.deltaTime * 1.4f);
                    if (nf != f || !foldApplied.Contains(uid))
                    {
                        foldApplied.Add(uid);
                        foldAnim[uid] = nf;
                        PartPose pose;
                        vessel.poses.TryGetValue(uid, out pose);
                        var dims = PartDims.Of(p);
                        float e = nf * nf * (3f - 2f * nf);
                        if (d.isLeg)
                        {
                            // Legs swing up about their hinge until they lie along the hull (like a booster's landing legs).
                            var hinge = new Vector3(0.12f, dims.height * 0.3f, 0f);
                            var q = Quaternion.Euler(0f, 0f, 140f * e);
                            go.transform.localRotation = pose.rotation * q;
                            go.transform.localPosition = pose.position + pose.rotation * (hinge - q * hinge);
                        }
                        else
                        {
                            // Solar arrays retract concertina-style towards their mount.
                            go.transform.localPosition = pose.position;
                            go.transform.localRotation = pose.rotation;
                            go.transform.localScale = new Vector3(Mathf.Lerp(1f, 0.26f, e), Mathf.Lerp(1f, 0.85f, e), 1f);
                        }
                    }
                }
                if (xray && d.fuelMass > 0f)
                {
                    float fuel;
                    vessel.state.fuel.TryGetValue(uid, out fuel);
                    XRay.SetFuel(go, fuel / Mathf.Max(1e-5f, PartMass.Fuel(p)));
                }
                if (d.isParachute) UpdateCanopy(uid, go, d);
            }
        }

        void UpdateCanopy(int uid, GameObject part, PartDefinition d)
        {
            bool show = vessel.chutesDeployed.Contains(uid) && vessel.chuteArmed && vessel.Altitude < 30000 && vessel.status != FlightStatus.Crashed;
            Transform c;
            if (!canopies.TryGetValue(uid, out c))
            {
                if (!show) return;
                if (canopyMesh == null)
                {
                    var mb = new MeshBuilder();
                    var dome = new List<Vector2>();
                    for (int i = 0; i <= 10; i++)
                    {
                        float t = i / 10f;
                        dome.Add(new Vector2(Mathf.Cos(t * Mathf.PI * 0.5f), Mathf.Sin(t * Mathf.PI * 0.5f) * 0.55f));
                    }
                    dome[dome.Count - 1] = new Vector2(0f, 0.55f);
                    mb.Lathe(dome, 24, 0);
                    mb.Lathe(new List<Vector2> { new Vector2(0f, 0.53f), new Vector2(1f, -0.02f) }, 24, 0); // underside
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 60f * Mathf.Deg2Rad;
                        mb.Tube(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), new Vector3(0f, -1.6f, 0f), 0.008f, 4, 0);
                    }
                    canopyMesh = mb.ToMesh("Canopy");
                }
                var go = new GameObject("Canopy");
                go.AddComponent<MeshFilter>().sharedMesh = canopyMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MaterialLibrary.Get("chute");
                c = go.transform;
                canopies[uid] = c;
            }
            c.gameObject.SetActive(show && part.activeInHierarchy);
            if (!show) return;
            // Drogue first (small), then the main canopy as it inflates; it trails above the part, against the airflow.
            float open = vessel.chuteOpen;
            float radius = (1.4f + open * 8.6f) * Mathf.Sqrt(d.chuteScale);
            var v = vessel.vel;
            Vector3 trail = v.magnitude > 2 ? -new Vector3((float)v.x, (float)v.y, 0f).normalized : vesselRoot.up;
            c.position = part.transform.position + trail * radius * 1.6f;
            c.rotation = Quaternion.FromToRotation(Vector3.up, trail);
            c.localScale = Vector3.one * radius;
        }

        public void HideVessel()
        {
            foreach (var go in partObjects.Values) if (go != null) go.SetActive(false);
        }

        // ------------------------------------------------------------------ per frame

        void UpdateVesselTransform()
        {
            var rot = RotationFor(vessel.angle);
            vesselRoot.rotation = rot;
            vesselRoot.position = ToRender(vessel.pos) + rot * new Vector3(-vessel.comLocal.x, -vessel.comLocal.y, 0f);
        }

        /// <summary>Lays out everything for this frame. viewSize is the visible height in metres.</summary>
        public void Render(float viewSize, Vector3 camPos)
        {
            origin = vessel.pos;
            UpdateVesselTransform();
            UpdateParts();
            double alt = vessel.Altitude;
            float pr = (float)EarthAtmosphere.PressureRatio(alt);

            // Engines: plume length/width follow throttle and expand as air pressure falls.
            foreach (var f in flames)
            {
                float target = 0f;
                vessel.engineOutput.TryGetValue(f.uid, out target);
                if (!partObjects.ContainsKey(f.uid)) target = 0f;
                f.current = Mathf.MoveTowards(f.current, target, Time.deltaTime * 6f);
                bool on = f.current > 0.01f;
                if (f.t.gameObject.activeSelf != on) f.t.gameObject.SetActive(on);
                if (!on) continue;
                float vac = 1f - pr;
                float width = f.exitDiameter * (0.95f + vac * 1.8f);
                float length = f.exitDiameter * (2.2f + 4.5f * f.current) * (1f + vac * 2.2f);
                Vector3 axis = -f.t.parent.up;
                f.t.rotation = Quaternion.LookRotation(Vector3.forward, -axis);
                var ls = f.t.parent.lossyScale;
                f.t.localScale = new Vector3(width / Mathf.Max(0.001f, ls.x), length / Mathf.Max(0.001f, ls.y), 1f);
                f.mat.SetFloat("_Intensity", (1.1f + f.current * 0.9f) * (0.6f + 0.4f * pr + vac * 0.2f));
                f.mat.SetFloat("_Expansion", vac);
            }

            // Smoke from the lowest firing engine while in the lower atmosphere
            padAnchor.position = ToRender(new Vector2d(0, FlightVessel.R));
            padAnchor.rotation = Quaternion.identity;
            FlameFx lowest = null;
            float lowY = float.MaxValue;
            foreach (var f in flames)
            {
                if (f.current < 0.05f) continue;
                if (f.t.position.y < lowY) { lowY = f.t.position.y; lowest = f; }
            }
            var em = smoke.emission;
            if (lowest != null && alt < 18000)
            {
                smoke.transform.position = lowest.t.position;
                smoke.transform.rotation = Quaternion.LookRotation(-lowest.t.parent.up);
                float thrustSum = 0f;
                foreach (var v in vessel.engineOutput.Values) thrustSum += v;
                em.rateOverTime = Mathf.Clamp(45f * thrustSum, 0f, 160f) * Mathf.Clamp01(pr * 1.5f);
            }
            else em.rateOverTime = 0f;

            // Debris
            var dead = new List<Debris>();
            foreach (var kv in debrisRoots)
            {
                var d = kv.Key;
                if (d.dead)
                {
                    if (d.impacted && (d.pos - vessel.pos).magnitude < 20000) Explode(ToRender(d.pos), 6f);
                    Destroy(kv.Value.gameObject);
                    dead.Add(d);
                    continue;
                }
                var dp = ToRender(d.pos);
                if (dp.sqrMagnitude > 1e10f) { kv.Value.gameObject.SetActive(false); continue; } // too far to see
                kv.Value.gameObject.SetActive(true);
                var rot = RotationFor(d.angle);
                kv.Value.rotation = rot;
                kv.Value.position = dp + rot * new Vector3(-d.comLocal.x, -d.comLocal.y, 0f);
            }
            foreach (var d in dead) debrisRoots.Remove(d);

            BuildGround(viewSize);

            // Launch complex
            var padPos = ToRender(new Vector2d(0, FlightVessel.R));
            bool padVisible = padPos.sqrMagnitude < 1.6e9f;
            if (pad.gameObject.activeSelf != padVisible) pad.gameObject.SetActive(padVisible);
            if (padVisible) pad.position = padPos;

            // Clouds
            for (int i = 0; i < clouds.Count; i++)
            {
                var p = ToRender(cloudPos[i]);
                bool vis = p.sqrMagnitude < 4e10f && viewSize < 300000f;
                if (clouds[i].gameObject.activeSelf != vis) clouds[i].gameObject.SetActive(vis);
                if (!vis) continue;
                p.z = 60f;
                clouds[i].position = p;
                double phi = System.Math.Atan2(cloudPos[i].y, cloudPos[i].x);
                clouds[i].rotation = Quaternion.Euler(0f, 0f, (float)(phi * Mathf.Rad2Deg) - 90f);
            }

            // Sky colour: blue at the surface, black in space and when zoomed out to orbit scale
            float k = Mathf.Clamp01((float)alt / 65000f);
            k = Mathf.Max(k, Mathf.InverseLerp(40000f, 400000f, viewSize));
            var skyLow = new Color(0.40f, 0.64f, 0.95f);
            var skyMid = new Color(0.08f, 0.15f, 0.36f);
            Color sky = k < 0.45f ? Color.Lerp(skyLow, skyMid, k / 0.45f) : Color.Lerp(skyMid, Color.black, (k - 0.45f) / 0.55f);
            cam.backgroundColor = sky;
            stars.position = camPos;
            starMat.SetFloat("_Brightness", Mathf.Clamp01((k - 0.35f) / 0.4f) * 1.4f);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.55f, 0.62f, 0.75f), new Color(0.12f, 0.13f, 0.16f), k);
        }

        /// <summary>Builds the visible stretch of planet surface (and the glow of the atmosphere above it).</summary>
        void BuildGround(float viewSize)
        {
            double R = FlightVessel.R;
            double phi0 = System.Math.Atan2(vessel.pos.y, vessel.pos.x);
            double alt = vessel.Altitude;
            double span = (viewSize * 2.2 + System.Math.Max(0, alt) * 1.2 + 2000) / R;
            if (span > System.Math.PI) span = System.Math.PI;
            double depth = System.Math.Min(R, viewSize * 3.0 + alt + 3000);
            double innerR = R - depth;
            double atmoTop = R + 90000;

            gv.Clear(); gc.Clear(); guv.Clear(); gt.Clear();
            av.Clear(); ac.Clear(); at.Clear();
            double tile = 18.0;
            double u0 = phi0 * R / tile;
            u0 -= System.Math.Floor(u0);
            float detail = 1f - Mathf.InverseLerp(2500f, 40000f, viewSize);
            groundMat.SetFloat("_Detail", detail);
            float atmoAlpha = Mathf.InverseLerp(8000f, 60000f, viewSize) * 0.6f;
            for (int i = 0; i <= Segments; i++)
            {
                double phi = phi0 - span + 2 * span * i / Segments;
                double c = System.Math.Cos(phi), s = System.Math.Sin(phi);
                var surface = new Vector2d(c * R, s * R);
                var inner = new Vector2d(c * innerR, s * innerR);
                gv.Add(ToRender(surface));
                gv.Add(ToRender(inner));
                double arc = (phi - System.Math.PI / 2) * R;
                Color col = System.Math.Abs(arc) < 260 ? new Color(0.55f, 0.55f, 0.53f) : band.Sample(phi);
                gc.Add(col);
                gc.Add(Color.Lerp(col, new Color(0.18f, 0.14f, 0.1f), 0.6f));
                float u = (float)((phi - phi0) * R / tile + u0);
                guv.Add(new Vector2(u, 0f));
                guv.Add(new Vector2(u, (float)(depth / tile)));

                av.Add(ToRender(surface));
                av.Add(ToRender(new Vector2d(c * atmoTop, s * atmoTop)));
                ac.Add(new Color(0.45f, 0.65f, 1f, atmoAlpha));
                ac.Add(new Color(0.2f, 0.35f, 0.9f, 0f));
                if (i < Segments)
                {
                    int b = i * 2;
                    gt.Add(b); gt.Add(b + 2); gt.Add(b + 1);
                    gt.Add(b + 1); gt.Add(b + 2); gt.Add(b + 3);
                    at.Add(b); at.Add(b + 1); at.Add(b + 2);
                    at.Add(b + 1); at.Add(b + 3); at.Add(b + 2);
                }
            }
            groundMesh.Clear();
            groundMesh.SetVertices(gv);
            groundMesh.SetColors(gc);
            groundMesh.SetUVs(0, guv);
            groundMesh.SetTriangles(gt, 0);
            groundMesh.RecalculateBounds();
            atmoMesh.Clear();
            atmoMesh.SetVertices(av);
            atmoMesh.SetColors(ac);
            atmoMesh.SetTriangles(at, 0);
            atmoMesh.RecalculateBounds();
            atmo.gameObject.SetActive(atmoAlpha > 0.01f);
            ground.position = Vector3.zero;
            atmo.position = new Vector3(0f, 0f, 200f);
        }

        public bool LandedInOcean()
        {
            return band.IsOcean(System.Math.Atan2(vessel.pos.y, vessel.pos.x));
        }
    }
}
