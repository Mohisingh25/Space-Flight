using System.Collections.Generic;
using System.Text;
using Apogee.Core;
using Apogee.Parts;
using Apogee.Rendering;
using Apogee.Rocket;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apogee.Builder
{
    /// <summary>Drag-or-tap item in the parts list. Horizontal drags pick the part up; vertical drags scroll.</summary>
    public class PartListItem : MonoBehaviour, IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public PartDefinition def;
        public BuilderController controller;
        public ScrollRect scroll;
        bool draggingPart;

        public void OnInitializePotentialDrag(PointerEventData e) { if (scroll != null) scroll.OnInitializePotentialDrag(e); }

        public void OnBeginDrag(PointerEventData e)
        {
            Vector2 d = e.position - e.pressPosition;
            draggingPart = Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.1f;
            if (draggingPart) controller.BeginDragFromList(def, e.position, e.pointerId);
            else if (scroll != null) scroll.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (draggingPart) controller.DragFromList(e.position);
            else if (scroll != null) scroll.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (draggingPart) { draggingPart = false; controller.EndDragFromList(e.position, e.pointerId); }
            else if (scroll != null) scroll.OnEndDrag(e);
        }

        public void OnScroll(PointerEventData e) { if (scroll != null) scroll.OnScroll(e); }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (AppRoot.Instance != null) AppRoot.Instance.Audio.Play(Sfx.Click);
            controller.BeginHoldFromList(def);
        }
    }

    /// <summary>
    /// Builder HUD: top bar, parts catalogue, live statistics, selection tools, placement bar and the
    /// staging editor. All widgets call straight into <see cref="BuilderController"/>.
    /// </summary>
    public class BuilderUI : MonoBehaviour
    {
        BuilderController c;
        RectTransform root;

        // top bar
        Button nameButton, undoButton, redoButton, symButton, stagingButton, modeButton, insideButton, mirrorButton;
        bool paletteOpen;
        int paletteFor = -1;

        // parts
        PartCategory category = PartCategory.Command;
        readonly Dictionary<PartCategory, Button> categoryButtons = new Dictionary<PartCategory, Button>();
        RectTransform partsContent;
        ScrollRect partsScroll;
        readonly Dictionary<string, RawImage> thumbs = new Dictionary<string, RawImage>();

        // stats
        RectTransform statsContent;

        // bottom bars
        RectTransform selectionBar, selectionBody, holdBar, stagingDrawer, stagingColumns, emptyHint;
        Text holdText;
        Button holdSymButton;
        Button stageEarlier, stageLater;

        // portrait-only
        RectTransform statsPanel, summaryStrip;
        Text summaryText;
        Button statsToggle;
        bool statsOpen;

        // Layout metrics; switched between landscape and portrait in Build().
        const float Margin = 14f;
        bool portrait;
        float SideW = 410f, TopH = 92f, SummaryH = 0f;

        public void Init(BuilderController controller)
        {
            c = controller;
            c.Changed += Refresh;
            c.SelectionChanged += Refresh;
            c.HoldChanged += Refresh;
            c.StatsChanged += RebuildStats;
            AppRoot.OrientationChanged += OnOrientationChanged;
            Build();
        }

        void OnDestroy() { AppRoot.OrientationChanged -= OnOrientationChanged; }

        void OnOrientationChanged()
        {
            if (root != null) Destroy(root.parent.gameObject);
            categoryButtons.Clear();
            thumbs.Clear();
            Build();
        }

        void Build()
        {
            portrait = UIFactory.Portrait;
            if (portrait) { SideW = 270f; TopH = 176f; SummaryH = 108f; }
            else { SideW = 410f; TopH = 92f; SummaryH = 0f; }
            statsOpen = false;
            statsPanel = summaryStrip = null;
            Canvas canvas;
            root = UIFactory.CreateCanvas("Builder HUD", 10, out canvas);
            BuildTopBar();
            BuildPartsPanel();
            BuildStatsPanel();
            if (portrait) BuildSummaryStrip();
            BuildBottomBars();
            Refresh();
        }

        public void OnThumbnail(string id, Texture2D tex)
        {
            RawImage img;
            if (thumbs.TryGetValue(id, out img) && img != null) { img.texture = tex; img.color = Color.white; }
        }

        // ================================================================== layout

        void BuildTopBar()
        {
            var bar = UIFactory.Card(root, "TopBar");
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(Margin, -Margin - TopH);
            rt.offsetMax = new Vector2(-Margin, -Margin);
            if (portrait) { BuildTopBarPortrait(bar.transform); return; }
            var row = UIFactory.Rect("Row", bar.transform);
            UIFactory.Stretch(row, 10, 10, 10, 10);
            var h = UIFactory.Horizontal(row, 10, 0, true);
            h.childAlignment = TextAnchor.MiddleLeft;

            UIFactory.Size(UIFactory.Button(row, "Menu", c.BackToMenu), 110);
            nameButton = UIFactory.Button(row, "Untitled", c.SaveAs, ButtonStyle.Ghost, 28);
            UIFactory.Size(nameButton, 200, -1, 1);
            nameButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            undoButton = UIFactory.Button(row, "Undo", c.Undo);
            UIFactory.Size(undoButton, 105);
            redoButton = UIFactory.Button(row, "Redo", c.Redo);
            UIFactory.Size(redoButton, 105);
            symButton = UIFactory.Button(row, "Sym ×1", c.CycleSymmetry);
            UIFactory.Size(symButton, 130);
            stagingButton = UIFactory.Button(row, "Staging", () => { c.StagingOpen = !c.StagingOpen; Refresh(); });
            UIFactory.Size(stagingButton, 140);
            modeButton = UIFactory.Button(row, "2D", c.ToggleMode2D);
            UIFactory.Size(modeButton, 100);
            insideButton = UIFactory.Button(row, "Inside", c.ToggleInside);
            UIFactory.Size(insideButton, 120);
            mirrorButton = UIFactory.Button(row, "Mirror", c.ToggleMirror);
            UIFactory.Size(mirrorButton, 120);
            UIFactory.Size(UIFactory.Button(row, "New", c.NewDesign), 100);
            UIFactory.Size(UIFactory.Button(row, "Open", c.OpenLoadDialog), 100);
            UIFactory.Size(UIFactory.Button(row, "Save", c.Save), 110);
            UIFactory.Size(UIFactory.Button(row, "Launch", c.Launch, ButtonStyle.Primary, 30), 180);
        }

        /// <summary>Portrait: two rows — [Menu | name | Launch] then the editing tools.</summary>
        void BuildTopBarPortrait(Transform bar)
        {
            var col = UIFactory.Rect("Rows", bar);
            UIFactory.Stretch(col, 10, 10, 10, 10);
            UIFactory.Vertical(col, 8, 0, true);

            var r1 = UIFactory.Rect("Row1", col);
            UIFactory.Size(r1, -1, -1, -1, 1);
            var h1 = UIFactory.Horizontal(r1, 10, 0, true);
            h1.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Size(UIFactory.Button(r1, "Menu", c.BackToMenu), 120);
            nameButton = UIFactory.Button(r1, "Untitled", c.SaveAs, ButtonStyle.Ghost, 28);
            UIFactory.Size(nameButton, 200, -1, 1);
            nameButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            UIFactory.Size(UIFactory.Button(r1, "Launch", c.Launch, ButtonStyle.Primary, 30), 220);

            var r2 = UIFactory.Rect("Row2", col);
            UIFactory.Size(r2, -1, -1, -1, 1);
            var h2 = UIFactory.Horizontal(r2, 6, 0, true);
            h2.childForceExpandWidth = true;
            undoButton = UIFactory.Button(r2, "Undo", c.Undo, ButtonStyle.Normal, 22);
            redoButton = UIFactory.Button(r2, "Redo", c.Redo, ButtonStyle.Normal, 22);
            symButton = UIFactory.Button(r2, "Sym ×1", c.CycleSymmetry, ButtonStyle.Normal, 22);
            stagingButton = UIFactory.Button(r2, "Stages", () => { c.StagingOpen = !c.StagingOpen; Refresh(); }, ButtonStyle.Normal, 22);
            modeButton = UIFactory.Button(r2, "2D", c.ToggleMode2D, ButtonStyle.Normal, 22);
            insideButton = UIFactory.Button(r2, "Inside", c.ToggleInside, ButtonStyle.Normal, 22);
            mirrorButton = UIFactory.Button(r2, "Mirror", c.ToggleMirror, ButtonStyle.Normal, 22);
            UIFactory.Button(r2, "New", c.NewDesign, ButtonStyle.Normal, 22);
            UIFactory.Button(r2, "Open", c.OpenLoadDialog, ButtonStyle.Normal, 22);
            UIFactory.Button(r2, "Save", c.Save, ButtonStyle.Normal, 22);
        }

        void BuildPartsPanel()
        {
            var panel = UIFactory.Card(root, "Parts");
            var rt = panel.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(Margin, Margin);
            rt.offsetMax = new Vector2(Margin + SideW, -Margin * 2 - TopH);

            var inner = UIFactory.Rect("Inner", panel.transform);
            UIFactory.Stretch(inner, 10, 10, 10, 10);
            UIFactory.Vertical(inner, 8, 0, true);

            var title = UIFactory.Label(inner, portrait ? "PARTS  ·  tap or drag" : "PARTS  ·  tap, or drag onto the rocket", 20, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, -1, 34);

            RectTransform chips;
            var chipScroll = UIFactory.ScrollList(inner, out chips, 6, 0, true);
            UIFactory.Size(chipScroll, -1, 62);
            foreach (PartCategory cat in System.Enum.GetValues(typeof(PartCategory)))
            {
                var cc = cat;
                var b = UIFactory.Button(chips, PartCatalog.CategoryLabel(cat), () => { category = cc; RebuildPartList(); }, ButtonStyle.Tab, 22);
                UIFactory.Size(b, portrait ? 130 : 150, 56);
                categoryButtons[cat] = b;
            }

            partsScroll = UIFactory.ScrollList(inner, out partsContent, 8, 2);
            UIFactory.Size(partsScroll, -1, -1, -1, 1);
            RebuildPartList();
        }

        void RebuildPartList()
        {
            foreach (var kv in categoryButtons) UIFactory.SetButtonColor(kv.Value, kv.Key == category ? Theme.ButtonPrimary : new Color(0.1f, 0.14f, 0.19f, 0.9f));
            for (int i = partsContent.childCount - 1; i >= 0; i--) Destroy(partsContent.GetChild(i).gameObject);
            thumbs.Clear();
            foreach (var def in PartCatalog.InCategory(category))
            {
                var item = UIFactory.Panel(partsContent, Theme.PanelLight, true, "Item_" + def.id);
                item.sprite = UIFactory.RoundedSmall;
                float itemH = portrait ? 100f : 112f, thumb = portrait ? 84f : 96f;
                UIFactory.Size(item, -1, itemH);
                var li = item.gameObject.AddComponent<PartListItem>();
                li.def = def;
                li.controller = c;
                li.scroll = partsScroll;

                var thumbBg = UIFactory.Panel(item.transform, new Color(0, 0, 0, 0.35f), true, "ThumbBg");
                thumbBg.sprite = UIFactory.RoundedSmall;
                thumbBg.raycastTarget = false;
                UIFactory.Place(thumbBg.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(thumb, thumb));
                var raw = UIFactory.Rect("Thumb", thumbBg.transform).gameObject.AddComponent<RawImage>();
                UIFactory.Stretch(raw.rectTransform, 2, 2, 2, 2);
                raw.raycastTarget = false;
                var tex = PartThumbnails.Get(def.id);
                raw.texture = tex;
                raw.color = tex != null ? Color.white : new Color(1, 1, 1, 0);
                thumbs[def.id] = raw;

                if (portrait)
                {
                    var pname = UIFactory.Label(item.transform, def.displayName, 20, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                    pname.rectTransform.anchorMin = new Vector2(0, 0);
                    pname.rectTransform.anchorMax = new Vector2(1, 1);
                    pname.rectTransform.offsetMin = new Vector2(thumb + 16, 6);
                    pname.rectTransform.offsetMax = new Vector2(-6, -6);
                    pname.raycastTarget = false;
                    continue;
                }
                var name = UIFactory.Label(item.transform, def.displayName, 23, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.anchorMin = new Vector2(0, 0);
                name.rectTransform.anchorMax = new Vector2(1, 1);
                name.rectTransform.offsetMin = new Vector2(114, 46);
                name.rectTransform.offsetMax = new Vector2(-8, -10);
                var stats = UIFactory.Label(item.transform, def.ShortStats(), 19, Theme.Muted, TextAnchor.LowerLeft);
                stats.rectTransform.anchorMin = new Vector2(0, 0);
                stats.rectTransform.anchorMax = new Vector2(1, 1);
                stats.rectTransform.offsetMin = new Vector2(114, 10);
                stats.rectTransform.offsetMax = new Vector2(-8, -54);
            }
            partsScroll.verticalNormalizedPosition = 1f;
        }

        void BuildStatsPanel()
        {
            var panel = UIFactory.Card(root, "Stats");
            var rt = panel.rectTransform;
            statsPanel = rt;
            if (portrait)
            {
                // Overlay above the summary strip, to the right of the parts list; toggled by "Stats".
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(Margin * 2 + SideW, Margin * 2 + SummaryH);
                rt.offsetMax = new Vector2(-Margin, -Margin * 2 - TopH);
            }
            else
            {
                rt.anchorMin = new Vector2(1, 0);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(1, 0.5f);
                rt.offsetMin = new Vector2(-Margin - SideW, Margin);
                rt.offsetMax = new Vector2(-Margin, -Margin * 2 - TopH);
            }
            var inner = UIFactory.Rect("Inner", panel.transform);
            UIFactory.Stretch(inner, 10, 10, 10, 10);
            UIFactory.Vertical(inner, 4, 0, true);
            var markers = UIFactory.ToggleRow(inner, "Show CoM / CoT / CoP", AppRoot.Instance.Settings.showBalanceMarkers, c.ToggleMarkers);
            UIFactory.Size(markers, -1, 54);
            var sc = UIFactory.ScrollList(inner, out statsContent, 2, 4);
            UIFactory.Size(sc, -1, -1, -1, 1);
        }

        /// <summary>Portrait: compact one-line vehicle summary plus a button that opens the full statistics.</summary>
        void BuildSummaryStrip()
        {
            var card = UIFactory.Card(root, "Summary");
            summaryStrip = card.rectTransform;
            summaryStrip.anchorMin = new Vector2(0, 0);
            summaryStrip.anchorMax = new Vector2(1, 0);
            summaryStrip.pivot = new Vector2(0.5f, 0);
            summaryStrip.offsetMin = new Vector2(Margin * 2 + SideW, Margin);
            summaryStrip.offsetMax = new Vector2(-Margin, Margin + SummaryH);
            var row = UIFactory.Rect("Row", summaryStrip);
            UIFactory.Stretch(row, 14, 10, 10, 10);
            var h = UIFactory.Horizontal(row, 10, 0, true);
            h.childAlignment = TextAnchor.MiddleLeft;
            summaryText = UIFactory.Label(row, "", 21, Theme.Text);
            UIFactory.Size(summaryText, 200, -1, 1);
            statsToggle = UIFactory.Button(row, "Stats", () => { statsOpen = !statsOpen; Refresh(); });
            UIFactory.Size(statsToggle, 150);
        }

        void UpdateSummary()
        {
            if (summaryText == null) return;
            var s = c.Stats;
            if (s == null || s.partCount == 0) { summaryText.text = "<color=#8fa3b8>Add parts to see mass, TWR and Δv.</color>"; return; }
            string twrCol = !s.hasThrust ? "#8fa3b8" : s.launchTwr >= 1.2f ? "#5fd38d" : s.launchTwr >= 1f ? "#f0c048" : "#ff6b6b";
            summaryText.text = "Mass <b>" + s.totalMass.ToString("N1") + " t</b>   TWR <b><color=" + twrCol + ">" + (s.hasThrust ? s.launchTwr.ToString("0.00") : "—") + "</color></b>\n" +
                "Δv <b><color=#7fd4ff>" + s.dvVacTotal.ToString("N0") + " m/s</color></b>   Lifts <b>" + (s.launchThrustAsl / 9.80665f).ToString("N0") + " t</b>";
        }

        void BuildBottomBars()
        {
            // Region between the side panels
            emptyHint = UIFactory.Rect("EmptyHint", root);
            BottomRegion(emptyHint, portrait ? 200 : 120);
            var hint = UIFactory.Label(emptyHint, portrait
                ? "Start with a command part (capsule or probe core): tap it, then tap the platform.\nStack tanks below it, add an engine, and check the numbers below."
                : "Start by choosing a command part (a capsule or probe core) and tap or drag it onto the platform.\nThen stack tanks below it, add an engine, and watch the numbers on the right.", 26, Theme.Muted, TextAnchor.MiddleCenter);
            UIFactory.Stretch(hint.rectTransform);

            selectionBar = UIFactory.Card(root, "Selection").rectTransform;
            selectionBody = UIFactory.Rect("Body", selectionBar);
            UIFactory.Stretch(selectionBody, 16, 12, 16, 12);
            UIFactory.Vertical(selectionBody, 8, 0, true);

            var hold = UIFactory.Card(root, "Hold");
            holdBar = hold.rectTransform;
            BottomRegion(holdBar, 96);
            var hrow = UIFactory.Rect("Row", holdBar);
            UIFactory.Stretch(hrow, 16, 10, 16, 10);
            var hh = UIFactory.Horizontal(hrow, 12, 0, true);
            hh.childAlignment = TextAnchor.MiddleLeft;
            holdText = UIFactory.Label(hrow, "", 24, Theme.Text);
            UIFactory.Size(holdText, 200, -1, 1);
            holdSymButton = UIFactory.Button(hrow, "Symmetry ×1", c.CycleSymmetry);
            UIFactory.Size(holdSymButton, 200);
            UIFactory.Size(UIFactory.Button(hrow, "Cancel", c.CancelHold, ButtonStyle.Danger), 160);

            var drawer = UIFactory.Card(root, "Staging");
            stagingDrawer = drawer.rectTransform;
            BottomRegion(stagingDrawer, 400);
            var dInner = UIFactory.Rect("Inner", stagingDrawer);
            UIFactory.Stretch(dInner, 14, 12, 14, 12);
            UIFactory.Vertical(dInner, 8, 0, true);
            var head = UIFactory.Rect("Head", dInner);
            UIFactory.Size(head, -1, 64);
            var hrow2 = UIFactory.Horizontal(head, 10, 0, true);
            hrow2.childAlignment = TextAnchor.MiddleLeft;
            var t = UIFactory.Label(head, portrait ? "STAGING" : "STAGING  ·  fires left to right", 22, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(t, portrait ? 100 : 200, -1, 1);
            stageEarlier = UIFactory.Button(head, portrait ? "Earlier" : "Fire earlier", () => { if (c.Selected != null) c.MoveStage(c.SelectedUid, -1); });
            UIFactory.Size(stageEarlier, portrait ? 130 : 190);
            stageLater = UIFactory.Button(head, portrait ? "Later" : "Fire later", () => { if (c.Selected != null) c.MoveStage(c.SelectedUid, +1); });
            UIFactory.Size(stageLater, portrait ? 120 : 170);
            UIFactory.Size(UIFactory.Button(head, "Auto", c.ResetStaging), portrait ? 100 : 110);
            UIFactory.Size(UIFactory.Button(head, "Close", () => { c.StagingOpen = false; Refresh(); }), portrait ? 110 : 120);
            var colScroll = UIFactory.ScrollList(dInner, out stagingColumns, 10, 2, true);
            UIFactory.Size(colScroll, -1, -1, -1, 1);
        }

        void BottomRegion(RectTransform rt, float height, float lift = 0f)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            float right = portrait ? Margin : Margin * 2 + SideW;
            float bottom = portrait ? Margin * 2 + SummaryH : Margin;
            rt.offsetMin = new Vector2(Margin * 2 + SideW, bottom + lift);
            rt.offsetMax = new Vector2(-right, bottom + lift + height);
        }

        // ================================================================== refresh

        public void Refresh()
        {
            if (c == null || c.Bp == null || nameButton == null) return;
            UIFactory.SetLabel(nameButton, c.Bp.name + (c.Bp.IsEmpty ? "" : "   <size=18><color=#8fa3b8>rename</color></size>"));
            undoButton.interactable = c.CanUndo;
            redoButton.interactable = c.CanRedo;
            UIFactory.SetLabel(symButton, "Sym ×" + c.Symmetry);
            UIFactory.SetLabel(holdSymButton, "Symmetry ×" + c.Symmetry);
            UIFactory.SetButtonColor(stagingButton, c.StagingOpen ? Theme.ButtonPrimary : Theme.ButtonBg);
            UIFactory.SetLabel(modeButton, c.Mode2D ? "3D" : "2D");
            UIFactory.SetButtonColor(insideButton, c.InsideView ? Theme.ButtonPrimary : Theme.ButtonBg);
            UIFactory.SetButtonColor(mirrorButton, c.MirrorMode ? Theme.ButtonPrimary : Theme.ButtonBg);

            emptyHint.gameObject.SetActive(c.Bp.IsEmpty && !c.IsHolding);
            holdBar.gameObject.SetActive(c.IsHolding);
            if (c.IsHolding)
            {
                var def = c.Held.Root.Def;
                string extra = c.Held.parts.Count > 1 ? " (+" + (c.Held.parts.Count - 1) + " attached)" : "";
                holdText.text = "<b>" + def.displayName + "</b>" + extra + "\n<size=20><color=#8fa3b8>" +
                    (c.Bp.IsEmpty ? "Tap the platform to place it." : def.radialMount && !RocketAssembly.CanStack(def) ? "Drag onto the side of a part. Symmetry places copies around it." : "Drag to a green node, or beside a tank to attach it to the side.") + "</color></size>";
                holdSymButton.gameObject.SetActive(def.radialMount || RocketAssembly.CanStack(def));
            }

            stagingDrawer.gameObject.SetActive(c.StagingOpen && !c.IsHolding);
            bool showSel = c.Selected != null && !c.IsHolding;
            selectionBar.gameObject.SetActive(showSel);
            if (showSel) RebuildSelection(c.StagingOpen ? 410f : 0f);
            if (portrait)
            {
                bool overlayBusy = c.IsHolding || showSel || c.StagingOpen;
                if (overlayBusy) statsOpen = false;
                statsPanel.gameObject.SetActive(statsOpen);
                UIFactory.SetButtonColor(statsToggle, statsOpen ? Theme.ButtonPrimary : Theme.ButtonBg);
                UIFactory.SetLabel(statsToggle, statsOpen ? "Close" : "Stats");
            }
            if (c.StagingOpen) RebuildStaging();
            RebuildStats();
        }

        void RebuildSelection(float lift)
        {
            var p = c.Selected;
            var def = p.Def;
            for (int i = selectionBody.childCount - 1; i >= 0; i--) Destroy(selectionBody.GetChild(i).gameObject);
            float h = def.isPayload ? 236f : 170f;
            BottomRegion(selectionBar, h, lift);

            var dims = PartDims.Of(p);
            var info = new StringBuilder();
            info.Append("<b>").Append(def.displayName).Append("</b>   <color=#8fa3b8>");
            info.Append(PartDefinition.FormatMass(PartMass.Wet(p)));
            if (RocketAssembly.CanStack(def)) info.Append("  ·  Ø ").Append(dims.diameter.ToString("0.###")).Append(" m × ").Append(dims.height.ToString("0.##")).Append(" m");
            if (def.IsEngine) info.Append("  ·  ").Append(def.thrustSL.ToString("0")).Append("/").Append(def.thrustVac.ToString("0")).Append(" kN  ·  Isp ").Append(def.ispSL.ToString("0")).Append("/").Append(def.ispVac.ToString("0")).Append(" s");
            float fuel = PartMass.Fuel(p);
            if (fuel > 0f) info.Append("  ·  ").Append(fuel.ToString("0.0")).Append(" t propellant");
            if (p.stage >= 0) info.Append("  ·  Stage ").Append(p.stage + 1);
            int sym = c.Bp.SymmetryMembers(p).Count;
            if (sym > 1) info.Append("  ·  ×").Append(sym).Append(" symmetry");
            info.Append("</color>");
            var label = UIFactory.Label(selectionBody, info.ToString(), portrait ? 20 : 23, Theme.Text);
            UIFactory.Size(label, -1, portrait ? 84 : 56);

            var tools = new List<KeyValuePair<string, System.Action>>();
            tools.Add(new KeyValuePair<string, System.Action>("Move", c.MoveSelected));
            tools.Add(new KeyValuePair<string, System.Action>("Rotate", c.RotateSelected));
            if (def.resizableDiameter)
            {
                tools.Add(new KeyValuePair<string, System.Action>("Wider", () => c.ResizeSelected(+1)));
                tools.Add(new KeyValuePair<string, System.Action>("Narrower", () => c.ResizeSelected(-1)));
            }
            if (def.resizableLength)
            {
                tools.Add(new KeyValuePair<string, System.Action>("Longer", () => c.StretchSelected(+1)));
                tools.Add(new KeyValuePair<string, System.Action>("Shorter", () => c.StretchSelected(-1)));
            }
            if (paletteFor != p.uid) { paletteOpen = false; paletteFor = p.uid; }
            if (def.Paintable) tools.Add(new KeyValuePair<string, System.Action>(paletteOpen ? "Paint ▲" : "Paint", () => { paletteOpen = !paletteOpen; Refresh(); }));
            if (def.isDecal) tools.Add(new KeyValuePair<string, System.Action>("Logo", c.EditLogo));
            tools.Add(new KeyValuePair<string, System.Action>("Duplicate", c.DuplicateSelected));
            tools.Add(new KeyValuePair<string, System.Action>("Delete", c.DeleteSelected));

            int perRow = portrait ? 4 : tools.Count;
            int rows = (tools.Count + perRow - 1) / perRow;
            bool showPalette = paletteOpen && def.Paintable;
            float paletteH = showPalette ? 150f : 0f;
            if (portrait) BottomRegion(selectionBar, h + 28f + (rows - 1) * 76f + paletteH, lift);
            else if (showPalette) BottomRegion(selectionBar, h + paletteH, lift);
            for (int r = 0; r < rows; r++)
            {
                var row = UIFactory.Rect("Tools" + r, selectionBody);
                UIFactory.Size(row, -1, portrait ? 68 : 78);
                var hl = UIFactory.Horizontal(row, 8, 0, true);
                hl.childForceExpandWidth = true;
                for (int i = r * perRow; i < Mathf.Min(tools.Count, (r + 1) * perRow); i++)
                {
                    bool del = tools[i].Key == "Delete";
                    UIFactory.Button(row, tools[i].Key, tools[i].Value, del ? ButtonStyle.Danger : ButtonStyle.Normal, portrait ? 22 : tools.Count > 7 ? 21 : 26);
                }
                // keep the last row's buttons the same width as full rows
                if (portrait) for (int k = tools.Count; k < (r + 1) * perRow && r == rows - 1; k++)
                {
                    var spacer = UIFactory.Rect("Spacer", row);
                    spacer.gameObject.AddComponent<LayoutElement>();
                }
            }

            if (showPalette) BuildPalette(p);

            if (def.isPayload)
            {
                var slider = UIFactory.SliderRow(selectionBody, "Payload mass", 0f, 40f, p.payloadMass, v => c.SetPayload(v, false), v => v.ToString("0.0") + " t");
                var trigger = slider.gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
                entry.callback.AddListener(e => c.SetPayload(slider.value, true));
                trigger.triggers.Add(entry);
            }
        }

        /// <summary>Colour swatches for the selected part, plus "factory finish" and "paint whole rocket".</summary>
        void BuildPalette(PartInstance p)
        {
            RectTransform swatches;
            var sc = UIFactory.ScrollList(selectionBody, out swatches, 8, 0, true);
            UIFactory.Size(sc, -1, 70);
            for (int i = 0; i < PartPaint.Colors.Length; i++)
            {
                int idx = i;
                var b = UIFactory.Button(swatches, "", () => c.PaintSelected(idx));
                UIFactory.SetButtonColor(b, PartPaint.Colors[i]);
                UIFactory.Size(b, 64, 62);
                if (p.paint == i) UIFactory.SetLabel(b, "<color=#00000099>●</color>");
            }
            var row = UIFactory.Rect("PaintActions", selectionBody);
            UIFactory.Size(row, -1, 62);
            var hl = UIFactory.Horizontal(row, 8, 0, true);
            hl.childForceExpandWidth = true;
            UIFactory.Button(row, "Factory finish", () => c.PaintSelected(-1), ButtonStyle.Normal, 21);
            UIFactory.Button(row, p.paint >= 0 ? "Paint whole rocket " + PartPaint.Names[p.paint] : "Reset whole rocket", () => c.PaintAll(p.paint), ButtonStyle.Normal, 21);
        }

        void RebuildStaging()
        {
            for (int i = stagingColumns.childCount - 1; i >= 0; i--) Destroy(stagingColumns.GetChild(i).gameObject);
            var bp = c.Bp;
            var sel = c.Selected;
            bool canMove = sel != null && sel.stage >= 0;
            stageEarlier.interactable = canMove;
            stageLater.interactable = canMove;
            if (bp.stageCount == 0)
            {
                var t = UIFactory.Label(stagingColumns, "No staged parts yet. Engines, decouplers, fairings and parachutes appear here automatically.", 24, Theme.Muted);
                UIFactory.Size(t, 900);
                return;
            }
            for (int s = 0; s < bp.stageCount; s++)
            {
                var col = UIFactory.Panel(stagingColumns, Theme.PanelLight, true, "Stage" + s);
                col.sprite = UIFactory.RoundedSmall;
                UIFactory.Size(col, 300);
                var v = UIFactory.Vertical(col, 6, 10, true);
                v.childAlignment = TextAnchor.UpperLeft;
                var title = UIFactory.Label(col.transform, "STAGE " + (s + 1) + (s == 0 ? "  ·  LAUNCH" : ""), 22, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Size(title, -1, 30);
                string sub = "";
                if (c.Stats != null && s < c.Stats.stages.Count)
                {
                    var st = c.Stats.stages[s];
                    if (st.dvVac > 0.5f) sub = "Δv " + st.dvVac.ToString("N0") + " m/s  ·  TWR " + (s == 0 ? st.twrAsl : st.twrVac).ToString("0.00") + "  ·  " + st.burnTime.ToString("0") + " s";
                    else sub = "No burn";
                }
                var subT = UIFactory.Label(col.transform, sub, 18, Theme.Muted);
                UIFactory.Size(subT, -1, 26);

                // Group symmetric counterparts into a single chip.
                var seen = new HashSet<int>();
                foreach (var p in StagingSystem.PartsInStage(bp, s))
                {
                    if (p.symmetryGroup != 0 && seen.Contains(p.symmetryGroup)) continue;
                    if (p.symmetryGroup != 0) seen.Add(p.symmetryGroup);
                    int count = 0;
                    foreach (var q in StagingSystem.PartsInStage(bp, s)) if (p.symmetryGroup != 0 ? q.symmetryGroup == p.symmetryGroup : q.uid == p.uid) count++;
                    int uid = p.uid;
                    bool selected = sel != null && (sel.uid == uid || (sel.symmetryGroup != 0 && sel.symmetryGroup == p.symmetryGroup));
                    var chip = UIFactory.Button(col.transform, StagingSystem.ActionVerb(p) + " · " + p.Def.displayName + (count > 1 ? " ×" + count : ""), () => c.Select(uid), selected ? ButtonStyle.Primary : ButtonStyle.Normal, 19);
                    chip.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    UIFactory.Size(chip, -1, 52);
                }
            }
        }

        // ================================================================== statistics

        void RebuildStats()
        {
            if (statsContent == null) return;
            UpdateSummary();
            for (int i = statsContent.childCount - 1; i >= 0; i--) Destroy(statsContent.GetChild(i).gameObject);
            var s = c.Stats;
            if (s == null || s.partCount == 0)
            {
                var t = UIFactory.Label(statsContent, "Vehicle statistics appear here as you build: mass, thrust, TWR, Δv per stage, estimated apogee and payload to orbit.", 22, Theme.Muted);
                UIFactory.Size(t, -1, 160);
                return;
            }

            UIFactory.Header(statsContent, "Vehicle");
            Row("Parts", s.partCount.ToString());
            Row("Total mass", s.totalMass.ToString("N2") + " t");
            Row("Dry mass", s.dryMass.ToString("N2") + " t");
            Row("Propellant", s.fuelMass.ToString("N2") + " t");
            Row("Height", s.height.ToString("0.0") + " m");
            if (s.crew > 0) Row("Crew", s.crew.ToString());

            UIFactory.Header(statsContent, "Propulsion");
            Row("Launch thrust (SL)", s.launchThrustAsl.ToString("N0") + " kN");
            Row("Launch thrust (vac)", s.launchThrustVac.ToString("N0") + " kN");
            Color twrCol = s.launchTwr >= 1.2f ? Theme.Good : s.launchTwr >= 1f ? Theme.Warn : Theme.Bad;
            Row("Launch TWR", s.hasThrust ? s.launchTwr.ToString("0.00") : "—", twrCol);
            float liftT = s.launchThrustAsl / 9.80665f;
            Row("Engines can lift", liftT.ToString("N1") + " t  (rocket " + s.totalMass.ToString("N1") + " t)", liftT > s.totalMass ? Theme.Good : Theme.Bad);
            Row("Δv vacuum", s.dvVacTotal.ToString("N0") + " m/s", Theme.Accent);
            Row("Δv sea level", s.dvAslTotal.ToString("N0") + " m/s");

            UIFactory.Header(statsContent, "Performance (estimates)");
            string alt = !s.hasThrust || s.launchTwr <= 1f ? "Can't lift off" : s.escapes ? "Escapes Earth" : FormatAltitude(s.maxAltitude);
            Row("Max altitude (vertical)", alt, s.maxAltitude >= 100000 || s.escapes ? Theme.Good : Theme.Text);
            Row("Payload to LEO", s.payloadToLeo >= 0f ? "≈ " + s.payloadToLeo.ToString("0.0") + " t" : "Can't reach orbit", s.payloadToLeo >= 0f ? Theme.Good : Theme.Muted);
            Row("Orbit needs ≈", PhysicsLeo() + " m/s Δv");

            UIFactory.Header(statsContent, "Balance");
            Row("Centre of mass", "h " + Height(s.centerOfMass.y) + " m", new Color(1f, 0.85f, 0.2f));
            if (s.hasThrust) Row("Centre of thrust", "h " + Height(s.centerOfThrust.y) + " m", new Color(1f, 0.45f, 0.9f));
            Row("Centre of pressure", "h " + Height(s.centerOfPressure.y) + " m", new Color(0.4f, 0.85f, 1f));
            string stab = s.stability == Stability.Stable ? "Stable" : s.stability == Stability.Marginal ? "Marginal" : s.stability == Stability.Unstable ? "Unstable" : "—";
            Color stabCol = s.stability == Stability.Stable ? Theme.Good : s.stability == Stability.Marginal ? Theme.Warn : Theme.Bad;
            Row("Aero stability", stab + "  (" + s.stabilityCalibers.ToString("0.0") + " cal)", stabCol);

            if (s.batteryKWh > 0f || s.solarKW > 0f)
            {
                UIFactory.Header(statsContent, "Power");
                Row("Battery", s.batteryKWh.ToString("0.0") + " kWh");
                if (s.solarKW > 0f) Row("Solar", s.solarKW.ToString("0.0") + " kW");
            }

            if (s.stages.Count > 0)
            {
                UIFactory.Header(statsContent, "Stages");
                for (int i = 0; i < s.stages.Count; i++)
                {
                    var st = s.stages[i];
                    if (st.dvVac < 0.5f && st.burnTime < 0.5f)
                    {
                        Row("Stage " + (i + 1), string.Join(", ", st.actions.ToArray()), Theme.Muted);
                        continue;
                    }
                    Row("Stage " + (i + 1), st.dvVac.ToString("N0") + " m/s  ·  TWR " + (i == 0 ? st.twrAsl : st.twrVac).ToString("0.00"), Theme.Text);
                }
            }

            if (s.warnings.Count > 0)
            {
                UIFactory.Header(statsContent, "Checks");
                foreach (var w in s.warnings)
                {
                    var t = UIFactory.Label(statsContent, "• " + w, 20, Theme.Warn, TextAnchor.UpperLeft);
                    UIFactory.Size(t, -1, 30 + 24 * (w.Length / 34));
                }
            }
            else
            {
                UIFactory.Header(statsContent, "Checks");
                var t = UIFactory.Label(statsContent, "• All design checks passed.", 20, Theme.Good);
                UIFactory.Size(t, -1, 32);
            }
        }

        static string PhysicsLeo() { return Apogee.Simulation.PhysicsConstants.LeoDeltaV.ToString("N0"); }

        string Height(float rocketY)
        {
            // Heights measured from the bottom of the vehicle
            var poses = RocketLayout.Compute(c.Bp);
            var b = RocketLayout.ComputeBounds(c.Bp, poses);
            return (rocketY - b.min.y).ToString("0.0");
        }

        static string FormatAltitude(double m)
        {
            if (m < 1000) return m.ToString("0") + " m";
            return (m / 1000.0).ToString(m < 1e6 ? "N1" : "N0") + " km";
        }

        void Row(string label, string value, Color? color = null)
        {
            var row = UIFactory.Rect("Row", statsContent);
            UIFactory.Size(row, -1, 36);
            var l = UIFactory.Label(row, label, 21, Theme.Muted);
            l.rectTransform.anchorMin = new Vector2(0, 0);
            l.rectTransform.anchorMax = new Vector2(0.52f, 1);
            l.rectTransform.offsetMin = new Vector2(4, 0);
            l.rectTransform.offsetMax = Vector2.zero;
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            var v = UIFactory.Label(row, value, 21, color ?? Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            v.rectTransform.anchorMin = new Vector2(0.4f, 0);
            v.rectTransform.anchorMax = new Vector2(1, 1);
            v.rectTransform.offsetMin = Vector2.zero;
            v.rectTransform.offsetMax = new Vector2(-4, 0);
            v.resizeTextForBestFit = true;
            v.resizeTextMinSize = 14;
            v.resizeTextMaxSize = 21;
        }
    }
}
