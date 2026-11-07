using System;
using Apogee.Core;
using Apogee.Rocket;
using Apogee.Simulation;
using Apogee.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Apogee.Flight
{
    /// <summary>
    /// Flight scene entry point: runs the physics with a fixed timestep (and time warp), reads touch,
    /// mouse and keyboard controls, drives the camera, renders the world and HUD, plays engine audio and
    /// handles staging, landing, crashes and the pause menu.
    /// </summary>
    public class FlightController : MonoBehaviour
    {
        static readonly int[] WarpLevels = { 1, 2, 3, 5, 10, 25, 50, 100, 1000, 10000, 100000 };

        FlightVessel vessel;
        FlightWorld world;
        FlightHUD hud;
        MapView map;
        Camera cam;
        AudioSource engineAudio;
        GameSettings settings;
        Dialogs dialogs;

        int warpIndex;
        double accumulator;
        float camDistance = 60f, camDistanceGoal = 60f;
        Vector3 camUp = Vector3.up;
        float shake;
        bool orbitAnnounced, crashHandled, landedAnnounced;
        RocketBlueprint launchBlueprint;

        void Start()
        {
            settings = AppRoot.Instance.Settings;
            dialogs = AppRoot.Instance.Dialogs;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            AppRoot.Instance.Audio.SetAmbience(Ambience.None);

            launchBlueprint = Session.flightBlueprint ?? SaveSystem.LoadAutosave() ?? ExampleRockets.Meridian();
            if (launchBlueprint.IsEmpty) launchBlueprint = ExampleRockets.Meridian();
            vessel = new FlightVessel(launchBlueprint.DeepCopy());
            if (settings.cheatsEnabled)
            {
                vessel.cheats.infiniteFuel = settings.cheatInfiniteFuel;
                vessel.cheats.noGravity = settings.cheatNoGravity;
                vessel.cheats.noDrag = settings.cheatNoDrag;
                vessel.cheats.unbreakable = settings.cheatUnbreakable;
                vessel.cheats.superThrust = settings.cheatSuperThrust;
            }

            var camGo = new GameObject("Flight Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 40f;
            cam.cullingMask = ~(1 << MapView.Layer);
            camGo.AddComponent<AudioListener>();
            camDistance = camDistanceGoal = Mathf.Clamp(vessel.length * 2.2f + 12f, 25f, 400f);

            world = gameObject.AddComponent<FlightWorld>();
            world.Init(vessel, cam);
            map = gameObject.AddComponent<MapView>();
            map.Init(world.band);

            hud = gameObject.AddComponent<FlightHUD>();
            hud.Init();
            hud.OnStage = DoStage;
            hud.OnToggleEngine = ToggleEngine;
            hud.OnMap = () => map.SetActive(!map.Active);
            hud.OnWarpUp = () => SetWarp(warpIndex + 1, true);
            hud.OnWarpDown = () => SetWarp(warpIndex - 1, true);
            hud.OnMenu = OpenMenu;
            hud.OnThrottle = v => vessel.throttle = v;
            hud.OnInside = ToggleInside;
            hud.OnRcs = ToggleRcs;
            hud.cheatBadge.gameObject.SetActive(vessel.cheats.Any);

            vessel.OnMessage += m => hud.Message(m);
            vessel.OnDebris += d => { world.AddDebris(d); AppRoot.Instance.Audio.Play(Sfx.Detach, 0.8f); };
            vessel.OnCrash += HandleCrash;
            vessel.OnLanded += HandleLanded;

            engineAudio = gameObject.AddComponent<AudioSource>();
            engineAudio.clip = AppRoot.Instance.Audio.EngineLoop();
            engineAudio.loop = true;
            engineAudio.volume = 0f;
            engineAudio.Play();

            hud.hint.text = "Press STAGE to ignite — or tap an engine to start just that one";
            Render();
        }

        void OnDestroy() { Screen.sleepTimeout = SleepTimeout.SystemSetting; }

        // ================================================================== controls

        void DoStage()
        {
            if (vessel.status == FlightStatus.Crashed) return;
            if (!vessel.HasNextStage) { hud.Message("No more stages"); return; }
            vessel.Stage();
            hud.hint.text = "";
            AppRoot.Instance.Audio.Play(Sfx.Snap, 0.9f);
            if (warpIndex > 0) SetWarp(0, false);
        }

        void ToggleEngine()
        {
            if (!vessel.AnyEngineAvailable()) { hud.Message("No engine lit — press STAGE"); return; }
            vessel.enginesOn = !vessel.enginesOn;
            hud.Message(vessel.enginesOn ? "Engine On" : "Engine Off", 1.2f);
            AppRoot.Instance.Audio.Play(Sfx.Click);
        }

        float preInsideDistance;

        void ToggleInside()
        {
            bool on = !world.XRayOn;
            world.SetXRay(on);
            if (on) { preInsideDistance = camDistanceGoal; camDistanceGoal = Mathf.Clamp(vessel.length * 1.5f + 6f, 10f, 200f); }
            else if (preInsideDistance > 0f) camDistanceGoal = preInsideDistance;
            hud.Message(on ? "Inside view — orange: fuel · blue: oxidiser" : "Outside view", 1.8f);
            AppRoot.Instance.Audio.Play(Sfx.Click);
        }

        void ToggleRcs()
        {
            if (!vessel.HasRcs) { hud.Message("No RCS thrusters on this craft — add an RCS Thruster Quad in the builder", 2.2f); return; }
            vessel.rcsOn = !vessel.rcsOn;
            hud.Message(vessel.rcsOn ? "RCS on — use the pad to translate" : "RCS off", 1.4f);
            AppRoot.Instance.Audio.Play(Sfx.Click);
        }

        // ---- tapping parts
        Vector2 tapStart;
        float tapStartTime;
        bool tapCandidate;

        void DetectTap()
        {
            if (map.Active) { tapCandidate = false; return; }
            bool down = false, up = false;
            Vector2 pos = Vector2.zero;
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                pos = t.position;
                down = t.phase == TouchPhase.Began;
                up = t.phase == TouchPhase.Ended;
            }
            else if (Input.touchCount == 0)
            {
                pos = Input.mousePosition;
                down = Input.GetMouseButtonDown(0);
                up = Input.GetMouseButtonUp(0);
            }
            else { tapCandidate = false; return; }

            if (down) { tapCandidate = !OverUI(); tapStart = pos; tapStartTime = Time.unscaledTime; }
            if (!up || !tapCandidate) return;
            tapCandidate = false;
            float slop = Mathf.Max(14f, Screen.dpi > 0 ? Screen.dpi * 0.1f : Screen.height * 0.015f);
            if ((pos - tapStart).magnitude > slop || Time.unscaledTime - tapStartTime > 0.5f) return;

            Physics.SyncTransforms();
            RaycastHit hit;
            if (!Physics.Raycast(cam.ScreenPointToRay(pos), out hit, 1e6f, 1 << RocketView.PartLayer)) return;
            var pv = hit.collider.GetComponent<PartView>();
            if (pv == null) return;
            string msg = vessel.TapPart(pv.uid);
            if (string.IsNullOrEmpty(msg)) return;
            hud.Message(msg, 1.8f);
            hud.hint.text = "";
            AppRoot.Instance.Audio.Play(Sfx.Click);
        }

        int MaxWarpIndex()
        {
            bool firing = vessel.engineOutput.Count > 0;
            double alt = vessel.Altitude;
            int max;
            if (firing) max = 3;                                     // 5×: real-time physics with thrust
            else if (vessel.status != FlightStatus.Flying) max = 3;
            else if (alt < EarthAtmosphere.Ceiling * 0.5) max = 5;   // 25× in the atmosphere
            else max = WarpLevels.Length - 1;
            return max;
        }

        void SetWarp(int index, bool fromUser)
        {
            int max = MaxWarpIndex();
            int clamped = Mathf.Clamp(index, 0, max);
            if (fromUser && index > max) hud.Message(vessel.engineOutput.Count > 0 ? "Can't warp faster while engines fire" : "Can't warp faster in the atmosphere", 1.6f);
            warpIndex = clamped;
        }

        static bool OverUI()
        {
            if (EventSystem.current == null) return false;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) return true;
                return false;
            }
            return EventSystem.current.IsPointerOverGameObject(-1);
        }

        void ReadInput()
        {
            int turn = 0;
            if (hud.left.Held || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) turn += 1;   // counter-clockwise
            if (hud.right.Held || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) turn -= 1; // clockwise
            vessel.turnInput = turn;

            float t = vessel.throttle;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.LeftShift)) t += Time.unscaledDeltaTime * 0.8f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.LeftControl)) t -= Time.unscaledDeltaTime * 0.8f;
            if (Input.GetKeyDown(KeyCode.Z)) t = 1f;
            if (Input.GetKeyDown(KeyCode.X)) t = 0f;
            t = Mathf.Clamp01(t);
            if (Mathf.Abs(t - vessel.throttle) > 1e-4f) { vessel.throttle = t; hud.throttle.value = t; }

            double rx = 0, ry = 0;
            if (hud.rcsRight.Held || Input.GetKey(KeyCode.L)) rx += 1;
            if (hud.rcsLeft.Held || Input.GetKey(KeyCode.J)) rx -= 1;
            if (hud.rcsUp.Held || Input.GetKey(KeyCode.I)) ry += 1;
            if (hud.rcsDown.Held || Input.GetKey(KeyCode.K)) ry -= 1;
            vessel.rcsX = rx;
            vessel.rcsY = ry;
            if (Input.GetKeyDown(KeyCode.R)) ToggleRcs();
            if (Input.GetKeyDown(KeyCode.V)) ToggleInside();
            DetectTap();

            if (Input.GetKeyDown(KeyCode.Space)) DoStage();
            if (Input.GetKeyDown(KeyCode.E)) ToggleEngine();
            if (Input.GetKeyDown(KeyCode.M)) map.SetActive(!map.Active);
            if (Input.GetKeyDown(KeyCode.Period)) SetWarp(warpIndex + 1, true);
            if (Input.GetKeyDown(KeyCode.Comma)) SetWarp(warpIndex - 1, true);

            // Zoom: pinch or mouse wheel; in map view a one-finger drag pans.
            float zoom = 1f;
            if (Input.touchCount >= 2 && !OverUI())
            {
                var a = Input.GetTouch(0);
                var b = Input.GetTouch(1);
                float prev = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
                float cur = (a.position - b.position).magnitude;
                if (prev > 2f && cur > 2f) zoom = prev / cur;
            }
            else if (Input.touchCount == 1 && map.Active && !OverUI() && Input.GetTouch(0).phase == TouchPhase.Moved)
                map.Pan(Input.GetTouch(0).deltaPosition);
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUI()) zoom *= Mathf.Pow(0.85f, wheel);
            if (Input.GetMouseButton(0) && map.Active && Input.touchCount == 0 && !OverUI())
                map.Pan(new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 20f);
            if (Mathf.Abs(zoom - 1f) > 1e-4f)
            {
                if (map.Active) map.Zoom(zoom);
                else camDistanceGoal = Mathf.Clamp(camDistanceGoal * zoom, 8f, 6e7f);
            }
        }

        // ================================================================== loop

        void Update()
        {
            if (AppRoot.Instance.BackPressed())
            {
                if (map.Active) map.SetActive(false);
                else OpenMenu();
            }
            bool paused = dialogs.HasModal;
            if (!paused)
            {
                ReadInput();
                StepPhysics();
            }
            Render();
        }

        void StepPhysics()
        {
            if (warpIndex > MaxWarpIndex()) SetWarp(MaxWarpIndex(), false);
            int warp = WarpLevels[warpIndex];
            double frame = Math.Min(Time.unscaledDeltaTime, 0.1) * warp;
            double dt = warp <= 25 ? 0.02 : Math.Min(5.0, warp * 0.0008);
            accumulator += frame;
            int steps = 0;
            while (accumulator >= dt && steps < 3000)
            {
                vessel.Step(dt);
                accumulator -= dt;
                steps++;
                if (warpIndex > MaxWarpIndex()) { accumulator = 0; break; }
            }
            if (steps >= 3000) accumulator = 0;

            if (!orbitAnnounced && vessel.status == FlightStatus.Flying && vessel.Altitude > EarthAtmosphere.Ceiling)
            {
                var o = vessel.CurrentOrbit();
                if (o.bound && o.periapsis - FlightVessel.R > EarthAtmosphere.Ceiling)
                {
                    orbitAnnounced = true;
                    hud.Message("Stable orbit achieved!", 3.5f);
                    AppRoot.Instance.Audio.Play(Sfx.Snap);
                }
            }
        }

        void Render()
        {
            // Camera: follow the vessel, keep "down" pointing at the planet, smooth zoom
            camDistance = Mathf.Lerp(camDistance, camDistanceGoal, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            var up = vessel.Up;
            var upR = new Vector3((float)up.x, (float)up.y, 0f);
            camUp = Vector3.Slerp(camUp, upR, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime)).normalized;
            float thrustG = (float)(vessel.lastAcceleration / 9.81);
            float target = vessel.engineOutput.Count > 0 ? Mathf.Clamp01(thrustG * 0.15f) * Mathf.Clamp01(1f - (float)vessel.TerrainHeight / 3000f) : 0f;
            shake = Mathf.Lerp(shake, target, Time.unscaledDeltaTime * 4f);
            Vector3 jitter = shake > 0.001f ? UnityEngine.Random.insideUnitSphere * shake * camDistance * 0.004f : Vector3.zero;
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, camUp);
            cam.transform.position = new Vector3(0f, 0f, -camDistance) + jitter;
            cam.nearClipPlane = Mathf.Max(0.3f, camDistance * 0.02f);
            cam.farClipPlane = camDistance * 4f + 2.5e7f;
            float viewSize = 2f * camDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            world.Render(viewSize, cam.transform.position);
            map.UpdateMap(vessel);
            cam.enabled = !map.Active;
            UpdateHud();

            // Engine audio: louder with throttle, muffled in thin air (heard through the structure)
            float output = 0f;
            foreach (var v in vessel.engineOutput.Values) output = Mathf.Max(output, v);
            float pr = (float)EarthAtmosphere.PressureRatio(vessel.Altitude);
            float vol = output > 0f ? (0.35f + 0.65f * output) * (0.25f + 0.75f * pr) : 0f;
            if (dialogs.HasModal) vol = 0f;
            engineAudio.volume = Mathf.MoveTowards(engineAudio.volume, vol * settings.sfxVolume * 0.9f, Time.unscaledDeltaTime * 2f);
            engineAudio.pitch = 0.85f + 0.25f * output;
        }

        void UpdateHud()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Height (Terrain):\n").Append(FlightHUD.FormatDistance(vessel.TerrainHeight)).Append('\n');
            sb.Append("Velocity:\n").Append(vessel.vel.magnitude.ToString("N1")).Append("m/s\n");
            if (vessel.status == FlightStatus.Flying && (vessel.Altitude > 2000 || vessel.vel.magnitude > 200))
            {
                var o = vessel.CurrentOrbit();
                sb.Append("<size=24>Apoapsis: ").Append(o.bound ? FlightHUD.FormatDistance(o.apoapsis - FlightVessel.R) : "Escape").Append('\n');
                sb.Append("Periapsis: ").Append(FlightHUD.FormatDistance(Math.Max(-FlightVessel.R, o.periapsis - FlightVessel.R))).Append("</size>\n");
            }
            sb.Append("<size=22>Accel: ").Append((vessel.lastAcceleration / 9.80665).ToString("0.00")).Append(" g   ");
            sb.Append("Air: ").Append(EarthAtmosphere.PressureKPa(vessel.Altitude).ToString("0.0")).Append(" kPa\n");
            sb.Append("T+ ").Append(FlightHUD.FormatTime(vessel.missionTime)).Append("</size>");
            hud.telemetry.text = sb.ToString();

            hud.warpLabel.text = WarpLevels[warpIndex] + "×";
            hud.throttleLabel.text = Mathf.RoundToInt(vessel.throttle * 100f) + "%";
            UIFactory.SetLabel(hud.engineButton, vessel.enginesOn ? "On" : "Off");
            UIFactory.SetButtonColor(hud.engineButton, vessel.enginesOn ? Theme.ButtonPrimary : Theme.ButtonBg);
            UIFactory.SetLabel(hud.stageButton, vessel.HasNextStage ? "STAGE" : "—");
            hud.stageButton.interactable = vessel.HasNextStage && vessel.status != FlightStatus.Crashed;
            hud.stageLabel.text = vessel.HasNextStage ? "Next: stage " + (vessel.nextStage + 1) + " of " + vessel.bp.stageCount + "  ·  " + NextStageSummary() : "All stages used";
            hud.liquidFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(vessel.LiquidFuelFraction()), 1f);
            float solid = vessel.SolidFuelFraction();
            hud.solidRow.SetActive(solid >= 0f);
            if (solid >= 0f) hud.solidFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(solid), 1f);
            UIFactory.SetLabel(hud.mapButton, map.Active ? "Flight" : "Map");
            UIFactory.SetButtonColor(hud.insideButton, world.XRayOn ? Theme.ButtonPrimary : Theme.ButtonBg);
            float rcsFuel = vessel.RcsFuelFraction();
            UIFactory.SetLabel(hud.rcsButton, rcsFuel >= 0f ? "RCS " + Mathf.RoundToInt(rcsFuel * 100f) + "%" : "RCS");
            UIFactory.SetButtonColor(hud.rcsButton, vessel.rcsOn ? Theme.ButtonPrimary : Theme.ButtonBg);
            hud.rcsPad.gameObject.SetActive(vessel.rcsOn && !map.Active);

            PlaceMapLabel(hud.apLabel, map.Active && map.HasApoapsis, map.ApoWorld, "Ap " + FlightHUD.FormatDistance(vessel.CurrentOrbit().apoapsis - FlightVessel.R));
            PlaceMapLabel(hud.peLabel, map.Active && map.HasPeriapsis, map.PeriWorld, "Pe " + FlightHUD.FormatDistance(vessel.CurrentOrbit().periapsis - FlightVessel.R));
        }

        string NextStageSummary()
        {
            var verbs = new System.Collections.Generic.List<string>();
            foreach (var p in vessel.bp.parts)
            {
                if (p.stage != vessel.nextStage || !vessel.state.alive.Contains(p.uid)) continue;
                string v = StagingSystem.ActionVerb(p);
                if (!verbs.Contains(v)) verbs.Add(v);
            }
            return verbs.Count > 0 ? string.Join(" + ", verbs.ToArray()) : "—";
        }

        void PlaceMapLabel(RectTransform label, bool show, Vector3 world, string text)
        {
            label.gameObject.SetActive(show);
            if (!show) return;
            var sp = map.MapCamera.WorldToScreenPoint(world + new Vector3(0, 0, 0));
            Vector2 local;
            var parent = (RectTransform)label.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, null, out local))
                label.anchoredPosition = local + new Vector2(parent.rect.width * 0.5f, parent.rect.height * 0.5f + 18f);
            label.GetComponent<UnityEngine.UI.Text>().text = text;
        }

        // ================================================================== events and menus

        void HandleCrash()
        {
            if (crashHandled) return;
            crashHandled = true;
            world.Explode(Vector3.zero, Mathf.Max(8f, vessel.length));
            world.HideVessel();
            AppRoot.Instance.Audio.Play(Sfx.Explosion);
            shake = 1f;
            warpIndex = 0;
            Invoke("ShowCrashDialog", 1.4f);
        }

        void ShowCrashDialog()
        {
            dialogs.Custom("Rocket destroyed", 900, 420, (body, close) =>
            {
                var t = UIFactory.Label(body, "Rocket science is hard. Max altitude reached: " + FlightHUD.FormatDistance(vessel.maxAltitude) + ".", 26, Theme.Muted);
                UIFactory.Size(t, -1, -1, -1, 1);
                Buttons(body, close, false);
            });
        }

        void HandleLanded()
        {
            if (landedAnnounced) return;
            landedAnnounced = true;
            bool ocean = world.LandedInOcean();
            hud.Message(ocean ? "Splashdown!" : "Touchdown!", 3f);
            AppRoot.Instance.Audio.Play(Sfx.Snap);
        }

        void OpenMenu()
        {
            if (dialogs.HasModal) return;
            dialogs.Custom(vessel.status == FlightStatus.Landed && vessel.everFlew ? "Landed safely" : "Flight paused", 900, 520, (body, close) =>
            {
                string info = "Mission time " + FlightHUD.FormatTime(vessel.missionTime) + "  ·  max altitude " + FlightHUD.FormatDistance(vessel.maxAltitude);
                var t = UIFactory.Label(body, info, 24, Theme.Muted);
                UIFactory.Size(t, -1, 50);
                var resume = UIFactory.Button(body, "Resume", close, ButtonStyle.Primary, 28);
                UIFactory.Size(resume, -1, 80);
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
            UIFactory.Button(row, vessel.status == FlightStatus.Landed && vessel.everFlew ? "Recover & build" : "Back to builder", () =>
            {
                close();
                Session.builderEntry = BuilderEntry.Continue;
                AppRoot.Instance.LoadScene(Scenes.Builder);
            });
            if (includeMenu) UIFactory.Button(row, "Main menu", () => { close(); AppRoot.Instance.LoadScene(Scenes.MainMenu); });
        }
    }
}
