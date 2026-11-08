using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.EditorTools
{
    /// <summary>
    /// One-time project configuration so the repository needs no hand-made assets:
    /// creates the URP pipeline asset, base materials (so shaders are included in builds),
    /// the MainMenu and Builder scenes, build settings and Android player settings.
    /// Runs automatically when the project is first opened and before every build. Idempotent.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string SetupVersion = "5";
        const string MarkerPath = "ProjectSettings/ApogeeSetup.txt";
        public const string MainMenuScene = "Assets/Scenes/MainMenu.unity";
        public const string BuilderScene = "Assets/Scenes/Builder.unity";
        public const string FlightScene = "Assets/Scenes/Flight.unity";
        public static readonly string[] AllScenes = { MainMenuScene, BuilderScene, FlightScene };

        static ProjectSetup()
        {
            if (Application.isBatchMode) return; // CI builds call Run() explicitly
            EditorApplication.delayCall += () =>
            {
                if (IsComplete()) return;
                Run();
                if (File.Exists(MainMenuScene)) EditorSceneManager.OpenScene(MainMenuScene);
            };
        }

        public static bool IsComplete()
        {
            return File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == SetupVersion
                && File.Exists(MainMenuScene) && File.Exists(BuilderScene) && File.Exists(FlightScene)
                && File.Exists("Assets/Resources/Apogee/PartLit.mat");
        }

        [MenuItem("Apogee/Run Project Setup", priority = 100)]
        public static void RunFromMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Apogee", "Project setup complete.\n\nOpen Assets/Scenes/MainMenu and press Play, or use Apogee ▸ Build Android APK.", "OK");
        }

        public static void Run()
        {
            Debug.Log("[Apogee] Running project setup…");
            EnsureFolder("Assets/Resources/Apogee");
            EnsureFolder("Assets/Settings");
            EnsureFolder("Assets/Scenes");
            SetupRenderPipeline();
            SetupMaterials();
            SetupScenes();
            SetupPlayerSettings();
            SetupGraphicsStripping();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.WriteAllText(MarkerPath, SetupVersion);
            Debug.Log("[Apogee] Project setup complete.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------ URP

        static void SetupRenderPipeline()
        {
            var assetType = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
            var dataType = FindType("UnityEngine.Rendering.Universal.UniversalRendererData");
            if (assetType == null || dataType == null)
            {
                Debug.LogWarning("[Apogee] URP package not found — the game will use the built-in render pipeline.");
                return;
            }
            const string assetPath = "Assets/Settings/Apogee_URP.asset";
            var asset = AssetDatabase.LoadAssetAtPath(assetPath, assetType) as RenderPipelineAsset;
            if (asset == null)
            {
                ScriptableObject data = null;
                // Preferred: URP's own editor helper, which also fills in default resources.
                try
                {
                    var m = assetType.GetMethod("CreateRendererAsset", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (m != null)
                    {
                        var ps = m.GetParameters();
                        var args = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (i == 0) args[i] = assetPath;
                            else if (ps[i].ParameterType.IsEnum) args[i] = Enum.Parse(ps[i].ParameterType, "UniversalRenderer");
                            else args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                        }
                        data = m.Invoke(null, args) as ScriptableObject;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Apogee] CreateRendererAsset unavailable (" + e.Message + "), using fallback."); }
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance(dataType);
                    AssetDatabase.CreateAsset(data, "Assets/Settings/Apogee_URP_Renderer.asset");
                }

                ScriptableObject created = null;
                foreach (var m in assetType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (m.Name == "Create" && m.GetParameters().Length == 1)
                        created = m.Invoke(null, new object[] { data }) as ScriptableObject;
                if (created == null) created = ScriptableObject.CreateInstance(assetType);
                AssetDatabase.CreateAsset(created, assetPath);
                asset = (RenderPipelineAsset)created;
                Set(asset, "supportsHDR", false);
                Set(asset, "msaaSampleCount", 2);
                Set(asset, "shadowDistance", 120f);
                Set(asset, "shadowCascadeCount", 2);
                Set(asset, "renderScale", 1f);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            EnsureUrpGlobalSettings();
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log("[Apogee] URP asset assigned: " + assetPath);
        }

        /// <summary>URP needs a global settings asset; ask URP to create it if it doesn't exist yet.</summary>
        static void EnsureUrpGlobalSettings()
        {
            try
            {
                var t = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
                if (t == null) return;
                var m = t.GetMethod("Ensure", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return;
                var ps = m.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType == typeof(bool) ? (object)true : null);
                m.Invoke(null, args);
            }
            catch (Exception e) { Debug.LogWarning("[Apogee] URP global settings check skipped: " + e.Message); }
        }

        static void Set(object target, string property, object value)
        {
            try
            {
                var p = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanWrite) p.SetValue(target, Convert.ChangeType(value, p.PropertyType), null);
            }
            catch (Exception e) { Debug.LogWarning("[Apogee] Could not set URP " + property + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ materials

        static void SetupMaterials()
        {
            Shader lit = GraphicsSettings.defaultRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : null;
            if (lit == null) lit = Shader.Find("Standard");
            Mat("PartLit", lit, false);
            Mat("PartLitEmissive", lit, true);
            foreach (var n in new[] { "Planet", "Atmosphere", "Stars", "Ghost", "Marker", "Flame", "Smoke", "VertexColor", "Ground", "Terrain", "Sky", "Unlit", "Ring", "Line", "Cloud" })
            {
                var s = Shader.Find("Apogee/" + n);
                if (s == null) { Debug.LogError("[Apogee] Shader Apogee/" + n + " missing."); continue; }
                Mat(n, s, false);
            }
        }

        static void Mat(string name, Shader shader, bool emissive)
        {
            string path = "Assets/Resources/Apogee/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ scenes

        static void SetupScenes()
        {
            CreateScene(MainMenuScene, typeof(Apogee.Menu.MainMenuController));
            CreateScene(BuilderScene, typeof(Apogee.Builder.BuilderController));
            CreateScene(FlightScene, typeof(Apogee.Flight.FlightController));
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScene, true),
                new EditorBuildSettingsScene(BuilderScene, true),
                new EditorBuildSettingsScene(FlightScene, true)
            };
        }

        static void CreateScene(string path, Type controller)
        {
            if (File.Exists(path)) return;
            if (!Application.isBatchMode) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject(controller.Name);
            go.AddComponent(controller);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Apogee] Created scene " + path);
        }

        // ------------------------------------------------------------------ player settings

        static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "Apogee Sim";
            PlayerSettings.productName = "Apogee Spaceflight";
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "0.1" || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.apogeesim.spaceflight");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.renderOutsideSafeArea = true;

            // macOS / desktop: Mono backend (builds on GitHub's Linux machines), resizable window
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.SetApplicationIdentifier(standalone, "com.apogeesim.spaceflight");
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;

            // Make sure the classic Input Manager is enabled (the game reads touches through it).
            try
            {
                var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (objs != null && objs.Length > 0)
                {
                    var so = new SerializedObject(objs[0]);
                    var p = so.FindProperty("activeInputHandler");
                    if (p != null && p.intValue != 0) { p.intValue = 0; so.ApplyModifiedProperties(); } // 0 = Input Manager (old)
                }
            }
            catch (Exception e) { Debug.LogWarning("[Apogee] Could not check input handler: " + e.Message); }
        }

        /// <summary>Keep linear-fog shader variants: our scenes enable fog at runtime, which automatic stripping can't see.</summary>
        static void SetupGraphicsStripping()
        {
            try
            {
                var gs = GraphicsSettings.GetGraphicsSettings();
                var so = new SerializedObject(gs);
                var strip = so.FindProperty("m_FogStripping");
                var keepLinear = so.FindProperty("m_FogKeepLinear");
                if (strip != null) strip.intValue = 1; // custom
                if (keepLinear != null) keepLinear.boolValue = true;
                so.ApplyModifiedProperties();
            }
            catch (Exception e) { Debug.LogWarning("[Apogee] Could not adjust fog stripping: " + e.Message); }
        }
    }
}
