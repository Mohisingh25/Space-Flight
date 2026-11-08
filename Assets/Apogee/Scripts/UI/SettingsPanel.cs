using System;
using Apogee.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apogee.UI
{
    /// <summary>Settings screen. Every control writes straight to <see cref="GameSettings"/>, which saves and applies.</summary>
    public static class SettingsPanel
    {
        public static void Build(RectTransform body, Action onClose)
        {
            var s = AppRoot.Instance.Settings;
            RectTransform c;
            var scroll = UIFactory.ScrollList(body, out c, 6, 6);
            UIFactory.Size(scroll, -1, -1, -1, 1);

            UIFactory.Header(c, "Graphics");
            UIFactory.Segmented(c, "Quality", new[] { "Low", "Medium", "High" }, s.quality, i => { s.quality = i; s.Save(); });
            UIFactory.SliderRow(c, "Resolution scale", 0.5f, 1f, s.renderScale, v => { s.renderScale = v; s.Save(); }, v => Mathf.RoundToInt(v * 100f) + " %");
            UIFactory.Segmented(c, "Frame rate", new[] { "30", "60", "120" }, s.targetFps >= 120 ? 2 : s.targetFps >= 60 ? 1 : 0, i => { s.targetFps = i == 2 ? 120 : i == 1 ? 60 : 30; s.Save(); });
            UIFactory.Segmented(c, "Anti-aliasing", new[] { "Off", "2×", "4×" }, s.msaa >= 4 ? 2 : s.msaa >= 2 ? 1 : 0, i => { s.msaa = i == 2 ? 4 : i == 1 ? 2 : 1; s.Save(); });
            UIFactory.ToggleRow(c, "Shadows", s.shadows, on => { s.shadows = on; s.Save(); });
            UIFactory.ToggleRow(c, "Battery saver (30 FPS, lower resolution)", s.batterySaver, on => { s.batterySaver = on; s.Save(); });
            UIFactory.ToggleRow(c, "Show FPS counter", s.showFps, on => { s.showFps = on; s.Save(); });
            var note = UIFactory.Label(c, "Planet texture detail follows Quality and applies the next time the main menu loads.", 19, Theme.Muted);
            UIFactory.Size(note, -1, 40);

            UIFactory.Header(c, "Audio");
            UIFactory.SliderRow(c, "Master volume", 0f, 1f, s.masterVolume, v => { s.masterVolume = v; s.Save(); }, Pct);
            UIFactory.SliderRow(c, "Effects", 0f, 1f, s.sfxVolume, v => { s.sfxVolume = v; s.Save(); }, Pct);
            UIFactory.SliderRow(c, "Ambience", 0f, 1f, s.ambientVolume, v => { s.ambientVolume = v; s.Save(); }, Pct);

            UIFactory.Header(c, "Controls");
            UIFactory.SliderRow(c, "Camera sensitivity", 0.3f, 2.5f, s.cameraSensitivity, v => { s.cameraSensitivity = v; s.Save(); }, v => v.ToString("0.0") + "×");
            var ctl = UIFactory.Label(c, "In flight, tap any part: engines start/stop one by one, legs and solar arrays fold, parachutes deploy, decouplers and docking ports separate.", 19, Theme.Muted);
            UIFactory.Size(ctl, -1, 52);

            UIFactory.Header(c, "Cheats");
            var warn = UIFactory.Label(c, "Off = real physics. Cheats apply only while the master switch is on.", 19, Theme.Muted);
            UIFactory.Size(warn, -1, 34);
            UIFactory.ToggleRow(c, "Enable cheats", s.cheatsEnabled, on => { s.cheatsEnabled = on; s.Save(); });
            UIFactory.ToggleRow(c, "Unlimited fuel", s.cheatInfiniteFuel, on => { s.cheatInfiniteFuel = on; s.Save(); });
            UIFactory.ToggleRow(c, "Zero gravity", s.cheatNoGravity, on => { s.cheatNoGravity = on; s.Save(); });
            UIFactory.ToggleRow(c, "No air drag", s.cheatNoDrag, on => { s.cheatNoDrag = on; s.Save(); });
            UIFactory.ToggleRow(c, "Unbreakable parts (no crashes)", s.cheatUnbreakable, on => { s.cheatUnbreakable = on; s.Save(); });
            UIFactory.ToggleRow(c, "Super thrust (5× engines)", s.cheatSuperThrust, on => { s.cheatSuperThrust = on; s.Save(); });
            UIFactory.ToggleRow(c, "No re-entry heat damage", s.cheatNoHeat, on => { s.cheatNoHeat = on; s.Save(); });
            Button startBtn = null;
            startBtn = UIFactory.Button(c, "Start: " + GameSettings.StartNames[Mathf.Clamp(s.cheatStart, 0, 3)], () =>
            {
                s.cheatStart = (s.cheatStart + 1) % GameSettings.StartNames.Length;
                s.Save();
                UIFactory.SetLabel(startBtn, "Start: " + GameSettings.StartNames[s.cheatStart]);
            });
            UIFactory.Size(startBtn, -1, 70);

            UIFactory.Header(c, "Credits");
            var cr = UIFactory.Label(c, "Planet maps: Solar System Scope (solarsystemscope.com), CC BY 4.0, based on NASA imagery and data.", 19, Theme.Muted);
            UIFactory.Size(cr, -1, 52);

            UIFactory.Header(c, "Language");
            var lang = UIFactory.Label(c, "English (more languages planned)", 24, Theme.Text);
            UIFactory.Size(lang, -1, 50);

            var row = UIFactory.Rect("Footer", body);
            UIFactory.Size(row, -1, 80);
            var h = UIFactory.Horizontal(row, 12, 0, true);
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleRight;
            var spacer = UIFactory.Rect("Spacer", row);
            UIFactory.Size(spacer, -1, -1, 1);
            UIFactory.Size(UIFactory.Button(row, "Done", onClose, ButtonStyle.Primary), 220, 72);
        }

        static string Pct(float v) { return Mathf.RoundToInt(v * 100f) + " %"; }
    }
}
