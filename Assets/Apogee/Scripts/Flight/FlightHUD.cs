using System;
using Apogee.Core;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apogee.Flight
{
    /// <summary>Button that reports whether it is currently held down (rotation controls).</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }
        Image img;
        Color baseColor;

        void Awake() { img = GetComponent<Image>(); if (img != null) baseColor = img.color; }
        public void OnPointerDown(PointerEventData e) { Held = true; if (img != null) img.color = Color.Lerp(baseColor, Theme.Accent, 0.5f); }
        public void OnPointerUp(PointerEventData e) { Release(); }
        public void OnPointerExit(PointerEventData e) { Release(); }
        void Release() { Held = false; if (img != null) img.color = baseColor; }
        void OnDisable() { Release(); }
    }

    /// <summary>
    /// Flight controls and telemetry, laid out like Spaceflight Simulator: menu and map top-left, time warp
    /// and telemetry top-right, engine on/off and throttle on the right, rotation at the bottom centre,
    /// staging and fuel at the bottom left.
    /// </summary>
    public class FlightHUD : MonoBehaviour
    {
        public HoldButton left, right;
        public Slider throttle;
        public Button engineButton, stageButton, mapButton, warpUp, warpDown, menuButton;
        public Text telemetry, throttleLabel, warpLabel, message, stageLabel, hint;
        public Image liquidFill, solidFill;
        public GameObject solidRow;
        public RectTransform apLabel, peLabel, root;
        public RectTransform controlsGroup;
        CanvasGroup messageGroup;
        float messageTime;

        public Action OnStage, OnToggleEngine, OnMap, OnWarpUp, OnWarpDown, OnMenu;
        public Action<float> OnThrottle;

        public void Init()
        {
            Canvas canvas;
            root = UIFactory.CreateCanvas("Flight HUD", 10, out canvas);

            // ---- top-left: menu + map
            menuButton = UIFactory.Button(root, "Menu", () => { if (OnMenu != null) OnMenu(); });
            UIFactory.Place((RectTransform)menuButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -18), new Vector2(150, 84));
            mapButton = UIFactory.Button(root, "Map", () => { if (OnMap != null) OnMap(); });
            UIFactory.Place((RectTransform)mapButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(182, -18), new Vector2(150, 84));

            // ---- top-right: warp + telemetry
            warpDown = UIFactory.Button(root, "<<", () => { if (OnWarpDown != null) OnWarpDown(); }, ButtonStyle.Normal, 34);
            UIFactory.Place((RectTransform)warpDown.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-196, -18), new Vector2(130, 84));
            warpUp = UIFactory.Button(root, ">>", () => { if (OnWarpUp != null) OnWarpUp(); }, ButtonStyle.Normal, 34);
            UIFactory.Place((RectTransform)warpUp.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(160, 84));
            warpLabel = UIFactory.Label(root, "1×", 26, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(warpLabel.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-340, -18), new Vector2(140, 84));
            telemetry = UIFactory.Label(root, "", 30, Theme.Text, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Place(telemetry.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -116), new Vector2(560, 420));
            var shadow = telemetry.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(2, -2);

            // ---- centre message
            message = UIFactory.Label(root, "", 40, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            HorizontalBand(message.rectTransform, 0.72f, 80f);
            message.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            messageGroup = message.gameObject.AddComponent<CanvasGroup>();
            messageGroup.alpha = 0f;
            hint = UIFactory.Label(root, "", 26, Theme.Text, TextAnchor.MiddleCenter);
            HorizontalBand(hint.rectTransform, 0.62f, 60f);
            hint.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);

            controlsGroup = UIFactory.Rect("Controls", root);
            UIFactory.Stretch(controlsGroup);

            // ---- right edge: engine toggle, throttle %, vertical throttle slider
            engineButton = UIFactory.Button(controlsGroup, "Off", () => { if (OnToggleEngine != null) OnToggleEngine(); }, ButtonStyle.Normal, 30);
            UIFactory.Place((RectTransform)engineButton.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 640), new Vector2(130, 80));
            throttleLabel = UIFactory.Label(controlsGroup, "100%", 28, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            var tlBg = UIFactory.Panel(controlsGroup, Theme.ButtonBg, true, "ThrottleLabelBg");
            tlBg.sprite = UIFactory.RoundedSmall;
            UIFactory.Place(tlBg.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 548), new Vector2(130, 74));
            throttleLabel.transform.SetParent(tlBg.transform, false);
            UIFactory.Stretch(throttleLabel.rectTransform);
            throttle = VerticalSlider(controlsGroup);

            // ---- bottom centre: rotation
            left = Hold(controlsGroup, "◀", new Vector2(-110, 30));
            right = Hold(controlsGroup, "▶", new Vector2(110, 30));

            // ---- bottom left: staging and fuel
            stageButton = UIFactory.Button(controlsGroup, "STAGE", () => { if (OnStage != null) OnStage(); }, ButtonStyle.Primary, 34);
            UIFactory.Place((RectTransform)stageButton.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 150), new Vector2(260, 110));
            stageLabel = UIFactory.Label(controlsGroup, "", 22, Theme.Text, TextAnchor.LowerLeft);
            UIFactory.Place(stageLabel.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(26, 266), new Vector2(520, 40));
            stageLabel.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            liquidFill = FuelBar(controlsGroup, "Liquid fuel", new Vector2(24, 86), new Color(0.92f, 0.94f, 0.97f));
            solidFill = FuelBar(controlsGroup, "Solid fuel", new Vector2(24, 26), new Color(1f, 0.72f, 0.35f));
            solidRow = solidFill.transform.parent.parent.gameObject;

            // ---- map labels
            apLabel = MapLabel("Ap");
            peLabel = MapLabel("Pe");
        }

        /// <summary>Full-width text band (with side margins) so messages fit in portrait as well as landscape.</summary>
        static void HorizontalBand(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(40f, -height * 0.5f);
            rt.offsetMax = new Vector2(-40f, height * 0.5f);
        }

        RectTransform MapLabel(string title)
        {
            var t = UIFactory.Label(root, title, 22, Theme.Text, TextAnchor.LowerCenter, FontStyle.Bold);
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.7f);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(300, 70);
            t.gameObject.SetActive(false);
            return rt;
        }

        HoldButton Hold(RectTransform parent, string label, Vector2 pos)
        {
            var img = UIFactory.Panel(parent, Theme.ButtonBg, true, "Rotate" + label);
            img.sprite = UIFactory.RoundedSmall;
            UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), pos, new Vector2(190, 130));
            var t = UIFactory.Label(img.transform, label, 56, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Stretch(t.rectTransform);
            return img.gameObject.AddComponent<HoldButton>();
        }

        Image FuelBar(RectTransform parent, string title, Vector2 pos, Color color)
        {
            var row = UIFactory.Rect(title, parent);
            UIFactory.Place(row, new Vector2(0, 0), new Vector2(0, 0), pos, new Vector2(300, 56));
            var t = UIFactory.Label(row, title + ":", 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Stretch(t.rectTransform);
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            var bg = UIFactory.Panel(row, new Color(0, 0, 0, 0.35f), true, "Track");
            bg.sprite = UIFactory.RoundedSmall;
            bg.rectTransform.anchorMin = new Vector2(0, 0);
            bg.rectTransform.anchorMax = new Vector2(1, 0);
            bg.rectTransform.pivot = new Vector2(0, 0);
            bg.rectTransform.sizeDelta = new Vector2(0, 20);
            bg.rectTransform.anchoredPosition = Vector2.zero;
            var fill = UIFactory.Panel(bg.transform, color, true, "Fill");
            fill.sprite = UIFactory.RoundedSmall;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(1, 1);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            return fill;
        }

        Slider VerticalSlider(RectTransform parent)
        {
            var sRoot = UIFactory.Rect("Throttle", parent);
            UIFactory.Place(sRoot, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-54, 40), new Vector2(76, 490));
            var slider = sRoot.gameObject.AddComponent<Slider>();
            var bg = UIFactory.Panel(sRoot, new Color(0.12f, 0.17f, 0.23f, 0.85f), true, "Background");
            bg.sprite = UIFactory.RoundedSmall;
            UIFactory.Stretch(bg.rectTransform);
            var fillArea = UIFactory.Rect("Fill Area", sRoot);
            UIFactory.Stretch(fillArea, 8, 8, 8, 8);
            var fill = UIFactory.Panel(fillArea, new Color(0.9f, 0.93f, 0.97f, 0.95f), true, "Fill");
            fill.sprite = UIFactory.RoundedSmall;
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = UIFactory.Rect("Handle Slide Area", sRoot);
            UIFactory.Stretch(handleArea, 0, 10, 0, 10);
            var handle = UIFactory.Panel(handleArea, new Color(1, 1, 1, 0.0f), false, "Handle");
            handle.rectTransform.sizeDelta = new Vector2(0, 30);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = bg;
            slider.direction = Slider.Direction.BottomToTop;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            var nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
            slider.value = 1f;
            slider.onValueChanged.AddListener(v => { if (OnThrottle != null) OnThrottle(v); });
            return slider;
        }

        public void Message(string text, float seconds = 2.2f)
        {
            message.text = text;
            messageTime = seconds;
            messageGroup.alpha = 1f;
        }

        void Update()
        {
            if (messageTime > 0f)
            {
                messageTime -= Time.unscaledDeltaTime;
                messageGroup.alpha = Mathf.Clamp01(messageTime / 0.6f);
            }
        }

        public static string FormatDistance(double m)
        {
            if (double.IsInfinity(m) || double.IsNaN(m)) return "Escape";
            if (Math.Abs(m) < 10000) return m.ToString("0.0") + "m";
            if (Math.Abs(m) < 1e7) return (m / 1000.0).ToString("N1") + "km";
            return (m / 1000.0).ToString("N0") + "km";
        }

        public static string FormatTime(double s)
        {
            int t = (int)s;
            if (t >= 3600) return (t / 3600) + "h " + ((t / 60) % 60).ToString("00") + "m";
            return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        }
    }
}
