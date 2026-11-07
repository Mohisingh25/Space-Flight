using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>
    /// Paint colours a player can give a part. Painting recolours the part's painted body panels (white
    /// paint, orange foam, stainless skin); fittings, nozzles, windows and heat shields keep their real look.
    /// </summary>
    public static class PartPaint
    {
        public static readonly string[] Names =
        {
            "White", "Black", "Orange", "Red", "Blue", "Navy", "Green", "Yellow", "Silver", "Gold", "Purple", "Teal"
        };

        public static readonly Color[] Colors =
        {
            new Color(0.93f, 0.93f, 0.91f), new Color(0.12f, 0.12f, 0.13f), new Color(0.92f, 0.45f, 0.12f),
            new Color(0.78f, 0.12f, 0.1f), new Color(0.15f, 0.42f, 0.85f), new Color(0.1f, 0.16f, 0.36f),
            new Color(0.2f, 0.6f, 0.3f), new Color(0.95f, 0.78f, 0.15f), new Color(0.72f, 0.74f, 0.77f),
            new Color(0.85f, 0.65f, 0.3f), new Color(0.45f, 0.25f, 0.7f), new Color(0.1f, 0.6f, 0.62f)
        };

        public static bool IsBodyKey(string key)
        {
            return key == "white" || key == "foam" || key == "stainless" || key == "black" || key == "carbon";
        }

        /// <summary>Colour of one submesh after painting.</summary>
        public static Color ColorFor(string key, int paint)
        {
            Color baseCol = MaterialLibrary.BaseColor(key);
            if (paint < 0 || paint >= Colors.Length || !IsBodyKey(key)) return baseCol;
            Color c = Colors[paint];
            // Keep two-tone patterns (roll-pattern black quadrants, carbon trim) readable as a darker shade.
            if (key == "black" || key == "carbon") return c * 0.3f;
            return c;
        }

        /// <summary>Applies paint to a renderer. Returns the per-submesh colours (used for highlight tinting).</summary>
        public static Color[] Apply(MeshRenderer r, string[] keys, int paint, MaterialPropertyBlock mpb)
        {
            var cols = new Color[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                cols[i] = ColorFor(keys[i], paint);
                mpb.Clear();
                if (paint >= 0 && IsBodyKey(keys[i]))
                {
                    mpb.SetColor("_BaseColor", cols[i]);
                    mpb.SetColor("_Color", cols[i]);
                }
                r.SetPropertyBlock(mpb, i);
            }
            return cols;
        }
    }
}
