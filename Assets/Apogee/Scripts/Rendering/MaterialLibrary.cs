using System.Collections.Generic;
using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>
    /// Physically based materials for parts and environments. Base materials live in
    /// Resources/Apogee (created by the editor setup so their shaders are included in builds);
    /// variants are cloned from them at runtime. Works with URP Lit and falls back to Standard.
    /// </summary>
    public static class MaterialLibrary
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Material litBase, emissiveBase;

        struct Spec
        {
            public Color color;
            public float metallic, smoothness;
            public string texture;
            public Vector2 tiling;
            public Color emission;
        }

        static Material LitBase
        {
            get
            {
                if (litBase == null)
                {
                    litBase = Resources.Load<Material>("Apogee/PartLit");
                    if (litBase == null) litBase = new Material(FindLitShader());
                }
                return litBase;
            }
        }

        static Material EmissiveBase
        {
            get
            {
                if (emissiveBase == null)
                {
                    emissiveBase = Resources.Load<Material>("Apogee/PartLitEmissive");
                    if (emissiveBase == null) { emissiveBase = new Material(FindLitShader()); emissiveBase.EnableKeyword("_EMISSION"); }
                }
                return emissiveBase;
            }
        }

        public static Shader FindLitShader()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        /// <summary>Loads one of the custom shader materials (Planet, Atmosphere, Stars, Ghost, Marker).</summary>
        public static Material Custom(string name)
        {
            var m = Resources.Load<Material>("Apogee/" + name);
            if (m != null) return new Material(m);
            var s = Shader.Find("Apogee/" + name);
            return s != null ? new Material(s) : new Material(FindLitShader());
        }

        static Spec S(float r, float g, float b, float metal, float smooth, string tex = null, float tx = 1f, float ty = 1f)
        {
            return new Spec { color = new Color(r, g, b, 1f), metallic = metal, smoothness = smooth, texture = tex, tiling = new Vector2(tx, ty), emission = Color.black };
        }

        static Spec Lookup(string key)
        {
            switch (key)
            {
                case "white": return S(0.93f, 0.93f, 0.91f, 0.05f, 0.55f, "panels");
                case "black": return S(0.10f, 0.10f, 0.11f, 0.1f, 0.5f, "panels_dark");
                case "stainless": return S(0.80f, 0.80f, 0.82f, 0.95f, 0.72f, "brushed");
                case "foam": return S(0.86f, 0.45f, 0.16f, 0f, 0.18f, "foam");
                case "dark_metal": return S(0.20f, 0.20f, 0.22f, 0.85f, 0.45f, "panels_dark");
                case "nozzle": return S(1f, 1f, 1f, 0.9f, 0.55f, "nozzle");
                case "gold": return S(1.0f, 0.76f, 0.33f, 1f, 0.68f, "foil");
                case "silver_foil": return S(0.86f, 0.86f, 0.88f, 1f, 0.66f, "foil");
                case "solar": return S(1f, 1f, 1f, 0.4f, 0.88f, "solar");
                case "glass": return S(0.04f, 0.06f, 0.09f, 0.3f, 0.96f);
                case "heatshield": return S(0.30f, 0.22f, 0.15f, 0f, 0.2f, "heatshield");
                case "hazard": return S(1f, 1f, 1f, 0.1f, 0.45f, "hazard");
                case "carbon": return S(0.13f, 0.13f, 0.14f, 0.25f, 0.62f, "carbon");
                case "aluminium": return S(0.72f, 0.73f, 0.75f, 0.9f, 0.5f, "panels");
                case "chute": return S(0.95f, 0.42f, 0.10f, 0f, 0.35f);
                case "rubber": return S(0.06f, 0.06f, 0.06f, 0f, 0.3f);
                case "copper": return S(0.85f, 0.52f, 0.32f, 1f, 0.6f);
                case "mli_white": return S(0.95f, 0.95f, 0.93f, 0.0f, 0.3f, "foil");
                case "fuel_rp1": return S(0.95f, 0.62f, 0.18f, 0f, 0.85f);
                case "fuel_lox": return S(0.55f, 0.8f, 1f, 0f, 0.9f);
                case "fuel_solid": return S(0.55f, 0.47f, 0.38f, 0f, 0.15f);
                case "seat": return S(0.25f, 0.27f, 0.3f, 0.2f, 0.4f);
                case "suit": return S(0.95f, 0.95f, 0.95f, 0f, 0.35f);
                // environment
                case "grass": return S(0.30f, 0.42f, 0.18f, 0f, 0.08f, "soil", 0.08f, 0.08f);
                case "asphalt": return S(0.17f, 0.17f, 0.18f, 0f, 0.2f, "soil", 0.12f, 0.12f);
                case "gravel": return S(0.55f, 0.52f, 0.47f, 0f, 0.1f, "soil", 0.2f, 0.2f);
                case "foliage": return S(0.16f, 0.32f, 0.12f, 0f, 0.15f);
                case "palm": return S(0.22f, 0.38f, 0.14f, 0f, 0.2f);
                case "bark": return S(0.36f, 0.27f, 0.18f, 0f, 0.1f);
                case "building": return S(0.82f, 0.82f, 0.8f, 0f, 0.3f, "wall", 0.05f, 0.05f);
                case "window_band": return S(0.18f, 0.25f, 0.32f, 0.6f, 0.85f);
                case "tank_white": return S(0.92f, 0.92f, 0.9f, 0.1f, 0.55f);
                case "water": return S(0.15f, 0.28f, 0.35f, 0.2f, 0.95f);
                case "tread": return S(0.22f, 0.22f, 0.22f, 0.7f, 0.35f);
                case "concrete": return S(1f, 1f, 1f, 0f, 0.25f, "concrete", 0.05f, 0.05f); // box UVs are in metres: one tile per 20 m
                case "wall": return S(0.52f, 0.55f, 0.58f, 0.3f, 0.35f, "wall", 10f, 4f);
                case "tower_steel": return S(0.62f, 0.15f, 0.10f, 0.4f, 0.4f);
                case "pad_steel": return S(0.35f, 0.36f, 0.38f, 0.8f, 0.4f, "grating", 4f, 4f);
                case "pad_hazard": return S(1f, 1f, 1f, 0.1f, 0.4f, "hazard", 24f, 1f);
                case "floor_marking": return S(0.9f, 0.7f, 0.1f, 0f, 0.3f);
                case "moon_rock": return S(0.55f, 0.55f, 0.55f, 0f, 0.1f);
            }
            if (key == "light_strip")
            {
                var s = S(1f, 1f, 1f, 0f, 0.5f);
                s.emission = new Color(3.2f, 3.1f, 2.9f);
                return s;
            }
            if (key == "light_red")
            {
                var s = S(1f, 0.2f, 0.15f, 0f, 0.5f);
                s.emission = new Color(4f, 0.4f, 0.3f);
                return s;
            }
            if (key == "screen_glow")
            {
                var s = S(0.1f, 0.3f, 0.4f, 0f, 0.9f);
                s.emission = new Color(0.25f, 0.9f, 1.4f);
                return s;
            }
            return S(1f, 0f, 1f, 0f, 0.5f);
        }

        public static Material Get(string key)
        {
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            var spec = Lookup(key);
            bool emissive = spec.emission.maxColorComponent > 0f;
            m = new Material(emissive ? EmissiveBase : LitBase);
            m.name = "Apogee_" + key;
            m.SetColor("_BaseColor", spec.color);
            m.SetColor("_Color", spec.color);
            m.SetFloat("_Metallic", spec.metallic);
            m.SetFloat("_Smoothness", spec.smoothness);
            m.SetFloat("_Glossiness", spec.smoothness);
            if (spec.texture != null)
            {
                var tex = ProceduralTextures.Get(spec.texture);
                m.SetTexture("_BaseMap", tex);
                m.SetTexture("_MainTex", tex);
                m.SetTextureScale("_BaseMap", spec.tiling);
                m.SetTextureScale("_MainTex", spec.tiling);
            }
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", spec.emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            cache[key] = m;
            return m;
        }

        public static Color BaseColor(string key) { return Lookup(key).color; }
    }
}
