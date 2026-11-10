using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Apogee.EditorTools
{
    /// <summary>
    /// Creates the camera post-processing profile (ACES tone mapping, bloom, vignette, colour grading) as an
    /// asset, so URP keeps the matching shaders in the build. Done through reflection, like the URP setup.
    /// </summary>
    public static class PostFXSetup
    {
        const string Path = "Assets/Resources/Apogee/PostFX.asset";

        static Type T(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        }

        public static void Create()
        {
            var profT = T("UnityEngine.Rendering.VolumeProfile");
            var bloomT = T("UnityEngine.Rendering.Universal.Bloom");
            var toneT = T("UnityEngine.Rendering.Universal.Tonemapping");
            var vigT = T("UnityEngine.Rendering.Universal.Vignette");
            var colT = T("UnityEngine.Rendering.Universal.ColorAdjustments");
            var modeT = T("UnityEngine.Rendering.Universal.TonemappingMode");
            if (profT == null || bloomT == null || toneT == null) { Debug.LogWarning("[Apogee] URP post-processing types not found."); return; }
            if (AssetDatabase.LoadAssetAtPath(Path, profT) != null) AssetDatabase.DeleteAsset(Path);
            var profile = ScriptableObject.CreateInstance(profT);
            AssetDatabase.CreateAsset(profile, Path);
            var add = profT.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            Func<Type, object> addComp = t =>
            {
                var c = add.Invoke(profile, new object[] { t, true }) as UnityEngine.Object;
                if (c != null) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
                return c;
            };
            var bloom = addComp(bloomT);
            Set(bloom, "intensity", 0.75f);
            Set(bloom, "threshold", 1.0f);
            Set(bloom, "scatter", 0.72f);
            var tone = addComp(toneT);
            if (modeT != null) Set(tone, "mode", Enum.Parse(modeT, "ACES"));
            if (vigT != null) { var v = addComp(vigT); Set(v, "intensity", 0.2f); Set(v, "smoothness", 0.45f); }
            if (colT != null) { var c = addComp(colT); Set(c, "postExposure", 0.25f); Set(c, "contrast", 10f); Set(c, "saturation", 8f); }
            EditorUtility.SetDirty(profile);

            // HDR on the pipeline asset so bloom has real highlights to work with
            var urp = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Settings/Apogee_URP.asset");
            if (urp != null)
            {
                var hdr = urp.GetType().GetProperty("supportsHDR");
                if (hdr != null && hdr.CanWrite) { hdr.SetValue(urp, true, null); EditorUtility.SetDirty(urp); }
            }
            Debug.Log("[Apogee] Post-processing profile created.");
        }

        static void Set(object comp, string field, object value)
        {
            if (comp == null) return;
            var f = comp.GetType().GetField(field);
            if (f == null) return;
            var param = f.GetValue(comp);
            var m = param.GetType().GetMethod("Override", new[] { value.GetType() }) ?? param.GetType().GetMethods().FirstOrDefault(x => x.Name == "Override" && x.GetParameters().Length == 1);
            if (m != null) m.Invoke(param, new[] { value });
        }
    }
}
