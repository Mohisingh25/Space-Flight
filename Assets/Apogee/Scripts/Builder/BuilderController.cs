using System;
using System.Collections.Generic;
using Apogee.Core;
using Apogee.Parts;
using Apogee.Rocket;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Apogee.Builder
{
    /// <summary>
    /// Scene entry point and brain of the Spacecraft Designer: owns the blueprint, handles touch/mouse
    /// input for camera, picking and drag-and-drop placement with snapping and symmetry, runs undo/redo,
    /// autosave, saving/loading and recomputes the live engineering statistics after every edit.
    /// </summary>
    public class BuilderController : MonoBehaviour
    {
        public static readonly int[] SymmetryModes = { 1, 2, 3, 4, 6, 8 };

        public RocketBlueprint Bp { get; private set; }
        public RocketStats Stats { get; private set; }
        public int SelectedUid { get; private set; }
        public bool IsHolding { get { return held != null; } }
        public HeldAssembly Held { get { return held; } }
        public int Symmetry { get { return SymmetryModes[symmetryIndex]; } }
        public bool StagingOpen { get; set; }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public string SavedPath { get; private set; }

        public event Action Changed;
        public event Action SelectionChanged;
        public event Action HoldChanged;
        public event Action StatsChanged;

        OrbitCamera cam;
        RocketView view;
        BuilderUI ui;
        GameSettings settings;
        AudioManager audioMgr;
        Dialogs dialogs;

        HeldAssembly held;
        bool holdFromListDrag;
        AttachCandidate candidate;
        string ghostKey;
        Dictionary<int, PartPose> poses = new Dictionary<int, PartPose>();
        List<AttachCandidate> stackCandidates = new List<AttachCandidate>();

        readonly List<string> undo = new List<string>();
        readonly List<string> redo = new List<string>();
        int symmetryIndex;
        float autosaveTimer = -1f;
        float lastHintTime = -10f;
        bool dirtySinceSave;

        // pointer state
        bool pointerActive, pointerMoved, multiTouch, pointerIsTouch;
        Vector2 pointerStart, lastMouse;
        float pointerStartTime;

        void Start()
        {
            settings = AppRoot.Instance.Settings;
            audioMgr = AppRoot.Instance.Audio;
            dialogs = AppRoot.Instance.Dialogs;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Light sun;
            BuilderEnvironment.Build(out sun);
            var camGo = new GameObject("Builder Camera");
            cam = camGo.AddComponent<OrbitCamera>();
            cam.sensitivity = settings.cameraSensitivity;

            view = new GameObject("Rocket View").AddComponent<RocketView>();
            view.baseHeight = BuilderEnvironment.PadTop;

            ui = gameObject.AddComponent<BuilderUI>();
            ui.Init(this);

            var thumbs = gameObject.AddComponent<PartThumbnails>();
            thumbs.Ready += ui.OnThumbnail;
            thumbs.RenderAll();

            SelectedUid = -1;
            Bp = LoadEntryBlueprint();
            audioMgr.SetAmbience(Ambience.Hangar);
            Refresh();
            cam.Frame(view.WorldBounds, true);
        }

        RocketBlueprint LoadEntryBlueprint()
        {
            RocketBlueprint bp = null;
            switch (Session.builderEntry)
            {
                case BuilderEntry.OpenBlueprint:
                    bp = Session.blueprintToOpen;
                    SavedPath = Session.blueprintPath;
                    break;
                case BuilderEntry.Continue:
                    bp = SaveSystem.LoadAutosave();
                    break;
            }
            Session.blueprintToOpen = null;
            Session.blueprintPath = null;
            if (bp == null) bp = new RocketBlueprint();
            StagingSystem.Restage(bp);
            return bp;
        }

        // ================================================================== frame loop

        void Update()
        {
            if (AppRoot.Instance.BackPressed()) OnBack();

            if (autosaveTimer > 0f)
            {
                autosaveTimer -= Time.unscaledDeltaTime;
                if (autosaveTimer <= 0f) SaveSystem.Autosave(Bp);
            }

            if (dialogs.HasModal || holdFromListDrag) return;
            if (Input.touchCount > 0) HandleTouches();
            else HandleMouse();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Bp != null) SaveSystem.Autosave(Bp);
        }

        void OnDestroy()
        {
            if (Bp != null) SaveSystem.Autosave(Bp);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        void OnBack()
        {
            if (IsHolding) { CancelHold(); return; }
            if (StagingOpen) { StagingOpen = false; ui.Refresh(); return; }
            if (SelectedUid >= 0) { Select(-1); return; }
            BackToMenu();
        }

        // ================================================================== input

        static bool OverUI(int pointerId)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        void HandleTouches()
        {
            if (Input.touchCount >= 2)
            {
                multiTouch = true;
                if (!pointerActive) return;
                var t0 = Input.GetTouch(0);
                var t1 = Input.GetTouch(1);
                if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
                {
                    Vector2 p0 = t0.position - t0.deltaPosition, p1 = t1.position - t1.deltaPosition;
                    float prev = (p0 - p1).magnitude, cur = (t0.position - t1.position).magnitude;
                    if (prev > 2f && cur > 2f) cam.Zoom(prev / cur);
                    cam.Pan((t0.deltaPosition + t1.deltaPosition) * 0.5f);
                }
                return;
            }
            var t = Input.GetTouch(0);
            switch (t.phase)
            {
                case TouchPhase.Began:
                    multiTouch = false;
                    PointerDown(t.position, OverUI(t.fingerId), true);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    PointerMove(t.position, t.deltaPosition);
                    break;
                case TouchPhase.Ended:
                    PointerUp(t.position);
                    break;
                case TouchPhase.Canceled:
                    pointerActive = false;
                    break;
            }
        }

        void HandleMouse()
        {
            Vector2 m = Input.mousePosition;
            Vector2 delta = m - lastMouse;
            if (Input.GetMouseButtonDown(0)) { multiTouch = false; PointerDown(m, OverUI(-1), false); }
            else if (Input.GetMouseButton(0)) PointerMove(m, delta);
            if (Input.GetMouseButtonUp(0)) PointerUp(m);

            if (!OverUI(-1))
            {
                if (Input.GetMouseButton(1)) cam.Rotate(delta);
                if (Input.GetMouseButton(2)) cam.Pan(delta);
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) cam.Zoom(Mathf.Pow(0.88f, wheel));
            }
            if (IsHolding && !Input.GetMouseButton(0) && !Input.touchSupported) UpdateHeldPointer(m, false);
            lastMouse = m;
        }

        float TapSlop { get { return Mathf.Max(10f, Screen.dpi > 0 ? Screen.dpi * 0.08f : Screen.height * 0.012f); } }

        void PointerDown(Vector2 pos, bool overUI, bool touch)
        {
            pointerActive = !overUI;
            if (!pointerActive) return;
            pointerStart = pos;
            pointerMoved = false;
            pointerIsTouch = touch;
            pointerStartTime = Time.unscaledTime;
            if (IsHolding) UpdateHeldPointer(pos, touch);
        }

        void PointerMove(Vector2 pos, Vector2 delta)
        {
            if (!pointerActive) return;
            if ((pos - pointerStart).magnitude > TapSlop) pointerMoved = true;
            if (IsHolding) UpdateHeldPointer(pos, pointerIsTouch);
            else if (pointerMoved && !multiTouch) cam.Rotate(delta);
        }

        void PointerUp(Vector2 pos)
        {
            if (!pointerActive) return;
            pointerActive = false;
            if (multiTouch) return;
            if (IsHolding)
            {
                UpdateHeldPointer(pos, pointerIsTouch);
                if (!TryPlace()) PlacementHint();
                return;
            }
            if (!pointerMoved && Time.unscaledTime - pointerStartTime < 0.45f) TapSelect(pos);
        }

        void TapSelect(Vector2 pos)
        {
            var ray = cam.Cam.ScreenPointToRay(pos);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 3000f, 1 << RocketView.PartLayer))
            {
                var pv = hit.collider.GetComponent<PartView>();
                if (pv != null) { Select(pv.uid); return; }
            }
            Select(-1);
        }

        // ================================================================== selection

        public void Select(int uid)
        {
            if (uid >= 0 && Bp.Find(uid) == null) uid = -1;
            SelectedUid = uid;
            ApplyHighlight();
            if (uid >= 0) audioMgr.Play(Sfx.Click, 0.6f);
            if (SelectionChanged != null) SelectionChanged();
        }

        void ApplyHighlight()
        {
            var set = new HashSet<int>();
            var p = SelectedUid >= 0 ? Bp.Find(SelectedUid) : null;
            if (p != null) foreach (var m in Bp.SymmetryMembers(p)) set.Add(m.uid);
            view.Highlight(set, new Color(0.25f, 0.85f, 1f));
        }

        public PartInstance Selected { get { return SelectedUid >= 0 ? Bp.Find(SelectedUid) : null; } }

        // ================================================================== holding & placement

        public void BeginHoldFromList(PartDefinition def)
        {
            StartHold(HeldAssembly.ForNewPart(def), false);
            if (!Bp.IsEmpty)
                dialogs.Toast("Drag onto a green node or a part's surface to attach.", Theme.Accent, 2.2f);
            else
                dialogs.Toast("Tap the platform to place your first part.", Theme.Accent, 2.2f);
        }

        public void BeginDragFromList(PartDefinition def, Vector2 pos, int pointerId)
        {
            StartHold(HeldAssembly.ForNewPart(def), true);
            pointerIsTouch = pointerId >= 0;
            UpdateHeldPointer(pos, pointerIsTouch);
        }

        public void DragFromList(Vector2 pos)
        {
            if (IsHolding) UpdateHeldPointer(pos, pointerIsTouch);
        }

        public void EndDragFromList(Vector2 pos, int pointerId)
        {
            if (!IsHolding) { holdFromListDrag = false; return; }
            holdFromListDrag = false;
            if (OverUI(pointerId)) { CancelHold(); return; }
            UpdateHeldPointer(pos, pointerId >= 0);
            if (!TryPlace())
            {
                CancelHold();
                PlacementHint();
            }
        }

        void StartHold(HeldAssembly h, bool fromDrag)
        {
            held = h;
            holdFromListDrag = fromDrag;
            held.symmetry = held.symmetry > 1 ? held.symmetry : Symmetry;
            ghostKey = null;
            candidate = new AttachCandidate();
            Select(-1);
            RefreshNodes();
            if (!fromDrag) ShowFreeGhost(DefaultGhostPoint());
            if (HoldChanged != null) HoldChanged();
        }

        public void CancelHold()
        {
            if (held == null) return;
            held = null;
            holdFromListDrag = false;
            view.ClearGhost();
            view.ShowNodes(null, 0f);
            audioMgr.Play(Sfx.Detach, 0.5f);
            if (HoldChanged != null) HoldChanged();
        }

        void RefreshNodes()
        {
            if (held == null || Bp.IsEmpty) { view.ShowNodes(null, 0f); return; }
            stackCandidates = RocketAssembly.StackCandidates(Bp, poses, held.Root.Def);
            var pts = new List<Vector3>();
            if (RocketAssembly.CanStack(held.Root.Def)) foreach (var c in stackCandidates) pts.Add(c.nodePosition);
            view.ShowNodes(pts, Mathf.Clamp(view.WorldBounds.size.y * 0.02f, 0.25f, 0.8f));
        }

        Vector3 DefaultGhostPoint()
        {
            if (Bp.IsEmpty) return EmptyPadPoint();
            var b = view.WorldBounds;
            return new Vector3(b.max.x + 3f, b.center.y, 0f);
        }

        Vector3 EmptyPadPoint()
        {
            var hb = held.AsBlueprint();
            var lb = RocketLayout.ComputeBounds(hb, RocketLayout.Compute(hb));
            return new Vector3(0f, BuilderEnvironment.PadTop - lb.min.y, 0f);
        }

        Vector2 SnapPoint(Vector2 screen, bool touch)
        {
            // Lift the reference point above the fingertip so the part isn't hidden under the finger.
            return touch ? screen + new Vector2(0f, Screen.height * 0.07f) : screen;
        }

        void UpdateHeldPointer(Vector2 screen, bool touch)
        {
            if (held == null) return;
            Vector2 sp = SnapPoint(screen, touch);
            if (Bp.IsEmpty)
            {
                candidate = new AttachCandidate();
                ShowFreeGhost(EmptyPadPoint());
                return;
            }
            var c = FindCandidate(sp);
            if (c.valid)
            {
                string key = c.parentUid + "|" + c.mode + "|" + c.angle.ToString("0") + "|" + c.heightFrac.ToString("0.00") + "|" + held.symmetry;
                if (key != ghostKey)
                {
                    ghostKey = key;
                    var preview = Bp.DeepCopy();
                    int firstNew = preview.nextUid;
                    RocketAssembly.Attach(preview, held, c);
                    var newUids = new HashSet<int>();
                    foreach (var p in preview.parts) if (p.uid >= firstNew) newUids.Add(p.uid);
                    view.ShowGhostSnapped(preview, newUids);
                }
                candidate = c;
            }
            else
            {
                candidate = c;
                ShowFreeGhost(FreePoint(sp));
            }
        }

        void ShowFreeGhost(Vector3 world)
        {
            if (ghostKey != "free")
            {
                ghostKey = "free";
                view.ShowGhostFree(held, world);
            }
            else view.MoveGhost(world);
        }

        Vector3 FreePoint(Vector2 sp)
        {
            var ray = cam.Cam.ScreenPointToRay(sp);
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var plane = new Plane(-fwd.normalized, new Vector3(0f, 0f, 0f));
            float d;
            Vector3 p = plane.Raycast(ray, out d) ? ray.GetPoint(d) : cam.transform.position + ray.direction * 25f;
            p.y = Mathf.Max(p.y, BuilderEnvironment.PadTop + 1f);
            return p;
        }

        AttachCandidate FindCandidate(Vector2 sp)
        {
            var none = new AttachCandidate();
            if (held == null || Bp.IsEmpty) return none;
            var def = held.Root.Def;
            var surface = none;
            if (def.radialMount)
            {
                var ray = cam.Cam.ScreenPointToRay(sp);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 3000f, 1 << RocketView.PartLayer))
                {
                    var pv = hit.collider.GetComponent<PartView>();
                    if (pv != null) surface = RocketAssembly.SurfaceCandidate(Bp, poses, pv.uid, view.ToRocket(hit.point));
                }
            }
            var best = none;
            float bestD = float.MaxValue;
            if (RocketAssembly.CanStack(def))
            {
                foreach (var c in stackCandidates)
                {
                    var s = cam.Cam.WorldToScreenPoint(view.ToWorld(c.nodePosition));
                    if (s.z < 0f) continue;
                    float dist = (new Vector2(s.x, s.y) - sp).magnitude;
                    if (dist < bestD) { bestD = dist; best = c; }
                }
            }
            float threshold = Screen.height * 0.1f;
            if (best.valid && bestD < threshold && (!surface.valid || bestD < threshold * 0.5f)) return best;
            if (surface.valid) return surface;
            return none;
        }

        bool TryPlace()
        {
            if (held == null) return false;
            if (Bp.IsEmpty)
            {
                PushUndo();
                RocketAssembly.PlaceAsRoot(Bp, held);
                var root = Bp.Root;
                FinishPlacement(root != null ? root.uid : -1);
                return true;
            }
            if (!candidate.valid) return false;
            PushUndo();
            var roots = RocketAssembly.Attach(Bp, held, candidate);
            if (roots.Count == 0)
            {
                undo.RemoveAt(undo.Count - 1);
                audioMgr.Play(Sfx.Error);
                dialogs.Toast("That node is already in use.", Theme.Warn);
                return false;
            }
            FinishPlacement(roots[0]);
            return true;
        }

        void FinishPlacement(int selectUid)
        {
            held = null;
            holdFromListDrag = false;
            view.ClearGhost();
            view.ShowNodes(null, 0f);
            audioMgr.Play(Sfx.Snap);
            Refresh();
            Select(selectUid);
            if (HoldChanged != null) HoldChanged();
        }

        void PlacementHint()
        {
            if (Time.unscaledTime - lastHintTime < 2.5f) return;
            lastHintTime = Time.unscaledTime;
            audioMgr.Play(Sfx.Error, 0.5f);
            var def = held != null ? held.Root.Def : null;
            if (def != null && !RocketAssembly.CanStack(def))
                dialogs.Toast("Radial parts attach to the side of tanks, capsules and boosters.", Theme.Warn);
            else
                dialogs.Toast("Drop the part on a green attach node.", Theme.Warn);
        }

        // ================================================================== edit commands

        void PushUndo()
        {
            undo.Add(Bp.ToJson());
            if (undo.Count > 60) undo.RemoveAt(0);
            redo.Clear();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            CancelHold();
            redo.Add(Bp.ToJson());
            Bp = RocketBlueprint.FromJson(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
            Select(-1);
            Refresh();
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            CancelHold();
            undo.Add(Bp.ToJson());
            Bp = RocketBlueprint.FromJson(redo[redo.Count - 1]);
            redo.RemoveAt(redo.Count - 1);
            Select(-1);
            Refresh();
        }

        public void CycleSymmetry()
        {
            symmetryIndex = (symmetryIndex + 1) % SymmetryModes.Length;
            if (held != null && held.Root.Def.radialMount)
            {
                held.symmetry = Symmetry;
                ghostKey = null;
            }
            if (Changed != null) Changed();
        }

        public void MoveSelected()
        {
            var p = Selected;
            if (p == null) return;
            PushUndo();
            string error;
            var h = RocketAssembly.Detach(Bp, p.uid, out error);
            if (h == null)
            {
                undo.RemoveAt(undo.Count - 1);
                audioMgr.Play(Sfx.Error);
                dialogs.Toast(error, Theme.Warn, 3.5f);
                return;
            }
            Refresh();
            audioMgr.Play(Sfx.Detach);
            StartHold(h, false);
            dialogs.Toast("Picked up " + h.parts.Count + (h.parts.Count == 1 ? " part" : " parts") + ". Drag it to a new spot.", Theme.Accent, 2.2f);
        }

        public void DuplicateSelected()
        {
            var p = Selected;
            if (p == null) return;
            var h = RocketAssembly.CopySubtree(Bp, p.uid);
            StartHold(h, false);
            dialogs.Toast("Copy ready — drag it onto the rocket.", Theme.Accent, 2f);
        }

        public void DeleteSelected()
        {
            var p = Selected;
            if (p == null) return;
            PushUndo();
            int n = RocketAssembly.Remove(Bp, p.uid);
            audioMgr.Play(Sfx.Detach);
            Select(-1);
            Refresh();
            dialogs.Toast("Removed " + n + (n == 1 ? " part" : " parts") + ". Undo is available.", Theme.Muted, 2f);
        }

        public void RotateSelected()
        {
            var p = Selected;
            if (p == null) return;
            PushUndo();
            RocketAssembly.Rotate(Bp, p.uid);
            Refresh();
        }

        public void ResizeSelected(int delta)
        {
            var p = Selected;
            if (p == null) return;
            PushUndo();
            if (!RocketAssembly.Resize(Bp, p.uid, delta)) { undo.RemoveAt(undo.Count - 1); audioMgr.Play(Sfx.Error, 0.6f); return; }
            Refresh();
        }

        public void StretchSelected(int delta)
        {
            var p = Selected;
            if (p == null) return;
            PushUndo();
            if (!RocketAssembly.Stretch(Bp, p.uid, delta)) { undo.RemoveAt(undo.Count - 1); audioMgr.Play(Sfx.Error, 0.6f); return; }
            Refresh();
        }

        float payloadEditStart = -1f;

        public void SetPayload(float tonnes, bool final)
        {
            var p = Selected;
            if (p == null) return;
            if (payloadEditStart < 0f) { PushUndo(); payloadEditStart = p.payloadMass; }
            RocketAssembly.SetPayload(Bp, p.uid, Mathf.Round(tonnes * 10f) / 10f);
            if (final)
            {
                payloadEditStart = -1f;
                Refresh(false);
            }
            else
            {
                // While the slider is being dragged only the numbers update (the selection bar stays put).
                Refresh(false, false);
                if (StatsChanged != null) StatsChanged();
            }
        }

        public void MoveStage(int uid, int delta)
        {
            if (Bp.Find(uid) == null) return;
            PushUndo();
            StagingSystem.MovePart(Bp, uid, delta);
            Refresh();
        }

        public void ResetStaging()
        {
            PushUndo();
            StagingSystem.ResetToAuto(Bp);
            Refresh();
            dialogs.Toast("Staging reset to automatic order.", Theme.Accent);
        }

        public void ToggleMarkers(bool on)
        {
            settings.showBalanceMarkers = on;
            settings.Save();
            view.ShowMarkers(Stats, on);
        }

        // ================================================================== file commands

        static bool NeedsName(RocketBlueprint bp)
        {
            return string.IsNullOrEmpty(bp.name) || bp.name == "Untitled Rocket" || bp.name.Contains("(example)");
        }

        public void Save()
        {
            if (Bp.IsEmpty) { dialogs.Toast("Nothing to save yet — add some parts first.", Theme.Warn); return; }
            if (NeedsName(Bp)) { SaveAs(); return; }
            DoSave();
        }

        public void SaveAs()
        {
            if (Bp.IsEmpty) { dialogs.Toast("Nothing to save yet — add some parts first.", Theme.Warn); return; }
            string suggestion = NeedsName(Bp) ? (Bp.name.Contains("(example)") ? Bp.name.Replace(" (example)", " Mk2") : "") : Bp.name;
            dialogs.Prompt("Save design as", suggestion, "Save", name =>
            {
                Bp.name = name;
                if (SaveSystem.Exists(name) && SaveSystem.PathFor(name) != SavedPath)
                    dialogs.Confirm("Replace existing design?", "A design called \"" + name + "\" already exists. Replace it?", "Replace", DoSave, true);
                else DoSave();
            });
        }

        void DoSave()
        {
            string error;
            if (SaveSystem.SaveBlueprint(Bp, out error))
            {
                SavedPath = SaveSystem.PathFor(Bp.name);
                dirtySinceSave = false;
                dialogs.Toast("Saved \"" + Bp.name + "\"", Theme.Good);
                if (Changed != null) Changed();
            }
            else
            {
                audioMgr.Play(Sfx.Error);
                dialogs.Toast("Save failed: " + error, Theme.Bad, 4f);
            }
        }

        public void NewDesign()
        {
            Action go = () =>
            {
                PushUndo();
                Bp = new RocketBlueprint();
                SavedPath = null;
                dirtySinceSave = false;
                CancelHold();
                Select(-1);
                Refresh();
                cam.Frame(view.WorldBounds);
            };
            if (Bp.IsEmpty) { go(); return; }
            dialogs.Confirm("Start a new design?", dirtySinceSave ? "Your current design has unsaved changes. You can still get it back with Undo." : "The current design will be cleared from the assembly platform.", "New design", go, true);
        }

        public void OpenLoadDialog()
        {
            dialogs.Custom("Open design", 1180, 820, (body, close) =>
            {
                BlueprintBrowser.Build(body, (bp, path) =>
                {
                    close();
                    PushUndo();
                    Bp = bp;
                    StagingSystem.Restage(Bp);
                    SavedPath = path;
                    dirtySinceSave = false;
                    CancelHold();
                    Select(-1);
                    Refresh();
                    cam.Frame(view.WorldBounds);
                    dialogs.Toast("Loaded \"" + bp.name + "\"", Theme.Good);
                }, close);
            });
        }

        public void BackToMenu()
        {
            CancelHold();
            SaveSystem.Autosave(Bp);
            AppRoot.Instance.LoadScene(Scenes.MainMenu);
        }

        // ================================================================== refresh

        /// <summary>Rebuilds the 3D vehicle, re-runs the analysis and updates the UI.</summary>
        void Refresh(bool rebuildView = true, bool notify = true)
        {
            poses = RocketLayout.Compute(Bp);
            if (rebuildView) view.Rebuild(Bp);
            Stats = RocketAnalyzer.Analyze(Bp);
            view.ShowMarkers(Stats, settings.showBalanceMarkers);
            ApplyHighlight();
            if (held != null) RefreshNodes();
            if (!Bp.IsEmpty) cam.Follow(view.WorldBounds);
            dirtySinceSave = true;
            autosaveTimer = 0.6f;
            if (notify && Changed != null) Changed();
        }
    }
}
