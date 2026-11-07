using System;
using Apogee.Core;
using Apogee.Rendering;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apogee.UI
{
    public static class Theme
    {
        public static readonly Color Panel = new Color(0.035f, 0.05f, 0.075f, 0.88f);
        public static readonly Color PanelSolid = new Color(0.05f, 0.07f, 0.1f, 0.97f);
        public static readonly Color PanelLight = new Color(0.10f, 0.14f, 0.19f, 0.92f);
        public static readonly Color Stroke = new Color(0.35f, 0.75f, 1f, 0.25f);
        public static readonly Color Accent = new Color(0.30f, 0.80f, 1f, 1f);
        public static readonly Color AccentDim = new Color(0.12f, 0.35f, 0.48f, 1f);
        public static readonly Color Text = new Color(0.92f, 0.95f, 0.98f, 1f);
        public static readonly Color Muted = new Color(0.58f, 0.65f, 0.73f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.9f, 0.55f, 1f);
        public static readonly Color Warn = new Color(1f, 0.75f, 0.25f, 1f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.32f, 1f);
        public static readonly Color ButtonBg = new Color(0.12f, 0.17f, 0.23f, 0.95f);
        public static readonly Color ButtonPrimary = new Color(0.16f, 0.55f, 0.78f, 1f);
        public static readonly Color ButtonDanger = new Color(0.55f, 0.16f, 0.14f, 1f);
    }

    public enum ButtonStyle { Normal, Primary, Danger, Ghost, Tab }

    /// <summary>Plays the click sound and gives a small press animation.</summary>
    public class ButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        Vector3 baseScale = Vector3.one;
        float target = 1f;
        Selectable sel;

        void Awake() { baseScale = transform.localScale; sel = GetComponent<Selectable>(); }

        public void OnPointerDown(PointerEventData e) { if (sel == null || sel.interactable) target = 0.95f; }
        public void OnPointerUp(PointerEventData e) { target = 1f; }
        public void OnPointerExit(PointerEventData e) { target = 1f; }

        void Update()
        {
            float s = Mathf.MoveTowards(transform.localScale.x / Mathf.Max(0.0001f, baseScale.x), target, Time.unscaledDeltaTime * 2.5f);
            transform.localScale = baseScale * s;
        }
    }

    /// <summary>Switches the canvas reference resolution between portrait (1080×1920) and landscape (1920×1080).</summary>
    public class AdaptiveScaler : MonoBehaviour
    {
        CanvasScaler scaler;
        bool? last;

        void Awake() { scaler = GetComponent<CanvasScaler>(); Apply(); }
        void Update() { Apply(); }

        void Apply()
        {
            bool portrait = UIFactory.Portrait;
            if (last.HasValue && last.Value == portrait) return;
            last = portrait;
            scaler.referenceResolution = portrait ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = portrait ? 0.15f : 0.6f;
        }
    }

    /// <summary>Keeps UI inside the device safe area (notches, rounded corners).</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect last;
        int lastW, lastH;
        RectTransform rt;

        void Awake() { rt = (RectTransform)transform; Apply(); }
        void Update() { if (Screen.safeArea != last || Screen.width != lastW || Screen.height != lastH) Apply(); }

        void Apply()
        {
            last = Screen.safeArea;
            lastW = Screen.width;
            lastH = Screen.height;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var min = new Vector2(last.xMin / Screen.width, last.yMin / Screen.height);
            var max = new Vector2(last.xMax / Screen.width, last.yMax / Screen.height);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Builds uGUI widgets from code with a consistent premium look.</summary>
    public static class UIFactory
    {
        static Font font;

        /// <summary>True when the screen is taller than it is wide.</summary>
        public static bool Portrait { get { return Screen.height > Screen.width; } }

        /// <summary>Usable width of a full-screen canvas in reference units.</summary>
        public static float CanvasWidth { get { return Portrait ? 1080f : 1920f; } }
        static Sprite rounded, roundedSmall, glow;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Sprite Rounded
        {
            get
            {
                if (rounded == null)
                {
                    var t = ProceduralTextures.RoundedRect(64, 22);
                    rounded = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
                }
                return rounded;
            }
        }

        public static Sprite RoundedSmall
        {
            get
            {
                if (roundedSmall == null)
                {
                    var t = ProceduralTextures.RoundedRect(32, 8);
                    roundedSmall = Sprite.Create(t, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
                }
                return roundedSmall;
            }
        }

        public static Sprite Glow
        {
            get
            {
                if (glow == null)
                {
                    var t = ProceduralTextures.Glow(64);
                    glow = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
                }
                return glow;
            }
        }

        // ------------------------------------------------------------------ roots

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        /// <summary>Creates a scaled overlay canvas and returns its safe-area root.</summary>
        public static RectTransform CreateCanvas(string name, int sortOrder, out Canvas canvas)
        {
            EnsureEventSystem();
            var go = new GameObject(name);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.6f;
            go.AddComponent<AdaptiveScaler>();
            go.AddComponent<GraphicRaycaster>();
            var safe = Rect("SafeArea", go.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            return safe;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Anchors a rect at a point of its parent (0..1) with a fixed size.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static LayoutElement Size(Component c, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (prefW >= 0) { le.preferredWidth = prefW; le.minWidth = prefW; }
            if (prefH >= 0) { le.preferredHeight = prefH; le.minHeight = prefH; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup Vertical(Component c, float spacing = 8, int pad = 0, bool expandWidth = true)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup Horizontal(Component c, float spacing = 8, int pad = 0, bool expandHeight = true)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = expandHeight;
            return h;
        }

        public static void FitHeight(Component c)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // ------------------------------------------------------------------ widgets

        public static Image Panel(Transform parent, Color color, bool roundedCorners = true, string name = "Panel")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (roundedCorners) { img.sprite = Rounded; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f; }
            return img;
        }

        /// <summary>Panel with a faint accent outline, used for the main HUD blocks.</summary>
        public static Image Card(Transform parent, string name = "Card")
        {
            var outline = Panel(parent, Theme.Stroke, true, name);
            var inner = Panel(outline.transform, Theme.Panel, true, "Fill");
            Stretch(inner.rectTransform, 2, 2, 2, 2);
            inner.raycastTarget = false;
            return outline;
        }

        public static Text Label(Transform parent, string text, int size = 26, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, ButtonStyle style = ButtonStyle.Normal, int fontSize = 26)
        {
            Color bg = style == ButtonStyle.Primary ? Theme.ButtonPrimary : style == ButtonStyle.Danger ? Theme.ButtonDanger :
                       style == ButtonStyle.Ghost ? new Color(1f, 1f, 1f, 0.04f) : style == ButtonStyle.Tab ? new Color(0.1f, 0.14f, 0.19f, 0.9f) : Theme.ButtonBg;
            var img = Panel(parent, Color.white, true, "Button_" + label);
            img.sprite = RoundedSmall;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = bg;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(bg, Theme.Accent, 0.35f);
            colors.selectedColor = bg;
            colors.disabledColor = new Color(bg.r * 0.6f, bg.g * 0.6f, bg.b * 0.6f, 0.45f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.targetGraphic = img;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            var t = Label(img.transform, label, fontSize, Theme.Text, TextAnchor.MiddleCenter, style == ButtonStyle.Primary ? FontStyle.Bold : FontStyle.Normal);
            Stretch(t.rectTransform, 10, 4, 10, 4);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(12, fontSize - 10);
            t.resizeTextMaxSize = fontSize;
            if (onClick != null)
                b.onClick.AddListener(() =>
                {
                    if (AppRoot.Instance != null) AppRoot.Instance.Audio.Play(Sfx.Click);
                    onClick();
                });
            img.gameObject.AddComponent<ButtonFeedback>();
            return b;
        }

        public static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static void SetButtonColor(Button b, Color bg)
        {
            var colors = b.colors;
            colors.normalColor = bg;
            colors.selectedColor = bg;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.12f);
            b.colors = colors;
        }

        public static ScrollRect ScrollList(Transform parent, out RectTransform content, float spacing = 8, int pad = 8, bool horizontal = false)
        {
            var root = Rect("Scroll", parent);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.001f);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", viewport);
            if (horizontal)
            {
                content.anchorMin = new Vector2(0, 0);
                content.anchorMax = new Vector2(0, 1);
                content.pivot = new Vector2(0, 0.5f);
                content.sizeDelta = Vector2.zero;
                var h = Horizontal(content, spacing, pad, true);
                h.childForceExpandHeight = true;
                var f = content.gameObject.AddComponent<ContentSizeFitter>();
                f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = new Vector2(1, 1);
                content.pivot = new Vector2(0.5f, 1);
                content.sizeDelta = Vector2.zero;
                Vertical(content, spacing, pad, true);
                FitHeight(content);
            }
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = horizontal;
            sr.vertical = !horizontal;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;
            sr.inertia = true;
            sr.decelerationRate = 0.12f;
            return sr;
        }

        public static Slider SliderRow(Transform parent, string label, float min, float max, float value, Action<float> onChange, Func<float, string> format, bool whole = false)
        {
            var row = Rect("Row_" + label, parent);
            Size(row, -1, 64);
            var lab = Label(row, label, 24, Theme.Text);
            lab.rectTransform.anchorMin = new Vector2(0, 0);
            lab.rectTransform.anchorMax = new Vector2(0.38f, 1);
            lab.rectTransform.offsetMin = Vector2.zero;
            lab.rectTransform.offsetMax = Vector2.zero;
            var val = Label(row, format(value), 22, Theme.Accent, TextAnchor.MiddleRight);
            val.rectTransform.anchorMin = new Vector2(0.84f, 0);
            val.rectTransform.anchorMax = new Vector2(1, 1);
            val.rectTransform.offsetMin = Vector2.zero;
            val.rectTransform.offsetMax = Vector2.zero;

            var sRoot = Rect("Slider", row);
            sRoot.anchorMin = new Vector2(0.40f, 0.5f);
            sRoot.anchorMax = new Vector2(0.82f, 0.5f);
            sRoot.sizeDelta = new Vector2(0, 44);
            var slider = sRoot.gameObject.AddComponent<Slider>();
            var bg = Panel(sRoot, new Color(1, 1, 1, 0.1f), true, "Background");
            bg.sprite = RoundedSmall;
            bg.rectTransform.anchorMin = new Vector2(0, 0.4f);
            bg.rectTransform.anchorMax = new Vector2(1, 0.6f);
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;
            var fillArea = Rect("Fill Area", sRoot);
            fillArea.anchorMin = new Vector2(0, 0.4f);
            fillArea.anchorMax = new Vector2(1, 0.6f);
            fillArea.offsetMin = new Vector2(4, 0);
            fillArea.offsetMax = new Vector2(-14, 0);
            var fill = Panel(fillArea, Theme.Accent, true, "Fill");
            fill.sprite = RoundedSmall;
            fill.rectTransform.sizeDelta = new Vector2(10, 0);
            var handleArea = Rect("Handle Slide Area", sRoot);
            Stretch(handleArea, 14, 0, 14, 0);
            var handle = Panel(handleArea, Color.white, true, "Handle");
            handle.sprite = UIFactory.RoundedSmall;
            handle.rectTransform.sizeDelta = new Vector2(30, 0);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.value = value;
            var nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
            slider.onValueChanged.AddListener(v => { val.text = format(v); if (onChange != null) onChange(v); });
            return slider;
        }

        public static Toggle ToggleRow(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var row = Rect("Row_" + label, parent);
            Size(row, -1, 60);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0.001f);
            var lab = Label(row, label, 24, Theme.Text);
            lab.rectTransform.anchorMin = new Vector2(0, 0);
            lab.rectTransform.anchorMax = new Vector2(0.8f, 1);
            lab.rectTransform.offsetMin = Vector2.zero;
            lab.rectTransform.offsetMax = Vector2.zero;
            var track = Panel(row, new Color(1, 1, 1, 0.12f), true, "Track");
            Place(track.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(84, 40));
            var knob = Panel(track.transform, Color.white, true, "Knob");
            Place(knob.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(32, 32));
            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = hit;
            var nav = toggle.navigation; nav.mode = Navigation.Mode.None; toggle.navigation = nav;
            Action<bool> visual = on =>
            {
                track.color = on ? Theme.ButtonPrimary : new Color(1, 1, 1, 0.12f);
                knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(on ? 1 : 0, 0.5f);
                knob.rectTransform.pivot = new Vector2(on ? 1 : 0, 0.5f);
                knob.rectTransform.anchoredPosition = new Vector2(on ? -4 : 4, 0);
            };
            toggle.isOn = value;
            visual(value);
            toggle.onValueChanged.AddListener(on =>
            {
                visual(on);
                if (AppRoot.Instance != null) AppRoot.Instance.Audio.Play(Sfx.Click);
                if (onChange != null) onChange(on);
            });
            return toggle;
        }

        /// <summary>A labelled row of mutually exclusive option buttons.</summary>
        public static Button[] Segmented(Transform parent, string label, string[] options, int selected, Action<int> onChange)
        {
            var row = Rect("Row_" + label, parent);
            Size(row, -1, 64);
            var lab = Label(row, label, 24, Theme.Text);
            lab.rectTransform.anchorMin = new Vector2(0, 0);
            lab.rectTransform.anchorMax = new Vector2(0.38f, 1);
            lab.rectTransform.offsetMin = Vector2.zero;
            lab.rectTransform.offsetMax = Vector2.zero;
            var group = Rect("Options", row);
            group.anchorMin = new Vector2(0.40f, 0.1f);
            group.anchorMax = new Vector2(1f, 0.9f);
            group.offsetMin = Vector2.zero;
            group.offsetMax = Vector2.zero;
            var h = Horizontal(group, 6, 0, true);
            h.childForceExpandWidth = true;
            var buttons = new Button[options.Length];
            int current = selected;
            Action refresh = () =>
            {
                for (int i = 0; i < buttons.Length; i++) SetButtonColor(buttons[i], i == current ? Theme.ButtonPrimary : Theme.ButtonBg);
            };
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = Button(group, options[i], () => { current = idx; refresh(); if (onChange != null) onChange(idx); }, ButtonStyle.Normal, 22);
            }
            refresh();
            return buttons;
        }

        public static InputField TextInput(Transform parent, string placeholder, string value)
        {
            var bg = Panel(parent, new Color(0, 0, 0, 0.45f), true, "Input");
            bg.sprite = RoundedSmall;
            var input = bg.gameObject.AddComponent<InputField>();
            var text = Label(bg.transform, "", 28, Theme.Text);
            Stretch(text.rectTransform, 18, 6, 18, 6);
            text.supportRichText = false;
            var ph = Label(bg.transform, placeholder, 28, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 18, 6, 18, 6);
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.characterLimit = 48;
            input.text = value ?? "";
            return input;
        }

        public static Image Divider(Transform parent)
        {
            var d = Panel(parent, new Color(1, 1, 1, 0.07f), false, "Divider");
            Size(d, -1, 2);
            return d;
        }

        public static Text Header(Transform parent, string text)
        {
            var t = Label(parent, text.ToUpperInvariant(), 20, Theme.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
            Size(t, -1, 40);
            return t;
        }
    }
}
