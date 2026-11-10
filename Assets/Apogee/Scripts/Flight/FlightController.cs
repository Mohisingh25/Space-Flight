using System;
using Apogee.Core;
using Apogee.Game;
using Apogee.Rocket;
using Apogee.Simulation;
using Apogee.Astro;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Apogee.Flight
{
    /// <summary>
    /// Runs a flight: crawler rollout from the assembly building, countdown and launch, then free flight
    /// anywhere in the Solar System with time warp, the 3D map, SAS, RCS, tapping parts, and the menus.
    /// </summary>
    public class FlightController : MonoBehaviour
    {
        public const double SiteLat = 0.0, SiteLon = -51.07;   // equatorial launch complex (Amazon delta)

        FlightVessel vessel;
        FlightWorld world;
        FlightHUD hud;
        MapView map;
        Dialogs dialogs;
        GameSettings settings;
        AudioSource engineAudio;
        RocketBlueprint launchBlueprint;

        static readonly float[] Warps = { 1, 2, 3, 4, 10, 100, 1000, 10000, 100000, 1000000 };
        const int MaxPhysicsWarp = 3;   // index of 4×
        int warpIndex;

        // pre-launch
        float crawlerEast = LaunchComplex.RolloutDistance;
        float lowering = -1f;            // 0..1 while the rocket is set down on the pad
        float countdown = -1f;
        bool staged0;
        float armOpen;

        void Start()
        {
            settings = AppRoot.Instance.Settings;
            dialogs = AppRoot.Instance.Dialogs;
            AppRoot.Instance.Audio.SetAmbience(Ambience.None);
            SolarSystem.Build();

            launchBlueprint = Session.flightBlueprint ?? ExampleRockets.Meridian();
            vessel = new FlightVessel(launchBlueprint.DeepCopy(), SolarSystem.Earth, SiteLat, SiteLon);
            // start in the morning at the launch site
            double sunLon = FlightVessel.SunFacingLongitude(SolarSystem.Earth, 0);
            double hourAngle = ((sunLon - (SiteLon + 40)) % 360 + 360) % 360;
            vessel.time = hourAngle / 360.0 * SolarSystem.Earth.rotationPeriod;
            vessel.status = FlightStatus.Rollout;
            vessel.PlaceEastOf(SiteLat, SiteLon, crawlerEast, LaunchComplex.CrawlerDeck);
            settings.ApplyCheats(vessel.cheats);
            if (Play.mode == GameMode.Sandbox)
            {
                switch (settings.cheatStart)
                {
                    case 1: vessel.PlaceInOrbit(SolarSystem.Earth, 250000); break;
                    case 2: vessel.PlaceInOrbit(SolarSystem.Moon, 100000); break;
                    case 3: vessel.PlaceInOrbit(SolarSystem.Mars, 300000); break;
                }
            }
            SetupMode();
            if (vessel.status == FlightStatus.Flying || vessel.body != SolarSystem.Earth) { staged0 = true; lowering = 1f; crawlerEast = 300f; }

            world = new GameObject("Flight World").AddComponent<FlightWorld>();
            world.Init(vessel);
            world.camMode = CamMode.Chase;
            world.camYaw = -60f; world.camPitch = 6f;
            world.camDistanceGoal = world.camDistance = Mathf.Clamp(vessel.length * 2.2f + 40f, 50f, 220f);

            hud = gameObject.AddComponent<FlightHUD>();
            hud.Init();
            map = new MapView(world.space, vessel, hud);
            hud.cheatBadge.gameObject.SetActive(vessel.cheats.Any);
            WireHud();

            vessel.OnMessage += m => hud.Message(m, 3f);
            vessel.OnDebris += d => AppRoot.Instance.Audio.Play(Sfx.Detach, 0.8f);
            vessel.OnExplosion += (p, s) => AppRoot.Instance.Audio.Play(Sfx.Explosion, Mathf.Clamp01(s / 20f));
            vessel.OnCrash += OnCrash;
            vessel.OnLanded += () =>
            {
                AppRoot.Instance.Audio.Play(Sfx.Snap);
                warpIndex = 0;
                double la, lo, d, b;
                vessel.body.LatLon(vessel.pos, vessel.time, out la, out lo);
                var site = LandingSite.Nearest(vessel.body, la, lo, out d, out b);
                hud.Message("Touchdown on " + vessel.body.name + "!" + (site != null ? "  " + FlightHUD.Dist(d) + " from " + site.name : ""), 6f);
                LogLanding(site, d);
            };
            vessel.OnSoiChange += b => { if (warpIndex > 6) warpIndex = 6; };

            engineAudio = gameObject.AddComponent<AudioSource>();
            engineAudio.clip = AppRoot.Instance.Audio.EngineLoop();
            engineAudio.loop = true;
            engineAudio.volume = 0f;
            engineAudio.Play();
            hud.Message(startMessage ?? (vessel.status == FlightStatus.Flying ? "Cheat start: in orbit around " + vessel.body.name : "Hold DRIVE to roll the rocket out to the pad"), 5f);
            engines = vessel.EngineList();
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < engines.Count; i++)
            {
                var p = vessel.state.byUid[engines[i]];
                names.Add("<b>" + (i + 1) + "</b>  " + p.Def.displayName);
            }
            hud.BuildEngineRows(names);
            hud.engineTab.gameObject.SetActive(engines.Count > 0);
            UIFactory.SetLabel(hud.speedButton, "×" + RolloutSpeeds[speedIndex]);
        }

        // ------------------------------------------------------------------ engine panel

        System.Collections.Generic.List<int> engines;
        bool enginePanelOpen;

        readonly System.Collections.Generic.HashSet<int> disarmed = new System.Collections.Generic.HashSet<int>();
        static readonly float[] Limits = { 1f, 0.8f, 0.6f, 0.4f, 0.2f };

        bool PreLaunch { get { return (vessel.status == FlightStatus.Rollout || vessel.status == FlightStatus.OnPad) && !staged0; } }

        void EngineLimit(int i)
        {
            if (i < 0 || i >= engines.Count) return;
            int uid = engines[i];
            var d = vessel.state.byUid[uid].Def;
            if (d.solidFuel) { hud.Message("Solid motors burn at a fixed thrust", 1.6f); return; }
            float cur = vessel.ThrustLimit(uid);
            int k = 0;
            for (int j = 0; j < Limits.Length; j++) if (Mathf.Abs(Limits[j] - cur) < 0.01f) k = j;
            float next = Limits[(k + 1) % Limits.Length];
            vessel.thrustLimit[uid] = next;
            hud.Message("Engine " + (i + 1) + " thrust limited to " + Mathf.RoundToInt(next * 100) + "% (" + (d.thrustSL * next).ToString("N0") + " kN at sea level)", 2f);
            Click();
        }

        void EngineRow(int i)
        {
            if (i < 0 || i >= engines.Count) return;
            int uid = engines[i];
            if (PreLaunch)
            {
                var p = vessel.state.byUid[uid];
                if (p.stage > 0) { hud.Message("Engine " + (i + 1) + " fires later, with stage " + (p.stage + 1), 2f); return; }
                if (!disarmed.Remove(uid)) disarmed.Add(uid);
                hud.Message("Engine " + (i + 1) + (disarmed.Contains(uid) ? " SAFE — it will not light at launch" : " ARMED for launch"), 1.8f);
                Click();
                return;
            }
            var st = vessel.GetEngineState(uid);
            if (st == FlightVessel.EngineState.Gone) return;
            string msg = vessel.SetEngine(uid, st != FlightVessel.EngineState.Running);
            if (msg != null) hud.Message("Engine " + (i + 1) + ": " + msg, 1.6f);
            Click();
        }

        void AllEngines(bool on)
        {
            if (PreLaunch)
            {
                foreach (int uid in engines) if (vessel.state.byUid[uid].stage <= 0) { if (on) disarmed.Remove(uid); else disarmed.Add(uid); }
                hud.Message(on ? "All launch engines ARMED" : "All launch engines SAFE", 1.6f);
                Click();
                return;
            }
            int n = 0;
            foreach (int uid in engines)
            {
                var st = vessel.GetEngineState(uid);
                if (st == FlightVessel.EngineState.Gone || st == FlightVessel.EngineState.NoFuel) continue;
                if (on && st == FlightVessel.EngineState.Ready && vessel.state.byUid[uid].stage > vessel.nextStage) continue; // don't light upper stages
                vessel.SetEngine(uid, on);
                n++;
            }
            hud.Message(on ? "All available engines on" : "All engines off (solids keep burning)", 1.6f);
            Click();
        }

        void UpdateEnginePanel()
        {
            bool open = enginePanelOpen && !map.Active && vessel.status != FlightStatus.Crashed;
            hud.enginePanel.gameObject.SetActive(open);
            hud.engineLabels.gameObject.SetActive(open);
            hud.telemetry.transform.parent.gameObject.SetActive(!open);
            hud.navInfo.gameObject.SetActive(!open);
            UIFactory.SetLabel(hud.engineTab, open ? "ENGINES ◂" : "ENGINES ▸");
            if (!open) return;
            var canvasRt = hud.engineLabels;
            for (int i = 0; i < engines.Count; i++)
            {
                int uid = engines[i];
                var st = vessel.GetEngineState(uid);
                var row = hud.engineRows[i];
                string state; Color col;
                var pp = vessel.state.byUid[uid];
                if (PreLaunch)
                {
                    if (pp.stage > 0) { state = "<color=#8fa3b8>STAGE " + (pp.stage + 1) + "</color>"; col = Theme.ButtonBg; }
                    else if (disarmed.Contains(uid)) { state = "<color=#ff9a7d>SAFE</color>"; col = Theme.ButtonBg; }
                    else { state = "<color=#7dff9a>ARMED</color>"; col = new Color(0.12f, 0.32f, 0.18f, 1f); }
                    st = FlightVessel.EngineState.Ready;
                }
                else
                switch (st)
                {
                    case FlightVessel.EngineState.Running: state = "<color=#7dff9a>ON</color>"; col = new Color(0.12f, 0.32f, 0.18f, 1f); break;
                    case FlightVessel.EngineState.Off: state = "<color=#ff9a7d>OFF</color>"; col = Theme.ButtonBg; break;
                    case FlightVessel.EngineState.Ready: state = "<color=#cfd8e3>READY</color>"; col = Theme.ButtonBg; break;
                    case FlightVessel.EngineState.NoFuel: state = "<color=#8fa3b8>NO FUEL</color>"; col = new Color(0.12f, 0.12f, 0.14f, 1f); break;
                    default: state = "<color=#667788>DROPPED</color>"; col = new Color(0.08f, 0.08f, 0.1f, 1f); break;
                }
                float o; vessel.engineOutput.TryGetValue(uid, out o);
                var p = vessel.state.byUid[uid];
                UIFactory.SetLabel(row, "<b>" + (i + 1) + "</b>  " + p.Def.displayName + "   " + state + (o > 0 ? "  " + (p.Def.thrustVac * o).ToString("N0") + " kN" : ""));
                UIFactory.SetLabel(hud.engineLimits[i], p.Def.solidFuel ? "fixed" : Mathf.RoundToInt(vessel.ThrustLimit(uid) * 100) + "%");
                UIFactory.SetButtonColor(row, col);
                // number tag next to the engine in 3D
                var holder = (RectTransform)hud.engineTags[i].transform.parent;
                Vector3 sp = Vector3.zero;
                bool vis = st != FlightVessel.EngineState.Gone && world.PartScreen(uid, out sp);
                if (holder.gameObject.activeSelf != vis) holder.gameObject.SetActive(vis);
                if (!vis) continue;
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out local);
                holder.anchoredPosition = local + canvasRt.rect.size * 0.5f;
                hud.engineTags[i].color = st == FlightVessel.EngineState.Running ? new Color(0.5f, 1f, 0.6f) : Color.white;
            }
        }

        // ------------------------------------------------------------------ flight milestones

        int lastLayer = -1;
        CelestialBody layerBody;
        bool machAnnounced, maxQAnnounced, cloudsAnnounced;
        double peakQ;

        static readonly double[] EarthLayers = { 0, 12000, 50000, 80000, 100000, 140000 };
        static readonly string[] EarthUp =
        {
            "", "Entered the STRATOSPHERE (12 km) — above the weather and into the ozone layer",
            "Entered the MESOSPHERE (50 km) — the air is 1/1000 of sea level",
            "Entered the THERMOSPHERE (80 km) — the sky is black now",
            "Crossed the KÁRMÁN LINE (100 km) — you are in SPACE!",
            "Left the atmosphere (140 km) — drag is negligible; you're in the exosphere"
        };
        static readonly string[] EarthDown =
        {
            "Back in the TROPOSPHERE (below 12 km) — the weather layer", "Descending through the STRATOSPHERE",
            "Descending through the MESOSPHERE — peak heating ahead", "Below the Kármán line — re-entering the atmosphere",
            "Entering the upper THERMOSPHERE — atmospheric interface", ""
        };

        void Milestones()
        {
            var v = vessel;
            if (v.status != FlightStatus.Flying) return;
            double alt = v.Altitude;
            if (layerBody != v.body) { layerBody = v.body; lastLayer = -1; }
            int layer;
            if (v.body.name == "Earth")
            {
                layer = 0;
                for (int i = 0; i < EarthLayers.Length; i++) if (alt >= EarthLayers[i]) layer = i;
                if (lastLayer >= 0 && layer != lastLayer)
                {
                    string m = layer > lastLayer ? EarthUp[layer] : EarthDown[layer];
                    if (m.Length > 0) Alert(m);
                }
            }
            else
            {
                layer = v.body.HasAtmosphere && alt < v.body.atmosphere.height ? 0 : 1;
                if (lastLayer >= 0 && layer != lastLayer)
                    Alert(layer == 0 ? "Entering " + v.body.name + "'s atmosphere" : "Left " + v.body.name + "'s atmosphere");
            }
            lastLayer = layer;

            if (v.body.name == "Earth" && !cloudsAnnounced && alt > 3200 && v.VerticalSpeed > 0) { cloudsAnnounced = true; Alert("Through the cloud deck"); }
            double mach = v.Mach;
            if (!machAnnounced && mach >= 1.0) { machAnnounced = true; Alert("MACH 1 — supersonic! (" + v.SurfaceVelocity.magnitude.ToString("0") + " m/s)"); }
            if (v.dynamicPressure > peakQ) peakQ = v.dynamicPressure;
            if (!maxQAnnounced && peakQ > 8000 && v.dynamicPressure < peakQ * 0.97 && v.VerticalSpeed > 0)
            {
                maxQAnnounced = true;
                Alert("MAX-Q — peak aerodynamic pressure " + (peakQ / 1000).ToString("0.0") + " kPa passed");
            }
        }

        void Alert(string text)
        {
            hud.Message(text, 4f);
            AppRoot.Instance.Audio.Play(Sfx.Click, 0.9f);
        }

        // ------------------------------------------------------------------ rollout speed

        static readonly int[] RolloutSpeeds = { 1, 10, 40, 100 };
        int speedIndex = 2;
        const float CrawlerRealSpeed = 0.447f;   // m/s — the real crawler-transporter does 1.6 km/h loaded

        void WireHud()
        {
            hud.OnStage = DoStage;
            hud.OnToggleEngine = () => { vessel.enginesOn = !vessel.enginesOn; Click(); };
            hud.OnThrottle = v => vessel.throttle = v;
            hud.OnMap = ToggleMap;
            hud.OnCamera = CycleCamera;
            hud.OnInside = ToggleInside;
            hud.OnRcs = ToggleRcs;
            hud.OnMenu = PauseMenu;
            hud.OnWarpUp = () => SetWarp(warpIndex + 1);
            hud.OnWarpDown = () => SetWarp(warpIndex - 1);
            hud.OnCountdown = StartCountdown;
            hud.OnSkipRollout = () => { crawlerEast = 0f; Click(); };
            hud.OnEngineTab = () => { enginePanelOpen = !enginePanelOpen; Click(); };
            hud.OnEngineRow = EngineRow;
            hud.OnEngineLimit = EngineLimit;
            hud.OnAllEngines = AllEngines;
            hud.OnSpeed = () => { speedIndex = (speedIndex + 1) % RolloutSpeeds.Length; UIFactory.SetLabel(hud.speedButton, "×" + RolloutSpeeds[speedIndex]); Click(); };
            hud.OnSas = m => { vessel.sas = vessel.sas == m ? SasMode.Off : m; if (m == SasMode.Target && vessel.target == null) hud.Message("Pick a target in the map first", 2f); Click(); };
            hud.OnTarget = () =>
            {
                if (map.focus == null || map.focus == vessel.body) { vessel.target = null; hud.Message("Target cleared"); }
                else { vessel.target = map.focus; hud.Message("Target: " + map.focus.name); }
                Click();
            };
        }

        static void Click() { AppRoot.Instance.Audio.Play(Sfx.Click); }

        // ------------------------------------------------------------------ actions

        void DoStage()
        {
            if (vessel.status == FlightStatus.Rollout) { hud.Message("Roll the rocket out to the pad first"); return; }
            if (vessel.status == FlightStatus.OnPad && countdown < 0f && !staged0) { StartCountdown(); return; }
            vessel.Stage();
            AppRoot.Instance.Audio.Play(Sfx.Snap, 0.9f);
        }

        void StartCountdown()
        {
            if (vessel.status != FlightStatus.OnPad || countdown >= 0f) return;
            countdown = 10f;
            world.SetLaunchCamera();
            Click();
        }

        void ToggleMap()
        {
            if (map.Active) { map.Close(); world.cam.enabled = true; }
            else { map.Open(); world.cam.enabled = false; }
            Click();
        }

        void CycleCamera()
        {
            switch (world.camMode)
            {
                case CamMode.Chase: world.camMode = CamMode.Free; break;
                case CamMode.Free: world.camMode = world.HasCockpit ? CamMode.Cockpit : CamMode.Chase; break;
                default: world.camMode = CamMode.Chase; break;
            }
            world.headYaw = world.headPitch = 0f;
            Click();
        }

        float preInside;
        void ToggleInside()
        {
            bool on = !world.XRayOn;
            world.SetXRay(on);
            if (on) { preInside = world.camDistanceGoal; world.camDistanceGoal = Mathf.Clamp(vessel.length * 1.4f + 8f, 12f, 200f); }
            else if (preInside > 0f) world.camDistanceGoal = preInside;
            hud.Message(on ? "Inside view — orange: fuel · blue: oxidiser" : "Outside view", 1.8f);
            Click();
        }

        void ToggleRcs()
        {
            if (!vessel.HasRcs) { hud.Message("No RCS thrusters — add an RCS Thruster Quad in the builder", 2.4f); return; }
            vessel.rcsOn = !vessel.rcsOn;
            hud.Message(vessel.rcsOn ? "RCS on" : "RCS off", 1.2f);
            Click();
        }

        void SetWarp(int i)
        {
            i = Mathf.Clamp(i, 0, Warps.Length - 1);
            if (i > MaxPhysicsWarp && !vessel.CanRails)
            {
                hud.Message(vessel.InAtmosphere ? "Can't warp faster than 4× in the atmosphere" : "Can't warp faster than 4× while burning or turning", 2f);
                i = MaxPhysicsWarp;
            }
            warpIndex = i;
            Click();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (AppRoot.Instance.BackPressed())
            {
                if (map.Active) ToggleMap(); else PauseMenu();
            }
            bool paused = dialogs.HasModal;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            HandleKeys();
            HandleGestures();
            UpdatePreLaunch(paused ? 0f : dt);

            // controls → vessel
            var s = hud.stick.Value;
            vessel.pitchInput = Mathf.Clamp(-s.y + Key(KeyCode.S, KeyCode.W), -1f, 1f);
            vessel.yawInput = Mathf.Clamp(s.x + Key(KeyCode.D, KeyCode.A), -1f, 1f);
            vessel.rollInput = Mathf.Clamp((hud.rollLeft.Held ? 1f : 0f) - (hud.rollRight.Held ? 1f : 0f) + Key(KeyCode.Q, KeyCode.E), -1f, 1f);
            var rcs = Vector3.zero;
            if (hud.rcsRight.Held || Input.GetKey(KeyCode.L)) rcs.x += 1;
            if (hud.rcsLeft.Held || Input.GetKey(KeyCode.J)) rcs.x -= 1;
            if (hud.rcsFwd.Held || Input.GetKey(KeyCode.H)) rcs.y += 1;
            if (hud.rcsBack.Held || Input.GetKey(KeyCode.N)) rcs.y -= 1;
            if (hud.rcsUp.Held || Input.GetKey(KeyCode.I)) rcs.z += 1;
            if (hud.rcsDown.Held || Input.GetKey(KeyCode.K)) rcs.z -= 1;
            vessel.rcsInput = rcs;

            // time warp & simulation
            if (warpIndex > MaxPhysicsWarp && !vessel.CanRails) warpIndex = MaxPhysicsWarp;
            if (warpIndex > MaxPhysicsWarp && vessel.status == FlightStatus.Flying && vessel.InAtmosphere) warpIndex = MaxPhysicsWarp;
            if (!paused) vessel.Advance(dt * Warps[warpIndex]);

            if (map.Active) map.Update(dt); else world.Render(dt);
            UpdateHud();
            UpdateEnginePanel();
            Milestones();
            UpdateMode();
            UpdateAudio(paused);
        }

        static float Key(KeyCode pos, KeyCode neg) { return (Input.GetKey(pos) ? 1f : 0f) - (Input.GetKey(neg) ? 1f : 0f); }

        void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) DoStage();
            if (Input.GetKey(KeyCode.LeftShift)) hud.throttle.value = Mathf.Min(1f, hud.throttle.value + Time.deltaTime * 0.6f);
            if (Input.GetKey(KeyCode.LeftControl)) hud.throttle.value = Mathf.Max(0f, hud.throttle.value - Time.deltaTime * 0.6f);
            if (Input.GetKeyDown(KeyCode.Z)) hud.throttle.value = 1f;
            if (Input.GetKeyDown(KeyCode.X)) hud.throttle.value = 0f;
            if (Input.GetKeyDown(KeyCode.M)) ToggleMap();
            if (Input.GetKeyDown(KeyCode.C)) CycleCamera();
            if (Input.GetKeyDown(KeyCode.V)) ToggleInside();
            if (Input.GetKeyDown(KeyCode.R)) ToggleRcs();
            if (Input.GetKeyDown(KeyCode.T)) vessel.sas = vessel.sas == SasMode.Off ? SasMode.Stability : SasMode.Off;
            if (Input.GetKeyDown(KeyCode.B)) StartCountdown();
            if (Input.GetKeyDown(KeyCode.Period)) SetWarp(warpIndex + 1);
            if (Input.GetKeyDown(KeyCode.Comma)) SetWarp(warpIndex - 1);
        }

        // ------------------------------------------------------------------ gestures: rotate / zoom / tap

        Vector2 lastPos, tapStart;
        float tapTime;
        bool dragging, tapCandidate;
        float lastPinch;

        static bool OverUI(int fingerId = -1)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return fingerId >= 0 ? es.IsPointerOverGameObject(fingerId) : es.IsPointerOverGameObject();
        }

        void HandleGestures()
        {
            if (dialogs.HasModal) return;
            if (Input.touchCount >= 2)
            {
                var a = Input.GetTouch(0); var b = Input.GetTouch(1);
                float d = (a.position - b.position).magnitude;
                if (b.phase == TouchPhase.Began || lastPinch <= 0f) lastPinch = d;
                else { Zoom(lastPinch / Mathf.Max(1f, d)); lastPinch = d; }
                tapCandidate = false;
                dragging = false;
                return;
            }
            lastPinch = 0f;
            bool down = false, held = false, up = false;
            Vector2 pos = Vector2.zero;
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                pos = t.position;
                down = t.phase == TouchPhase.Began;
                held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                up = t.phase == TouchPhase.Ended;
                if (down && OverUI(t.fingerId)) { dragging = false; tapCandidate = false; return; }
            }
            else
            {
                pos = Input.mousePosition;
                down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
                held = Input.GetMouseButton(0) || Input.GetMouseButton(1);
                up = Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1);
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !OverUI()) Zoom(Mathf.Pow(0.88f, wheel));
                if (down && OverUI()) { dragging = false; tapCandidate = false; return; }
            }
            if (down) { dragging = true; tapCandidate = true; lastPos = tapStart = pos; tapTime = Time.unscaledTime; }
            if (dragging && held)
            {
                var delta = pos - lastPos;
                lastPos = pos;
                float slop = Mathf.Max(14f, Screen.dpi > 0 ? Screen.dpi * 0.1f : Screen.height * 0.015f);
                if ((pos - tapStart).magnitude > slop) tapCandidate = false;
                if (!tapCandidate) Rotate(delta * (900f / Mathf.Max(400f, Screen.height)) * settings.cameraSensitivity);
            }
            if (up)
            {
                if (dragging && tapCandidate && Time.unscaledTime - tapTime < 0.5f) Tap(pos);
                dragging = false;
                tapCandidate = false;
            }
        }

        void Rotate(Vector2 d)
        {
            if (map.Active) { map.Rotate(d); return; }
            if (world.camMode == CamMode.LaunchCam) world.camMode = CamMode.Chase;
            if (world.camMode == CamMode.Cockpit)
            {
                world.headYaw = Mathf.Clamp(world.headYaw + d.x * 0.15f, -120f, 120f);
                world.headPitch = Mathf.Clamp(world.headPitch - d.y * 0.15f, -70f, 70f);
                return;
            }
            world.camYaw += d.x * 0.25f;
            world.camPitch = Mathf.Clamp(world.camPitch - d.y * 0.25f, -85f, 85f);
        }

        void Zoom(float factor)
        {
            if (map.Active) { map.Zoom(factor); return; }
            if (world.camMode == CamMode.Cockpit) { world.cam.fieldOfView = Mathf.Clamp(world.cam.fieldOfView * factor, 25f, 90f); return; }
            world.camDistanceGoal = Mathf.Clamp(world.camDistanceGoal * factor, Mathf.Max(4f, vessel.length * 0.6f), 30000f);
        }

        void Tap(Vector2 pos)
        {
            if (map.Active) return;
            Physics.SyncTransforms();
            RaycastHit hit;
            if (!Physics.Raycast(world.cam.ScreenPointToRay(pos), out hit, 1e6f, 1 << RocketView.PartLayer)) return;
            var pv = hit.collider.GetComponent<PartView>();
            if (pv == null) return;
            string msg = vessel.TapPart(pv.uid);
            if (string.IsNullOrEmpty(msg)) return;
            hud.Message(msg, 2f);
            Click();
        }

        // ------------------------------------------------------------------ rollout & countdown

        void UpdatePreLaunch(float dt)
        {
            if (vessel.status == FlightStatus.Rollout)
            {
                bool driving = hud.drive.Held || Input.GetKey(KeyCode.F);
                if (lowering < 0f)
                {
                    if (driving) crawlerEast = Mathf.Max(0f, crawlerEast - dt * CrawlerRealSpeed * RolloutSpeeds[speedIndex]);
                    vessel.PlaceEastOf(SiteLat, SiteLon, crawlerEast, LaunchComplex.CrawlerDeck);
                    world.site.SetCrawler(crawlerEast);
                    if (crawlerEast <= 0f) { lowering = 0f; hud.Message("At the pad — setting the rocket on the launch mount", 3f); }
                }
                else
                {
                    lowering = Mathf.Min(1f, lowering + dt / 3f);
                    double h = Mathf.Lerp(LaunchComplex.CrawlerDeck, (float)FlightVessel.PadTop, lowering);
                    vessel.PlaceEastOf(SiteLat, SiteLon, 0, h);
                    if (lowering >= 1f)
                    {
                        vessel.status = FlightStatus.OnPad;
                        hud.Message("Secured on the pad. Crawler backing away.", 3f);
                    }
                }
                return;
            }
            // crawler drives back to the building once the rocket is on the pad
            if (vessel.status == FlightStatus.OnPad && crawlerEast < 260f && countdown < 0f)
            {
                crawlerEast += dt * 14f;
                world.site.SetCrawler(crawlerEast);
            }
            if (countdown >= 0f)
            {
                float before = countdown;
                countdown -= dt;
                armOpen = Mathf.MoveTowards(armOpen, 1f, dt / 4f);
                world.site.SetArm(armOpen);
                if (Mathf.CeilToInt(before) != Mathf.CeilToInt(countdown) && countdown > 0f) AppRoot.Instance.Audio.Play(Sfx.Click, 0.8f);
                if (countdown <= 3f && !staged0)
                {
                    staged0 = true;
                    vessel.holdDown = true;
                    vessel.Stage();
                    foreach (int uid in disarmed) vessel.SetEngine(uid, false);
                    hud.Message(disarmed.Count > 0 ? "Ignition — " + disarmed.Count + " engine(s) kept safe" : "Ignition sequence start", 2f);
                }
                if (countdown <= 0f)
                {
                    countdown = -1f;
                    vessel.holdDown = false;
                }
            }
        }

        // ------------------------------------------------------------------ HUD

        void UpdateHud()
        {
            var v = vessel;
            bool pre = v.status == FlightStatus.Rollout || (v.status == FlightStatus.OnPad && !staged0);
            hud.rolloutPanel.gameObject.SetActive(pre && !map.Active);
            hud.controls.gameObject.SetActive(!pre && v.status != FlightStatus.Crashed);
            if (pre)
            {
                bool rolling = v.status == FlightStatus.Rollout;
                hud.drive.gameObject.SetActive(rolling && lowering < 0f);
                hud.skipButton.gameObject.SetActive(rolling && lowering < 0f);
                hud.countdownButton.gameObject.SetActive(!rolling && countdown < 0f);
                hud.rolloutText.text = rolling
                    ? (lowering < 0f
                        ? "<b>ROLLOUT</b> — crawler-transporter to Pad 1\n" + Mathf.RoundToInt(crawlerEast) + " m to go · " +
                          (hud.drive.Held || Input.GetKey(KeyCode.F) ? (CrawlerRealSpeed * RolloutSpeeds[speedIndex] * 3.6f).ToString("0.0") + " km/h" + (speedIndex == 0 ? " (real speed)" : " (real 1.6 km/h, ×" + RolloutSpeeds[speedIndex] + ")") + " · ETA " + FlightHUD.FormatTime(crawlerEast / (CrawlerRealSpeed * RolloutSpeeds[speedIndex])) : "stopped")
                        : "Lowering onto the hold-down clamps…")
                    : "<b>ON THE PAD</b> — " + launchBlueprint.name + "\n" + LaunchSummary() + "\n<size=18>Tap ENGINES ▸ to choose which engines light and their thrust</size>";
                hud.rolloutText.supportRichText = true;
            }
            hud.countdownText.text = countdown >= 0f ? (countdown > 0.5f ? "T−" + Mathf.CeilToInt(countdown) : "LIFTOFF") : "";
            hud.cockpitFrame.gameObject.SetActive(world.camMode == CamMode.Cockpit && !map.Active);
            hud.targetButton.gameObject.SetActive(map.Active);
            if (map.Active) UIFactory.SetLabel(hud.targetButton, map.focus == vessel.body ? "Clear tgt" : "Target " + map.focus.name);

            UIFactory.SetLabel(hud.mapButton, map.Active ? "Flight" : "Map");
            UIFactory.SetLabel(hud.camButton, world.camMode == CamMode.Cockpit ? "Cockpit" : world.camMode == CamMode.Free ? "Free" : world.camMode == CamMode.LaunchCam ? "Pad cam" : "Chase");
            UIFactory.SetButtonColor(hud.insideButton, world.XRayOn ? Theme.ButtonPrimary : Theme.ButtonBg);
            UIFactory.SetButtonColor(hud.rcsButton, v.rcsOn ? Theme.ButtonPrimary : Theme.ButtonBg);
            UIFactory.SetLabel(hud.engineButton, v.enginesOn ? "Engines\nON" : "Engines\nOFF");
            UIFactory.SetButtonColor(hud.engineButton, v.enginesOn ? new Color(0.2f, 0.45f, 0.25f, 1f) : Theme.ButtonBg);
            foreach (var kv in hud.sasButtons) UIFactory.SetButtonColor(kv.Value, v.sas == kv.Key ? Theme.ButtonPrimary : Theme.ButtonBg);
            hud.rcsPad.gameObject.SetActive(v.rcsOn && !map.Active);
            hud.warpLabel.text = Warps[warpIndex] >= 1000 ? (Warps[warpIndex] / 1000) + "k×" : Warps[warpIndex] + "×";
            hud.stageLabel.text = v.StagesRemaining ? "Next: stage " + (v.nextStage + 1) + " of " + launchBlueprint.stageCount : "No stages left";

            FlightHUD.SetFill(hud.liquidFill, v.LiquidFuelFraction());
            float solid = v.SolidFuelFraction();
            hud.solidRow.SetActive(solid >= 0f);
            if (solid >= 0f) FlightHUD.SetFill(hud.solidFill, solid);
            float mono = v.RcsFuelFraction();
            hud.rcsFill.transform.parent.parent.gameObject.SetActive(mono >= 0f);
            if (mono >= 0f) FlightHUD.SetFill(hud.rcsFill, mono);

            // telemetry
            var sb = new System.Text.StringBuilder();
            double alt = v.Altitude;
            sb.Append("<b>").Append(v.body.name).Append("</b>  ").Append(StatusText()).Append('\n');
            sb.Append("Altitude  ").Append(FlightHUD.Dist(alt)).Append('\n');
            if (alt < 100000 || v.Grounded) sb.Append("Radar     ").Append(FlightHUD.Dist(v.TerrainAltitude)).Append('\n');
            sb.Append("Vertical  ").Append(v.VerticalSpeed.ToString("N1")).Append(" m/s\n");
            sb.Append("Surface   ").Append(v.HorizontalSurfaceSpeed.ToString("N0")).Append(" m/s\n");
            sb.Append("Orbital   ").Append(v.vel.magnitude.ToString("N0")).Append(" m/s\n");
            if (v.status == FlightStatus.Flying)
            {
                sb.Append("Apoapsis  ").Append(FlightHUD.Dist(v.Apoapsis)).Append('\n');
                sb.Append("Periapsis ").Append(FlightHUD.Dist(v.Periapsis)).Append('\n');
            }
            double g = v.body.mu / (v.pos.sqrMagnitude);
            double twr = v.MaxThrustNow() * v.throttle / (v.mass * g);
            sb.Append("Mass ").Append(v.mass.ToString("N1")).Append(" t · TWR ").Append(twr.ToString("0.00")).Append('\n');
            sb.Append("Stage Δv  ").Append(v.StageDeltaV().ToString("N0")).Append(" m/s\n");
            sb.Append("G-force   ").Append((v.lastAcceleration / 9.80665).ToString("0.0")).Append(" g\n");
            if (v.heatFlux > 1000 || v.maxTempFraction > 0.4f)
            {
                string col = v.maxTempFraction > 0.85f ? "#ff5544" : v.maxTempFraction > 0.6f ? "#ffbb44" : "#ffffff";
                sb.Append("<color=").Append(col).Append(">Heat ").Append(Mathf.RoundToInt(v.maxTempFraction * 100f)).Append("% of limit</color>\n");
            }
            // position on the planet, power, and where to land
            double lat, lon;
            v.body.LatLon(v.pos, v.time, out lat, out lon);
            sb.Append(Math.Abs(lat).ToString("0.00")).Append(lat >= 0 ? "°N " : "°S ").Append(Math.Abs(lon).ToString("0.00")).Append(lon >= 0 ? "°E" : "°W").Append('\n');
            if (v.body == SolarSystem.Earth)
            {
                double dr, brg;
                LandingSite.All[0].DistanceFrom(lat, lon, v.body.radius, out dr, out brg);
                sb.Append("Downrange ").Append(FlightHUD.Dist(dr)).Append('\n');
            }
            if (v.capacity > 0)
            {
                float pct = (float)(v.charge / v.capacity);
                string pc = pct < 0.1f ? "#ff5544" : pct < 0.3f ? "#ffbb44" : "#ffffff";
                sb.Append("<color=").Append(pc).Append(">Power ").Append(Mathf.RoundToInt(pct * 100)).Append("%</color> +")
                  .Append(v.powerIn.ToString("0.00")).Append(" / -").Append(v.powerOut.ToString("0.00")).Append(" kW").Append(v.sunlit ? " (sun)" : " (night)").Append('\n');
            }
            else sb.Append("<color=#ff5544>No batteries</color>\n");
            if (v.transferFrom >= 0)
                sb.Append("<color=#7fd4ff>Pumping ").Append(v.transferRate.ToString("0.0")).Append(" t/s → ").Append(v.state.byUid[v.transferTo].Def.displayName).Append("</color>\n");
            else if (v.transferPick >= 0)
                sb.Append("<color=#7fd4ff>Tank picked — tap the tank to fill</color>\n");
            if (v.dynamicPressure > 1000) sb.Append("Q ").Append((v.dynamicPressure / 1000).ToString("N1")).Append(" kPa\n");
            hud.telemetry.text = sb.ToString();

            // navigation
            var nav = new System.Text.StringBuilder();
            if (v.status == FlightStatus.Flying && map.prediction != null) nav.Append(MapView.Summary(v, map.prediction));
            if (v.status == FlightStatus.Flying && v.body.name != "Earth")
            {
                double slat, slon, sd, sbr;
                v.body.LatLon(v.pos, v.time, out slat, out slon);
                var site = LandingSite.Nearest(v.body, slat, slon, out sd, out sbr);
                if (site != null)
                {
                    // bearing relative to where we're moving over the ground
                    var sv = v.SurfaceVelocity;
                    var up = v.Up;
                    var north = (Vector3d.up - up * Vector3d.Dot(Vector3d.up, up)).normalized;
                    var east = Vector3d.Cross(up, Vector3d.up).normalized;
                    var h = sv - up * Vector3d.Dot(sv, up);
                    double heading = h.magnitude > 1 ? (Math.Atan2(Vector3d.Dot(h, east), Vector3d.Dot(h, north)) * 180 / Math.PI + 360) % 360 : 0;
                    double rel = ((sbr - heading) % 360 + 540) % 360 - 180;
                    nav.Append("Landing site: ").Append(site.name).Append("\n").Append(FlightHUD.Dist(sd)).Append(" away, ")
                       .Append(Math.Abs(rel) < 15 ? "dead ahead" : rel > 0 ? Math.Abs(rel).ToString("0") + "° right" : Math.Abs(rel).ToString("0") + "° left").Append('\n');
                }
            }
            if (v.target != null)
            {
                nav.Append("Target ").Append(v.target.name).Append(": ").Append(FlightHUD.Dist(v.TargetRelativePosition().magnitude)).Append('\n');
                string adv = NavPlanner.TransferAdvice(v.body, v.pos, v.vel, v.time, v.target);
                if (adv != null && v.status == FlightStatus.Flying) nav.Append(adv);
            }
            hud.navInfo.text = nav.ToString();
            if (world.camMode == CamMode.Cockpit)
                hud.cockpitInstruments.text = "ALT " + FlightHUD.Dist(alt) + "   V/S " + v.VerticalSpeed.ToString("N0") + "   SPD " + v.SurfaceVelocity.magnitude.ToString("N0") + "   THR " + Mathf.RoundToInt(v.throttle * 100) + "%";
        }

        /// <summary>Liftoff thrust of the armed launch engines vs the rocket's weight.</summary>
        string LaunchSummary()
        {
            double F = 0;
            foreach (int uid in engines)
            {
                var p = vessel.state.byUid[uid];
                if (p.stage > 0 || disarmed.Contains(uid)) continue;
                F += p.Def.thrustSL * (p.Def.solidFuel ? 1f : vessel.ThrustLimit(uid));
            }
            double w = vessel.mass * 9.80665;
            double twr = F / w;
            string col = twr > 1.15 ? "#7dff9a" : twr > 1.0 ? "#ffd27d" : "#ff7d7d";
            return vessel.mass.ToString("N1") + " t · liftoff thrust " + F.ToString("N0") + " kN (" + (F / 9.80665).ToString("N0") + " t) · <color=" + col + ">TWR " + twr.ToString("0.00") + (twr <= 1.0 ? " — too heavy to lift off" : "") + "</color>";
        }

        string StatusText()
        {
            switch (vessel.status)
            {
                case FlightStatus.Rollout: return "rollout";
                case FlightStatus.OnPad: return "on the pad";
                case FlightStatus.Landed: return "landed";
                case FlightStatus.Crashed: return "destroyed";
            }
            if (vessel.InAtmosphere) return "in flight";
            return vessel.CurrentOrbit.e < 1 && vessel.Periapsis > 0 ? "in orbit" : vessel.CurrentOrbit.e >= 1 ? "escape trajectory" : "sub-orbital";
        }

        void UpdateAudio(bool paused)
        {
            float output = 0f;
            foreach (var o in vessel.engineOutput.Values) output = Mathf.Max(output, o);
            double pr = vessel.body.HasAtmosphere ? vessel.body.atmosphere.PressureAtm(Math.Max(0, vessel.Altitude)) : 0;
            float vol = output * (0.25f + 0.75f * Mathf.Clamp01((float)pr * 2f));
            bool rumbling = vessel.status == FlightStatus.Rollout && (hud.drive.Held || Input.GetKey(KeyCode.F)) && lowering < 0f;
            if (rumbling) { vol = 0.22f; output = -1.6f; }   // diesel-electric crawler rumble
            if (paused || map.Active) vol *= 0.3f;
            engineAudio.volume = Mathf.MoveTowards(engineAudio.volume, vol * settings.sfxVolume * 0.9f, Time.unscaledDeltaTime * 2f);
            engineAudio.pitch = 0.85f + 0.25f * output;
        }

        // ------------------------------------------------------------------ game modes

        MissionTracker tracker;
        string startMessage;
        bool wasDocked, recovered;
        float modeTimer;

        void SetupMode()
        {
            var pr = Progress.Current;
            // Planet Explorer: start on (or around) another world
            if (Play.mode == GameMode.Explorer && Play.explorerBody != null)
            {
                var b = SolarSystem.Get(Play.explorerBody);
                if (b != null)
                {
                    bool landed = !Play.explorerOrbit && Play.explorerSite >= 0 && Play.explorerSite < LandingSite.All.Count && LandingSite.All[Play.explorerSite].body == b.name;
                    if (landed)
                    {
                        var site = LandingSite.All[Play.explorerSite];
                        vessel = new FlightVessel(launchBlueprint.DeepCopy(), b, site.lat, site.lon);
                        // arrive in the local morning
                        double sunLon = FlightVessel.SunFacingLongitude(b, 0);
                        double ha = ((sunLon - (site.lon + 35)) % 360 + 360) % 360;
                        vessel.time = ha / 360.0 * Math.Abs(b.rotationPeriod);
                        vessel.PlaceOnSurface(site.lat, site.lon, 0.15);
                        startMessage = "Planet Explorer: on " + b.name + " at " + site.name + ". Unfold the legs, then STAGE to lift off.";
                    }
                    else
                    {
                        vessel = new FlightVessel(launchBlueprint.DeepCopy(), b, 0, 0);
                        double alt = b.HasAtmosphere ? b.atmosphere.height + 40000 : Math.Max(30000, b.radius * 0.03);
                        vessel.PlaceInOrbit(b, alt);
                        startMessage = "Planet Explorer: in a " + FlightHUD.Dist(alt) + " orbit around " + b.name + ". Burn retrograde to come down.";
                    }
                    settings.ApplyCheats(vessel.cheats);
                }
            }

            // Space station (Station mode or a mission that needs it)
            var mission = Play.Mission;
            if (Play.mode == GameMode.Station || (mission != null && mission.needsStation))
            {
                int modules = Mathf.Clamp(pr.stationModules, 0, 8);
                if (Play.stationPractice)
                {
                    vessel.PlaceInOrbit(SolarSystem.Earth, 400000);
                    var st = new Station(ExampleRockets.Station(modules), SolarSystem.Earth, 400000, 0, vessel);
                    vessel.station = st;
                    int port = -1;
                    foreach (int u in vessel.state.alive) if (vessel.state.byUid[u].Def.isDockingPort) { port = u; break; }
                    if (port >= 0)
                    {
                        Vector3 pl, al;
                        vessel.PortFace(port, out pl, out al);
                        var ax = st.PortAxisWorld;
                        vessel.rot = Quaternion.FromToRotation(al, -ax);
                        var goal = st.PortWorld + (Vector3d)(ax * 120f) + (Vector3d)(st.rot * new Vector3(6f, 0f, 4f));
                        vessel.pos = goal - (Vector3d)(vessel.rot * (pl - vessel.comLocal));
                        vessel.vel = st.vel;
                        vessel.angVel = Vector3.zero;
                    }
                    vessel.sas = SasMode.Target;
                    vessel.rcsOn = true;
                    startMessage = "Docking practice: 120 m behind the station. RCS is on — FWD to close in, keep under 0.6 m/s at contact.";
                }
                else
                {
                    vessel.station = new Station(ExampleRockets.Station(modules), SolarSystem.Earth, 400000, vessel.status == FlightStatus.Flying ? 3 : 25, vessel);
                    if (startMessage == null && vessel.status != FlightStatus.Flying)
                        startMessage = "The station orbits at 400 km. Launch east, reach orbit, then raise it to catch up and dock.";
                }
            }

            if (mission != null)
            {
                tracker = new MissionTracker(mission);
                tracker.StepDone += o => { hud.Message("✓ " + o.text, 4f); AppRoot.Instance.Audio.Play(Sfx.Snap); };
                tracker.Completed += OnMissionComplete;
                if (startMessage == null) startMessage = (Play.Career ? "Contract: " : "Mission: ") + mission.title + " — " + mission.steps[0].text;
            }
        }

        void UpdateMode()
        {
            if (tracker != null) tracker.Update(vessel);
            modeTimer -= Time.unscaledDeltaTime;
            if (modeTimer > 0f) return;
            modeTimer = 0.2f;
            var sb = new System.Text.StringBuilder();
            if (tracker != null) sb.Append(tracker.Panel());
            var st = vessel.station;
            if (st != null)
            {
                if (sb.Length > 0) sb.Append("\n");
                if (st.docked) sb.Append("<color=#7be08a>Docked to the station</color> · tap your docking port to undock");
                else if (st.body != vessel.body) sb.Append("Station: around Earth");
                else
                {
                    var rel = st.pos - vessel.pos;
                    var rv = vessel.vel - st.vel;
                    double closing = -Vector3d.Dot(rv, rel.normalized);
                    sb.Append("<b>Station</b> ").Append(FlightHUD.Dist(rel.magnitude)).Append(" · rel. speed ").Append(rv.magnitude < 100 ? rv.magnitude.ToString("0.00") : rv.magnitude.ToString("N0")).Append(" m/s")
                      .Append(closing >= 0 ? " closing" : " opening");
                    double dAlt = st.pos.magnitude - vessel.pos.magnitude;
                    if (rel.magnitude > 5000) sb.Append("\nStation is ").Append(FlightHUD.Dist(Math.Abs(dAlt))).Append(dAlt > 0 ? " higher" : " lower").Append(" · Tgt points at it");
                    else sb.Append("\nUse Retro to kill relative speed, Tgt to face it, RCS FWD to approach");
                }
                if (st.docked && !wasDocked && !Play.stationPractice) DeliverModule();
                wasDocked = st.docked;
            }
            else if (Play.Career && tracker == null)
                sb.Append("Career · funds ").Append(Costs.Money(Progress.Current.funds));
            hud.SetMission(sb.ToString());
        }

        void DeliverModule()
        {
            var pr = Progress.Current;
            if (pr.stationModules >= 8) { hud.Message("Docked! The station is complete — crew exchange done.", 5f); return; }
            pr.stationModules++;
            if (Play.Career) { pr.funds += 150f; pr.science += 10; }
            pr.Save();
            hud.Message("Module delivered! The station now has " + (pr.stationModules + 1) + " modules" + (Play.Career ? "  (+$150k, +10 science)" : ""), 6f);
        }

        void OnMissionComplete()
        {
            var m = tracker.mission;
            var pr = Progress.Current;
            string reward = "";
            if (Play.Career)
            {
                if (!pr.careerDone.Contains(m.id))
                {
                    pr.careerDone.Add(m.id);
                    pr.funds += m.reward; pr.science += m.science; pr.reputation += m.tier * 5;
                    reward = "\n+" + Costs.Money(m.reward) + "   +" + m.science + " science   +" + (m.tier * 5) + " reputation";
                }
            }
            else if (!pr.missionsDone.Contains(m.id)) pr.missionsDone.Add(m.id);
            pr.Save();
            AppRoot.Instance.Audio.Play(Sfx.Snap);
            StartCoroutine(ShowComplete(m.title, reward));
        }

        System.Collections.IEnumerator ShowComplete(string title, string reward)
        {
            yield return new WaitForSeconds(1.5f);
            dialogs.Custom("Mission complete!", 900, 460, (body, close) =>
            {
                var t = UIFactory.Label(body, "<b>" + title + "</b>" + reward + "\n\nMission time " + FlightHUD.FormatTime(vessel.time), 26, Theme.Good, TextAnchor.MiddleCenter);
                t.supportRichText = true;
                UIFactory.Size(t, -1, 170);
                var r = UIFactory.Rect("Row", body);
                UIFactory.Size(r, -1, 84);
                UIFactory.Horizontal(r, 12, 0, true).childForceExpandWidth = true;
                UIFactory.Button(r, "Keep flying", close, ButtonStyle.Primary);
                UIFactory.Button(r, "Main menu", () => { close(); AppRoot.Instance.LoadScene(Scenes.MainMenu); });
            });
        }

        void LogLanding(LandingSite site, double dist)
        {
            var pr = Progress.Current;
            string b = vessel.body.name;
            bool changed = false;
            if (b != "Earth" && !pr.bodiesLanded.Contains(b))
            {
                pr.bodiesLanded.Add(b); changed = true;
                if (Play.Career) pr.science += 25;
                hud.Message("First landing on " + b + "!" + (Play.Career ? "  +25 science" : ""), 6f);
            }
            if (site != null && site.body != "Earth" && dist < 25000 && !pr.sitesVisited.Contains(site.name))
            {
                pr.sitesVisited.Add(site.name); changed = true;
                if (Play.Career) pr.science += 15;
                hud.Message("Explored " + site.name + (Play.Career ? "  +15 science" : ""), 6f);
            }
            if (Play.Career && b == "Earth" && !recovered && vessel.HasCommand && vessel.time > 30)
            {
                recovered = true;
                float value = 0f;
                foreach (int u in vessel.state.alive) value += Costs.Part(vessel.state.byUid[u]);
                value *= 0.85f;
                pr.funds += value; changed = true;
                hud.Message("Vessel recovered: +" + Costs.Money(value), 6f);
            }
            if (changed) pr.Save();
        }

        // ------------------------------------------------------------------ menus

        void OnCrash()
        {
            warpIndex = 0;
            world.HideVessel();
            AppRoot.Instance.Audio.Play(Sfx.Explosion);
            StartCoroutine(ShowCrash());
        }

        System.Collections.IEnumerator ShowCrash()
        {
            yield return new WaitForSeconds(2.2f);
            dialogs.Custom("Vehicle destroyed", 900, 420, (body, close) =>
            {
                var t = UIFactory.Label(body, hud.message.text, 26, Theme.Text, TextAnchor.MiddleCenter);
                UIFactory.Size(t, -1, 110);
                Buttons(body, close, false);
            });
        }

        void PauseMenu()
        {
            if (dialogs.HasModal) return;
            dialogs.Custom(vessel.status == FlightStatus.Landed ? "Landed on " + vessel.body.name : "Flight paused", 900, tracker != null ? 700 : 560, (body, close) =>
            {
                var t = UIFactory.Label(body, "Mission time " + FlightHUD.FormatTime(vessel.time) + " · " + vessel.body.name, 24, Theme.Muted, TextAnchor.MiddleCenter);
                UIFactory.Size(t, -1, 60);
                if (tracker != null)
                {
                    var mt = UIFactory.Label(body, tracker.Panel(), 21, Theme.Text, TextAnchor.UpperLeft);
                    mt.supportRichText = true;
                    UIFactory.Size(mt, -1, 30 + 28 * tracker.mission.steps.Count);
                }
                var r0 = UIFactory.Rect("Row0", body);
                UIFactory.Size(r0, -1, 84);
                UIFactory.Horizontal(r0, 12, 0, true).childForceExpandWidth = true;
                UIFactory.Button(r0, "Resume", close, ButtonStyle.Primary);
                UIFactory.Button(r0, "Cheats", () => { close(); GameModesUI.OpenCheats(vessel.cheats, () => hud.cheatBadge.gameObject.SetActive(vessel.cheats.Any)); });
                Buttons(body, close, true);
            });
        }

        void Buttons(RectTransform body, Action close, bool includeMenu)
        {
            var row = UIFactory.Rect("Row", body);
            UIFactory.Size(row, -1, 84);
            var h = UIFactory.Horizontal(row, 12, 0, true);
            h.childForceExpandWidth = true;
            UIFactory.Button(row, "Revert to launch", () => { close(); AppRoot.Instance.LoadScene(Scenes.Flight); });
            UIFactory.Button(row, "Back to builder", () =>
            {
                close();
                Session.builderEntry = BuilderEntry.Continue;
                AppRoot.Instance.LoadScene(Scenes.Builder);
            });
            if (includeMenu) UIFactory.Button(row, "Main menu", () => { close(); AppRoot.Instance.LoadScene(Scenes.MainMenu); });
        }
    }
}
