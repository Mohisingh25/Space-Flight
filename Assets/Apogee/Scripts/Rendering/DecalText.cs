using Apogee.Rocket;
using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>Puts the player's logo text on a Logo Panel part (3D text facing outwards from the hull).</summary>
    public static class DecalText
    {
        public static void Attach(GameObject partGo, PartInstance p)
        {
            if (p == null || !p.Def.isDecal) return;
            var old = partGo.transform.Find("Logo");
            if (old != null) Object.Destroy(old.gameObject);
            string text = string.IsNullOrEmpty(p.label) ? "APOGEE" : p.label;
            var dims = PartDims.Of(p);
            var go = new GameObject("Logo");
            go.layer = partGo.layer;
            go.transform.SetParent(partGo.transform, false);
            go.transform.localPosition = new Vector3(0.035f, 0f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            var tm = go.AddComponent<TextMesh>();
            var font = Apogee.UI.UIFactory.Font;
            tm.font = font;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.text = text;
            float lines = text.Split('\n').Length;
            int longest = 1;
            foreach (var l in text.Split('\n')) longest = Mathf.Max(longest, l.Length);
            float charH = Mathf.Min(dims.height * 0.62f / lines, dims.diameter / (longest * 0.62f));
            tm.characterSize = charH * 10f / tm.fontSize;
            bool dark = p.paint >= 0 && PartPaint.Colors[p.paint].grayscale < 0.45f;
            tm.color = dark ? Color.white : new Color(0.08f, 0.1f, 0.14f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
