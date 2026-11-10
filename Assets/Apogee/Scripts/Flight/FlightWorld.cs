using System.Collections;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rendering;
using Apogee.Rocket;
using Apogee.Simulation;
using Apogee.Astro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Flight
{
    public enum CamMode { Chase, Free, Cockpit, LaunchCam }

    /// <summary>
    /// Landing leg built from moving pieces: the strut swings down from the hull, the inner piston
    /// telescopes out, and the shock absorber compresses under the vehicle's weight after touchdown.
    /// </summary>
    public class LegRig : MonoBehaviour
    {
        Transform pivot, piston;
        Vector3 dirDeployed;
        float length;
        public float deploy = 1f, compress;

        public static LegRig Build(GameObject part, float h)
        {
            var rig = part.AddComponent<LegRig>();
            var hip = new Vector3(0.12f, h * 0.3f, 0f);
            var foot = new Vector3(0.95f, -h / 2 + 0.12f, 0f) * (h > 3f ? 1.25f : 1f);
            foot.y = -h / 2 + 0.12f;
            rig.dirDeployed = (foot - hip).normalized;
            rig.length = (foot - hip).magnitude;

            var housing = new MeshBuilder();
            housing.Box(new Vector3(0.06f, h * 0.3f, 0f), new Vector3(0.14f, 0.45f, 0.34f), 0);
            Child(part.transform, "Housing", housing, "dark_metal");

            rig.pivot = new GameObject("Strut").transform;
            rig.pivot.SetParent(part.transform, false);
            rig.pivot.localPosition = hip;
            var outer = new MeshBuilder();
            outer.Tube(Vector3.zero, rig.dirDeployed * rig.length * 0.62f, 0.085f, 12, 0);
            outer.Tube(rig.dirDeployed * rig.length * 0.1f, rig.dirDeployed * rig.length * 0.3f, 0.1f, 12, 0);
            Child(rig.pivot, "Outer", outer, "carbon");

            rig.piston = new GameObject("Piston").transform;
            rig.piston.SetParent(rig.pivot, false);
            var inner = new MeshBuilder();
            inner.Tube(rig.dirDeployed * rig.length * 0.4f, rig.dirDeployed * rig.length, 0.055f, 10, 0);
            Child(rig.piston, "Inner", inner, "aluminium");
            var pad = new MeshBuilder();
            pad.transform = Matrix4x4.TRS(rig.dirDeployed * rig.length, Quaternion.identity, Vector3.one);
            pad.Lathe(new List<Vector2> { new Vector2(0f, -0.02f), new Vector2(0.28f, -0.02f), new Vector2(0.24f, 0.09f), new Vector2(0.08f, 0.13f) }, 20, 0);
            Child(rig.piston, "Foot", pad, "aluminium");
            return rig;
        }

        static void Child(Transform parent, string name, MeshBuilder mb, string mat)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Get(mat);
            r.shadowCastingMode = ShadowCastingMode.On;
        }

        /// <summary>deploy 0 = stowed against the hull, 1 = locked down. Swing first, then telescope.</summary>
        public void Apply()
        {
            float swing = Mathf.Clamp01(deploy / 0.65f);
            float extend = Mathf.Clamp01((deploy - 0.65f) / 0.35f);
            swing = swing * swing * (3f - 2f * swing);
            // stowed: strut folded up along the hull (like a booster's landing legs)
            pivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(150f, 0f, swing));
            float travel = length * 0.42f;
            float offset = -travel * (1f - extend) - length * 0.12f * compress * extend;
            piston.localPosition = dirDeployed * offset;
        }
    }

    /// <summary>
    /// Renders the flight: the Solar System (scaled space) behind, and in front the rocket with paint,
    /// 3D engine plumes, RCS jets, folding legs, parachutes, re-entry plasma, smoke, debris, explosions,
    /// nearby terrain and the launch complex. Floating origin: the vessel's centre of mass is at (0,0,0).
    /// </summary>
    public class FlightWorld : MonoBehaviour
    {
        public Camera cam;
        public SpaceView space;
        public LocalTerrain terrain;
        public LaunchComplex site;
        public CloudField clouds;
        Transform vapor;
        Material vaporMat;
        public Light sun;
        FlightVessel vessel;

        Transform vesselRoot;
        readonly Dictionary<int, GameObject> partObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, LegRig> legs = new Dictionary<int, LegRig>();
        readonly Dictionary<int, float> solarAnim = new Dictionary<int, float>();
        readonly Dictionary<int, Transform> canopies = new Dictionary<int, Transform>();
        readonly Dictionary<Debris, Transform> debrisRoots = new Dictionary<Debris, Transform>();
        Mesh canopyMesh;
        bool xray;
        public bool XRayOn { get { return xray; } }

        class FlameFx { public Transform t; public Material mat; public float exitDiameter, current; public int uid; }
        readonly List<FlameFx> flames = new List<FlameFx>();
        readonly List<FlameFx> rcsJets = new List<FlameFx>();
        ParticleSystem smoke, blast;
        Transform smokeAnchor;
        Transform plasma;
        Material plasmaMat;

        // camera state
        public CamMode camMode = CamMode.LaunchCam;
        public float camYaw = 35f, camPitch = 8f, camDistance = 60f, camDistanceGoal = 60f;
        public float headYaw, headPitch;
        Vector3 launchCamPos;
        public Vector3d ViewerFrame { get; private set; }

        public void Init(FlightVessel v)
        {
            vessel = v;
            var spaceGo = new GameObject("Scaled Space");
            space = spaceGo.AddComponent<SpaceView>();
            space.Init();

            var camGo = new GameObject("Flight Camera");
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.cullingMask = ~((1 << SpaceView.Layer) | (1 << 29));
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 160000f;
            cam.fieldOfView = 55f;
            cam.depth = 0;
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();

            StackOverlay(space.cam, cam);
            PostFX.Apply(space.cam, cam);

            var lightGo = new GameObject("Sun");
            sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.color = new Color(1f, 0.97f, 0.92f);
            RenderSettings.ambientMode = AmbientMode.Flat;

            terrain = new GameObject("Local Terrain").AddComponent<LocalTerrain>();
            terrain.Init();
            clouds = new GameObject("Clouds").AddComponent<CloudField>();
            clouds.Init();
            site = new GameObject("Launch Complex").AddComponent<LaunchComplex>();
            site.Build(SolarSystem.Earth, vessel.LaunchLat, vessel.LaunchLon);
            smokeAnchor = site.transform;

            BuildVessel();
            BuildParticles();
            BuildPlasma();
            BuildVapor();
            vessel.OnDebris += AddDebris;
            vessel.OnExplosion += (p, size) => Explode((p - vessel.pos).ToFloat(), size);
        }

        /// <summary>
        /// URP composites cameras through a camera stack: the Solar System camera is the base, the flight camera
        /// an overlay drawn on top (clearing only depth). Done via reflection so the code also builds without URP.
        /// </summary>
        public static void StackOverlay(Camera baseCam, Camera overlay)
        {
            try
            {
                var t = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                var rt = System.Type.GetType("UnityEngine.Rendering.Universal.CameraRenderType, Unity.RenderPipelines.Universal.Runtime");
                if (t == null || rt == null) return;
                var od = overlay.GetComponent(t) ?? overlay.gameObject.AddComponent(t);
                t.GetProperty("renderType").SetValue(od, System.Enum.Parse(rt, "Overlay"), null);
                var bd = baseCam.GetComponent(t) ?? baseCam.gameObject.AddComponent(t);
                t.GetProperty("renderType").SetValue(bd, System.Enum.Parse(rt, "Base"), null);
                var stack = t.GetProperty("cameraStack").GetValue(bd, null) as System.Collections.IList;
                if (stack != null && !stack.Contains(overlay)) stack.Add(overlay);
                var shadows = t.GetProperty("renderShadows");
                if (shadows != null) shadows.SetValue(bd, false, null);
            }
            catch (System.Exception e) { Debug.LogWarning("Camera stacking unavailable: " + e.Message); }
        }

        // ------------------------------------------------------------------ vessel

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
                go.layer = RocketView.PartLayer;
                go.transform.SetParent(vesselRoot, false);
                go.transform.localPosition = pose.position;
                go.transform.localRotation = pose.rotation;
                go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = model.materials;
                r.shadowCastingMode = ShadowCastingMode.On;
                PartPaint.Apply(r, model.materialKeys, p.paint, new MaterialPropertyBlock());
                DecalText.Attach(go, p);
                go.AddComponent<MeshCollider>().sharedMesh = model.mesh;
                var pv = go.AddComponent<PartView>();
                pv.uid = p.uid;
                pv.part = p;
                partObjects[p.uid] = go;
                var d = p.Def;
                var dims = PartDims.Of(p);
                if (d.isLeg)
                {
                    r.enabled = false;                    // replaced by the moving rig (collider stays for tapping)
                    var rig = LegRig.Build(go, dims.height);
                    rig.deploy = vessel.folded.Contains(p.uid) ? 0f : 1f;
                    rig.Apply();
                    legs[p.uid] = rig;
                }
                if (d.solarKW > 0f) solarAnim[p.uid] = vessel.folded.Contains(p.uid) ? 1f : 0f;
                if (d.IsEngine && !d.isRcs)
                {
                    float exit = d.shape == PartShape.VacuumEngine ? dims.Radius * 2.64f : d.shape == PartShape.SolidBooster ? dims.Radius * 1.2f : dims.Radius * (d.minThrottle <= 0.15f ? 1.64f : 1.76f);
                    var fx = MakeFlame(go.transform, new Vector3(0f, -dims.height * 0.5f + 0.05f, 0f), Quaternion.identity, new Material(flameBase), exit, p.uid);
                    Color col = d.solidFuel ? new Color(1f, 0.72f, 0.35f) : d.finish == Finish.Stainless ? new Color(0.55f, 0.6f, 1f) : new Color(1f, 0.5f, 0.18f);
                    fx.mat.SetColor("_Color", col);
                    flames.Add(fx);
                }
                if (d.isRcs)
                {
                    // four nozzles around the block: white puffs of cold gas
                    for (int k = 0; k < 4; k++)
                    {
                        var dir = Quaternion.Euler(0f, 0f, k * 90f) * Vector3.up;
                        var m = new Material(flameBase);
                        m.SetColor("_Color", new Color(0.85f, 0.9f, 1f));
                        m.SetColor("_Core", Color.white);
                        var fx = MakeFlame(go.transform, new Vector3(0.18f, 0f, 0f) + dir * 0.12f, Quaternion.FromToRotation(Vector3.down, dir), m, 0.12f, p.uid);
                        rcsJets.Add(fx);
                    }
                }
            }
        }

        FlameFx MakeFlame(Transform parent, Vector3 localPos, Quaternion localRot, Material mat, float exit, int uid)
        {
            var go = new GameObject("Flame");
            go.layer = parent.gameObject.layer == RocketView.PartLayer ? 0 : parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = CrossedQuads();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return new FlameFx { t = go.transform, mat = mat, exitDiameter = exit, uid = uid };
        }

        static Mesh crossed;
        /// <summary>Three quads crossed around the axis: a plume that looks round from every angle in 3D.</summary>
        static Mesh CrossedQuads()
        {
            if (crossed != null) return crossed;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                var q = Quaternion.Euler(0f, k * 60f, 0f);
                int b = v.Count;
                v.Add(q * new Vector3(-0.5f, 0f, 0f)); v.Add(q * new Vector3(0.5f, 0f, 0f));
                v.Add(q * new Vector3(0.5f, -1f, 0f)); v.Add(q * new Vector3(-0.5f, -1f, 0f));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            crossed = new Mesh { name = "Plume" };
            crossed.SetVertices(v);
            crossed.SetUVs(0, uv);
            crossed.SetTriangles(t, 0);
            crossed.bounds = new Bounds(new Vector3(0, -0.5f, 0), new Vector3(2, 2, 2));
            return crossed;
        }

        public void SetXRay(bool on)
        {
            xray = on;
            foreach (var kv in partObjects)
                if (kv.Value != null && !legs.ContainsKey(kv.Key)) XRay.Apply(kv.Value, vessel.state.byUid[kv.Key], on);
        }

        public void AddDebris(Debris d)
        {
            var root = new GameObject("Debris").transform;
            foreach (int u in d.parts)
            {
                GameObject go;
                if (partObjects.TryGetValue(u, out go) && go != null)
                {
                    go.transform.SetParent(root, false);
                    partObjects.Remove(u);
                    Transform c;
                    if (canopies.TryGetValue(u, out c)) { Destroy(c.gameObject); canopies.Remove(u); }
                }
            }
            debrisRoots[d] = root;
        }

        // ------------------------------------------------------------------ effects

        void BuildParticles()
        {
            var smat = MaterialLibrary.Custom("Smoke");
            smoke = MakeSystem("Exhaust Smoke", smat, 900);
            var main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 28f);
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.94f, 0.92f, 0.9f, 0.55f), new Color(0.8f, 0.78f, 0.76f, 0.45f));
            var shape = smoke.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = 0.8f;
            var sol = smoke.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 5f));
            var col = smoke.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
                      new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var lim = smoke.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = new ParticleSystem.MinMaxCurve(6f);
            lim.dampen = 0.08f;

            blast = MakeSystem("Explosions", smat, 700);
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

        ParticleSystem MakeSystem(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = smokeAnchor;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void BuildPlasma()
        {
            var mb = new MeshBuilder();
            mb.Lathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, -0.4f), new Vector2(1.25f, -1.6f), new Vector2(1.1f, -3f) }, 24, 0);
            var go = new GameObject("Plasma");
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("Plasma");
            plasmaMat = MaterialLibrary.Custom("Flame");
            plasmaMat.SetColor("_Color", new Color(1f, 0.45f, 0.25f));
            plasmaMat.SetColor("_Core", new Color(1f, 0.85f, 0.7f));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = plasmaMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            plasma = go.transform;
            go.SetActive(false);
        }

        /// <summary>Prandtl–Glauert vapour cone that flashes around the rocket as it goes through the sound barrier.</summary>
        void BuildVapor()
        {
            var mb = new MeshBuilder();
            mb.Lathe(new List<Vector2> { new Vector2(0.05f, 0.5f), new Vector2(0.55f, 0.1f), new Vector2(1f, -0.5f) }, 32, 0);
            var go = new GameObject("Vapor Cone");
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("Vapor");
            vaporMat = MaterialLibrary.Custom("Ghost");
            vaporMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.35f));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = vaporMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            vapor = go.transform;
            go.SetActive(false);
        }

        /// <summary>Screen position of a part (for the engine number tags). False if hidden/behind.</summary>
        public bool PartScreen(int uid, out Vector3 screen)
        {
            screen = Vector3.zero;
            GameObject go;
            if (!partObjects.TryGetValue(uid, out go) || go == null || !cam.enabled) return false;
            var dims = PartDims.Of(vessel.state.byUid[uid]);
            screen = cam.WorldToScreenPoint(go.transform.TransformPoint(new Vector3(0f, -dims.height * 0.5f, dims.Radius * 1.3f)));
            return screen.z > 0f;
        }

        public void Explode(Vector3 renderPos, float size)
        {
            var ep = new ParticleSystem.EmitParams();
            ep.position = smokeAnchor.InverseTransformPoint(renderPos);
            ep.applyShapeToPosition = true;
            blast.Emit(ep, Mathf.Clamp((int)(size * 8f), 20, 160));
            StartCoroutine(Flash(renderPos, size));
        }

        IEnumerator Flash(Vector3 pos, float size)
        {
            var mat = MaterialLibrary.Custom("Stars");
            mat.SetTexture("_MainTex", ProceduralTextures.Glow(128));
            var go = new GameObject("Flash");
            go.AddComponent<MeshFilter>().sharedMesh = PlanetFactory.Quad();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var t = go.transform;
            for (float a = 0f; a < 1.2f; a += Time.deltaTime)
            {
                float k = a / 1.2f;
                t.position = pos;
                t.rotation = cam.transform.rotation;
                t.localScale = Vector3.one * size * (2f + k * 6f);
                mat.SetFloat("_Brightness", 3f * (1f - k) * (1f - k));
                yield return null;
            }
            Destroy(go);
        }

        Transform Canopy(int uid)
        {
            Transform c;
            if (canopies.TryGetValue(uid, out c)) return c;
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
                mb.Lathe(new List<Vector2> { new Vector2(0f, 0.53f), new Vector2(1f, -0.02f) }, 24, 0);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    mb.Tube(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), new Vector3(0f, -1.6f, 0f), 0.006f, 4, 0);
                }
                canopyMesh = mb.ToMesh("Canopy");
            }
            var go = new GameObject("Canopy");
            go.AddComponent<MeshFilter>().sharedMesh = canopyMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("chute");
            c = go.transform;
            canopies[uid] = c;
            return c;
        }

        // ------------------------------------------------------------------ per frame

        public Vector3 SunDirection { get; private set; }

        public void Render(float dt)
        {
            var v = vessel;
            var b = v.body;
            double t = v.time;
            var bodyAbs = b.AbsolutePosition(t);
            var vesselAbs = bodyAbs + v.pos;
            var sunDir = (-vesselAbs).normalized.ToFloat();
            SunDirection = sunDir;
            sun.transform.rotation = Quaternion.LookRotation(-sunDir);
            var up = v.Up.ToFloat();

            // vessel at the origin (its centre of mass)
            vesselRoot.rotation = v.rot;
            vesselRoot.position = -(v.rot * v.comLocal);

            UpdateParts(dt);
            UpdateFlames(dt);
            UpdateDebris();
            UpdateStation();
            UpdateCamera(dt, up);

            // atmosphere colour and light: blue sky by day at the ground, fading to black with altitude
            double alt = v.Altitude;
            float sunUp = Vector3.Dot(up, sunDir);
            float day = Mathf.Clamp01(sunUp * 3f + 0.25f);
            Color sky = Color.black, zenith = Color.black, horizon = Color.black, ground = Color.black;
            float stars = 1f, haze = 0f, atmo = 0f;
            if (b.HasAtmosphere && alt < b.atmosphere.height)
            {
                float thick = Mathf.Clamp01((float)(b.atmosphere.Pressure(alt) / b.atmosphere.p0));
                float k = Mathf.Pow(thick, 0.3f);                 // ~1 at the ground, ~0.3 at 30 km, 0 in space
                atmo = Mathf.Clamp01(k * 1.15f) * Mathf.Clamp01(day * 1.2f);
                var baseSky = b.atmosphere.skyColor;
                zenith = Color.Lerp(new Color(0.02f, 0.03f, 0.1f), baseSky * 0.72f, k);
                horizon = Color.Lerp(baseSky * 0.6f, Color.Lerp(baseSky, Color.white, 0.55f), k);
                float dusk = Mathf.Clamp01(1f - Mathf.Abs(sunUp) * 4f);
                horizon = Color.Lerp(horizon, new Color(1f, 0.55f, 0.28f), dusk * 0.6f);
                ground = Color.Lerp(horizon, new Color(0.45f, 0.5f, 0.55f), 0.4f);
                sky = horizon * atmo;
                stars = Mathf.Clamp01(1f - atmo * 1.6f);
                haze = 0.000012f * thick;
            }
            space.SetSky(up, sunDir, zenith, horizon, ground, Color.Lerp(new Color(1f, 0.6f, 0.3f), new Color(1f, 0.95f, 0.85f), Mathf.Clamp01(sunUp * 3f)) * atmo, atmo);
            sun.intensity = Mathf.Lerp(0.05f, 1.25f, Mathf.Clamp01(sunUp * 4f + 0.6f));
            RenderSettings.ambientLight = Color.Lerp(new Color(0.05f, 0.05f, 0.07f), new Color(0.45f, 0.5f, 0.6f), day * (b.HasAtmosphere ? 1f : 0.25f));

            // local ground and launch site
            terrain.UpdatePatch(b, v.pos, t, sunDir, Color.Lerp(sky, Color.white, 0.25f), haze);
            if (b == site.Body) site.Place(t, v.pos); else site.gameObject.SetActive(false);

            // weather and the sound barrier
            Color sunCol = Color.Lerp(new Color(1f, 0.55f, 0.3f), new Color(1f, 0.97f, 0.92f), Mathf.Clamp01(sunUp * 4f));
            clouds.UpdateField(b, v.pos, t, sunDir, sunCol * Mathf.Clamp01(sunUp * 3f + 0.4f), Color.Lerp(new Color(0.08f, 0.09f, 0.12f), new Color(0.55f, 0.62f, 0.72f), day));
            double mach = v.Mach;
            bool cone = v.status == FlightStatus.Flying && mach > 0.93 && mach < 1.1 && v.dynamicPressure > 12000;
            if (vapor.gameObject.activeSelf != cone) vapor.gameObject.SetActive(cone);
            if (cone)
            {
                var air = v.airVelocity.normalized.ToFloat();
                float rr = v.maxRadius * 2.6f;
                vapor.position = air * (v.length * 0.1f);
                vapor.rotation = Quaternion.FromToRotation(Vector3.up, air);
                vapor.localScale = new Vector3(rr, rr * 1.4f, rr);
                vaporMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.45f * (1f - Mathf.Abs((float)mach - 1f) / 0.12f)));
            }

            // the Solar System behind it all
            ViewerFrame = v.pos + (Vector3d)cam.transform.position;
            space.Render(bodyAbs + ViewerFrame, cam.transform.rotation, cam.fieldOfView, t, 1e5, stars * 0.9f, sky, 0f);
            space.HideAllLines();
        }

        void UpdateParts(float dt)
        {
            foreach (var kv in partObjects)
            {
                int uid = kv.Key;
                var go = kv.Value;
                if (go == null) continue;
                var p = vessel.state.byUid[uid];
                var d = p.Def;
                if (d.IsEngine && !d.isRcs && d.gimbalRange > 0f)
                {
                    // gimbal: rotate the engine about its mount (top), opposite to the turn it produces
                    PartPose pose;
                    if (vessel.poses.TryGetValue(uid, out pose))
                    {
                        float on; vessel.engineOutput.TryGetValue(uid, out on);
                        var g = vessel.gimbalCmd * d.gimbalRange * (on > 0.01f ? 1f : 0.15f);
                        var q = Quaternion.Euler(-g.x, 0f, -g.z);
                        var top = pose.position + pose.rotation * new Vector3(0f, PartDims.Of(p).height * 0.5f, 0f);
                        go.transform.localRotation = q * pose.rotation;
                        go.transform.localPosition = top + q * (pose.position - top);
                    }
                }
                LegRig rig;
                if (legs.TryGetValue(uid, out rig))
                {
                    float target = vessel.folded.Contains(uid) ? 0f : 1f;
                    rig.deploy = Mathf.MoveTowards(rig.deploy, target, dt * 0.45f);
                    float load = vessel.status == FlightStatus.Landed ? 1f : vessel.status == FlightStatus.OnPad ? 0f : 0f;
                    rig.compress = Mathf.MoveTowards(rig.compress, load, dt * 2f);
                    rig.Apply();
                }
                float s;
                if (solarAnim.TryGetValue(uid, out s))
                {
                    float target = vessel.folded.Contains(uid) ? 1f : 0f;
                    float ns = Mathf.MoveTowards(s, target, dt * 0.6f);
                    solarAnim[uid] = ns;
                    float e = ns * ns * (3f - 2f * ns);
                    go.transform.localScale = new Vector3(Mathf.Lerp(1f, 0.22f, e), Mathf.Lerp(1f, 0.9f, e), 1f);
                }
                if (xray && d.fuelMass > 0f)
                {
                    float fuel; vessel.state.fuel.TryGetValue(uid, out fuel);
                    XRay.SetFuel(go, fuel / Mathf.Max(1e-5f, PartMass.Fuel(p)));
                }
                if (d.isParachute) UpdateCanopy(uid, go, d);
            }
        }

        void UpdateCanopy(int uid, GameObject part, PartDefinition d)
        {
            bool show = vessel.chutesDeployed.Contains(uid) && vessel.chuteArmed && vessel.InAtmosphere && vessel.status == FlightStatus.Flying;
            if (!show) { Transform c0; if (canopies.TryGetValue(uid, out c0)) c0.gameObject.SetActive(false); return; }
            var c = Canopy(uid);
            c.gameObject.SetActive(true);
            float radius = (1.4f + vessel.chuteOpen * 8.6f) * Mathf.Sqrt(d.chuteScale);
            var air = vessel.airVelocity;
            Vector3 trail = air.magnitude > 2 ? -air.normalized.ToFloat() : vessel.Up.ToFloat();
            c.position = part.transform.position + trail * radius * 1.6f;
            c.rotation = Quaternion.FromToRotation(Vector3.up, trail);
            c.localScale = Vector3.one * radius;
        }

        void UpdateFlames(float dt)
        {
            var b = vessel.body;
            double alt = vessel.Altitude;
            float pr = b.HasAtmosphere ? Mathf.Clamp01((float)b.atmosphere.PressureAtm(System.Math.Max(0, alt))) : 0f;
            foreach (var f in flames)
            {
                float target;
                vessel.engineOutput.TryGetValue(f.uid, out target);
                if (!partObjects.ContainsKey(f.uid)) target = 0f;
                f.current = Mathf.MoveTowards(f.current, target, dt * 6f);
                bool on = f.current > 0.01f;
                if (f.t.gameObject.activeSelf != on) f.t.gameObject.SetActive(on);
                if (!on) continue;
                float vac = 1f - pr;
                float width = f.exitDiameter * (0.95f + vac * 1.8f);
                float length = f.exitDiameter * (2.2f + 4.5f * f.current) * (1f + vac * 2.2f);
                var ls = f.t.parent.lossyScale;
                f.t.localScale = new Vector3(width / Mathf.Max(0.001f, ls.x), length / Mathf.Max(0.001f, ls.y), width / Mathf.Max(0.001f, ls.z));
                f.mat.SetFloat("_Intensity", (1.1f + f.current * 0.9f) * (0.6f + 0.4f * pr + vac * 0.2f) * (0.9f + 0.1f * Mathf.Sin(Time.time * 47f)));
                f.mat.SetFloat("_Expansion", vac);
            }
            // RCS: puffs on the nozzles that push in the commanded direction
            var cmd = vessel.rcsInput;
            bool rotating = vessel.rcsOn && (Mathf.Abs(vessel.pitchInput) + Mathf.Abs(vessel.yawInput) + Mathf.Abs(vessel.rollInput) > 0.05f);
            foreach (var j in rcsJets)
            {
                bool on = false;
                if (vessel.rcsOn && partObjects.ContainsKey(j.uid) && vessel.RcsFuelFraction() > 0f)
                {
                    // the nozzle pushes opposite to where its gas goes
                    var gasDirVessel = Quaternion.Inverse(vessel.rot) * (j.t.rotation * Vector3.down);
                    var push = -gasDirVessel;
                    on = (vessel.rcsFiring && Vector3.Dot(push, cmd.normalized) > 0.5f) || (rotating && Random.value < 0.5f);
                }
                j.current = Mathf.MoveTowards(j.current, on ? 1f : 0f, dt * 18f);
                bool vis = j.current > 0.02f;
                if (j.t.gameObject.activeSelf != vis) j.t.gameObject.SetActive(vis);
                if (!vis) continue;
                float len = 0.9f * j.current * (0.8f + Random.value * 0.4f);
                j.t.localScale = new Vector3(0.22f, len, 0.22f);
                j.mat.SetFloat("_Intensity", 0.7f * j.current);
                j.mat.SetFloat("_Expansion", 1f);
            }

            // exhaust smoke near the ground / in thick air
            FlameFx lowest = null;
            float lowY = float.MaxValue;
            var upv = vessel.Up.ToFloat();
            foreach (var f in flames)
            {
                if (f.current < 0.05f) continue;
                float h = Vector3.Dot(f.t.position, upv);
                if (h < lowY) { lowY = h; lowest = f; }
            }
            var em = smoke.emission;
            if (lowest != null && pr > 0.05f && site.gameObject.activeSelf)
            {
                smoke.transform.position = lowest.t.position;
                smoke.transform.rotation = Quaternion.LookRotation(-lowest.t.parent.up);
                float sum = 0f;
                foreach (var o in vessel.engineOutput.Values) sum += o;
                em.rateOverTime = Mathf.Clamp(50f * sum, 0f, 180f) * Mathf.Clamp01(pr * 1.4f);
            }
            else em.rateOverTime = 0f;

            // re-entry plasma around the leading end
            bool hot = vessel.heatFlux > 4e4 && vessel.status == FlightStatus.Flying;
            if (plasma.gameObject.activeSelf != hot) plasma.gameObject.SetActive(hot);
            if (hot)
            {
                var air = vessel.airVelocity.normalized.ToFloat();
                float r = vessel.maxRadius * 1.3f;
                plasma.position = air * (vessel.length * 0.5f + r * 0.3f);
                plasma.rotation = Quaternion.FromToRotation(Vector3.up, air);
                plasma.localScale = new Vector3(r, r * (1f + Mathf.Clamp01((float)(vessel.heatFlux / 1e6))), r);
                plasmaMat.SetFloat("_Intensity", Mathf.Clamp((float)(vessel.heatFlux / 3e5), 0.2f, 2.5f));
            }
        }

        void UpdateDebris()
        {
            var gone = new List<Debris>();
            foreach (var kv in debrisRoots)
            {
                var d = kv.Key;
                if (d.dead || !vessel.debris.Contains(d) || d.body != vessel.body)
                {
                    Destroy(kv.Value.gameObject);
                    gone.Add(d);
                    continue;
                }
                var rel = (d.pos - vessel.pos);
                bool vis = rel.magnitude < 150000;
                if (kv.Value.gameObject.activeSelf != vis) kv.Value.gameObject.SetActive(vis);
                if (!vis) continue;
                kv.Value.rotation = d.rot;
                kv.Value.position = rel.ToFloat() - d.rot * d.comLocal;
            }
            foreach (var d in gone) debrisRoots.Remove(d);
        }

        // ------------------------------------------------------------------ camera

        public void SetLaunchCamera()
        {
            camMode = CamMode.LaunchCam;
            launchCamPos = Vector3.zero;
        }

        void UpdateCamera(float dt, Vector3 up)
        {
            UpdateCameraPose(dt, up);
            Shake(cam.transform);
        }

        void UpdateCameraPose(float dt, Vector3 up)
        {
            camDistance = Mathf.Lerp(camDistance, camDistanceGoal, 1f - Mathf.Exp(-dt * 6f));
            var t = cam.transform;
            switch (camMode)
            {
                case CamMode.LaunchCam:
                {
                    // ground camera beside the pad, tracking the rocket as it climbs away
                    if (site.gameObject.activeSelf)
                    {
                        var p = site.transform.TransformPoint(new Vector3(140f, 4f, -170f));
                        t.position = p;
                        var target = Vector3.zero;
                        t.rotation = Quaternion.LookRotation(target - p, up);
                        cam.fieldOfView = Mathf.Clamp(Mathf.Rad2Deg * 2f * Mathf.Atan((vessel.length * 1.6f + 20f) / Mathf.Max(1f, p.magnitude)), 4f, 60f);
                        if (p.magnitude > 9000f) camMode = CamMode.Chase;
                        break;
                    }
                    camMode = CamMode.Chase;
                    goto case CamMode.Chase;
                }
                case CamMode.Chase:
                case CamMode.Free:
                {
                    cam.fieldOfView = 55f;
                    // chase: horizon-level (up = local vertical); free: locked to the rocket's own axes
                    Quaternion frame = camMode == CamMode.Chase
                        ? Quaternion.LookRotation(Vector3.ProjectOnPlane(vessel.rot * Vector3.forward, up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(vessel.rot * Vector3.forward, up).normalized : Vector3.ProjectOnPlane(Vector3.forward, up).normalized, up)
                        : vessel.rot;
                    if (camMode == CamMode.Chase) frame = HorizonFrame(up);
                    var q = frame * Quaternion.Euler(camPitch, camYaw, 0f);
                    float dist = Mathf.Max(camDistance, vessel.length * 0.8f + 4f);
                    t.position = q * new Vector3(0f, 0f, -dist);
                    t.rotation = Quaternion.LookRotation(-t.position.normalized, frame * Vector3.up);
                    break;
                }
                case CamMode.Cockpit:
                {
                    cam.fieldOfView = 70f;
                    var ctl = CommandPart();
                    if (ctl == null) { camMode = CamMode.Chase; break; }
                    var dims = PartDims.Of(vessel.state.byUid[vessel.state.controlUid]);
                    // pilot's eye: inside the capsule, looking out of the forward window
                    t.position = ctl.position + ctl.rotation * new Vector3(0f, dims.height * 0.12f, dims.Radius * 0.35f);
                    t.rotation = vessel.rot * Quaternion.Euler(headPitch, headYaw, 0f);
                    break;
                }
            }
        }

        /// <summary>Rumble: strongest at ignition near the ground and through max-Q, gone in vacuum.</summary>
        void Shake(Transform t)
        {
            float thrust = 0f;
            foreach (var o in vessel.engineOutput.Values) thrust = Mathf.Max(thrust, o);
            var b = vessel.body;
            float pr = b.HasAtmosphere ? Mathf.Clamp01((float)b.atmosphere.PressureAtm(System.Math.Max(0, vessel.Altitude))) : 0f;
            float ground = vessel.Grounded || vessel.Altitude < 1500 ? 1f : 0f;
            float amp = thrust * (0.25f * pr + 0.6f * ground * pr) + Mathf.Clamp01((float)(vessel.dynamicPressure / 45000.0)) * 0.35f;
            if (amp < 0.005f) return;
            float tt = Time.time * 22f;
            var n = new Vector3(Mathf.PerlinNoise(tt, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, tt) - 0.5f, Mathf.PerlinNoise(tt, tt * 0.7f) - 0.5f);
            if (camMode == CamMode.Cockpit) t.rotation = t.rotation * Quaternion.Euler(n * amp * 3f);
            else t.position += t.rotation * n * amp * Mathf.Clamp(camDistance * 0.02f, 0.2f, 3f) * (camMode == CamMode.LaunchCam ? 0.4f : 1f);
        }

        Quaternion horizonFrame = Quaternion.identity;
        bool horizonInit;

        /// <summary>A frame whose up is the local vertical and whose heading changes smoothly.</summary>
        Quaternion HorizonFrame(Vector3 up)
        {
            if (!horizonInit) { horizonFrame = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up).normalized, up); horizonInit = true; }
            var f = Vector3.ProjectOnPlane(horizonFrame * Vector3.forward, up);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(horizonFrame * Vector3.up, up);
            horizonFrame = Quaternion.LookRotation(f.normalized, up);
            return horizonFrame;
        }

        Transform CommandPart()
        {
            GameObject go;
            return partObjects.TryGetValue(vessel.state.controlUid, out go) && go != null ? go.transform : null;
        }

        public bool HasCockpit
        {
            get
            {
                PartInstance p;
                return vessel.state.byUid.TryGetValue(vessel.state.controlUid, out p) && vessel.state.alive.Contains(p.uid) && p.Def.crew > 0;
            }
        }

        // ------------------------------------------------------------------ space station

        Transform stationRoot;

        void UpdateStation()
        {
            var st = vessel.station;
            if (st == null) return;
            if (stationRoot == null)
            {
                stationRoot = new GameObject("Space Station").transform;
                foreach (var p in st.bp.parts)
                {
                    PartPose pose;
                    if (!st.poses.TryGetValue(p.uid, out pose)) continue;
                    var model = PartMeshFactory.Get(p);
                    var go = new GameObject("Station_" + p.partId);
                    go.transform.SetParent(stationRoot, false);
                    go.transform.localPosition = pose.position;
                    go.transform.localRotation = pose.rotation;
                    go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = model.materials;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    PartPaint.Apply(r, model.materialKeys, p.paint, new MaterialPropertyBlock());
                }
                // navigation lights so it can be spotted against the dark
                var beacon = new GameObject("Beacon").AddComponent<Light>();
                beacon.transform.SetParent(stationRoot, false);
                beacon.type = LightType.Point;
                beacon.color = new Color(1f, 0.35f, 0.3f);
                beacon.range = 25f;
                beacon.intensity = 2f;
            }
            var rel = st.pos - vessel.pos;
            bool show = st.body == vessel.body && rel.magnitude < 40000;
            if (stationRoot.gameObject.activeSelf != show) stationRoot.gameObject.SetActive(show);
            if (!show) return;
            stationRoot.position = rel.ToFloat();
            stationRoot.rotation = st.rot;
        }

        public void HideVessel()
        {
            if (vesselRoot != null) vesselRoot.gameObject.SetActive(false);
            plasma.gameObject.SetActive(false);
        }
    }
}
