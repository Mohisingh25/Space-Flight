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
            double want = System.Math.Max(25000, System.Math.Min(700000, alt * 4.5 + 20000));
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
            var east = Vector3d.Cross(Vector3d.up, center);
            if (east.magnitude < 1e-6) east = new Vector3d(1, 0, 0);
            east = east.normalized;
            var north = Vector3d.Cross(center, east).normalized;
            int rings = 60, segs = 96;
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
                    double h = b.radius + b.TerrainHeight(dir);
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
            var dir = b.SurfaceDirection(lat, lon, 0);
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
            var wt = new MeshBuilder();
            mb.Lathe(new List<Vector2> { new Vector2(0f, 58f), new Vector2(7f, 60f), new Vector2(7f, 70f), new Vector2(0f, 73f) }, 20, 7);
            // crawlerway: two gravel lanes from the VAB to the pad
            for (int k = -1; k <= 1; k += 2)
                mb.Box(new Vector3(RolloutDistance * 0.5f + 15f, 0.05f, k * 9f), new Vector3(RolloutDistance + 60f, 0.12f, 12f), 4);
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
            SetCrawler(RolloutDistance);
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

        public void SetCrawler(float east) { if (crawler != null) crawler.localPosition = new Vector3(east, 0f, 0f); }

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
