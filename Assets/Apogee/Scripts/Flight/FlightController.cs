using System;
using Apogee.Core;
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
            if (settings.cheatsEnabled)
            {
                vessel.cheats.infiniteFuel = settings.cheatInfiniteFuel;
                vessel.cheats.noGravity = settings.cheatNoGravity;
                vessel.cheats.noDrag = settings.cheatNoDrag;
                vessel.cheats.unbreakable = settings.cheatUnbreakable;
                vessel.cheats.superThrust = settings.cheatSuperThrust;
                vessel.cheats.noHeat = settings.cheatNoHeat;
                switch (settings.cheatStart)
                {
                    case 1: vessel.PlaceInOrbit(SolarSystem.Earth, 250000); break;
                    case 2: vessel.PlaceInOrbit(SolarSystem.Moon, 100000); break;
                    case 3: vessel.PlaceInOrbit(SolarSystem.Mars, 300000); break;
                }
                if (vessel.status == FlightStatus.Flying) { staged0 = true; lowering = 1f; crawlerEast = 300f; }
            }

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
            vessel.OnLanded += () => { AppRoot.Instance.Audio.Play(Sfx.Snap); warpIndex = 0; };
            vessel.OnSoiChange += b => { if (warpIndex > 6) warpIndex = 6; };

            engineAudio = gameObject.AddComponent<AudioSource>();
            engineAudio.clip = AppRoot.Instance.Audio.EngineLoop();
            engineAudio.loop = true;
            engineAudio.volume = 0f;
            engineAudio.Play();
            hud.Message(vessel.status == FlightStatus.Flying ? "Cheat start: in orbit around " + vessel.body.name : "Hold DRIVE to roll the rocket out to the pad", 4f);
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

        void EngineRow(int i)
        {
            if (i < 0 || i >= engines.Count) return;
            int uid = engines[i];
            var st = vessel.GetEngineState(uid);
            if (st == FlightVessel.EngineState.Gone) return;
            string msg = vessel.SetEngine(uid, st != FlightVessel.EngineState.Running);
            if (msg != null) hud.Message("Engine " + (i + 1) + ": " + msg, 1.6f);
            Click();
        }

        void AllEngines(bool on)
        {
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
            bool open = enginePanelOpen && !map.Active && hud.controls.gameObject.activeSelf;
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
                UIFactory.SetLabel(row, "<b>" + (i + 1) + "</b>  " + p.Def.displayName + "   " + state + (o > 0 ? "  " + Mathf.RoundToInt(o * 100) + "%" : ""));
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
                    hud.Message("Ignition", 2f);
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
                    : "<b>ON THE PAD</b> — " + launchBlueprint.name + "\n" + v.mass.ToString("N1") + " t · thrust " + v.MaxThrustNow().ToString("N0") + " kN";
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
            if (v.transferFrom >= 0)
                sb.Append("<color=#7fd4ff>Pumping ").Append(v.transferRate.ToString("0.0")).Append(" t/s → ").Append(v.state.byUid[v.transferTo].Def.displayName).Append("</color>\n");
            else if (v.transferPick >= 0)
                sb.Append("<color=#7fd4ff>Tank picked — tap the tank to fill</color>\n");
            if (v.dynamicPressure > 1000) sb.Append("Q ").Append((v.dynamicPressure / 1000).ToString("N1")).Append(" kPa\n");
            hud.telemetry.text = sb.ToString();

            // navigation
            var nav = new System.Text.StringBuilder();
            if (v.status == FlightStatus.Flying && map.prediction != null) nav.Append(MapView.Summary(v, map.prediction));
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
            dialogs.Custom(vessel.status == FlightStatus.Landed ? "Landed on " + vessel.body.name : "Flight paused", 900, 520, (body, close) =>
            {
                var t = UIFactory.Label(body, "Mission time " + FlightHUD.FormatTime(vessel.time) + " · " + vessel.body.name, 24, Theme.Muted, TextAnchor.MiddleCenter);
                UIFactory.Size(t, -1, 60);
                UIFactory.Size(UIFactory.Button(body, "Resume", close, ButtonStyle.Primary), -1, 84);
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
