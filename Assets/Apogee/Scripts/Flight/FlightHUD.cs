using System;
using System.Collections.Generic;
using Apogee.Core;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apogee.Flight
{
    /// <summary>Button that reports whether it is currently held down.</summary>
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

    /// <summary>On-screen stick for pitch and yaw. Springs back to centre when released.</summary>
    public class VirtualStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob;
        public Vector2 Value { get; private set; }
        RectTransform rt;
        float radius;

        void Awake() { rt = (RectTransform)transform; }
        public void OnPointerDown(PointerEventData e) { OnDrag(e); }
        public void OnDrag(PointerEventData e)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
            radius = rt.rect.width * 0.5f;
            var v = Vector2.ClampMagnitude(local, radius);
            knob.anchoredPosition = v;
            Value = v / radius;
        }
        public void OnPointerUp(PointerEventData e) { Value = Vector2.zero; knob.anchoredPosition = Vector2.zero; }
        void OnDisable() { Value = Vector2.zero; if (knob != null) knob.anchoredPosition = Vector2.zero; }
    }

    /// <summary>
    /// Flight controls: menu/map/camera/inside top-left; warp and telemetry top-right; SAS modes down the
    /// left; joystick and roll bottom-left; throttle, engines and STAGE bottom-right; RCS pad (6 directions)
    /// at the bottom centre; rollout/countdown panel; cockpit window frame.
    /// </summary>
    public class FlightHUD : MonoBehaviour
    {
        public RectTransform root, controls, rcsPad, rolloutPanel, cockpitFrame, mapLabels;
        public VirtualStick stick;
        public HoldButton rollLeft, rollRight, rcsUp, rcsDown, rcsLeft, rcsRight, rcsFwd, rcsBack, drive;
        public Slider throttle;
        public Button engineButton, stageButton, mapButton, camButton, insideButton, rcsButton, menuButton, warpUp, warpDown, targetButton;
        public Button countdownButton, skipButton;
        public Text telemetry, navInfo, warpLabel, message, hint, stageLabel, rolloutText, countdownText, cheatBadge, cockpitInstruments;
        public Image liquidFill, solidFill, rcsFill;
        public GameObject solidRow;
        public readonly Dictionary<SasMode, Button> sasButtons = new Dictionary<SasMode, Button>();
        CanvasGroup messageGroup;
        float messageTime;

        public Action OnStage, OnToggleEngine, OnMap, OnCamera, OnInside, OnRcs, OnMenu, OnWarpUp, OnWarpDown, OnCountdown, OnSkipRollout, OnTarget;
        public Action<float> OnThrottle;
        public Action<SasMode> OnSas;

        public void Init()
        {
            Canvas canvas;
            root = UIFactory.CreateCanvas("Flight HUD", 10, out canvas);

            // cockpit window frame (behind the controls)
            cockpitFrame = UIFactory.Rect("Cockpit", root);
            UIFactory.Stretch(cockpitFrame);
            var frameCol = new Color(0.07f, 0.08f, 0.09f, 0.97f);
            Bar(cockpitFrame, new Vector2(0, 0), new Vector2(1, 0.2f), frameCol);
            Bar(cockpitFrame, new Vector2(0, 0.86f), new Vector2(1, 1), frameCol);
            Bar(cockpitFrame, new Vector2(0, 0), new Vector2(0.08f, 1), frameCol);
            Bar(cockpitFrame, new Vector2(0.92f, 0), new Vector2(1, 1), frameCol);
            Bar(cockpitFrame, new Vector2(0.495f, 0.2f), new Vector2(0.505f, 0.86f), frameCol);   // window mullion
            cockpitInstruments = UIFactory.Label(cockpitFrame, "", 22, new Color(0.45f, 1f, 0.6f), TextAnchor.MiddleCenter, FontStyle.Bold);
            cockpitInstruments.rectTransform.anchorMin = new Vector2(0.2f, 0.02f);
            cockpitInstruments.rectTransform.anchorMax = new Vector2(0.8f, 0.18f);
            cockpitInstruments.rectTransform.offsetMin = cockpitInstruments.rectTransform.offsetMax = Vector2.zero;
            cockpitFrame.gameObject.SetActive(false);

            mapLabels = UIFactory.Rect("Map Labels", root);
            UIFactory.Stretch(mapLabels);

            // ---- top-left
            menuButton = Btn("Menu", () => Call(OnMenu), new Vector2(0, 1), new Vector2(20, -18), new Vector2(140, 76));
            mapButton = Btn("Map", () => Call(OnMap), new Vector2(0, 1), new Vector2(170, -18), new Vector2(140, 76));
            camButton = Btn("Chase", () => Call(OnCamera), new Vector2(0, 1), new Vector2(20, -104), new Vector2(140, 66), 22);
            insideButton = Btn("Inside", () => Call(OnInside), new Vector2(0, 1), new Vector2(170, -104), new Vector2(140, 66), 22);
            targetButton = Btn("Target", () => Call(OnTarget), new Vector2(0, 1), new Vector2(320, -18), new Vector2(150, 76));
            targetButton.gameObject.SetActive(false);
            cheatBadge = UIFactory.Label(root, "CHEATS ON", 20, Theme.Bad, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(cheatBadge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -178), new Vector2(300, 30));
            cheatBadge.gameObject.SetActive(false);

            // ---- top-right: warp + telemetry
            warpDown = Btn("<<", () => Call(OnWarpDown), new Vector2(1, 1), new Vector2(-190, -18), new Vector2(110, 76), 30);
            warpUp = Btn(">>", () => Call(OnWarpUp), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(110, 76), 30);
            warpLabel = UIFactory.Label(root, "1×", 26, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(warpLabel.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-132, -18), new Vector2(60, 76));
            var tp = UIFactory.Panel(root, new Color(0f, 0f, 0f, 0.38f), true, "Telemetry");
            UIFactory.Place(tp.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -104), new Vector2(380, 380));
            telemetry = UIFactory.Label(tp.transform, "", 21, Theme.Text, TextAnchor.UpperLeft);
            UIFactory.Stretch(telemetry.rectTransform, 14, 10, 10, 10);
            telemetry.supportRichText = true;
            navInfo = UIFactory.Label(root, "", 20, Theme.Accent, TextAnchor.UpperRight);
            UIFactory.Place(navInfo.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -490), new Vector2(560, 120));
            navInfo.supportRichText = true;

            // ---- centre messages
            message = UIFactory.Label(root, "", 32, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Band(message.rectTransform, 0.74f, 90f);
            message.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            messageGroup = message.gameObject.AddComponent<CanvasGroup>();
            messageGroup.alpha = 0f;
            messageGroup.blocksRaycasts = false;
            hint = UIFactory.Label(root, "", 24, Theme.Accent, TextAnchor.MiddleCenter);
            Band(hint.rectTransform, 0.66f, 60f);
            hint.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);

            controls = UIFactory.Rect("Controls", root);
            UIFactory.Stretch(controls);

            // ---- SAS column, left middle
            var sas = UIFactory.Rect("SAS", controls);
            sas.anchorMin = sas.anchorMax = new Vector2(0f, 0.5f);
            sas.pivot = new Vector2(0f, 0.5f);
            sas.anchoredPosition = new Vector2(16f, 110f);
            sas.sizeDelta = new Vector2(120f, 520f);
            var v = UIFactory.Vertical(sas, 6, 0, true);
            v.childForceExpandHeight = true;
            var modes = new[] { SasMode.Stability, SasMode.Prograde, SasMode.Retrograde, SasMode.RadialOut, SasMode.RadialIn, SasMode.Normal, SasMode.AntiNormal, SasMode.Target };
            var names = new[] { "SAS", "Pro", "Retro", "Rad+", "Rad−", "Nrm", "Anti", "Tgt" };
            for (int i = 0; i < modes.Length; i++)
            {
                var m = modes[i];
                var b = UIFactory.Button(sas, names[i], () => { if (OnSas != null) OnSas(m); }, ButtonStyle.Normal, 21);
                sasButtons[m] = b;
            }

            // ---- joystick + roll (bottom-left)
            var stickRt = UIFactory.Panel(controls, new Color(0.1f, 0.15f, 0.2f, 0.55f), true, "Stick");
            stickRt.sprite = UIFactory.RoundedSmall;
            UIFactory.Place(stickRt.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(280, 280));
            var knob = UIFactory.Panel(stickRt.transform, new Color(0.85f, 0.92f, 1f, 0.85f), true, "Knob");
            knob.sprite = UIFactory.RoundedSmall;
            UIFactory.Place(knob.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96));
            knob.raycastTarget = false;
            stick = stickRt.gameObject.AddComponent<VirtualStick>();
            stick.knob = knob.rectTransform;
            var lbl = UIFactory.Label(stickRt.transform, "PITCH / YAW", 16, Theme.Muted, TextAnchor.UpperCenter);
            UIFactory.Stretch(lbl.rectTransform, 0, 0, 0, 6);
            lbl.raycastTarget = false;
            rollLeft = Hold(controls, "⟲", new Vector2(0, 0), new Vector2(334, 186), new Vector2(110, 84), 40);
            rollRight = Hold(controls, "⟳", new Vector2(0, 0), new Vector2(334, 90), new Vector2(110, 84), 40);

            // ---- throttle, engines, stage, fuel (bottom-right)
            throttle = VerticalSlider(controls);
            engineButton = Btn("Engines\nON", () => Call(OnToggleEngine), new Vector2(1, 0), new Vector2(-150, 360), new Vector2(150, 96), 22, controls);
            stageButton = Btn("STAGE", () => Call(OnStage), new Vector2(1, 0), new Vector2(-150, 40), new Vector2(240, 120), 34, controls);
            UIFactory.SetButtonColor(stageButton, Theme.ButtonPrimary);
            stageLabel = UIFactory.Label(controls, "", 20, Theme.Muted, TextAnchor.LowerRight);
            UIFactory.Place(stageLabel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-150, 168), new Vector2(420, 34));
            liquidFill = FuelBar(controls, "Liquid", 210, new Color(1f, 0.62f, 0.2f));
            var solid = FuelBar(controls, "Solid", 245, new Color(0.95f, 0.85f, 0.5f));
            solidFill = solid;
            solidRow = solid.transform.parent.gameObject;
            rcsFill = FuelBar(controls, "Mono", 280, new Color(0.7f, 0.85f, 1f));

            // ---- RCS (bottom centre)
            rcsButton = Btn("RCS", () => Call(OnRcs), new Vector2(0, 0), new Vector2(334, 282), new Vector2(110, 70), 22, controls);
            rcsPad = UIFactory.Rect("RcsPad", controls);
            rcsPad.anchorMin = rcsPad.anchorMax = new Vector2(0.5f, 0f);
            rcsPad.pivot = new Vector2(0.5f, 0f);
            rcsPad.sizeDelta = new Vector2(360, 230);
            rcsPad.anchoredPosition = new Vector2(30, 30);
            rcsUp = Hold(rcsPad, "▲", new Vector2(0.5f, 0), new Vector2(0, 160), new Vector2(90, 64), 28);
            rcsDown = Hold(rcsPad, "▼", new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(90, 64), 28);
            rcsLeft = Hold(rcsPad, "◀", new Vector2(0.5f, 0), new Vector2(-105, 80), new Vector2(90, 64), 28);
            rcsRight = Hold(rcsPad, "▶", new Vector2(0.5f, 0), new Vector2(105, 80), new Vector2(90, 64), 28);
            rcsFwd = Hold(rcsPad, "FWD", new Vector2(0.5f, 0), new Vector2(-120, 160), new Vector2(84, 54), 18);
            rcsBack = Hold(rcsPad, "BACK", new Vector2(0.5f, 0), new Vector2(120, 160), new Vector2(84, 54), 18);
            var rl = UIFactory.Label(rcsPad, "RCS", 18, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(rl.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 92), new Vector2(90, 30));
            rcsPad.gameObject.SetActive(false);

            // ---- rollout / countdown panel (bottom centre, before launch)
            rolloutPanel = UIFactory.Rect("Rollout", root);
            rolloutPanel.anchorMin = rolloutPanel.anchorMax = new Vector2(0.5f, 0f);
            rolloutPanel.pivot = new Vector2(0.5f, 0f);
            rolloutPanel.sizeDelta = new Vector2(620, 330);
            rolloutPanel.anchoredPosition = new Vector2(0, 30);
            var rp = UIFactory.Panel(rolloutPanel, new Color(0f, 0f, 0f, 0.55f), true, "Bg");
            UIFactory.Stretch(rp.rectTransform);
            rolloutText = UIFactory.Label(rolloutPanel, "", 22, Theme.Text, TextAnchor.UpperCenter);
            UIFactory.Stretch(rolloutText.rectTransform, 16, 160, 16, 12);
            drive = Hold(rolloutPanel, "HOLD TO DRIVE ▶", new Vector2(0.5f, 0), new Vector2(-110, 20), new Vector2(360, 120), 28);
            skipButton = Btn("Skip ⏩", () => Call(OnSkipRollout), new Vector2(0.5f, 0), new Vector2(200, 20), new Vector2(170, 120), 26, rolloutPanel, true);
            countdownButton = Btn("START COUNTDOWN", () => Call(OnCountdown), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(520, 120), 30, rolloutPanel, true);
            UIFactory.SetButtonColor(countdownButton, Theme.ButtonPrimary);
            countdownText = UIFactory.Label(root, "", 120, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Band(countdownText.rectTransform, 0.5f, 180f);
            countdownText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            countdownText.raycastTarget = false;
        }

        static void Call(Action a) { if (a != null) a(); }

        void Bar(RectTransform parent, Vector2 min, Vector2 max, Color c)
        {
            var img = UIFactory.Panel(parent, c, false, "Frame");
            img.rectTransform.anchorMin = min;
            img.rectTransform.anchorMax = max;
            img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
            img.raycastTarget = false;
        }

        static void Band(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(40f, -height * 0.5f);
            rt.offsetMax = new Vector2(-40f, height * 0.5f);
        }

        Button Btn(string label, Action a, Vector2 anchor, Vector2 pos, Vector2 size, int font = 26, RectTransform parent = null, bool centrePivot = false)
        {
            var b = UIFactory.Button(parent ?? root, label, a, ButtonStyle.Normal, font);
            var pivot = centrePivot ? new Vector2(0.5f, 0f) : anchor;
            UIFactory.Place((RectTransform)b.transform, anchor, pivot, pos, size);
            return b;
        }

        HoldButton Hold(RectTransform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, int font)
        {
            var img = UIFactory.Panel(parent, Theme.ButtonBg, true, "Hold " + label);
            img.sprite = UIFactory.RoundedSmall;
            UIFactory.Place(img.rectTransform, anchor, anchor.x == 0.5f ? new Vector2(0.5f, 0f) : anchor, pos, size);
            var t = UIFactory.Label(img.transform, label, font, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(t.rectTransform);
            t.raycastTarget = false;
            return img.gameObject.AddComponent<HoldButton>();
        }

        Image FuelBar(RectTransform parent, string name, float y, Color c)
        {
            var row = UIFactory.Rect(name + "Row", parent);
            UIFactory.Place(row, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-150, y), new Vector2(240, 26));
            var l = UIFactory.Label(row, name, 16, Theme.Muted, TextAnchor.MiddleLeft);
            UIFactory.Place(l.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(60, 26));
            var bg = UIFactory.Panel(row, new Color(1f, 1f, 1f, 0.1f), true, "Bg");
            bg.sprite = UIFactory.RoundedSmall;
            bg.rectTransform.anchorMin = new Vector2(0, 0);
            bg.rectTransform.anchorMax = new Vector2(1, 1);
            bg.rectTransform.offsetMin = new Vector2(64, 4);
            bg.rectTransform.offsetMax = new Vector2(0, -4);
            var fill = UIFactory.Panel(bg.transform, c, true, "Fill");
            fill.sprite = UIFactory.RoundedSmall;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(1, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return fill;
        }

        public static void SetFill(Image fill, float f)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(f), 1f);
        }

        Slider VerticalSlider(RectTransform parent)
        {
            var sRoot = UIFactory.Rect("Throttle", parent);
            UIFactory.Place(sRoot, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 40), new Vector2(96, 420));
            var slider = sRoot.gameObject.AddComponent<Slider>();
            var bg = UIFactory.Panel(sRoot, new Color(0.12f, 0.17f, 0.23f, 0.85f), true, "Background");
            bg.sprite = UIFactory.RoundedSmall;
            UIFactory.Stretch(bg.rectTransform);
            var fillArea = UIFactory.Rect("Fill Area", sRoot);
            UIFactory.Stretch(fillArea, 8, 8, 8, 8);
            var fill = UIFactory.Panel(fillArea, new Color(1f, 0.62f, 0.2f, 0.95f), true, "Fill");
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
            slider.onValueChanged.AddListener(x => { if (OnThrottle != null) OnThrottle(x); });
            var t = UIFactory.Label(sRoot, "THR", 16, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Stretch(t.rectTransform, 0, 0, 0, 6);
            t.raycastTarget = false;
            return slider;
        }

        public void Message(string text, float seconds = 2.4f)
        {
            message.text = text;
            messageTime = seconds;
            messageGroup.alpha = 1f;
        }

        void Update()
        {
            // keep the RCS pad clear of the stage button on narrow (portrait) screens
            if (rcsPad != null) rcsPad.anchoredPosition = UIFactory.Portrait ? new Vector2(0f, 440f) : new Vector2(30f, 30f);
            if (messageTime > 0f)
            {
                messageTime -= Time.unscaledDeltaTime;
                messageGroup.alpha = Mathf.Clamp01(messageTime / 0.6f);
            }
        }

        public Text MakeMapLabel(string text, Color c)
        {
            var t = UIFactory.Label(mapLabels, text, 20, c, TextAnchor.LowerCenter, FontStyle.Bold);
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
            t.rectTransform.pivot = new Vector2(0.5f, 0f);
            t.rectTransform.sizeDelta = new Vector2(260, 60);
            return t;
        }

        public static string Dist(double m)
        {
            if (double.IsInfinity(m) || double.IsNaN(m)) return "escape";
            double a = Math.Abs(m);
            if (a < 10000) return m.ToString("N0") + " m";
            if (a < 1e7) return (m / 1000.0).ToString("N1") + " km";
            if (a < 1e10) return (m / 1000.0).ToString("N0") + " km";
            return (m / 1.495978707e11).ToString("0.00") + " AU";
        }

        public static string FormatTime(double s)
        {
            if (double.IsInfinity(s) || double.IsNaN(s) || s < 0) return "—";
            long t = (long)s;
            if (t >= 86400) return (t / 86400) + "d " + ((t / 3600) % 24) + "h";
            if (t >= 3600) return (t / 3600) + "h " + ((t / 60) % 60).ToString("00") + "m";
            if (t >= 60) return (t / 60) + "m " + (t % 60).ToString("00") + "s";
            return t + "s";
        }
    }
}
