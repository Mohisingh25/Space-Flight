using System;
using System.Collections;
using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rendering;
using Apogee.Rocket;
using UnityEngine;

namespace Apogee.Builder
{
    /// <summary>
    /// Draws a shaded icon of every part for the parts list. Rendered on the CPU from the part's actual
    /// mesh (a small z-buffered rasteriser), so it works the same on every GPU and render pipeline.
    /// One part per frame; icons are cached for the session.
    /// </summary>
    public class PartThumbnails : MonoBehaviour
    {
        const int Size = 128;
        const int SS = 2; // supersampling
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public event Action<string, Texture2D> Ready;

        public static Texture2D Get(string partId)
        {
            Texture2D t;
            return cache.TryGetValue(partId, out t) ? t : null;
        }

        public void RenderAll() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            foreach (var def in PartCatalog.All)
            {
                Texture2D tex;
                if (!cache.TryGetValue(def.id, out tex))
                {
                    try
                    {
                        var dims = PartDims.Of(new PartInstance { partId = def.id });
                        tex = Render(PartMeshFactory.Get(def, dims), !RocketAssembly.CanStack(def));
                        cache[def.id] = tex;
                    }
                    catch (Exception e) { Debug.LogWarning("Icon for " + def.id + " failed: " + e.Message); tex = null; }
                    yield return null;
                }
                if (tex != null && Ready != null) Ready(def.id, tex);
            }
        }

        static Color IconColor(string key)
        {
            switch (key)
            {
                case "nozzle": return new Color(0.5f, 0.36f, 0.3f);
                case "hazard": return new Color(0.9f, 0.72f, 0.12f);
                case "solar": return new Color(0.12f, 0.18f, 0.45f);
                case "heatshield": return new Color(0.32f, 0.24f, 0.17f);
                case "foam": return new Color(0.86f, 0.45f, 0.16f);
                default: return MaterialLibrary.BaseColor(key);
            }
        }

        static bool IsMetal(string key)
        {
            return key == "stainless" || key == "aluminium" || key == "gold" || key == "silver_foil" || key == "copper" || key == "dark_metal" || key == "nozzle";
        }

        public static Texture2D Render(PartModel model, bool radial)
        {
            int S = Size * SS;
            var mesh = model.mesh;
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var rot = Quaternion.Euler(14f, radial ? 205f : 28f, 0f);
            var pv = new Vector3[verts.Length];
            var pn = new Vector3[verts.Length];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                pv[i] = rot * verts[i];
                pn[i] = rot * norms[i];
                minX = Mathf.Min(minX, pv[i].x); maxX = Mathf.Max(maxX, pv[i].x);
                minY = Mathf.Min(minY, pv[i].y); maxY = Mathf.Max(maxY, pv[i].y);
            }
            float span = Mathf.Max(maxX - minX, maxY - minY, 0.01f);
            float k = S * 0.86f / span;
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            for (int i = 0; i < pv.Length; i++)
                pv[i] = new Vector3((pv[i].x - cx) * k + S * 0.5f, (pv[i].y - cy) * k + S * 0.5f, pv[i].z);

            var z = new float[S * S];
            var col = new Color[S * S];
            for (int i = 0; i < z.Length; i++) z[i] = float.MaxValue;
            var L = new Vector3(-0.45f, 0.65f, -0.62f).normalized;
            var V = new Vector3(0f, 0f, -1f);
            var H = (L + V).normalized;

            for (int sub = 0; sub < mesh.subMeshCount && sub < model.materialKeys.Length; sub++)
            {
                string key = model.materialKeys[sub];
                Color baseC = IconColor(key);
                bool metal = IsMetal(key);
                var tris = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    Vector3 a = pv[i0], b = pv[i1], c = pv[i2];
                    float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                    int x1 = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                    int y1 = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                    float inv = 1f / area;
                    for (int y = y0; y <= y1; y++)
                    {
                        float py = y + 0.5f;
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f;
                            float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) * inv;
                            float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) * inv;
                            float w2 = 1f - w0 - w1;
                            if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                            float depth = w0 * a.z + w1 * b.z + w2 * c.z;
                            int idx = y * S + x;
                            if (depth >= z[idx]) continue;
                            z[idx] = depth;
                            Vector3 n = (pn[i0] * w0 + pn[i1] * w1 + pn[i2] * w2).normalized;
                            if (Vector3.Dot(n, V) < 0f) n = -n;
                            float diff = Mathf.Max(0f, Vector3.Dot(n, L));
                            float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(n, H)), metal ? 40f : 18f) * (metal ? 0.7f : 0.3f);
                            float rim = Mathf.Pow(1f - Mathf.Abs(n.z), 3f) * 0.12f;
                            Color cc = baseC * (0.30f + 0.78f * diff) + new Color(spec + rim, spec + rim, spec + rim);
                            cc.a = 1f;
                            col[idx] = cc;
                        }
                    }
                }
            }

            var px32 = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = 0f, g = 0f, bl = 0f, al = 0f;
                    for (int j = 0; j < SS; j++)
                        for (int i = 0; i < SS; i++)
                        {
                            var c = col[(y * SS + j) * S + x * SS + i];
                            r += c.r * c.a; g += c.g * c.a; bl += c.b * c.a; al += c.a;
                        }
                    float n = SS * SS;
                    Color o = al > 0f ? new Color(r / al, g / al, bl / al, al / n) : new Color(0, 0, 0, 0);
                    px32[y * Size + x] = o;
                }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px32);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }
    }
}
