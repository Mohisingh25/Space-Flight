# Apogee — Spaceflight Simulator (Android + macOS, Unity 6 + URP)

A realistic 3D spaceflight simulator for Android, built in phases. This repository holds **Phase 1: Foundation** —
the project, mobile rendering setup, main menu and a working modular rocket builder with live engineering analysis.

> **Status: Phase 1 is code-complete but hasn't run in Unity yet.**
> The C# was type-checked against Unity API stubs, and the physics/staging logic was unit-tested outside Unity.
> The first GitHub Actions build is the first real Unity compile. If it fails, copy the error lines from the
> build log and send them over so they can be fixed.

---

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
