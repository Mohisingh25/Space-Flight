using System.Collections.Generic;
using Apogee.Rendering;
using Apogee.Simulation;
using Apogee.Astro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Flight
{
    /// <summary>
    /// High-detail ground around the vessel: a curved disc of the real planet surface (same map as from
    /// orbit, plus fine detail texture) rebuilt as the craft moves, so landing on the Moon or flying low over
    /// Earth shows real ground and a real horizon.
    /// </summary>
    public class LocalTerrain : MonoBehaviour
    {
        CelestialBody body;
        Mesh mesh;
        Material mat;
        MeshRenderer mr;
        Vector3d centerFixed;     // body-fixed unit direction of the patch centre
        double centerRadius;
        double patchRadius;

        public void Init()
        {
            mesh = new Mesh { name = "LocalTerrain" };
            mesh.indexFormat = IndexFormat.UInt32;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = gameObject.AddComponent<MeshRenderer>();
            mat = MaterialLibrary.Custom("Terrain");
            mat.SetTexture("_DetailTex", ProceduralTextures.Get("soil"));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            gameObject.SetActive(false);
        }

        public void UpdatePatch(CelestialBody b, Vector3d originFrame, double t, Vector3 sunDir, Color haze, float hazeDensity)
        {
            double alt = originFrame.magnitude - b.radius;
            if (b.star || b.gasGiant || alt > 250000)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            var spin = b.Spin(t);
            var subFixed = Vector3d.Rotate(Quaternion.Inverse(spin), originFrame).normalized;
            double want = System.Math.Max(60000, System.Math.Min(800000, alt * 5 + 60000));
            bool rebuild = body != b || mesh.vertexCount == 0
                || Vector3d.Angle(subFixed, centerFixed) * b.radius > patchRadius * 0.18
                || want > patchRadius * 1.6 || want < patchRadius * 0.4;
            if (rebuild) Build(b, subFixed, want);
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            var centerFrame = Vector3d.Rotate(spin, centerFixed * centerRadius);
            transform.position = (centerFrame - originFrame).ToFloat();
            transform.rotation = spin;
            mat.SetVector("_SunDir", sunDir);
            mat.SetColor("_FogColor", haze);
            mat.SetFloat("_FogDensity", hazeDensity);
        }

        void Build(CelestialBody b, Vector3d center, double radius)
        {
            if (body != b)
            {
                body = b;
                mat.SetTexture("_MainTex", SpaceView.Tex(b.texture));
                mat.SetColor("_Tint", b.tint == Color.white ? Color.white : b.tint * 1.2f);
                mat.SetFloat("_DetailStrength", b.name == "Earth" ? 0.45f : 0.6f);
            }
            centerFixed = center;
            centerRadius = b.radius + b.TerrainHeight(center);
            patchRadius = radius;
            // true east (direction of increasing longitude) and north, so triangles face up like the site's own ground
            var east = Vector3d.Cross(center, Vector3d.up);
            if (east.magnitude < 1e-6) east = new Vector3d(1, 0, 0);
            east = east.normalized;
            var north = Vector3d.Cross(east, center).normalized;
            int rings = 72, segs = 112;
            var verts = new List<Vector3>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var tris = new List<int>();
            var centerPoint = center * centerRadius;
            for (int i = 0; i <= rings; i++)
            {
                // denser near the centre, reaching out to the horizon
                double f = i / (double)rings;
                double s = radius * f * f;
                double ang = s / b.radius;
                for (int j = 0; j <= segs; j++)
                {
                    double th = j / (double)segs * System.Math.PI * 2;
                    var tangent = east * System.Math.Cos(th) + north * System.Math.Sin(th);
                    var dir = (center * System.Math.Cos(ang) + tangent * System.Math.Sin(ang)).normalized;
                    double h = b.radius + b.TerrainHeight(dir) - (b.name == "Earth" ? 0.6 : 0.0);   // just under the launch site's own ground
                    // drop the far rim below the horizon to hide the edge against the scaled planet
                    if (i == rings) h -= radius * 0.02;
                    verts.Add((dir * h - centerPoint).ToFloat());
                    double lat = System.Math.Asin(System.Math.Max(-1, System.Math.Min(1, dir.y)));
                    double lon = System.Math.Atan2(dir.z, dir.x);
                    uv.Add(new Vector2((float)((lon * 180 / System.Math.PI + 180) / 360), (float)((lat * 180 / System.Math.PI + 90) / 180)));
                    uv2.Add(new Vector2((float)(s * System.Math.Cos(th) / 37.0), (float)(s * System.Math.Sin(th) / 37.0)));
                }
            }
            int row = segs + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = i * row + j, bb = a + 1, c = a + row + 1, d = a + row;
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(bb); tris.Add(c);
                }
            // seam: avoid the texture wrapping backwards across longitude ±180°
            for (int k = 0; k < uv.Count; k++)
            {
                var c0 = uv[0].x;
                var u = uv[k];
                if (u.x - c0 > 0.5f) u.x -= 1f; else if (c0 - u.x > 0.5f) u.x += 1f;
                uv[k] = u;
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    /// <summary>
    /// The launch site: assembly building, crawlerway, crawler-transporter with mobile launcher, the pad
    /// with flame trench, hold-down clamps and a service tower with swing arms, lightning masts and lights.
    /// Laid out in the site's local frame (x = east, y = up, z = north), pad at the origin.
    /// </summary>
    public class LaunchComplex : MonoBehaviour
    {
        public const float RolloutDistance = 1400f;   // VAB doors → pad, metres east
        public const float CrawlerDeck = 7.5f;          // height of the mobile launcher deck
        public Transform crawler, armPivot;
        public Vector3d siteFixed;                      // body-fixed position of the pad centre
        public Quaternion siteRotFixed;                 // body-fixed orientation of the site frame
        CelestialBody body;

        public void Build(CelestialBody b, double lat, double lon)
        {
            body = b;
            var dir = b.FixedDirection(lat, lon);
            siteFixed = dir * b.radius;
            var up = dir.ToFloat();
            var north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            siteRotFixed = Quaternion.LookRotation(north, up);

            var mats = new[]
            {
                MaterialLibrary.Get("concrete"), MaterialLibrary.Get("dark_metal"), MaterialLibrary.Get("hazard"),
                MaterialLibrary.Get("tower_steel"), MaterialLibrary.Get("aluminium"), MaterialLibrary.Get("light_red"),
                MaterialLibrary.Get("light_strip"), MaterialLibrary.Get("white"), MaterialLibrary.Get("pad_steel")
            };

            // ---- ground apron, pad and tower
            var mb = new MeshBuilder();
            float top = (float)FlightVessel.PadTop;
            mb.Box(new Vector3(0f, -0.2f, 0f), new Vector3(180f, 0.4f, 180f), 0);                          // apron
            mb.Box(new Vector3(0f, top * 0.5f, 0f), new Vector3(46f, top, 46f), 8);                          // pad deck
            mb.Box(new Vector3(0f, top * 0.5f, -26f), new Vector3(12f, top + 0.1f, 8f), 1);                  // flame trench exit
            mb.Box(new Vector3(0f, top + 0.02f, -10f), new Vector3(14f, 0.05f, 0.6f), 2);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                mb.Box(new Vector3(Mathf.Cos(a) * 4.2f, top + 0.7f, Mathf.Sin(a) * 4.2f), new Vector3(0.9f, 1.4f, 0.9f), 1); // hold-downs
            }
            float tx = -16f, th = 96f, tw = 2.4f;
            Vector3[] c = { new Vector3(tx - tw, 0, -tw), new Vector3(tx + tw, 0, -tw), new Vector3(tx + tw, 0, tw), new Vector3(tx - tw, 0, tw) };
            foreach (var p in c) mb.Box(p + Vector3.up * (top + th * 0.5f), new Vector3(0.45f, th, 0.45f), 3);
            for (float y = top; y <= top + th; y += 4f)
                for (int i = 0; i < 4; i++)
                {
                    var a = c[i] + Vector3.up * y;
                    var bb = c[(i + 1) % 4] + Vector3.up * y;
                    mb.Box((a + bb) * 0.5f, new Vector3(Mathf.Abs(bb.x - a.x) + 0.3f, 0.25f, Mathf.Abs(bb.z - a.z) + 0.3f), 3);
                    if (y + 4f <= top + th) mb.Tube(a, c[(i + 1) % 4] + Vector3.up * (y + 4f), 0.08f, 6, 3);
                }
            mb.Box(new Vector3(tx, top + th + 0.6f, 0f), new Vector3(1f, 1.2f, 1f), 5);
            foreach (float y in new[] { 20f, 40f, 60f })
                mb.Box(new Vector3(tx + tw + 2.5f, top + y, 0f), new Vector3(5f, 1f, 1.4f), 4);
            // lightning masts and floodlights around the pad
            foreach (var p in new[] { new Vector3(-60, 0, 60), new Vector3(60, 0, 60), new Vector3(60, 0, -60), new Vector3(-60, 0, -60) })
            {
                mb.Box(p + Vector3.up * 55f, new Vector3(1.2f, 110f, 1.2f), 3);
                mb.Box(p + Vector3.up * 111f, new Vector3(0.6f, 2f, 0.6f), 5);
            }
            // water tower
            mb.Box(new Vector3(70f, 30f, -40f), new Vector3(1.5f, 60f, 1.5f), 3);
            mb.Lathe(new List<Vector2> { new Vector2(0f, 58f), new Vector2(7f, 60f), new Vector2(7f, 70f), new Vector2(0f, 73f) }, 20, 7);
            // crawlerway: two gravel lanes from the VAB to the pad
            var go = Part("Launch Pad", mb, mats);
            go.transform.SetParent(transform, false);

            // ---- swing arm (crew access) — rotates away at launch
            var arm = new GameObject("Swing Arm").transform;
            arm.SetParent(transform, false);
            arm.localPosition = new Vector3(tx + tw, top + 52f, 0f);
            var amb = new MeshBuilder();
            amb.Box(new Vector3(6f, 0f, 0f), new Vector3(12f, 2.2f, 2f), 3);
            amb.Box(new Vector3(12f, 0f, 0f), new Vector3(2f, 3f, 3f), 7);
            var ago = Part("Arm", amb, mats);
            ago.transform.SetParent(arm, false);
            armPivot = arm;

            // ---- Vertical Assembly Building at the start of the crawlerway (west of the doors is the high bay)
            var vb = new MeshBuilder();
            float vx = RolloutDistance + 110f;
            vb.Box(new Vector3(vx, 80f, 0f), new Vector3(160f, 160f, 150f), 7);
            vb.Box(new Vector3(vx - 80.2f, 70f, 0f), new Vector3(0.6f, 140f, 50f), 1);                        // high-bay door
            for (float y = 20f; y < 160f; y += 20f) vb.Box(new Vector3(vx - 80.3f, y, 0f), new Vector3(0.4f, 0.6f, 50f), 4);
            vb.Box(new Vector3(vx - 80.3f, 130f, -55f), new Vector3(0.4f, 40f, 30f), 5);                     // painted flag panel
            for (float z = -70f; z <= 70f; z += 10f) vb.Box(new Vector3(vx, 80f, z), new Vector3(160.6f, 160f, 0.5f), 4); // structural ribs
            vb.Box(new Vector3(vx + 40f, 15f, 100f), new Vector3(80f, 30f, 50f), 7);                         // low bay
            var vgo = Part("Assembly Building", vb, mats);
            vgo.transform.SetParent(transform, false);

            // ---- crawler transporter with mobile launcher platform
            var cr = new MeshBuilder();
            cr.Box(new Vector3(0f, 3.2f, 0f), new Vector3(36f, 2.4f, 30f), 1);                                // chassis
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -13f : 13f, z = i % 2 == 0 ? -11f : 11f;
                cr.Box(new Vector3(x, 1.0f, z), new Vector3(12f, 2f, 6f), 1);                                 // track trucks
                for (int k = -2; k <= 2; k++) cr.Box(new Vector3(x + k * 2.3f, 1.0f, z + (z > 0 ? 3.05f : -3.05f)), new Vector3(1.4f, 1.6f, 0.2f), 4);
            }
            cr.Box(new Vector3(0f, 5.8f, 0f), new Vector3(44f, 2.6f, 38f), 3);                              // mobile launcher deck
            cr.Box(new Vector3(0f, CrawlerDeck - 0.1f, 0f), new Vector3(12f, 0.25f, 12f), 2);
            cr.Box(new Vector3(-12f, 4.4f, 16.2f), new Vector3(6f, 1.4f, 2.2f), 5);                          // driver cab
            var cgo = Part("Crawler", cr, mats);
            crawler = cgo.transform;
            crawler.SetParent(transform, false);
            BuildTreads(mats);
            BuildScenery(b);
            BuildDust();
            SetCrawler(RolloutDistance);
        }

        // ------------------------------------------------------------------ crawler treads & dust

        readonly List<Transform> treadsBottom = new List<Transform>(), treadsTop = new List<Transform>();
        const float TreadPitch = 1.15f;
        float lastEast = float.NaN;
        ParticleSystem dust;

        void BuildTreads(Material[] mats)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -13f : 13f, z = i % 2 == 0 ? -11f : 11f;
                for (int layer = 0; layer < 2; layer++)
                {
                    var mb = new MeshBuilder();
                    float y = layer == 0 ? 0.18f : 2.05f;
                    for (float px = -6.9f; px <= 6.9f; px += TreadPitch)
                        mb.Box(new Vector3(px, y, 0f), new Vector3(0.9f, 0.3f, 6.2f), 0);
                    var go = Part("Tread", mb, new[] { MaterialLibrary.Get("tread") });
                    go.transform.SetParent(crawler, false);
                    var holder = go.transform;
                    holder.localPosition = new Vector3(x, 0f, z);
                    (layer == 0 ? treadsBottom : treadsTop).Add(holder);
                }
            }
        }

        void BuildDust()
        {
            var go = new GameObject("Crawler Dust");
            go.transform.SetParent(transform, false);
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.66f, 0.56f, 0.35f), new Color(0.6f, 0.55f, 0.47f, 0.25f));
            var em = dust.emission;
            em.rateOverTime = 0f;
            var sol = dust.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialLibrary.Custom("Smoke");
            r.shadowCastingMode = ShadowCastingMode.Off;
            dust.Play();
        }

        /// <summary>Moves the crawler; its track plates roll and it kicks up dust from the gravel while moving.</summary>
        public void SetCrawler(float east)
        {
            if (crawler == null) return;
            crawler.localPosition = new Vector3(east, 0f, 0f);
            float travelled = float.IsNaN(lastEast) ? 0f : lastEast - east;
            lastEast = east;
            float phase = Mathf.Repeat(east, TreadPitch);
            foreach (var t in treadsBottom) t.localPosition = new Vector3((t.localPosition.x < 0 ? -13f : 13f) + phase, 0f, t.localPosition.z);
            foreach (var t in treadsTop) t.localPosition = new Vector3((t.localPosition.x < 0 ? -13f : 13f) - phase, 0f, t.localPosition.z);
            if (dust != null && Mathf.Abs(travelled) > 1e-4f)
            {
                var ep = new ParticleSystem.EmitParams();
                for (int i = 0; i < 4; i++)
                {
                    float x = (i < 2 ? -13f : 13f) + east + (travelled > 0 ? 6f : -6f);
                    float z = i % 2 == 0 ? -11f : 11f;
                    ep.position = new Vector3(x, 0.5f, z + UnityEngine.Random.Range(-2f, 2f));
                    if (UnityEngine.Random.value < Mathf.Clamp01(Mathf.Abs(travelled) * 2f)) dust.Emit(ep, 1);
                }
            }
        }

        public float CrawlerEast { get { return crawler != null ? crawler.localPosition.x : 0f; } }

        // ------------------------------------------------------------------ scenery

        /// <summary>
        /// The space-centre landscape around the pad: tropical grassland with palms and forest, gravel
        /// crawlerway with lamp posts, roads, propellant storage spheres, control centre, hangars and a fence.
        /// Ground drops away with the planet's curvature so it meets the terrain at the horizon.
        /// </summary>
        void BuildScenery(CelestialBody b)
        {
            float R = (float)b.radius;
            var rng = new System.Random(4242);

            // ---- grass plain (curved disc) with the paved roads and the crawlerway
            var g = new MeshBuilder();
            int rings = 40, segs = 72;
            float radius = 5000f;
            var centre = new Vector3(700f, 0f, 0f);
            var verts = new List<Vector3>();
            for (int i = 0; i <= rings; i++)
            {
                float d = radius * (i / (float)rings) * (i / (float)rings);
                for (int j = 0; j <= segs; j++)
                {
                    float a = j / (float)segs * Mathf.PI * 2f;
                    var p = centre + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    float dist = p.magnitude;
                    p.y = -0.03f - dist * dist / (2f * R) - (i == rings ? 4f : 0f);
                    verts.Add(p);
                }
            }
            var mesh = new Mesh { name = "Grass" };
            var tris = new List<int>();
            var uvs = new List<Vector2>();
            foreach (var v in verts) uvs.Add(new Vector2(v.x, v.z));
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a0 = i * (segs + 1) + j, b0 = a0 + 1, c0 = a0 + segs + 2, d0 = a0 + segs + 1;
                    tris.Add(a0); tris.Add(c0); tris.Add(d0);
                    tris.Add(a0); tris.Add(b0); tris.Add(c0);
                }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var grassGo = new GameObject("Grassland");
            grassGo.transform.SetParent(transform, false);
            grassGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var gr = grassGo.AddComponent<MeshRenderer>();
            gr.sharedMaterial = MaterialLibrary.Get("grass");
            gr.shadowCastingMode = ShadowCastingMode.Off;

            var roads = new MeshBuilder();
            float vx = RolloutDistance + 110f;
            for (int k = -1; k <= 1; k += 2)
                roads.Box(new Vector3(RolloutDistance * 0.5f + 15f, 0.06f, k * 9f), new Vector3(RolloutDistance + 60f, 0.12f, 12f), 0);   // crawlerway lanes
            roads.Box(new Vector3(RolloutDistance * 0.5f + 15f, 0.03f, 0f), new Vector3(RolloutDistance + 60f, 0.06f, 6f), 2);           // median
            roads.Box(new Vector3(vx, 0.05f, -900f), new Vector3(10f, 0.1f, 1650f), 1);                                                    // access road south
            roads.Box(new Vector3(300f, 0.05f, -820f), new Vector3(2400f, 0.1f, 10f), 1);                                                   // east–west road
            roads.Box(new Vector3(-900f, 0.05f, -820f), new Vector3(10f, 0.1f, 200f), 1);
            roads.Box(new Vector3(-60f, 0.05f, -420f), new Vector3(10f, 0.1f, 800f), 1);                                                    // pad service road
            roads.Box(new Vector3(-900f, 0.05f, -640f), new Vector3(140f, 0.1f, 90f), 1);                                                   // car park
            for (int i = 0; i < 14; i++) roads.Box(new Vector3(-960f + i * 9f, 0.11f, -640f), new Vector3(0.2f, 0.02f, 70f), 3);
            var roadsGo = Part("Roads", roads, new[] { MaterialLibrary.Get("gravel"), MaterialLibrary.Get("asphalt"), MaterialLibrary.Get("grass"), MaterialLibrary.Get("white") });
            roadsGo.transform.SetParent(transform, false);
            roadsGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---- structures: propellant farm, lamp posts, fence, control centre, hangars
            var st = new MeshBuilder();
            const int TANK = 0, STEEL = 1, BLD = 2, WIN = 3, DARK = 4, LAMP = 5, ALU = 6;
            Sphere(st, new Vector3(-75f, 13f, 62f), 10f, TANK);                       // LOX sphere
            Sphere(st, new Vector3(-75f, 13f, -62f), 12f, TANK);                      // LH2 sphere
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                st.Tube(new Vector3(-75f + Mathf.Cos(a) * 8f, 0f, 62f + Mathf.Sin(a) * 8f), new Vector3(-75f + Mathf.Cos(a) * 8f, 9f, 62f + Mathf.Sin(a) * 8f), 0.4f, 6, STEEL);
                st.Tube(new Vector3(-75f + Mathf.Cos(a) * 9.5f, 0f, -62f + Mathf.Sin(a) * 9.5f), new Vector3(-75f + Mathf.Cos(a) * 9.5f, 9f, -62f + Mathf.Sin(a) * 9.5f), 0.45f, 6, STEEL);
            }
            st.Tube(new Vector3(70f, 4f, 70f), new Vector3(70f, 4f, 30f), 4f, 16, TANK);   // kerosene tank (horizontal)
            st.Tube(new Vector3(-65f, 1f, 62f), new Vector3(-8f, 1f, 4f), 0.35f, 6, ALU);   // feed lines to the pad
            st.Tube(new Vector3(-65f, 1f, -62f), new Vector3(-8f, 1f, -4f), 0.4f, 6, ALU);
            st.Tube(new Vector3(66f, 1f, 30f), new Vector3(8f, 1f, 4f), 0.3f, 6, ALU);
            for (float x = 80f; x < RolloutDistance; x += 110f)
                for (int k = -1; k <= 1; k += 2)
                {
                    st.Box(new Vector3(x, 7f, k * 24f), new Vector3(0.35f, 14f, 0.35f), STEEL);
                    st.Box(new Vector3(x, 14.2f, k * 22.8f), new Vector3(0.8f, 0.4f, 2.6f), DARK);
                    st.Box(new Vector3(x, 13.95f, k * 22.6f), new Vector3(0.6f, 0.1f, 2.1f), LAMP);
                }
            for (float t = -90f; t <= 90f; t += 9f)
            {
                foreach (var p in new[] { new Vector3(t, 0f, -90f), new Vector3(t, 0f, 90f), new Vector3(-90f, 0f, t) })
                    st.Box(p + Vector3.up * 1.25f, new Vector3(0.12f, 2.5f, 0.12f), ALU);
            }
            st.Box(new Vector3(0f, 2.3f, -90f), new Vector3(180f, 0.05f, 0.05f), ALU);
            st.Box(new Vector3(0f, 2.3f, 90f), new Vector3(180f, 0.05f, 0.05f), ALU);
            st.Box(new Vector3(-90f, 2.3f, 0f), new Vector3(0.05f, 0.05f, 180f), ALU);
            // launch control centre
            st.Box(new Vector3(-900f, 11f, -720f), new Vector3(90f, 22f, 45f), BLD);
            for (float y = 4f; y < 22f; y += 5f) st.Box(new Vector3(-900f, y, -742.6f), new Vector3(86f, 2.2f, 0.4f), WIN);
            st.Box(new Vector3(-870f, 30f, -720f), new Vector3(1f, 16f, 1f), STEEL);
            Sphere(st, new Vector3(-870f, 38f, -720f), 2f, TANK);
            // hangars near the assembly building
            for (int h = 0; h < 3; h++)
            {
                var c = new Vector3(vx + 160f, 0f, -260f - h * 110f);
                st.Box(c + Vector3.up * 12f, new Vector3(70f, 24f, 80f), BLD);
                st.Box(c + new Vector3(-35.3f, 9f, 0f), new Vector3(0.4f, 18f, 60f), DARK);
            }
            var stGo = Part("Site Structures", st, new[]
            {
                MaterialLibrary.Get("tank_white"), MaterialLibrary.Get("tower_steel"), MaterialLibrary.Get("building"),
                MaterialLibrary.Get("window_band"), MaterialLibrary.Get("dark_metal"), MaterialLibrary.Get("light_strip"), MaterialLibrary.Get("aluminium")
            });
            stGo.transform.SetParent(transform, false);

            // ---- tropical vegetation: palms near the roads, forest further out
            int chunk = 0;
            var veg = new MeshBuilder();
            int placed = 0;
            for (int n = 0; n < 4000 && placed < 900; n++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float d = 260f + (float)System.Math.Pow(rng.NextDouble(), 0.7) * 4300f;
                var p = centre + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (Blocked(p, vx)) continue;
                float dist = p.magnitude;
                p.y = -dist * dist / (2f * R);
                float h = 7f + (float)rng.NextDouble() * 10f;
                if (rng.NextDouble() < 0.6) Palm(veg, p, h, (float)rng.NextDouble() * 360f, rng);
                else Broadleaf(veg, p, h * 0.9f, rng);
                placed++;
                if (veg.VertexCount > 60000)
                {
                    var vgo = Part("Vegetation " + chunk++, veg, new[] { MaterialLibrary.Get("bark"), MaterialLibrary.Get("palm"), MaterialLibrary.Get("foliage") });
                    vgo.transform.SetParent(transform, false);
                    veg = new MeshBuilder();
                }
            }
            if (veg.VertexCount > 0)
            {
                var vgo = Part("Vegetation " + chunk, veg, new[] { MaterialLibrary.Get("bark"), MaterialLibrary.Get("palm"), MaterialLibrary.Get("foliage") });
                vgo.transform.SetParent(transform, false);
            }
        }

        /// <summary>Keeps trees off the pad, crawlerway, roads and buildings.</summary>
        static bool Blocked(Vector3 p, float vx)
        {
            if (Mathf.Abs(p.x) < 120f && Mathf.Abs(p.z) < 120f) return true;                         // pad + farm
            if (p.x > -40f && p.x < vx + 120f && Mathf.Abs(p.z) < 45f) return true;                  // crawlerway
            if (p.x > vx - 110f && p.x < vx + 220f && p.z > -620f && p.z < 160f) return true;      // VAB + hangars
            if (Mathf.Abs(p.x - vx) < 18f) return true;
            if (Mathf.Abs(p.z + 820f) < 18f && p.x > -920f && p.x < 1520f) return true;
            if (Mathf.Abs(p.x + 60f) < 18f && p.z < 0f && p.z > -830f) return true;
            if (p.x > -1000f && p.x < -800f && p.z > -770f && p.z < -590f) return true;
            return false;
        }

        static void Sphere(MeshBuilder mb, Vector3 c, float r, int mat)
        {
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(c, Quaternion.identity, Vector3.one * r);
            var prof = new List<Vector2>();
            for (int i = 0; i <= 12; i++) { float a = -Mathf.PI / 2 + Mathf.PI * i / 12f; prof.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a))); }
            prof[0] = new Vector2(0f, -1f); prof[12] = new Vector2(0f, 1f);
            mb.Lathe(prof, 20, mat);
            mb.transform = saved;
        }

        static void Palm(MeshBuilder mb, Vector3 p, float h, float yaw, System.Random rng)
        {
            // gently curved trunk in three segments, crown of drooping fronds
            var lean = Quaternion.Euler(0f, yaw, 0f) * new Vector3(1f, 0f, 0f) * h * 0.08f;
            var a = p;
            var b = p + Vector3.up * h * 0.4f + lean * 0.3f;
            var c = p + Vector3.up * h * 0.75f + lean * 0.75f;
            var top = p + Vector3.up * h + lean;
            mb.Tube(a, b, 0.24f, 6, 0);
            mb.Tube(b, c, 0.2f, 6, 0);
            mb.Tube(c, top, 0.17f, 6, 0);
            int fronds = 9;
            for (int k = 0; k < fronds; k++)
            {
                float ang = k * Mathf.PI * 2f / fronds + (float)rng.NextDouble() * 0.4f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float len = h * 0.38f;
                var mid = top + dir * len * 0.5f + Vector3.up * len * 0.12f;
                var tip = top + dir * len - Vector3.up * len * 0.35f;
                Leaf(mb, top, mid, dir, len * 0.18f);
                Leaf(mb, mid, tip, dir, len * 0.14f);
            }
        }

        static void Leaf(MeshBuilder mb, Vector3 from, Vector3 to, Vector3 dir, float width)
        {
            var side = Vector3.Cross(dir, Vector3.up).normalized * width;
            var mid = (from + to) * 0.5f;
            var len = (to - from).magnitude;
            var rot = Quaternion.LookRotation((to - from).normalized, Vector3.Cross(side, to - from).normalized);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(mid, rot, Vector3.one);
            mb.Box(Vector3.zero, new Vector3(width * 2f, 0.04f, len), 1);
            mb.transform = saved;
        }

        static void Broadleaf(MeshBuilder mb, Vector3 p, float h, System.Random rng)
        {
            mb.Tube(p, p + Vector3.up * h * 0.55f, 0.3f, 6, 0);
            int blobs = 3 + rng.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                var off = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.5f, (float)rng.NextDouble() - 0.5f) * h * 0.5f;
                float r = h * (0.22f + (float)rng.NextDouble() * 0.15f);
                var c = p + Vector3.up * h * 0.7f + off;
                var saved = mb.transform;
                mb.transform = saved * Matrix4x4.TRS(c, Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f), new Vector3(r, r * 0.8f, r));
                var prof = new List<Vector2> { new Vector2(0f, -1f), new Vector2(0.7f, -0.7f), new Vector2(1f, 0f), new Vector2(0.7f, 0.7f), new Vector2(0f, 1f) };
                mb.Lathe(prof, 7, 2);
                mb.transform = saved;
            }
        }

        GameObject Part(string name, MeshBuilder mb, Material[] mats)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }


        public void SetArm(float open01) { if (armPivot != null) armPivot.localRotation = Quaternion.Euler(0f, -100f * open01, 0f); }

        /// <summary>Places the site in the render frame (origin at the vessel).</summary>
        public void Place(double t, Vector3d originFrame)
        {
            var spin = body.Spin(t);
            var p = Vector3d.Rotate(spin, siteFixed) - originFrame;
            bool near = p.magnitude < 60000;
            if (gameObject.activeSelf != near) gameObject.SetActive(near);
            if (!near) return;
            transform.position = p.ToFloat();
            transform.rotation = spin * siteRotFixed;
        }

        public CelestialBody Body { get { return body; } }
    }
}
