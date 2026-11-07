using System.Collections;
using Apogee.Rocket;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Apogee.Core
{
    public enum BuilderEntry { Continue, NewDesign, OpenBlueprint }

    /// <summary>Data handed from one scene to the next.</summary>
    public static class Session
    {
        public static BuilderEntry builderEntry = BuilderEntry.Continue;
        public static RocketBlueprint blueprintToOpen;
        public static string blueprintPath;
        public static RocketBlueprint flightBlueprint;
    }

    public static class Scenes
    {
        public const string MainMenu = "MainMenu";
        public const string Builder = "Builder";
        public const string Flight = "Flight";
    }

    /// <summary>
    /// Persistent application root, created automatically before the first scene loads. Owns settings,
    /// audio, dialogs, scene transitions (real async loading with progress) and the FPS overlay.
    /// </summary>
    public class AppRoot : MonoBehaviour
    {
        public static AppRoot Instance { get; private set; }

        /// <summary>Raised when the screen switches between portrait and landscape (phone rotated, window resized).</summary>
        public static event System.Action OrientationChanged;
        bool lastPortrait;

        public GameSettings Settings { get; private set; }
        public AudioManager Audio { get; private set; }
        public Dialogs Dialogs { get; private set; }

        CanvasGroup fade;
        Image progressFill;
        Text fpsText;
        float fpsAccum;
        int fpsFrames;
        bool loading;
        int backFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("AppRoot");
            go.AddComponent<AppRoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Input.simulateMouseWithTouches = false; // we read touches directly; avoids double input
            Input.multiTouchEnabled = true;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            lastPortrait = UIFactory.Portrait;
            Settings = GameSettings.Load();
            Settings.Apply();
            Settings.Changed += () => { Settings.Apply(); if (fpsText != null) fpsText.gameObject.SetActive(Settings.showFps); };

            Audio = gameObject.AddComponent<AudioManager>();
            Audio.Init(Settings);

            Canvas canvas;
            var overlay = UIFactory.CreateCanvas("Overlay", 1000, out canvas);
            canvas.transform.SetParent(transform, false);
            if (EventSystem.current != null) EventSystem.current.transform.SetParent(transform, false); // survives scene loads
            Dialogs = gameObject.AddComponent<Dialogs>();
            Dialogs.Init(overlay);

            var fadeImg = UIFactory.Panel(canvas.transform, Color.black, false, "Fade");
            UIFactory.Stretch(fadeImg.rectTransform);
            fade = fadeImg.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.blocksRaycasts = false;
            var bar = UIFactory.Panel(fadeImg.transform, new Color(1, 1, 1, 0.08f), true, "ProgressTrack");
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 8));
            progressFill = UIFactory.Panel(bar.transform, Theme.Accent, true, "Progress");
            progressFill.rectTransform.anchorMin = Vector2.zero;
            progressFill.rectTransform.anchorMax = new Vector2(0, 1);
            progressFill.rectTransform.offsetMin = Vector2.zero;
            progressFill.rectTransform.offsetMax = Vector2.zero;

            fpsText = UIFactory.Label(overlay, "", 22, Theme.Good, TextAnchor.UpperRight);
            UIFactory.Place(fpsText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -8), new Vector2(240, 40));
            fpsText.gameObject.SetActive(Settings.showFps);
        }

        void Update()
        {
            bool portrait = UIFactory.Portrait;
            if (portrait != lastPortrait)
            {
                lastPortrait = portrait;
                if (OrientationChanged != null) OrientationChanged();
            }
            fpsAccum += Time.unscaledDeltaTime;
            fpsFrames++;
            if (fpsAccum >= 0.5f)
            {
                if (fpsText != null && fpsText.gameObject.activeSelf)
                    fpsText.text = (fpsFrames / fpsAccum).ToString("0") + " FPS";
                fpsAccum = 0f;
                fpsFrames = 0;
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) PlayerPrefs.Save();
        }

        /// <summary>
        /// True once per Android back press that wasn't used to close a dialog. Scenes call this each frame.
        /// </summary>
        public bool BackPressed()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || backFrame == Time.frameCount || loading) return false;
            backFrame = Time.frameCount;
            if (Dialogs.CloseTop()) return false;
            return true;
        }

        public void LoadScene(string scene)
        {
            if (loading) return;
            StartCoroutine(LoadRoutine(scene));
        }

        IEnumerator LoadRoutine(string scene)
        {
            loading = true;
            fade.blocksRaycasts = true;
            progressFill.rectTransform.anchorMax = new Vector2(0, 1);
            Audio.Play(Sfx.Whoosh, 0.7f);
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.25f) { fade.alpha = t; yield return null; }
            fade.alpha = 1f;
            var op = SceneManager.LoadSceneAsync(scene);
            while (!op.isDone)
            {
                progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(op.progress / 0.9f), 1);
                yield return null;
            }
            progressFill.rectTransform.anchorMax = new Vector2(1, 1);
            yield return null; // let the new scene build its first frame
            for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.35f) { fade.alpha = t; yield return null; }
            fade.alpha = 0f;
            fade.blocksRaycasts = false;
            loading = false;
        }
    }
}
