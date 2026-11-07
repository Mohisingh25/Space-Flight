using System.Collections.Generic;
using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>
    /// Generates the surface textures for parts and environments at startup (no external image assets
    /// needed). Every texture is cached, so each one is built once per session.
    /// </summary>
    public static class ProceduralTextures
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly Noise noise = new Noise(1337);

        public static Texture2D Get(string key)
        {
            Texture2D t;
            if (cache.TryGetValue(key, out t) && t != null) return t;
            switch (key)
            {
                case "panels": t = Panels(256, 0.86f, 0.72f); break;
                case "panels_dark": t = Panels(256, 0.95f, 0.8f); break;
                case "brushed": t = Brushed(256); break;
                case "foam": t = Foam(256); break;
                case "foil": t = Foil(256); break;
                case "solar": t = Solar(256); break;
                case "hazard": t = Hazard(128); break;
                case "nozzle": t = Nozzle(32, 256); break;
                case "carbon": t = Carbon(128); break;
                case "heatshield": t = HeatShield(128); break;
                case "concrete": t = Concrete(256); break;
                case "wall": t = Wall(256); break;
                case "grating": t = Grating(128); break;
                case "soil": t = Soil(256); break;
                default: t = Solid(Color.white); break;
            }
            t.name = "Apogee_" + key;
            cache[key] = t;
            return t;
        }

        static Texture2D Make(int w, int h, Color32[] px, bool repeat = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.SetPixels32(px);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.Apply(true, true);
            return t;
        }

        static Color32 Gray(float v, float a = 1f)
        {
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            return new Color32(b, b, b, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
        }

        static float N(float x, float y, float s, int oct = 3)
        {
            return (float)noise.Fbm(x * s, y * s, 0.37, oct) * 0.5f + 0.5f;
        }

        // Seamless noise by sampling on a torus.
        static float Tile(float u, float v, float freq, int oct = 4, float z = 0f)
        {
            float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
            return (float)noise.Fbm(Mathf.Cos(a) * freq, Mathf.Sin(a) * freq + Mathf.Cos(b) * freq, Mathf.Sin(b) * freq + z, oct) * 0.5f + 0.5f;
        }

        static Texture2D Solid(Color c)
        {
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            return Make(4, 4, px);
        }

        /// <summary>Painted skin: subtle grime, panel seams every quarter tile and rivet rows.</summary>
        static Texture2D Panels(int s, float baseV, float seamV)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float val = baseV + (Tile(u, v, 3f, 4) - 0.5f) * 0.06f;
                    int gx = x % (s / 2), gy = y % (s / 4);
                    if (gx == 0 || gy == 0) val = seamV;
                    else if (gx == 1 || gy == 1) val = Mathf.Lerp(val, seamV, 0.4f);
                    // rivets along seams
                    if ((gy == 4 || gy == s / 4 - 4) && x % 8 == 4) val *= 0.9f;
                    px[y * s + x] = Gray(val);
                }
            return Make(s, s, px);
        }

        static Texture2D Brushed(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float streak = Tile(u * 0.02f, v, 40f, 2) - 0.5f;
                    float blotch = Tile(u, v, 2f, 3, 3f) - 0.5f;
                    float val = 0.86f + streak * 0.12f + blotch * 0.1f;
                    if (y % (s / 4) == 0) val -= 0.18f; // weld seams
                    px[y * s + x] = Gray(val, Mathf.Clamp01(0.75f + blotch * 0.4f));
                }
            return Make(s, s, px);
        }

        static Texture2D Foam(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float n = Tile(u, v, 6f, 5);
                    float val = 0.75f + (n - 0.5f) * 0.5f;
                    px[y * s + x] = new Color32((byte)(255 * Mathf.Clamp01(val)), (byte)(255 * Mathf.Clamp01(val * 0.92f)), (byte)(255 * Mathf.Clamp01(val * 0.85f)), 255);
                }
            return Make(s, s, px);
        }

        static Texture2D Foil(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float crinkle = Mathf.Abs(Tile(u, v, 9f, 4) - 0.5f) * 2f;
                    float val = 0.65f + crinkle * 0.45f;
                    px[y * s + x] = Gray(val, 0.55f + crinkle * 0.45f);
                }
            return Make(s, s, px);
        }

        static Texture2D Solar(int s)
        {
            var px = new Color32[s * s];
            int cell = s / 8;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int cx = x % cell, cy = y % cell;
                    bool gap = cx < 2 || cy < 2;
                    bool busbar = cx == cell / 2;
                    Color c = gap ? new Color(0.75f, 0.75f, 0.78f) : busbar ? new Color(0.45f, 0.48f, 0.55f) : new Color(0.06f, 0.10f, 0.28f);
                    if (!gap && !busbar) c *= 0.9f + 0.2f * N(x, y, 0.08f, 2);
                    c.a = 1f;
                    px[y * s + x] = c;
                }
            return Make(s, s, px);
        }

        static Texture2D Hazard(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool yellow = ((x + y) / (s / 4)) % 2 == 0;
                    px[y * s + x] = yellow ? new Color32(235, 180, 20, 255) : new Color32(25, 25, 25, 255);
                }
            return Make(s, s, px);
        }

        /// <summary>Nozzle bell: hot copper/bronze near the throat (v≈0) fading to heat-tinted steel at the exit.</summary>
        static Texture2D Nozzle(int w, int h)
        {
            var px = new Color32[w * h];
            Color throat = new Color(0.62f, 0.38f, 0.22f);
            Color mid = new Color(0.36f, 0.30f, 0.42f);
            Color exit = new Color(0.30f, 0.30f, 0.32f);
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / (h - 1);
                Color c = v < 0.4f ? Color.Lerp(throat, mid, v / 0.4f) : Color.Lerp(mid, exit, (v - 0.4f) / 0.6f);
                for (int x = 0; x < w; x++)
                {
                    float rib = (x % 4 == 0) ? 0.85f : 1f; // regenerative cooling tubes
                    Color cc = c * rib;
                    cc.a = 1f;
                    px[y * w + x] = cc;
                }
            }
            return Make(w, h, px);
        }

        static Texture2D Carbon(int s)
        {
            var px = new Color32[s * s];
            int cell = 8;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int cx = (x / cell) % 2, cy = (y / cell) % 2;
                    float t = (cx ^ cy) == 0 ? (float)(x % cell) / cell : (float)(y % cell) / cell;
                    float val = 0.75f + Mathf.Sin(t * Mathf.PI) * 0.25f;
                    px[y * s + x] = Gray(val, 0.6f + 0.4f * val);
                }
            return Make(s, s, px);
        }

        static Texture2D HeatShield(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float n = Tile(u, v, 5f, 5);
                    float val = 0.6f + (n - 0.5f) * 0.8f;
                    if (x % 16 == 0 || y % 16 == 0) val *= 0.7f; // tile gaps
                    px[y * s + x] = Gray(val, 0.25f);
                }
            return Make(s, s, px);
        }

        /// <summary>Hangar floor: stained concrete slabs with painted safety lines.</summary>
        static Texture2D Concrete(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float n = Tile(u, v, 4f, 6);
                    float stain = Tile(u, v, 1.5f, 3, 7f);
                    float val = 0.55f + (n - 0.5f) * 0.25f - Mathf.Max(0f, stain - 0.6f) * 0.5f;
                    if (x % (s / 2) < 2 || y % (s / 2) < 2) val *= 0.6f; // slab joints
                    Color c = new Color(val, val * 0.99f, val * 0.96f, 1f);
                    if (y % (s / 2) > s / 2 - 10 && y % (s / 2) < s / 2 - 4) c = Color.Lerp(c, new Color(0.85f, 0.65f, 0.1f, 1f), 0.75f);
                    px[y * s + x] = c;
                }
            return Make(s, s, px);
        }

        static Texture2D Wall(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float val = 0.7f + (Tile(u, v, 3f, 3) - 0.5f) * 0.1f;
                    if (x % (s / 4) < 3) val *= 0.75f;          // vertical ribs
                    if (y % (s / 2) < 2) val *= 0.6f;
                    px[y * s + x] = Gray(val);
                }
            return Make(s, s, px);
        }

        /// <summary>Ground detail: mottled earth with grass tufts and pebbles (multiplied with biome colour).</summary>
        static Texture2D Soil(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (float)x / s, v = (float)y / s;
                    float n = Tile(u, v, 5f, 5);
                    float fine = Tile(u, v, 24f, 2, 4f);
                    float val = 0.72f + (n - 0.5f) * 0.35f + (fine - 0.5f) * 0.25f;
                    px[y * s + x] = Gray(val);
                }
            return Make(s, s, px);
        }

        static Texture2D Grating(int s)
        {
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool bar = x % 8 < 2 || y % 16 < 2;
                    px[y * s + x] = bar ? Gray(0.75f) : Gray(0.18f);
                }
            return Make(s, s, px);
        }

        /// <summary>Rounded-rectangle sprite texture for UI panels (white, alpha anti-aliased).</summary>
        public static Texture2D RoundedRect(int size, int radius)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    if (dx <= 0f && dy <= 0f) a = 1f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return t;
        }

        /// <summary>Soft radial glow (for star flares, sun glare and UI highlights).</summary>
        public static Texture2D Glow(int size)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply();
            return t;
        }
    }
}
