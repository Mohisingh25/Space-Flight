# Apogee — Spaceflight Simulator (Android + macOS, Unity 6 + URP)

A realistic 3D spaceflight simulator for Android, built in phases. This repository holds **Phase 1: Foundation** —
the project, mobile rendering setup, main menu and a working modular rocket builder with live engineering analysis.

> **Status: Phase 1 is code-complete but hasn't run in Unity yet.**
> The C# was type-checked against Unity API stubs, and the physics/staging logic was unit-tested outside Unity.
> The first GitHub Actions build is the first real Unity compile. If it fails, copy the error lines from the
> build log and send them over so they can be fixed.

---

## Phase 4 — real 3D Solar System

- **Real-scale Solar System in 3D:** the Sun, all 8 planets and the Moon, Phobos, Deimos, Io, Europa, Ganymede, Callisto and Titan on real orbits (J2000 elements; game starts 8 Nov 2026). Real radii, gravity, rotation and atmospheres (Earth, Venus, Mars, Titan, the gas giants).
- **Real Earth:** NASA-based planet maps (Solar System Scope, CC BY 4.0) with clouds, ocean glint, night lights and an atmosphere glow; detailed local terrain when near the ground.
- **3D flight physics:** every engine pushes from where it's mounted (off-centre engines twist the rocket), pitch/yaw/roll with reaction wheels, gimbals, RCS and fins, aerodynamic drag with centre of pressure, SAS (stability, prograde, retrograde, radial, normal, target), patched-conic sphere-of-influence changes, time warp up to 1,000,000× on rails.
- **Damage:** re-entry heating per part (heat shields protect what's behind them; parts melt above their limit), structural joints that break under load, and g-limits (a huge engine on a tiny tank crushes it).
- **Navigation:** 3D map with predicted trajectory, Moon/planet encounters with their periapsis, Ap/Pe markers, targets, and transfer advice (burn Δv, phase angle, and when to burn).
- **Launch:** crawler rollout from the assembly building to the pad, rocket set on the hold-down clamps, countdown with ignition at T−3, swing arm retracts, pad camera tracks the climb.
- **Cameras:** chase (horizon-level), free orbit, cockpit (look around from the pilot's seat), inside x-ray view. Drag to rotate, pinch to zoom.
- **Landing legs** swing down, telescope out and compress on touchdown; tap to retract. **RCS** jets puff from the nozzles; 6-way translation pad.
- **Builder:** Mirror mode, Logo Panels with your own text, and new parts — Condor-M methalox, Hydra-V hydrolox, Spark kick motor, linear aerospike, nuclear thermal, small solid; 5 m and 10 m tanks, spherical lander tank, monopropellant tank; 2.5→3.75→5 m adapters; I-beams, strut nodes; omni antenna, high-gain and relay dishes, RTG, large solar wing; floodlight, ladder, launch escape tower, crew cabin.

Not yet: rovers and astronauts leaving the craft, driving on wheels, docking two craft together, aerodynamic control surfaces, and power/communication limits (antennas and batteries don't constrain you yet).

## Credits

Planet maps: [Solar System Scope](https://www.solarsystemscope.com/textures/), CC BY 4.0, based on NASA imagery.

## Phase 3A — building and flight controls

- **Side attachment:** tanks, engines, boosters and girders snap to the side of other tanks (drop them beside a tank; symmetry ×2–×8). Side tanks crossfeed into the core. For boosters you want to drop, use a Radial Separator.
- **New parts:** Mini Tank, Heavy Parachute, Radial Parachute (fits on any tank), Heavy Landing Leg, Rover Wheel, Heavy Wheel, Structural Girder, Structural Panel, Ablative Heat Shield, Aero Winglet. RCS blocks now carry monopropellant. Docking ports can be undocked.
- **Paint:** select a part → Paint → 12 colours, factory finish, or paint the whole rocket.
- **Bigger sizes:** diameters up to 7.5 m, tank lengths up to 8×.
- **Inside view:** see-through hulls showing fuel (orange) and oxidiser (blue) tanks that drain in flight, solid-fuel grain, combustion chambers, crew in their seats and payload. "Inside" button in the builder and in flight (key V).
- **Tap parts in flight:** each engine starts/stops on its own; landing legs fold/unfold; solar arrays open/stow; parachutes deploy; decouplers, docking ports and fairings separate; RCS blocks toggle RCS.
- **RCS:** RCS button + 4-way pad to move the craft without turning it (keys I/J/K/L, R toggles).
- **Real physics:** engines off the centreline twist the rocket; the builder shows how many tonnes the engines can lift versus the rocket's mass.
- **Cheats (Settings → Cheats, off by default):** unlimited fuel, zero gravity, no air drag, unbreakable parts, 5× thrust.

## Orientation

The game runs in **portrait and landscape** and re-lays out every screen when the phone rotates (auto-rotate; upside-down portrait is disabled). On Mac, resizing the window to taller-than-wide switches to the portrait layout.

## Get the APK (no Unity install needed for building)

The workflow in `.github/workflows/build-android.yml` builds the APK on GitHub's servers with
[GameCI](https://game.ci). You need a free Unity account and a one-time license file.

### 1. Get your Unity license file (one time, on your Mac)
1. Install **Unity Hub** (unity.com/download) and sign in with your Unity account. The editor itself isn't needed for this step.
2. In Unity Hub, go to **Settings › Licenses › Add › Get a free personal license**.
3. This creates `Unity_lic.ulf`. On macOS it's at
   `/Library/Application Support/Unity/Unity_lic.ulf`
   (in Finder: **Go › Go to Folder…** and paste that path).
4. Open it in TextEdit and copy **all** of its contents.

> Turn off two-factor authentication on the Unity account used for CI, or activation in the build can fail.

### 2. Add three secrets to your GitHub repository
**Settings › Secrets and variables › Actions › New repository secret**

| Name | Value |
|---|---|
| `UNITY_LICENSE` | the full contents of `Unity_lic.ulf` |
| `UNITY_EMAIL` | your Unity account email |
| `UNITY_PASSWORD` | your Unity account password |

### 3. Push the project and build
- Upload this folder to the repository's `main` branch. **Include the hidden `.github` and `.gitignore`.**
  Finder hides dot-files (show them with **Cmd + Shift + .**). GitHub Desktop or `git push` includes them automatically.
- Every push to `main` starts a build. You can also run it by hand: **Actions › Build Android APK › Run workflow**.
  Tick *Development build* there if you want a debug APK.
- The first build takes about **30–60 minutes** (it downloads Unity and imports everything). Later builds are faster because the Library is cached.

### 4. Install on your phone
1. Open the finished run in the **Actions** tab and download **Apogee-Android-APK** under *Artifacts* (it's a .zip).
2. Unzip it to get `Apogee.apk`, then copy it to the phone (or download it on the phone directly).
3. Open it. Allow "Install unknown apps" for your browser or file manager when Android asks.

Requirements: Android 8.0+ (API 26), 64-bit ARM. Target devices have 6 GB RAM or more; lower-end phones can use the Low quality setting.

---

## Build locally instead (optional)
1. Install **Unity 6.3 LTS** with **Android Build Support** (including OpenJDK and Android SDK & NDK) via Unity Hub.
2. **Add › Add project from disk** and choose this folder. On first open, the project sets itself up:
   it creates the URP pipeline asset, materials, both scenes, build settings and Android player settings.
   You can re-run this with **Apogee › Run Project Setup**.
3. Press Play in `Assets/Scenes/MainMenu`, or use **Apogee › Build Android APK (Debug / Release)**.
   APKs go to `Builds/Android/`.

The Unity version is pinned in `ProjectSettings/ProjectVersion.txt` (`6000.3.8f1`). If GameCI reports that no Docker image
exists for that version, change it to another 6000.3.x release that has one and push again.

---

## What works in Phase 1

| Feature | Status |
|---|---|
| Unity 6 project, URP (auto-configured), IL2CPP ARM64, landscape, safe-area UI | ✅ Implemented |
| Main menu: rotating rocket in orbit, procedural Earth (continents, clouds, ocean glint, night-side lights, atmosphere), Moon, star field and sun glare | ✅ Implemented |
| Menu: Sandbox Mode, Spacecraft Designer, Saved Designs, Settings | ✅ Working |
| Menu: Career, Mission Mode, Space Station, Planet Explorer | 🔒 Labelled with the phase that delivers them; tapping one explains this |
| Vehicle assembly building: hangar, assembly platform, service tower with access arms, captured reflections | ✅ Implemented |
| 36 parts in 8 categories: capsules, probe core, 6 tanks, 5 liquid engines, 2 solid boosters, RCS, decouplers, interstage, radial separators, nose cone, fairing, adapters, truss, payload/cargo modules, fins, legs, parachute, solar, battery, antenna, nav unit, docking port, science pod | ✅ Detailed procedural 3D models |
| Drag-and-drop (or tap-to-hold) placement with snapping to attach nodes and surfaces | ✅ |
| Radial symmetry ×1/2/3/4/6/8, side boosters on radial separators | ✅ |
| Move, rotate, resize diameter, stretch length, duplicate, delete (with counterparts), undo/redo | ✅ |
| Live stats: wet/dry/fuel mass, thrust (SL/vac), TWR, Δv per stage (vac and SL), estimated max altitude (ascent simulation), estimated payload to LEO, CoM/CoT/CoP with 3D markers, aerodynamic stability, design checks | ✅ |
| Staging editor: automatic staging plus manual reordering | ✅ (stages are *executed* in flight from Phase 2) |
| Save/load designs, 3 example rockets, crash-safe autosave | ✅ |
| Settings: quality, resolution scale, frame rate, anti-aliasing, shadows, battery saver, FPS counter, volumes, camera sensitivity | ✅ Applied immediately and saved |
| Procedural UI sounds and ambience | ✅ (engine and flight audio come with Phase 2) |
| **Launch & flight (Phase 2)** | ✅ Launch pad, real thrust/fuel/mass flow, staging into falling debris, drag, gravity turn, parachutes, landing / splashdown / crash, explosions |
| Flight controls | ✅ Throttle slider, engine On/Off, rotate ◀ ▶ (hold), STAGE, time warp, map view with predicted orbit + Ap/Pe, pause menu, revert, recover. Keyboard on Mac: A/D rotate, W/S throttle, Z/X full/cut, Space stage, M map, , . warp |
| Flight visuals | ✅ Curved Earth surface with oceans/land along the flight plane, launch complex, clouds, sky fading to black, stars, atmosphere glow from orbit, procedural exhaust plumes (expand in vacuum), smoke, shake, engine audio |
| 2D build mode | ✅ Blueprint-style side view: drag parts from the list, drag parts on the rocket to move them, drop on the list to delete |
| Part icons | ✅ Rendered from each part's real 3D model |

**Physics model.** The world is full-scale: Earth radius 6,371 km, μ = 3.986×10¹⁴ m³/s², and an exponential atmosphere.
Engines have separate sea-level and vacuum thrust/Isp with constant mass flow. Δv uses Tsiolkovsky, integrated across
engines that burn out at different times (for example, boosters dropping before the core stage). Crossfeed stops at
decouplers. Solid boosters burn their own casing. Max altitude comes from a vertical ascent integration with gravity
falling off with altitude, drag and pressure-dependent Isp. LEO payload is a binary search against about 9.3 km/s of
ascent Δv. Example results: *Meridian Orbital* has 10.4 km/s of vacuum Δv and lifts ≈1.3 t to LEO;
*Pathfinder I* reaches ≈138 km.

## Not done yet (by phase)
- **Phase 2 — Flight (remaining):** launch countdown, cockpit/chase camera options, re-entry heating.
- **Phase 3 — Space:** floating-origin double-precision orbital integration, Earth and Moon at scale, navigation map, manoeuvre nodes, transfers.
- **Phase 4 — Advanced:** interactive cockpit, astronaut EVA, docking, landing, reusable boosters.
- **Phase 5 — Full game:** Mars and other planets, missions, career, stations, satellites.
- **Phase 6 — Release:** device testing, optimisation, signed release (Play Store keystore).

Release APKs are currently signed with Unity's debug key. That's fine for installing on your own phone, but Google Play needs your own keystore.

---

## Architecture

```
Assets/Apogee/
  Scripts/
    Core/        AppRoot (boot, scene loading, back button), GameSettings, SaveSystem, AudioManager
    Simulation/  Physical constants, Earth atmosphere model
    Parts/       PartDefinition, PartCatalog (all part stats)
    Rocket/      RocketBlueprint (save format), RocketLayout (attachment → poses), RocketAssembly (edit ops),
                 StagingSystem, RocketAnalyzer (Δv, TWR, ascent sim, payload), RocketView (3D), ExampleRockets
    Rendering/   MeshBuilder, PartMeshFactory (procedural models), MaterialLibrary, ProceduralTextures,
                 PlanetFactory (Earth/Moon textures, stars), Noise
    Builder/     BuilderController (input, placement, undo, save), BuilderUI, BuilderEnvironment,
                 OrbitCamera, PartThumbnails
    Menu/        MainMenuController
    UI/          UIFactory + Theme, Dialogs, SettingsPanel, BlueprintBrowser
  Editor/        ProjectSetup (auto-configures project), BuildScript (menu + CI build entry point)
  Shaders/       Planet, Atmosphere, Stars, Ghost, Marker
```

Everything visible is generated in code: meshes, textures, UI and audio. The repository therefore contains no binary assets, and
scenes are just a bootstrap object created by `ProjectSetup`. Vehicle data (`RocketBlueprint`) is plain serialisable C#,
independent of rendering. The Phase 2 flight systems will consume the same blueprint and staging data.
