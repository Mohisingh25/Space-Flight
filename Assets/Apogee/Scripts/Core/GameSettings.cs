using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Core
{
    /// <summary>Player-facing settings. Persisted as JSON in PlayerPrefs and applied immediately.</summary>
    [Serializable]
    public class GameSettings
    {
        const string Key = "apogee.settings.v1";

        public int quality = -1;            // 0 low, 1 medium, 2 high (-1 = pick from device RAM on first run)
        public float renderScale = 1f;      // 0.5 .. 1.0
        public int targetFps = 60;          // 30 / 60 / 120
        public bool shadows = true;
        public int msaa = 2;                // 1 / 2 / 4
        public float masterVolume = 1f;
        public float sfxVolume = 0.8f;
        public float ambientVolume = 0.6f;
        public float cameraSensitivity = 1f;
        public bool showFps;
        public bool batterySaver;
        public bool showBalanceMarkers;

        public event Action Changed;

        public static GameSettings Load()
        {
            GameSettings s = null;
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json)) s = JsonUtility.FromJson<GameSettings>(json);
            }
            catch (Exception e) { Debug.LogWarning("Settings unreadable, using defaults: " + e.Message); }
            if (s == null) s = new GameSettings();
            if (s.quality < 0)
            {
                int ram = SystemInfo.systemMemorySize;
                s.quality = ram >= 7000 ? 2 : ram >= 4500 ? 1 : 0;
                s.msaa = s.quality == 2 ? 4 : s.quality == 1 ? 2 : 1;
                s.renderScale = s.quality == 0 ? 0.8f : 1f;
                s.shadows = s.quality > 0;
            }
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
            if (Changed != null) Changed();
        }

        public int PlanetTextureSize { get { return quality == 0 ? 512 : quality == 1 ? 1024 : 2048; } }

        public int EffectiveFps { get { return batterySaver ? 30 : targetFps; } }

        public void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = EffectiveFps;
            QualitySettings.globalTextureMipmapLimit = quality == 0 ? 1 : 0;
            QualitySettings.lodBias = quality == 0 ? 0.7f : quality == 1 ? 1f : 1.5f;
            QualitySettings.anisotropicFiltering = quality == 0 ? AnisotropicFiltering.Disable : AnisotropicFiltering.Enable;
            AudioListener.volume = Mathf.Clamp01(masterVolume);

            float scale = Mathf.Clamp(renderScale, 0.5f, 1f);
            int aa = msaa;
            if (batterySaver) { scale = Mathf.Min(scale, 0.8f); aa = 1; }
            float shadowDistance = !shadows ? 0f : quality == 0 ? 40f : quality == 1 ? 90f : 160f;

            SetPipeline("renderScale", scale);
            SetPipeline("msaaSampleCount", aa);
            SetPipeline("shadowDistance", shadowDistance);
            QualitySettings.shadowDistance = shadowDistance; // built-in pipeline fallback
            QualitySettings.antiAliasing = aa > 1 ? aa : 0;
        }

        /// <summary>
        /// Sets a property on the active render pipeline asset (URP) by name. Reflection keeps this code
        /// independent of the URP package version and lets the game still run on the built-in pipeline.
        /// </summary>
        static void SetPipeline(string property, object value)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            try
            {
                var pi = rp.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null && pi.CanWrite) pi.SetValue(rp, Convert.ChangeType(value, pi.PropertyType), null);
            }
            catch (Exception e) { Debug.LogWarning("Could not set " + property + ": " + e.Message); }
        }
    }
}
