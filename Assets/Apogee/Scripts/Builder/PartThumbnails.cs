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
    /// Renders a small studio-lit picture of every part for the parts list, one per frame, using an
    /// off-screen camera. Results are kept for the session.
    /// </summary>
    public class PartThumbnails : MonoBehaviour
    {
        const int Size = 128;
        const int Layer = 30;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public event Action<string, Texture2D> Ready;

        public static Texture2D Get(string partId)
        {
            Texture2D t;
            return cache.TryGetValue(partId, out t) ? t : null;
        }

        public void RenderAll()
        {
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var studio = new GameObject("Thumbnail Studio");
            studio.transform.position = new Vector3(0f, -2000f, 0f);
            var camGo = new GameObject("Thumb Camera");
            camGo.transform.SetParent(studio.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;

            var holder = new GameObject("Subject");
            holder.layer = Layer;
            holder.transform.SetParent(studio.transform, false);
            var mf = holder.AddComponent<MeshFilter>();
            var mr = holder.AddComponent<MeshRenderer>();

            foreach (var def in PartCatalog.All)
            {
                if (cache.ContainsKey(def.id)) { if (Ready != null) Ready(def.id, cache[def.id]); continue; }
                var dims = PartDims.Of(new PartInstance { partId = def.id });
                var model = PartMeshFactory.Get(def, dims);
                mf.sharedMesh = model.mesh;
                mr.sharedMaterials = model.materials;
                bool radial = !RocketAssembly.CanStack(def);
                holder.transform.localRotation = radial ? Quaternion.Euler(0f, 200f, 0f) : Quaternion.Euler(0f, 20f, 0f);
                var b = model.mesh.bounds;
                Vector3 c = holder.transform.TransformPoint(b.center);
                float r = b.extents.magnitude;
                float dist = r / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
                camGo.transform.position = c + Quaternion.Euler(18f, -30f, 0f) * Vector3.back * dist;
                camGo.transform.LookAt(c);
                cam.enabled = true;
                yield return new WaitForEndOfFrame();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cache[def.id] = tex;
                if (Ready != null) Ready(def.id, tex);
            }
            cam.targetTexture = null;
            rt.Release();
            Destroy(studio);
        }
    }
}
