using Apogee.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Builder
{
    /// <summary>
    /// The vehicle assembly building: concrete floor, an octagonal assembly platform with hold-down
    /// clamps, a lattice service tower with crew access arms, propellant spheres, hangar walls with
    /// light strips, and lighting with a captured reflection probe so metal parts reflect the hall.
    /// </summary>
    public static class BuilderEnvironment
    {
        public const float PadTop = 1.2f;
        public const float TowerX = -17f;

        public static GameObject Build(out Light sun)
        {
            var root = new GameObject("Environment");

            // Lighting
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.41f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.19f, 0.18f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.27f, 0.29f, 0.32f);
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 520f;
            RenderSettings.skybox = null;
            DynamicGI.UpdateEnvironment();

            var sunGo = new GameObject("Key Light");
            sunGo.transform.SetParent(root.transform, false);
            sunGo.transform.rotation = Quaternion.Euler(58f, -38f, 0f);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;

            var fillGo = new GameObject("Fill Light");
            fillGo.transform.SetParent(root.transform, false);
            fillGo.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.65f, 0.75f, 0.95f);
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;

            // Floor
            var floor = new MeshBuilder();
            floor.Box(new Vector3(0f, -0.5f, 0f), new Vector3(300f, 1f, 300f), 0);
            Add(root, "Floor", floor.ToMesh("Floor"), new[] { MaterialLibrary.Get("concrete") }, false, true);

            // Hangar shell (inward-facing walls) with ceiling light strips
            var hall = new MeshBuilder();
            float W = 150f, H = 150f, D = 150f;
            hall.Quad(new Vector3(W, 0, D), new Vector3(-W, 0, D), new Vector3(-W, H, D), new Vector3(W, H, D), 0);      // back wall (faces -Z, inward)
            hall.Quad(new Vector3(-W, 0, -D), new Vector3(W, 0, -D), new Vector3(W, H, -D), new Vector3(-W, H, -D), 0);  // front wall (faces +Z)
            hall.Quad(new Vector3(-W, 0, D), new Vector3(-W, 0, -D), new Vector3(-W, H, -D), new Vector3(-W, H, D), 0);  // left wall (faces +X)
            hall.Quad(new Vector3(W, 0, -D), new Vector3(W, 0, D), new Vector3(W, H, D), new Vector3(W, H, -D), 0);      // right wall (faces -X)
            hall.Quad(new Vector3(-W, H, -D), new Vector3(W, H, -D), new Vector3(W, H, D), new Vector3(-W, H, D), 1);    // ceiling (faces down)
            for (int i = -3; i <= 3; i++)
                hall.Box(new Vector3(i * 38f, H - 1f, 0f), new Vector3(3f, 1.2f, 2 * D - 20f), 2);
            for (int i = -2; i <= 2; i++)
            {
                hall.Box(new Vector3(-W + 0.6f, 40f, i * 50f), new Vector3(1f, 70f, 2.2f), 2); // tall wall lights
                hall.Box(new Vector3(W - 0.6f, 40f, i * 50f), new Vector3(1f, 70f, 2.2f), 2);
            }
            // Giant door outline on the back wall
            hall.Box(new Vector3(0f, 60f, D - 0.5f), new Vector3(90f, 2f, 1f), 3);
            hall.Box(new Vector3(-45f, 30f, D - 0.5f), new Vector3(2f, 62f, 1f), 3);
            hall.Box(new Vector3(45f, 30f, D - 0.5f), new Vector3(2f, 62f, 1f), 3);
            Add(root, "Hall", hall.ToMesh("Hall"), new[] { MaterialLibrary.Get("wall"), MaterialLibrary.Get("dark_metal"), MaterialLibrary.Get("light_strip"), MaterialLibrary.Get("floor_marking") }, false, false);

            // Assembly platform
            var pad = new MeshBuilder();
            pad.Lathe(new[] { new Vector2(10.5f, 0f), new Vector2(10f, PadTop) }, 8, 1, 24f, 1f / PadTop, 22.5f);
            pad.Lathe(new[] { new Vector2(10f, PadTop), new Vector2(0f, PadTop) }, 8, 0, 1f, 0.2f, 22.5f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(a) * 4.2f, PadTop + 0.5f, Mathf.Sin(a) * 4.2f);
                pad.Box(c, new Vector3(0.9f, 1f, 0.9f), Quaternion.Euler(0f, -i * 90f - 45f, 0f), 2);   // hold-down clamps
                pad.Box(c + Vector3.up * 0.6f, new Vector3(1.1f, 0.2f, 0.5f), Quaternion.Euler(0f, -i * 90f - 45f, 0f), 1);
            }
            // Umbilical mast stubs and floor markings around the pad
            pad.Box(new Vector3(0f, 0.01f, 0f), new Vector3(28f, 0.02f, 0.4f), 3);
            pad.Box(new Vector3(0f, 0.01f, 0f), new Vector3(0.4f, 0.02f, 28f), 3);
            Add(root, "Assembly Platform", pad.ToMesh("Pad"), new[] { MaterialLibrary.Get("pad_steel"), MaterialLibrary.Get("pad_hazard"), MaterialLibrary.Get("dark_metal"), MaterialLibrary.Get("floor_marking") }, true, true);

            BuildTower(root);
            BuildProps(root);

            // Capture the hall into a reflection probe so metallic parts look metallic.
            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.SetParent(root.transform, false);
            probeGo.transform.position = new Vector3(0f, 20f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(320f, 320f, 320f);
            probe.boxProjection = false;
            probe.hdr = false;
            probe.cullingMask = ~(1 << 8); // everything except rocket parts
            probe.RenderProbe();

            return root;
        }

        static void BuildTower(GameObject root)
        {
            var mb = new MeshBuilder();
            float half = 2.2f, height = 96f, bay = 4f;
            var cx = new Vector3(TowerX, 0f, 0f);
            Vector3[] corners =
            {
                cx + new Vector3(-half, 0, -half), cx + new Vector3(half, 0, -half),
                cx + new Vector3(half, 0, half), cx + new Vector3(-half, 0, half)
            };
            foreach (var c in corners) mb.Box(c + Vector3.up * height * 0.5f, new Vector3(0.45f, height, 0.45f), 0);
            int bays = Mathf.RoundToInt(height / bay);
            for (int b = 0; b <= bays; b++)
            {
                float y = b * bay;
                for (int i = 0; i < 4; i++)
                {
                    var a = corners[i] + Vector3.up * y;
                    var c = corners[(i + 1) % 4] + Vector3.up * y;
                    mb.Box((a + c) * 0.5f, new Vector3(Mathf.Abs(c.x - a.x) + 0.3f, 0.3f, Mathf.Abs(c.z - a.z) + 0.3f), 0);
                    if (b < bays)
                    {
                        var c2 = corners[(i + 1) % 4] + Vector3.up * (y + bay);
                        mb.Tube(a, c2, 0.1f, 6, 0);
                    }
                }
                if (b % 3 == 0 && b > 0)
                    mb.Box(cx + Vector3.up * (y + 0.05f), new Vector3(half * 2f + 1.5f, 0.12f, half * 2f + 1.5f), 1); // platforms
            }
            // Crew / service access arms reaching toward the pad
            float[] arms = { 18f, 34f, 50f };
            foreach (float y in arms)
            {
                float len = 7.5f;
                var start = new Vector3(TowerX + half, y, 0f);
                mb.Box(start + new Vector3(len * 0.5f, 0f, 0f), new Vector3(len, 1.6f, 1.8f), 2);
                mb.Box(start + new Vector3(len, 0f, 0f), new Vector3(0.4f, 2.4f, 2.4f), 3);
            }
            // Hammerhead crane at the top
            mb.Box(cx + new Vector3(4f, height + 1.5f, 0f), new Vector3(24f, 1.2f, 1.2f), 0);
            mb.Box(cx + new Vector3(-6f, height + 0.6f, 0f), new Vector3(4f, 2.4f, 2.4f), 2);
            mb.Tube(cx + new Vector3(14f, height + 1f, 0f), cx + new Vector3(14f, height - 14f, 0f), 0.05f, 6, 2);
            Add(root, "Service Tower", mb.ToMesh("Tower"), new[] { MaterialLibrary.Get("tower_steel"), MaterialLibrary.Get("pad_steel"), MaterialLibrary.Get("aluminium"), MaterialLibrary.Get("light_red") }, true, true);
        }

        static void BuildProps(GameObject root)
        {
            var mb = new MeshBuilder();
            // Spherical propellant tanks on legs (gives a sense of scale)
            for (int i = 0; i < 3; i++)
            {
                var c = new Vector3(70f + i * 18f, 0f, 70f - i * 6f);
                var saved = mb.transform;
                mb.transform = Matrix4x4.TRS(c + Vector3.up * 11f, Quaternion.identity, Vector3.one);
                var prof = new System.Collections.Generic.List<Vector2>();
                for (int k = 0; k <= 16; k++)
                {
                    float t = -Mathf.PI / 2 + Mathf.PI * k / 16f;
                    prof.Add(new Vector2(Mathf.Cos(t) * 7f, Mathf.Sin(t) * 7f));
                }
                prof[0] = new Vector2(0f, -7f);
                prof[prof.Count - 1] = new Vector2(0f, 7f);
                mb.Lathe(prof, 32, 0);
                mb.transform = saved;
                for (int l = 0; l < 6; l++)
                {
                    float a = l * 60f * Mathf.Deg2Rad;
                    var top = c + new Vector3(Mathf.Cos(a) * 6.2f, 10f, Mathf.Sin(a) * 6.2f);
                    mb.Tube(new Vector3(top.x, 0f, top.z), top, 0.3f, 8, 1);
                }
            }
            // Parts racks and crates along the walls
            for (int i = 0; i < 6; i++)
            {
                var c = new Vector3(-120f + i * 14f, 0f, -110f);
                mb.Box(c + new Vector3(0, 2.5f, 0), new Vector3(10f, 5f, 4f), 2);
                mb.Box(c + new Vector3(0, 5.1f, 0), new Vector3(10.4f, 0.2f, 4.4f), 1);
            }
            // Transporter platform and a pair of mobile cranes
            mb.Box(new Vector3(40f, 1f, -30f), new Vector3(16f, 2f, 9f), 1);
            for (int w = 0; w < 4; w++) mb.Box(new Vector3(34f + w * 4f, 0.6f, -34.8f), new Vector3(2.5f, 1.2f, 1.5f), 3);
            mb.Box(new Vector3(-60f, 15f, 60f), new Vector3(2f, 30f, 2f), 4);
            mb.Box(new Vector3(-54f, 29f, 60f), new Vector3(16f, 1.5f, 1.5f), 4);
            Add(root, "Props", mb.ToMesh("Props"), new[] { MaterialLibrary.Get("white"), MaterialLibrary.Get("dark_metal"), MaterialLibrary.Get("aluminium"), MaterialLibrary.Get("rubber"), MaterialLibrary.Get("floor_marking") }, true, true);
        }

        static GameObject Add(GameObject root, string name, Mesh mesh, Material[] mats, bool castShadows, bool receiveShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = receiveShadows;
            return go;
        }
    }
}
