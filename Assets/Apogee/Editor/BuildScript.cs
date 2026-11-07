using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Apogee.EditorTools
{
    /// <summary>
    /// One-click builds from the editor menu, and the entry points GitHub Actions (GameCI) calls:
    ///   Android: Apogee.EditorTools.BuildScript.BuildCI
    ///   macOS:   Apogee.EditorTools.BuildScript.BuildMacCI
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Apogee/Build Android APK (Debug)", priority = 1)]
        public static void BuildDebugMenu() { BuildAndroid(true, DefaultApkPath(true)); }

        [MenuItem("Apogee/Build Android APK (Release)", priority = 2)]
        public static void BuildReleaseMenu() { BuildAndroid(false, DefaultApkPath(false)); }

        [MenuItem("Apogee/Build macOS App", priority = 3)]
        public static void BuildMacMenu() { BuildMac(false, "Builds/macOS/Apogee.app"); }

        static string DefaultApkPath(bool dev)
        {
            return "Builds/Android/Apogee-" + (dev ? "debug" : "release") + "-" + PlayerSettings.bundleVersion + ".apk";
        }

        // ------------------------------------------------------------------ CI entry points

        /// <summary>Android CI build. Pass -apogeeDev for a development (debug) build.</summary>
        public static void BuildCI()
        {
            bool dev = HasArg("-apogeeDev");
            string path = GetArg("-customBuildPath");
            if (string.IsNullOrEmpty(path)) path = DefaultApkPath(dev);
            if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Apogee.apk");
            int code;
            if (int.TryParse(GetArg("-androidVersionCode"), out code) && code > 0) PlayerSettings.Android.bundleVersionCode = code;
            Finish(() => BuildAndroid(dev, path), "Android");
        }

        /// <summary>macOS CI build (Universal: Apple Silicon + Intel).</summary>
        public static void BuildMacCI()
        {
            bool dev = HasArg("-apogeeDev");
            string path = GetArg("-customBuildPath");
            if (string.IsNullOrEmpty(path)) path = "Builds/macOS/Apogee.app";
            if (!path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Apogee.app");
            Finish(() => BuildMac(dev, path), "macOS");
        }

        static void Finish(Func<BuildReport> build, string label)
        {
            BuildReport report = null;
            try { report = build(); }
            catch (Exception e) { Debug.LogError("[Apogee] Build threw: " + e); }
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Apogee] " + label + " build FAILED.");
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ builds

        public static BuildReport BuildAndroid(bool development, string path)
        {
            ProjectSetup.Run();
            EditorUserBuildSettings.buildAppBundle = false;
            return Run(BuildTarget.Android, BuildTargetGroup.Android, development, path);
        }

        public static BuildReport BuildMac(bool development, string path)
        {
            ProjectSetup.Run();
            SetMacUniversal();
            return Run(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, development, path);
        }

        static BuildReport Run(BuildTarget target, BuildTargetGroup group, bool development, string path)
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            EditorUserBuildSettings.development = development;
            var options = new BuildPlayerOptions
            {
                scenes = ProjectSetup.AllScenes,
                locationPathName = full,
                target = target,
                targetGroup = group,
                options = development ? BuildOptions.Development : BuildOptions.None
            };
            Debug.Log("[Apogee] Building " + target + (development ? " (DEBUG)" : " (RELEASE)") + " → " + full);
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log("[Apogee] Build " + s.result + " in " + s.totalTime + " — " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " + s.totalErrors + " errors, " + s.totalWarnings + " warnings");
            if (!Application.isBatchMode && s.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(full);
            return report;
        }

        /// <summary>
        /// Builds one app that runs natively on both Apple Silicon (M1+) and Intel Macs. Set via reflection
        /// because the macOS build module (and its API) only exists where Mac Build Support is installed.
        /// </summary>
        static void SetMacUniversal()
        {
            try
            {
                Type settings = null, arch = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (settings == null) settings = asm.GetType("UnityEditor.OSXStandalone.UserBuildSettings");
                    if (arch == null) arch = asm.GetType("UnityEditor.Build.OSArchitecture");
                }
                if (settings == null || arch == null) { Debug.LogWarning("[Apogee] macOS architecture API not found; using default."); return; }
                var prop = settings.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static);
                if (prop != null) prop.SetValue(null, Enum.Parse(arch, "x64ARM64"), null);
                Debug.Log("[Apogee] macOS architecture: Universal (Apple Silicon + Intel)");
            }
            catch (Exception e) { Debug.LogWarning("[Apogee] Could not set macOS architecture: " + e.Message); }
        }

        static bool HasArg(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == name) return true;
            return false;
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
