using System;
using System.Collections.Generic;
using Apogee.Core;
using Apogee.Parts;
using Apogee.Rendering;
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
        int pointerId = -1;
        int pressedPartUid = -1;
        bool dragMoveActive;
        GameObject envRoot, gridBackground;
        public bool Mode2D { get; private set; }
        Vector2 pointerStart, lastMouse;
        float pointerStartTime;

        void Start()
        {
            settings = AppRoot.Instance.Settings;
            audioMgr = AppRoot.Instance.Audio;
            dialogs = AppRoot.Instance.Dialogs;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Light sun;
            envRoot = BuilderEnvironment.Build(out sun);
            gridBackground = BuildGrid();
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
            pointerId = touch && Input.touchCount > 0 ? Input.GetTouch(0).fingerId : -1;
            pointerStartTime = Time.unscaledTime;
            dragMoveActive = false;
            pressedPartUid = -1;
            if (IsHolding) { UpdateHeldPointer(pos, touch); return; }
            // In 2D mode, pressing on a part and dragging picks it up (like Spaceflight Simulator).
            if (Mode2D) pressedPartUid = PartAt(pos);
        }

        void PointerMove(Vector2 pos, Vector2 delta)
        {
            if (!pointerActive) return;
            if ((pos - pointerStart).magnitude > TapSlop) pointerMoved = true;
            if (IsHolding) { UpdateHeldPointer(pos, pointerIsTouch); return; }
            if (pointerMoved && !multiTouch)
            {
                if (pressedPartUid >= 0)
                {
                    int uid = pressedPartUid;
                    pressedPartUid = -1;
                    if (BeginDragMove(uid)) { UpdateHeldPointer(pos, pointerIsTouch); return; }
                }
                cam.Rotate(delta); // pans in 2D mode
            }
        }

        void PointerUp(Vector2 pos)
        {
            if (!pointerActive) return;
            pointerActive = false;
            if (multiTouch) return;
            if (IsHolding)
            {
                if (dragMoveActive && OverUI(pointerId))
                {
                    // Dropped back on the parts panel: delete it.
                    dragMoveActive = false;
                    held = null;
                    view.ClearGhost();
                    view.ShowNodes(null, 0f);
                    audioMgr.Play(Sfx.Detach);
                    dialogs.Toast("Part removed. Undo brings it back.", Theme.Muted, 2f);
                    if (HoldChanged != null) HoldChanged();
                    Refresh();
                    return;
                }
                UpdateHeldPointer(pos, pointerIsTouch);
                if (!TryPlace())
                {
                    if (dragMoveActive)
                    {
                        // Not dropped on a node: put the part back where it was.
                        dragMoveActive = false;
                        held = null;
                        view.ClearGhost();
                        view.ShowNodes(null, 0f);
                        Undo();
                        if (HoldChanged != null) HoldChanged();
                        PlacementHint();
                    }
                    else PlacementHint();
                }
                dragMoveActive = false;
                return;
            }
            if (!pointerMoved && Time.unscaledTime - pointerStartTime < 0.45f) TapSelect(pos);
        }

        int PartAt(Vector2 pos)
        {
            var ray = cam.Cam.ScreenPointToRay(pos);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 3000f, 1 << RocketView.PartLayer))
            {
                var pv = hit.collider.GetComponent<PartView>();
                if (pv != null) return pv.uid;
            }
            return -1;
        }

        /// <summary>Picks a part up off the rocket for dragging. The root part (which holds everything) can't be lifted.</summary>
        bool BeginDragMove(int uid)
        {
            var p = Bp.Find(uid);
            if (p == null) return false;
            if (p.mode == AttachMode.Root && Bp.parts.Count > 1) return false;
            PushUndo();
            string error;
            var h = RocketAssembly.Detach(Bp, uid, out error);
            if (h == null) { undo.RemoveAt(undo.Count - 1); return false; }
            Refresh();
            audioMgr.Play(Sfx.Detach, 0.6f);
            StartHold(h, false);
            dragMoveActive = true;
            return true;
        }

        // ------------------------------------------------------------------ 2D / 3D

        GameObject BuildGrid()
        {
            var mat = MaterialLibrary.Custom("Ground");
            mat.SetTexture("_MainTex", BlueprintTexture());
            mat.SetTextureScale("_MainTex", new Vector2(100f, 100f));
            mat.SetFloat("_Detail", 1f);
            mat.SetFloat("_Light", 0.8f);
            var m = new Mesh { name = "Grid" };
            m.SetVertices(new List<Vector3> { new Vector3(-200, -40, 15), new Vector3(200, -40, 15), new Vector3(200, 360, 15), new Vector3(-200, 360, 15) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            var c = new Color(0.55f, 0.7f, 1f);
            m.SetColors(new List<Color> { c, c, c, c });
            m.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            var go = new GameObject("Blueprint Grid");
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.SetActive(false);
            return go;
        }

        static Texture2D BlueprintTexture()
        {
            int s = 64;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    bool major = x == 0 || y == 0;
                    bool minor = x % 16 == 0 || y % 16 == 0;
                    byte v = major ? (byte)150 : minor ? (byte)112 : (byte)92;
                    px[y * s + x] = new Color32((byte)(v * 0.42f), (byte)(v * 0.62f), v, 255);
                }
            var t = new Texture2D(s, s, TextureFormat.RGBA32, true);
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return t;
        }

        public void ToggleMode2D()
        {
            Mode2D = !Mode2D;
            cam.SetMode2D(Mode2D);
            cam.Cam.backgroundColor = Mode2D ? new Color(0.14f, 0.27f, 0.48f) : new Color(0.12f, 0.13f, 0.15f);
            gridBackground.SetActive(Mode2D);
            for (int i = 0; i < envRoot.transform.childCount; i++)
            {
                var ch = envRoot.transform.GetChild(i).gameObject;
                if (ch.GetComponent<Light>() != null || ch.GetComponent<ReflectionProbe>() != null) continue;
                ch.SetActive(!Mode2D);
            }
            RenderSettings.fog = !Mode2D;
            cam.Frame(view.WorldBounds);
            dialogs.Toast(Mode2D ? "2D build mode: drag parts to move them, drop on the parts list to delete." : "3D build mode", Theme.Accent, 2.4f);
            if (Changed != null) Changed();
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
            held.mirror = MirrorMode;
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
            if (Mode2D)
            {
                var p2 = ray.origin + ray.direction * ((0f - ray.origin.z) / (Mathf.Abs(ray.direction.z) > 1e-5f ? ray.direction.z : 1e-5f));
                p2.y = Mathf.Max(p2.y, BuilderEnvironment.PadTop + 0.5f);
                return p2;
            }
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var plane = new Plane(-fwd.normalized, new Vector3(0f, 0f, 0f));
            float d;
            Vector3 p = plane.Raycast(ray, out d) ? ray.GetPoint(d) : cam.transform.position + ray.direction * 25f;
            p.y = Mathf.Max(p.y, BuilderEnvironment.PadTop + 1f);
            return p;
        }

        /// <summary>
        /// Finds where the held part would attach. The ghost is centred on the (lifted) pointer, and we compare
        /// the ghost's own top/bottom nodes with every free node on the rocket, so long parts snap naturally
        /// when their ends meet. Surface (radial) attachment uses a ray hit on the part under the pointer.
        /// </summary>
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
                Vector3 p = FreePoint(sp);
                var dims = PartDims.Of(held.Root);
                Vector3 heldTop = p + Vector3.up * dims.height * 0.5f;
                Vector3 heldBottom = p - Vector3.up * dims.height * 0.5f;
                foreach (var c in stackCandidates)
                {
                    Vector3 node = view.ToWorld(c.nodePosition);
                    Vector3 mine = c.mode == AttachMode.Below ? heldTop : c.mode == AttachMode.Above ? heldBottom : p;
                    float dist = c.mode == AttachMode.Side ? Mathf.Max(0f, Vector3.Distance(mine, node) - dims.Radius) : Vector3.Distance(mine, node);
                    if (dist < bestD) { bestD = dist; best = c; }
                }
            }
            float threshold = Mathf.Max(1.2f, cam.ViewHeight * (Mode2D ? 0.12f : 0.09f));
            if (best.valid && bestD < threshold && (!surface.valid || bestD < threshold * 0.4f)) return best;
            if (surface.valid) return surface;

            // Stack parts (tanks, engines, boosters, girders) can also stand against the side of another part.
            if (RocketAssembly.CanStack(def) && !def.radialMount)
            {
                Vector3 rp = view.ToRocket(FreePoint(sp));
                var flank = RocketAssembly.FlankCandidate(Bp, poses, PartDims.Of(held.Root), rp, threshold * 0.8f);
                if (flank.valid && (!best.valid || bestD > threshold * 0.5f)) return flank;
            }
            if (best.valid && bestD < threshold) return best;
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
                dialogs.Toast("Drop it on a green node, or right beside a tank to attach it to the side.", Theme.Warn);
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
            if (held != null && (held.Root.Def.radialMount || RocketAssembly.CanStack(held.Root.Def)))
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

        /// <summary>Copies the selected part (with anything attached to it). Stack parts are attached straight away
        /// to the nearest free node — usually right below the original; radial parts are handed to you to place.</summary>
        public void DuplicateSelected()
        {
            var p = Selected;
            if (p == null) return;
            var h = RocketAssembly.CopySubtree(Bp, p.uid);
            if (RocketAssembly.CanStack(h.Root.Def) && h.Root.Def.hasTopNode)
            {
                PartPose pose;
                poses.TryGetValue(p.uid, out pose);
                Vector3 bottom = RocketLayout.BottomNode(p, pose), top = RocketLayout.TopNode(p, pose);
                var cands = RocketAssembly.StackCandidates(Bp, poses, h.Root.Def);
                var best = new AttachCandidate();
                float bestScore = float.MaxValue;
                foreach (var c in cands)
                {
                    if (c.mode == AttachMode.Side) continue;
                    float score = c.mode == AttachMode.Below ? Vector3.Distance(c.nodePosition, bottom) : Vector3.Distance(c.nodePosition, top) + 0.5f;
                    if (score < bestScore) { bestScore = score; best = c; }
                }
                if (best.valid)
                {
                    PushUndo();
                    var roots = RocketAssembly.Attach(Bp, h, best);
                    if (roots.Count > 0)
                    {
                        audioMgr.Play(Sfx.Snap);
                        Refresh();
                        Select(roots[0]);
                        dialogs.Toast("Duplicated " + p.Def.displayName + ".", Theme.Good, 1.6f);
                        return;
                    }
                    undo.RemoveAt(undo.Count - 1);
                }
            }
            StartHold(h, false);
            dialogs.Toast("Copy ready — drag it onto the rocket.", Theme.Accent, 2f);
        }

        /// <summary>Hands the design to the flight scene.</summary>
        public void Launch()
        {
            CancelHold();
            if (Bp.IsEmpty) { audioMgr.Play(Sfx.Error); dialogs.Toast("Build a rocket first.", Theme.Warn); return; }
            bool engine = false;
            foreach (var q in Bp.parts) if (q.Def.IsEngine) engine = true;
            if (!engine) { audioMgr.Play(Sfx.Error); dialogs.Toast("Add an engine before launching.", Theme.Warn); return; }
            StagingSystem.Restage(Bp);
            SaveSystem.Autosave(Bp);
            Session.flightBlueprint = Bp.DeepCopy();
            AppRoot.Instance.LoadScene(Scenes.Flight);
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

        /// <summary>Mirror mode: side parts are also placed as a mirror image on the opposite side.</summary>
        public bool MirrorMode { get; private set; }

        public void ToggleMirror()
        {
            MirrorMode = !MirrorMode;
            if (held != null) held.mirror = MirrorMode;
            audioMgr.Play(Sfx.Click);
            dialogs.Toast(MirrorMode ? "Mirror on: side parts get a mirror-image twin" : "Mirror off", Theme.Accent, 1.8f);
            if (Changed != null) Changed();
            if (HoldChanged != null) HoldChanged();
        }

        /// <summary>See-through view of the whole rocket: propellant, engines, crew and payload inside.</summary>
        public bool InsideView { get; private set; }

        public void ToggleInside()
        {
            InsideView = !InsideView;
            view.SetXRay(InsideView);
            audioMgr.Play(Sfx.Click);
            dialogs.Toast(InsideView ? "Inside view: fuel (orange) and oxidiser (blue) tanks, engines and crew." : "Inside view off.", Theme.Accent, 2.2f);
            if (Changed != null) Changed();
        }

        /// <summary>Paints the selected part and its symmetry copies (-1 = factory finish).</summary>
        public void PaintSelected(int paint)
        {
            var p = Selected;
            if (p == null) return;
            PushUndo();
            int uid = p.uid;
            foreach (var m in Bp.SymmetryMembers(p)) m.paint = paint;
            Refresh();
            Select(uid);
        }

        /// <summary>Asks for the text shown on the selected Logo Panel (and its symmetry copies).</summary>
        public void EditLogo()
        {
            var p = Selected;
            if (p == null || !p.Def.isDecal) return;
            int uid = p.uid;
            dialogs.Prompt("Logo text", string.IsNullOrEmpty(p.label) ? "APOGEE" : p.label, "Apply", text =>
            {
                var q = Bp.Find(uid);
                if (q == null) return;
                PushUndo();
                foreach (var m in Bp.SymmetryMembers(q)) m.label = (text ?? "").Trim().ToUpperInvariant();
                Refresh();
                Select(uid);
            });
        }

        public void PaintAll(int paint)
        {
            if (Bp.IsEmpty) return;
            PushUndo();
            int uid = SelectedUid;
            foreach (var m in Bp.parts) if (m.Def.Paintable) m.paint = paint;
            Refresh();
            Select(uid);
            dialogs.Toast(paint < 0 ? "Factory colours restored." : "Whole rocket painted " + PartPaint.Names[paint] + ".", Theme.Good, 1.8f);
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
