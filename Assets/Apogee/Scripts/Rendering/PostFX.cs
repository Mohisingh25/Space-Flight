using System;
using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>Turns on the post-processing profile (tone mapping, bloom, vignette, grading) for a camera stack.</summary>
    public static class PostFX
    {
        static GameObject volume;

        public static void Apply(params Camera[] cams)
        {
            try
            {
                var camDataT = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                var volT = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                var profile = Resources.Load<UnityEngine.Object>("Apogee/PostFX");
                if (camDataT == null || volT == null || profile == null) return;
                foreach (var c in cams)
                {
                    if (c == null) continue;
                    c.allowHDR = true;
                    var data = c.GetComponent(camDataT) ?? c.gameObject.AddComponent(camDataT);
                    var pp = camDataT.GetProperty("renderPostProcessing");
                    if (pp != null) pp.SetValue(data, true, null);
                    var aa = camDataT.GetProperty("antialiasing");
                    if (aa != null) try { aa.SetValue(data, Enum.Parse(aa.PropertyType, "FastApproximateAntialiasing"), null); } catch { }
                }
                if (volume == null)
                {
                    volume = new GameObject("Post Processing");
                    var v = volume.AddComponent(volT);
                    var g = volT.GetField("isGlobal");
                    if (g != null) g.SetValue(v, true);
                    var sp = volT.GetField("sharedProfile");
                    if (sp != null) sp.SetValue(v, profile);
                }
            }
            catch (Exception e) { Debug.LogWarning("Post-processing unavailable: " + e.Message); }
        }
    }
}
