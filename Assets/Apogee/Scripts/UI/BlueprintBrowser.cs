using System;
using Apogee.Core;
using Apogee.Rocket;
using UnityEngine;
using UnityEngine.UI;

namespace Apogee.UI
{
    /// <summary>List of saved designs and the bundled examples, with open and delete actions.</summary>
    public static class BlueprintBrowser
    {
        public static void Build(RectTransform body, Action<RocketBlueprint, string> onOpen, Action onClose)
        {
            RectTransform content;
            var scroll = UIFactory.ScrollList(body, out content, 10, 4);
            UIFactory.Size(scroll, -1, -1, -1, 1);
            Populate(content, onOpen);

            var row = UIFactory.Rect("Footer", body);
            UIFactory.Size(row, -1, 80);
            var h = UIFactory.Horizontal(row, 12, 0, true);
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleRight;
            var path = UIFactory.Label(row, "Saved on this device", 18, Theme.Muted);
            UIFactory.Size(path, 200, -1, 1);
            UIFactory.Size(UIFactory.Button(row, "Close", onClose), 200, 72);
        }

        static void Populate(RectTransform content, Action<RocketBlueprint, string> onOpen)
        {
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);

            UIFactory.Header(content, "Your designs");
            var saves = SaveSystem.List();
            if (saves.Count == 0)
            {
                var t = UIFactory.Label(content, "No saved designs yet. Build a rocket in the Spacecraft Designer and tap Save.", 22, Theme.Muted);
                UIFactory.Size(t, -1, 60);
            }
            foreach (var info in saves)
            {
                var i2 = info;
                Entry(content, info.name, info.partCount + " parts  ·  " + info.mass.ToString("N1") + " t  ·  saved " + info.savedAt.ToString("d MMM yyyy, HH:mm"),
                    () =>
                    {
                        var bp = SaveSystem.LoadBlueprint(i2.path);
                        if (bp == null) { AppRoot.Instance.Dialogs.Toast("That file could not be read.", Theme.Bad); return; }
                        onOpen(bp, i2.path);
                    },
                    () => AppRoot.Instance.Dialogs.Confirm("Delete design?", "\"" + i2.name + "\" will be permanently deleted from this device.", "Delete", () =>
                    {
                        if (SaveSystem.Delete(i2.path)) AppRoot.Instance.Dialogs.Toast("Deleted \"" + i2.name + "\"", Theme.Muted);
                        Populate(content, onOpen);
                    }, true));
            }

            UIFactory.Header(content, "Example designs");
            foreach (var ex in ExampleRockets.All())
            {
                var e2 = ex;
                float mass = 0f;
                foreach (var p in ex.parts) mass += PartMass.Wet(p);
                Entry(content, ex.name, ex.parts.Count + " parts  ·  " + mass.ToString("N1") + " t  ·  " + ex.stageCount + " stages", () => onOpen(e2.DeepCopy(), null), null);
            }
        }

        static void Entry(RectTransform parent, string title, string detail, Action open, Action delete)
        {
            var card = UIFactory.Panel(parent, Theme.PanelLight, true, "Entry");
            card.sprite = UIFactory.RoundedSmall;
            UIFactory.Size(card, -1, 104);
            var h = UIFactory.Horizontal(card, 12, 14, true);
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            var texts = UIFactory.Rect("Texts", card.transform);
            UIFactory.Size(texts, 300, -1, 1);
            UIFactory.Vertical(texts, 2, 0, true);
            var t = UIFactory.Label(texts, title, 26, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(t, -1, 38);
            var d = UIFactory.Label(texts, detail, 19, Theme.Muted);
            UIFactory.Size(d, -1, 30);
            if (delete != null) UIFactory.Size(UIFactory.Button(card.transform, "Delete", delete, ButtonStyle.Danger, 22), 150, 70);
            UIFactory.Size(UIFactory.Button(card.transform, "Open", open, ButtonStyle.Primary, 24), 160, 70);
        }
    }
}
