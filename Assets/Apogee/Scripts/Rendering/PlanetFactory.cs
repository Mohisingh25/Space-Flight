using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>
    /// Procedurally generates planet surface/cloud textures and the star field. Earth is generated on a
    /// worker thread the first time and cached as PNG in persistent storage so later launches load instantly.
    /// </summary>
    public static class PlanetFactory
    {
        const int EarthSeed = 20240917;

        // ---------------------------------------------------------------- Earth

        struct PixelJob
        {
            public int w, h;
            public Color32[] albedo;
            public Color32[] clouds;
        }

        static Vector3 Dir(float u, float v)
        {
            float ph = (1f - u) * Mathf.PI * 2f, th = v * Mathf.PI;
            return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), -Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
        }

        static string CachePath(string name)
        {
            return Path.Combine(Application.persistentDataPath, "cache", name);
        }

        static Texture2D TryLoad(string name)
        {
            try
            {
                string p = CachePath(name);
                if (!File.Exists(p)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!t.LoadImage(File.ReadAllBytes(p))) return null;
                t.wrapMode = TextureWrapMode.Repeat;
                t.filterMode = FilterMode.Trilinear;
                t.anisoLevel = 4;
                return t;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Planet cache load failed: " + e.Message);
                return null;
            }
        }

        static void TrySave(string name, Texture2D t)
        {
            try
            {
                string p = CachePath(name);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, t.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("Planet cache save failed: " + e.Message); }
        }

        static Texture2D ToTexture(int w, int h, Color32[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.Apply(true, false);
            return t;
        }

        /// <summary>Builds (or loads) Earth's albedo and cloud maps. Generation runs off the main thread.</summary>
        public static IEnumerator BuildEarth(int width, Action<Texture2D, Texture2D> done)
        {
            int height = width / 2;
            string an = "earth_albedo_" + width + ".png", cn = "earth_clouds_" + width + ".png";
            var a = TryLoad(an);
            var c = TryLoad(cn);
            if (a != null && c != null) { done(a, c); yield break; }

            var job = new PixelJob { w = width, h = height, albedo = new Color32[width * height], clouds = new Color32[width * height] };
            var task = Task.Run(() => GenerateEarth(job));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                Debug.LogError("Earth generation failed: " + task.Exception);
                done(null, null);
                yield break;
            }
            a = ToTexture(width, height, job.albedo);
            c = ToTexture(width, height, job.clouds);
            TrySave(an, a);
            TrySave(cn, c);
            done(a, c);
        }

        static void GenerateEarth(PixelJob job)
        {
            var n = new Noise(EarthSeed);
            var n2 = new Noise(EarthSeed + 1);
            var n3 = new Noise(EarthSeed + 2);
            int w = job.w, h = job.h;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    var p = Dir((x + 0.5f) / w, (y + 0.5f) / h);
                    double px = p.x, py = p.y, pz = p.z;
                    double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, py)));
                    double absLat = Math.Abs(lat) / (Math.PI / 2);

                    // Continents: domain-warped fBm, plus ridged mountains on land
                    double wx = n2.Fbm(px * 2.2, py * 2.2, pz * 2.2, 3) * 0.45;
                    double wy = n2.Fbm(px * 2.2 + 5.2, py * 2.2, pz * 2.2, 3) * 0.45;
                    double e = n.Fbm(px * 1.4 + wx, py * 1.4 + wy, pz * 1.4, 7);
                    double sea = 0.06;
                    double moist = n3.Fbm(px * 3.1, py * 3.1, pz * 3.1, 4);
                    Color col;
                    bool ocean = e < sea;
                    if (ocean)
                    {
                        double depth = Math.Min(1.0, (sea - e) * 4.0);
                        col = Color.Lerp(new Color(0.04f, 0.20f, 0.33f), new Color(0.01f, 0.045f, 0.14f), (float)Math.Sqrt(depth));
                        if (absLat > 0.86) col = Color.Lerp(col, new Color(0.88f, 0.92f, 0.96f), (float)Math.Min(1.0, (absLat - 0.86) * 12));
                    }
                    else
                    {
                        double height = (e - sea) / (1 - sea);
                        double ridge = n.Ridged(px * 4, py * 4, pz * 4, 5);
                        double elev = height * 2.2 + ridge * height * 1.6;
                        double temp = 1.0 - absLat * 1.15 - elev * 0.5;
                        double dryBand = Math.Exp(-Math.Pow((absLat - 0.27) / 0.1, 2)); // subtropical deserts
                        double wet = moist + 0.15 - dryBand * 0.45;

                        Color forest = new Color(0.10f, 0.22f, 0.07f);
                        Color grass = new Color(0.30f, 0.37f, 0.16f);
                        Color desert = new Color(0.78f, 0.66f, 0.46f);
                        Color tundra = new Color(0.38f, 0.37f, 0.30f);
                        Color rock = new Color(0.40f, 0.34f, 0.28f);
                        Color snow = new Color(0.93f, 0.95f, 0.98f);

                        col = wet > 0.12 ? Color.Lerp(grass, forest, (float)Math.Min(1.0, (wet - 0.12) * 4)) : Color.Lerp(desert, grass, (float)Math.Max(0.0, (wet + 0.15) * 3.7));
                        if (temp < 0.38) col = Color.Lerp(tundra, col, (float)Math.Max(0.0, (temp - 0.25) / 0.13));
                        col = Color.Lerp(col, rock, (float)Math.Min(1.0, Math.Max(0.0, elev - 0.55) * 1.8));
                        if (temp < 0.2 || elev > 1.35) col = Color.Lerp(col, snow, (float)Math.Min(1.0, Math.Max((0.2 - temp) * 8, (elev - 1.35) * 4)));
                        double detail = n3.Fbm(px * 24, py * 24, pz * 24, 3) * 0.08;
                        col *= (float)(1.0 + detail);
                    }
                    col.a = ocean && absLat < 0.86 ? 1f : 0f;
                    job.albedo[y * w + x] = col;

                    // Clouds: warped swirls, more in storm belts, fewer over deserts
                    double cwx = n3.Fbm(px * 3 + 11, py * 3, pz * 3, 3) * 0.6;
                    double cwy = n3.Fbm(px * 3, py * 3 + 7, pz * 3, 3) * 0.6;
                    double cl = n2.Fbm(px * 4.5 + cwx, py * 9 + cwy, pz * 4.5, 6);
                    double belt = 0.12 * Math.Cos(lat * 6.0) - 0.08 * Math.Exp(-Math.Pow((absLat - 0.27) / 0.08, 2));
                    double cover = Math.Max(0.0, Math.Min(1.0, (cl + belt + 0.02) * 2.6));
                    cover = cover * cover * (3 - 2 * cover);
                    job.clouds[y * w + x] = new Color32(255, 255, 255, (byte)(cover * 235));
                }
            });
        }

        // ---------------------------------------------------------------- Moon

        public static Texture2D BuildMoon(int width)
        {
            string name = "moon_" + width + ".png";
            var cached = TryLoad(name);
            if (cached != null) return cached;
            int height = width / 2;
            var px = new Color32[width * height];
            var n = new Noise(77);
            var rng = new System.Random(5);
            int craterCount = 160;
            var centers = new Vector3[craterCount];
            var radii = new float[craterCount];
            var cosLimit = new float[craterCount];
            for (int i = 0; i < craterCount; i++)
            {
                var c = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1);
                centers[i] = c.sqrMagnitude > 1e-4f ? c.normalized : Vector3.up;
                radii[i] = Mathf.Lerp(0.02f, 0.16f, Mathf.Pow((float)rng.NextDouble(), 3f));
                cosLimit[i] = Mathf.Cos(Mathf.Min(Mathf.PI, radii[i] * 1.3f));
            }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var p = Dir((x + 0.5f) / width, (y + 0.5f) / height);
                    float maria = (float)n.Fbm(p.x * 1.6, p.y * 1.6, p.z * 1.6, 4);
                    float v = 0.62f + (float)n.Fbm(p.x * 10, p.y * 10, p.z * 10, 4) * 0.12f;
                    if (maria < -0.05f) v *= Mathf.Lerp(1f, 0.62f, Mathf.Clamp01((-0.05f - maria) * 6f));
                    for (int i = 0; i < craterCount; i++)
                    {
                        float dot = Vector3.Dot(p, centers[i]);
                        if (dot < cosLimit[i]) continue;
                        float d = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) / radii[i];
                        if (d > 1.3f) continue;
                        if (d < 1f) v *= Mathf.Lerp(0.78f, 1f, d * d);
                        else v *= 1f + (1.3f - d) * 0.5f; // bright rim / ejecta
                    }
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    px[y * width + x] = new Color32(b, b, (byte)Mathf.Clamp(b - 4, 0, 255), 0);
                }
            var t = ToTexture(width, height, px);
            TrySave(name, t);
            return t;
        }

        // ---------------------------------------------------------------- stars

        /// <summary>
        /// Star field as camera-facing quads on a sphere: realistic colour temperatures and magnitudes,
        /// with a denser band along a tilted "galactic plane" for a Milky Way impression.
        /// </summary>
        public static Mesh StarField(int count, float radius, int seed)
        {
            var rng = new System.Random(seed);
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var cols = new List<Color>(count * 4);
            var tris = new List<int>(count * 6);
            var galaxyNormal = Quaternion.Euler(62f, 0f, 25f) * Vector3.up;
            Color[] palette =
            {
                new Color(0.62f, 0.72f, 1.0f), new Color(0.80f, 0.86f, 1.0f), new Color(1f, 1f, 1f),
                new Color(1.0f, 0.95f, 0.85f), new Color(1.0f, 0.85f, 0.65f), new Color(1.0f, 0.7f, 0.5f)
            };
            float[] weights = { 0.05f, 0.18f, 0.3f, 0.25f, 0.15f, 0.07f };
            for (int i = 0; i < count; i++)
            {
                Vector3 d;
                if (i % 5 < 2)
                {
                    // galactic band
                    var a = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1);
                    var inPlane = Vector3.ProjectOnPlane(a, galaxyNormal).normalized;
                    float off = (float)(rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 0.18f;
                    d = (inPlane + galaxyNormal * off).normalized;
                }
                else
                {
                    double z = rng.NextDouble() * 2 - 1, t = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - z * z);
                    d = new Vector3((float)(rr * Math.Cos(t)), (float)z, (float)(rr * Math.Sin(t)));
                }
                float pick = (float)rng.NextDouble(), acc = 0f;
                Color c = palette[2];
                for (int k = 0; k < palette.Length; k++) { acc += weights[k]; if (pick <= acc) { c = palette[k]; break; } }
                float mag = Mathf.Pow((float)rng.NextDouble(), 4f); // most stars faint, a few bright
                float size = radius * (0.0016f + mag * 0.006f);
                c.a = 0.25f + mag * 1.6f;

                var t1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized * size;
                var t2 = Vector3.Cross(d, t1).normalized * size;
                var center = d * radius;
                int b = verts.Count;
                verts.Add(center - t1 - t2); verts.Add(center + t1 - t2); verts.Add(center + t1 + t2); verts.Add(center - t1 + t2);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) cols.Add(c);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh();
            mesh.name = "StarField";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.2f);
            return mesh;
        }

        /// <summary>Unit quad in the XY plane facing -Z (for camera-facing glows).</summary>
        public static Mesh Quad()
        {
            var m = new Mesh();
            m.name = "Quad";
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
