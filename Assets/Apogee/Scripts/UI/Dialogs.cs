using System;
using System.Collections.Generic;
using Apogee.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apogee.UI
{
    /// <summary>Toasts, confirmation dialogs and text prompts drawn on the global overlay canvas.</summary>
    public class Dialogs : MonoBehaviour
    {
        RectTransform root;
        RectTransform toastStack;
        readonly List<GameObject> modals = new List<GameObject>();

        public void Init(RectTransform overlayRoot)
        {
            root = overlayRoot;
            toastStack = UIFactory.Rect("Toasts", root);
            toastStack.anchorMin = new Vector2(0.5f, 0f);
            toastStack.anchorMax = new Vector2(0.5f, 0f);
            toastStack.pivot = new Vector2(0.5f, 0f);
            toastStack.anchoredPosition = new Vector2(0, 150);
            toastStack.sizeDelta = new Vector2(900, 400);
            var v = UIFactory.Vertical(toastStack, 10, 0, false);
            v.childAlignment = TextAnchor.LowerCenter;
        }

        public bool HasModal { get { return modals.Count > 0; } }

        public void Toast(string message, Color? color = null, float seconds = 2.6f)
        {
            if (toastStack == null) return;
            var card = UIFactory.Panel(toastStack, new Color(0.04f, 0.06f, 0.09f, 0.94f), true, "Toast");
            card.raycastTarget = false;
            var h = UIFactory.Horizontal(card, 12, 18, true);
            h.childAlignment = TextAnchor.MiddleCenter;
            var bar = UIFactory.Panel(card.transform, color ?? Theme.Accent, true, "Bar");
            UIFactory.Size(bar, 6, 34);
            var t = UIFactory.Label(card.transform, message, 26, Theme.Text, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIFactory.Size(t, Mathf.Min(760, 14 * message.Length + 20), -1);
            var cg = card.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(FadeAndDestroy(cg, seconds));
            while (toastStack.childCount > 3) DestroyImmediate(toastStack.GetChild(0).gameObject);
        }

        System.Collections.IEnumerator FadeAndDestroy(CanvasGroup cg, float seconds)
        {
            float t = 0f;
            while (t < seconds && cg != null)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(Mathf.Min(t * 6f, (seconds - t) * 3f));
                yield return null;
            }
            if (cg != null) Destroy(cg.gameObject);
        }

        RectTransform OpenModal(string title, float width, float height)
        {
            var dim = UIFactory.Panel(root, new Color(0, 0, 0, 0.62f), false, "Modal");
            UIFactory.Stretch(dim.rectTransform);
            var card = UIFactory.Card(dim.transform, "Dialog");
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var body = UIFactory.Rect("Body", card.transform);
            UIFactory.Stretch(body, 36, 30, 36, 30);
            UIFactory.Vertical(body, 18, 0, true);
            var t = UIFactory.Label(body, title, 34, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(t, -1, 50);
            modals.Add(dim.gameObject);
            if (AppRoot.Instance != null) AppRoot.Instance.Audio.Play(Sfx.Whoosh, 0.6f);
            return body;
        }

        void Close(GameObject modal)
        {
            modals.Remove(modal);
            if (modal != null) Destroy(modal);
        }

        /// <summary>Closes the top-most dialog. Returns false when none was open.</summary>
        public bool CloseTop()
        {
            if (modals.Count == 0) return false;
            Close(modals[modals.Count - 1]);
            return true;
        }

        RectTransform ButtonRow(Transform body)
        {
            var row = UIFactory.Rect("Buttons", body);
            UIFactory.Size(row, -1, 84);
            var h = UIFactory.Horizontal(row, 16, 0, true);
            h.childAlignment = TextAnchor.MiddleRight;
            h.childForceExpandWidth = false;
            return row;
        }

        public void Confirm(string title, string message, string confirmLabel, Action onConfirm, bool danger = false, string cancelLabel = "Cancel")
        {
            var body = OpenModal(title, 900, 400);
            var modal = modals[modals.Count - 1];
            var msg = UIFactory.Label(body, message, 26, Theme.Muted, TextAnchor.UpperLeft);
            UIFactory.Size(msg, -1, -1, -1, 1);
            var row = ButtonRow(body);
            var spacer = UIFactory.Rect("Spacer", row);
            UIFactory.Size(spacer, -1, -1, 1);
            if (cancelLabel != null)
                UIFactory.Size(UIFactory.Button(row, cancelLabel, () => Close(modal)), 230, 76);
            UIFactory.Size(UIFactory.Button(row, confirmLabel, () => { Close(modal); if (onConfirm != null) onConfirm(); }, danger ? ButtonStyle.Danger : ButtonStyle.Primary), 260, 76);
        }

        public void Prompt(string title, string initial, string okLabel, Action<string> onOk)
        {
            var body = OpenModal(title, 900, 360);
            var modal = modals[modals.Count - 1];
            var input = UIFactory.TextInput(body, "Name", initial);
            UIFactory.Size(input, -1, 84);
            var row = ButtonRow(body);
            var spacer = UIFactory.Rect("Spacer", row);
            UIFactory.Size(spacer, -1, -1, 1);
            UIFactory.Size(UIFactory.Button(row, "Cancel", () => Close(modal)), 230, 76);
            UIFactory.Size(UIFactory.Button(row, okLabel, () =>
            {
                string v = input.text.Trim();
                if (v.Length == 0) { Toast("Please enter a name.", Theme.Warn); return; }
                Close(modal);
                if (onOk != null) onOk(v);
            }, ButtonStyle.Primary), 260, 76);
            input.Select();
            input.ActivateInputField();
        }

        /// <summary>Generic modal with custom content. The builder callback fills the body; returns a close action.</summary>
        public Action Custom(string title, float width, float height, Action<RectTransform, Action> build)
        {
            var body = OpenModal(title, width, height);
            var modal = modals[modals.Count - 1];
            Action close = () => Close(modal);
            build(body, close);
            return close;
        }
    }
}
