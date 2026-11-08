using System.Collections;
using Apogee.Core;
using Apogee.Rendering;
using Apogee.Rocket;
using Apogee.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Apogee.Menu
{
    /// <summary>
    /// Main menu: a slowly turning rocket in Earth orbit with a procedurally generated Earth (clouds,
    /// ocean glint, night-side city lights, atmospheric rim), the Moon, a star field and the sun, behind
    /// the game's menu. Locked modes are clearly labelled with the roadmap phase that delivers them.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        Camera cam;
        Transform earth, rocket, stars, moon;
        Material earthMat, atmoMat, moonMat;
        Vector3 sunDir;
        Text status;
        float t;
        Vector3 camBase;
        Vector3 rocketBase = new Vector3(13f, 1f, 64f);
        RectTransform uiRoot;

        void Start()
        {
            AppRoot.Instance.Audio.SetAmbience(Ambience.Space);
            BuildScene();
            BuildUI();
            ApplyOrientation();
            AppRoot.OrientationChanged += OnOrientationChanged;
            StartCoroutine(LoadEarth());
        }

        void OnDestroy() { AppRoot.OrientationChanged -= OnOrientationChanged; }

        void OnOrientationChanged()
        {
            string keep = status != null ? status.text : "";
            if (uiRoot != null) Destroy(uiRoot.parent.gameObject);
            BuildUI();
            status.text = keep;
            ApplyOrientation();
        }

        /// <summary>Re-frames the showcase scene: rocket beside the menu in landscape, above it in portrait.</summary>
        void ApplyOrientation()
        {
            if (UIFactory.Portrait)
            {
                rocketBase = new Vector3(0f, 8f, 64f);
                earth.position = new Vector3(22f, -112f, 260f);
                earth.localScale = Vector3.one * 108f;
            }
            else
            {
                rocketBase = new Vector3(13f, 1f, 64f);
                earth.position = new Vector3(70f, -62f, 185f);
                earth.localScale = Vector3.one * 92f;
            }
            if (rocket != null) rocket.position = rocketBase;
        }

        // ------------------------------------------------------------------ scene

        void BuildScene()
        {
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.04f, 0.055f);
            DynamicGI.UpdateEnvironment();

            var camGo = new GameObject("Menu Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 6000f;
            camGo.AddComponent<AudioListener>();
            camBase = Vector3.zero;

            sunDir = new Vector3(-0.75f, 0.32f, 0.58f).normalized;
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.9f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.LookRotation(-sunDir);
            RenderSettings.sun = sun;

            // Stars (follow the camera so they behave like an infinitely distant sky)
            var starMat = MaterialLibrary.Custom("Stars");
            starMat.SetTexture("_MainTex", ProceduralTextures.Glow(64));
            stars = MakeObject("Stars", PlanetFactory.StarField(4200, 2500f, 42), starMat).transform;

            // Sun glare
            var glareMat = MaterialLibrary.Custom("Stars");
            glareMat.SetTexture("_MainTex", ProceduralTextures.Glow(128));
            glareMat.SetFloat("_Brightness", 1.6f);
            var glare = MakeObject("Sun Glare", PlanetFactory.Quad(), glareMat);
            glare.transform.position = sunDir * 2300f;
            glare.transform.rotation = Quaternion.LookRotation(sunDir);
            glare.transform.localScale = Vector3.one * 900f;
            SetMeshColor(glare, new Color(1f, 0.93f, 0.8f, 0.9f));
            var core = MakeObject("Sun Core", PlanetFactory.Quad(), glareMat);
            core.transform.position = sunDir * 2290f;
            core.transform.rotation = Quaternion.LookRotation(sunDir);
            core.transform.localScale = Vector3.one * 160f;
            core.transform.SetParent(glare.transform, true);
            glare.transform.SetParent(stars, true);

            // Earth
            var sphere = Apogee.Flight.SpaceView.Sphere();
            earthMat = MaterialLibrary.Custom("Planet");
            earthMat.SetVector("_SunDir", sunDir);
            var earthGo = MakeObject("Earth", sphere, earthMat);
            earth = earthGo.transform;
            earth.position = new Vector3(70f, -62f, 185f);
            earth.localScale = Vector3.one * 92f;
            earth.rotation = Quaternion.Euler(-23.4f, 0f, 0f);
            earthGo.SetActive(false);

            atmoMat = MaterialLibrary.Custom("Atmosphere");
            atmoMat.SetVector("_SunDir", sunDir);
            var atmo = MakeObject("Atmosphere", sphere, atmoMat);
            atmo.transform.SetParent(earth, false);
            atmo.transform.localScale = Vector3.one * 1.035f;

            // Moon
            moonMat = MaterialLibrary.Custom("Planet");
            moonMat.SetVector("_SunDir", sunDir);
            moonMat.SetFloat("_CloudStrength", 0f);
            moonMat.SetFloat("_AtmoStrength", 0f);
            moonMat.SetFloat("_Specular", 0f);
            moonMat.SetFloat("_NightLights", 0f);
            moonMat.SetFloat("_Ambient", 0.01f);
            moon = MakeObject("Moon", Apogee.Flight.SpaceView.Sphere(), moonMat).transform;
            moon.position = new Vector3(-340f, 150f, 900f);
            moon.localScale = Vector3.one * 16f;
            moon.gameObject.SetActive(false);

            // Rocket in orbit
            var viewGo = new GameObject("Showcase Rocket");
            var view = viewGo.AddComponent<RocketView>();
            view.buildColliders = false;
            view.baseHeight = 0f;
            var bp = ExampleRockets.Meridian();
            view.Rebuild(bp);
            var b = view.WorldBounds;
            var pivot = new GameObject("Rocket Pivot").transform;
            pivot.position = new Vector3(13f, 1f, 64f);
            viewGo.transform.SetParent(pivot, false);
            viewGo.transform.localPosition = new Vector3(0f, -b.center.y, 0f);
            pivot.rotation = Quaternion.Euler(8f, 0f, -16f);
            rocket = pivot;
        }

        static void SetMeshColor(GameObject go, Color c)
        {
            var mf = go.GetComponent<MeshFilter>();
            var m = Instantiate(mf.sharedMesh);
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            m.colors = cols;
            mf.sharedMesh = m;
        }

        static GameObject MakeObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        IEnumerator LoadEarth()
        {
            // Real Earth: Solar System Scope maps (CC BY 4.0), based on NASA imagery
            Texture2D albedo = Apogee.Flight.SpaceView.Tex("earth"), cloudTex = Apogee.Flight.SpaceView.Tex("earth_clouds");
            if (albedo == null)
            {
                int size = AppRoot.Instance.Settings.PlanetTextureSize;
                yield return PlanetFactory.BuildEarth(size, (a, c) => { albedo = a; cloudTex = c; });
            }
            if (albedo != null)
            {
                earthMat.SetTexture("_MainTex", albedo);
                earthMat.SetTexture("_CloudTex", cloudTex);
                earth.gameObject.SetActive(true);
            }
            yield return null;
            var moonTex = Apogee.Flight.SpaceView.Tex("moon");
            moonMat.SetTexture("_MainTex", moonTex != null ? moonTex : PlanetFactory.BuildMoon(512));
            moon.gameObject.SetActive(true);
            status.text = "";

            // Let the rocket's metal reflect Earth and space.
            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.position = rocket.position;
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.resolution = 128;
            probe.size = Vector3.one * 200f;
            probe.hdr = false;
            probe.cullingMask = ~(1 << RocketView.PartLayer);
            probe.RenderProbe();
        }

        void Update()
        {
            t += Time.deltaTime;
            if (earth != null) earth.Rotate(Vector3.up, -0.6f * Time.deltaTime, Space.Self);
            if (earthMat != null) earthMat.SetFloat("_CloudShift", t * 0.0015f);
            if (rocket != null)
            {
                rocket.Rotate(Vector3.up, 6f * Time.deltaTime, Space.Self);
                rocket.position = rocketBase + new Vector3(0f, Mathf.Sin(t * 0.35f) * 0.6f, 0f);
            }
            if (cam != null)
            {
                cam.transform.position = camBase + new Vector3(Mathf.Sin(t * 0.07f) * 2.2f, Mathf.Sin(t * 0.05f) * 1.2f, 0f);
                cam.transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.06f) * 1.2f, Mathf.Sin(t * 0.045f) * 2f, 0f);
                if (stars != null) stars.position = cam.transform.position;
            }

            if (AppRoot.Instance.BackPressed())
                AppRoot.Instance.Dialogs.Confirm("Quit Apogee?", "Your designs are saved on this device.", "Quit", Application.Quit, true);
        }

        // ------------------------------------------------------------------ UI

        void BuildUI()
        {
            Canvas canvas;
            var root = UIFactory.CreateCanvas("Main Menu", 10, out canvas);
            uiRoot = root;
            bool portrait = UIFactory.Portrait;

            // Soft vignette behind the menu column for legibility
            var shade = UIFactory.Panel(root, new Color(0, 0, 0, 0.35f), false, "Shade");
            shade.rectTransform.anchorMin = new Vector2(0, 0);
            shade.rectTransform.anchorMax = portrait ? new Vector2(1, 0.56f) : new Vector2(0.42f, 1);
            shade.rectTransform.offsetMin = new Vector2(-200, -200);
            shade.rectTransform.offsetMax = portrait ? new Vector2(200, 0) : new Vector2(0, 200);
            shade.raycastTarget = false;

            var col = UIFactory.Rect("Menu", root);
            if (portrait)
            {
                // Title at the top, rocket and Earth in the middle, menu column along the bottom.
                var head = UIFactory.Rect("Title", root);
                head.anchorMin = new Vector2(0, 1);
                head.anchorMax = new Vector2(1, 1);
                head.pivot = new Vector2(0.5f, 1);
                head.offsetMin = new Vector2(40, -250);
                head.offsetMax = new Vector2(-40, -70);
                UIFactory.Vertical(head, 4, 0, true).childAlignment = TextAnchor.UpperCenter;
                var pt = UIFactory.Label(head, "A P O G E E", 104, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Size(pt, -1, 124);
                var ps = UIFactory.Label(head, "SPACEFLIGHT  SIMULATOR", 30, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Size(ps, -1, 46);

                col.anchorMin = new Vector2(0, 0);
                col.anchorMax = new Vector2(1, 0);
                col.pivot = new Vector2(0.5f, 0);
                col.offsetMin = new Vector2(60, 90);
                col.offsetMax = new Vector2(-60, 90 + 760);
            }
            else
            {
                col.anchorMin = new Vector2(0, 0);
                col.anchorMax = new Vector2(0, 1);
                col.pivot = new Vector2(0, 0.5f);
                col.offsetMin = new Vector2(90, 70);
                col.offsetMax = new Vector2(90 + 620, -70);
            }
            var v = UIFactory.Vertical(col, 12, 0, true);
            v.childAlignment = portrait ? TextAnchor.LowerCenter : TextAnchor.MiddleLeft;

            if (!portrait)
            {
                var title = UIFactory.Label(col, "A P O G E E", 96, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Size(title, -1, 120);
                var sub = UIFactory.Label(col, "SPACEFLIGHT  SIMULATOR", 28, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Size(sub, -1, 44);
                var gap = UIFactory.Rect("Gap", col);
                UIFactory.Size(gap, -1, 26);
            }

            bool hasAutosave = SaveSystem.HasAutosave();
            MenuButton(col, "Sandbox Mode", hasAutosave ? "Continue your last design with unlimited parts" : "Unlimited parts — build anything", ButtonStyle.Primary, () =>
            {
                Session.builderEntry = BuilderEntry.Continue;
                AppRoot.Instance.LoadScene(Scenes.Builder);
            });
            MenuButton(col, "Spacecraft Designer", "Start a fresh design on an empty platform", ButtonStyle.Normal, () =>
            {
                Session.builderEntry = BuilderEntry.NewDesign;
                AppRoot.Instance.LoadScene(Scenes.Builder);
            });
            MenuButton(col, "Saved Designs", "Open, or delete, rockets saved on this device", ButtonStyle.Normal, OpenSaved);
            MenuButton(col, "Settings", "Graphics, audio and controls", ButtonStyle.Normal, OpenSettings);

            var gap2 = UIFactory.Rect("Gap", col);
            UIFactory.Size(gap2, -1, 14);
            var soon = UIFactory.Label(col, "IN DEVELOPMENT", 20, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(soon, -1, 30);
            var grid = UIFactory.Rect("Locked", col);
            UIFactory.Size(grid, -1, 130);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = portrait ? new Vector2(470, 58) : new Vector2(300, 58);
            g.spacing = new Vector2(12, 12);
            Locked(grid, "New Career", 5, "Career mode (research, budget, tech tree) is planned for Phase 5.");
            Locked(grid, "Mission Mode", 5, "Structured missions — from first launch to Mars — are planned for Phase 5.");
            Locked(grid, "Space Station", 4, "Docking arrives in Phase 4 and station building in Phase 5.");
            Locked(grid, "Planet Explorer", 3, "Orbital flight, the Moon and the navigation map arrive in Phase 3.");

            status = UIFactory.Label(root, "", 20, Theme.Muted, TextAnchor.LowerRight);
            if (portrait) status.alignment = TextAnchor.LowerCenter;
            UIFactory.Place(status.rectTransform, portrait ? new Vector2(0.5f, 0) : new Vector2(1, 0), portrait ? new Vector2(0.5f, 0) : new Vector2(1, 0),
                portrait ? new Vector2(0, 52) : new Vector2(-30, 24), new Vector2(portrait ? 1000 : 900, 40));
            var ver = UIFactory.Label(root, "Phase 4 build  ·  v" + Application.version, 18, Theme.Muted, TextAnchor.LowerLeft);
            UIFactory.Place(ver.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 24), new Vector2(600, 36));
        }

        void MenuButton(RectTransform parent, string label, string detail, ButtonStyle style, System.Action onClick)
        {
            var b = UIFactory.Button(parent, label, onClick, style, 34);
            UIFactory.Size(b, -1, 100);
            var text = b.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.resizeTextForBestFit = false;
            text.text = label + "\n<size=20><color=#9fb2c6>" + detail + "</color></size>";
            text.rectTransform.offsetMin = new Vector2(28, 6);
        }

        void Locked(RectTransform parent, string label, int phase, string message)
        {
            var b = UIFactory.Button(parent, label + "  ·  Phase " + phase, () => AppRoot.Instance.Dialogs.Toast(message, Theme.Accent, 3.2f), ButtonStyle.Ghost, 21);
            var text = b.GetComponentInChildren<Text>();
            text.color = Theme.Muted;
        }

        void OpenSaved()
        {
            AppRoot.Instance.Dialogs.Custom("Saved designs", 1180, 820, (body, close) =>
                BlueprintBrowser.Build(body, (bp, path) =>
                {
                    close();
                    Session.builderEntry = BuilderEntry.OpenBlueprint;
                    Session.blueprintToOpen = bp;
                    Session.blueprintPath = path;
                    AppRoot.Instance.LoadScene(Scenes.Builder);
                }, close));
        }

        void OpenSettings()
        {
            AppRoot.Instance.Dialogs.Custom("Settings", 1180, 900, (body, close) => SettingsPanel.Build(body, close));
        }
    }
}
