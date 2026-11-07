using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Apogee.EditorTools
{
    /// <summary>
    /// One-click Android builds from the editor menu, and the entry point GitHub Actions (GameCI) calls:
    /// Apogee.EditorTools.BuildScript.BuildCI
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Apogee/Build Android APK (Debug)", priority = 1)]
        public static void BuildDebugMenu() { Build(true, DefaultPath(true)); }

        [MenuItem("Apogee/Build Android APK (Release)", priority = 2)]
        public static void BuildReleaseMenu() { Build(false, DefaultPath(false)); }

        static string DefaultPath(bool dev)
        {
            return "Builds/Android/Apogee-" + (dev ? "debug" : "release") + "-" + PlayerSettings.bundleVersion + ".apk";
        }

        /// <summary>Command-line / CI entry point. Pass -apogeeDev for a development (debug) build.</summary>
        public static void BuildCI()
        {
            bool dev = HasArg("-apogeeDev");
            string path = GetArg("-customBuildPath");
            if (string.IsNullOrEmpty(path)) path = DefaultPath(dev);
            if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Apogee.apk");
            int code;
            if (int.TryParse(GetArg("-androidVersionCode"), out code) && code > 0) PlayerSettings.Android.bundleVersionCode = code;
            BuildReport report = null;
            try { report = Build(dev, path); }
            catch (Exception e) { Debug.LogError("[Apogee] Build threw: " + e); }
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Apogee] Android build FAILED.");
                EditorApplication.Exit(1);
            }
        }

        public static BuildReport Build(bool development, string path)
        {
            ProjectSetup.Run();
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = development;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainMenuScene, ProjectSetup.BuilderScene },
                locationPathName = full,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None
            };
            Debug.Log("[Apogee] Building " + (development ? "DEBUG" : "RELEASE") + " APK → " + full);
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log("[Apogee] Build " + s.result + " in " + s.totalTime + " — " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " + s.totalErrors + " errors, " + s.totalWarnings + " warnings");
            if (!Application.isBatchMode && s.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(full);
            return report;
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
