using System;
using System.Collections.Generic;
using Apogee.Astro;
using Apogee.Flight;
using Apogee.Parts;
using Apogee.Rocket;
using UnityEngine;

namespace Apogee.Game
{
    public enum GameMode { Sandbox, Career, Mission, Station, Explorer }

    public enum ObjKind { Altitude, Mach, Orbit, OrbitRange, EnterSoi, Land, LandAt, ReturnEarth, Dock }

    /// <summary>One step of a mission. Steps are completed in order.</summary>
    public class Objective
    {
        public ObjKind kind;
        public string body = "Earth";
        public double a, b;          // altitude / Mach / periapsis / range limits (metres or km, see kind)
        public int site = -1;        // LandingSite index for LandAt
        public string text;

        public bool Met(FlightVessel v)
        {
            if (v == null || v.status == FlightStatus.Crashed) return false;
            bool here = v.body != null && v.body.name == body;
            switch (kind)
            {
                case ObjKind.Altitude: return here && v.status == FlightStatus.Flying && v.Altitude >= a;
                case ObjKind.Mach: return v.status == FlightStatus.Flying && v.Mach >= a;
                case ObjKind.EnterSoi: return here;
                case ObjKind.Orbit:
                case ObjKind.OrbitRange:
                {
                    if (!here || v.status != FlightStatus.Flying) return false;
                    var o = v.CurrentOrbit;
                    if (o == null || o.e >= 1) return false;
                    double pe = o.Periapsis - v.body.radius, ap = o.Apoapsis - v.body.radius;
                    double floor = v.body.HasAtmosphere ? v.body.atmosphere.height : 5000;
                    if (kind == ObjKind.Orbit) return pe >= Math.Max(a, floor);
                    return pe >= a && ap <= b;
                }
                case ObjKind.Land: return here && v.status == FlightStatus.Landed;
                case ObjKind.ReturnEarth: return v.body.name == "Earth" && v.status == FlightStatus.Landed && v.HasCommand;
                case ObjKind.LandAt:
                {
                    if (!here || v.status != FlightStatus.Landed || site < 0 || site >= LandingSite.All.Count) return false;
                    double la, lo, d, br;
                    v.body.LatLon(v.pos, v.time, out la, out lo);
                    LandingSite.All[site].DistanceFrom(la, lo, v.body.radius, out d, out br);
                    return d <= a;
                }
                case ObjKind.Dock: return v.station != null && v.station.docked;
            }
            return false;
        }
    }

    public class Mission
    {
        public string id, title, brief;
        public int tier;                   // 1 Earth … 5 outer planets
        public float reward;               // k$
        public int science;
        public string rocket;              // suggested example rocket
        public bool needsStation;
        public readonly List<Objective> steps = new List<Objective>();

        Mission Step(ObjKind k, string body, double a, double b, string text, int site = -1)
        {
            steps.Add(new Objective { kind = k, body = body, a = a, b = b, text = text, site = site });
            return this;
        }

        // ------------------------------------------------------------------ catalogue

        static List<Mission> all;

        public static List<Mission> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Mission>();
                Func<string, string, string, int, float, int, string, Mission> M = (id, t, br, tier, k, sci, r) =>
                {
                    var m = new Mission { id = id, title = t, brief = br, tier = tier, reward = k, science = sci, rocket = r };
                    all.Add(m);
                    return m;
                };
                M("first_flight", "First Flight", "Light the engine and climb 1 km above the launch complex.", 1, 40, 2, "Pathfinder")
                    .Step(ObjKind.Altitude, "Earth", 1000, 0, "Reach 1 km altitude");
                M("sound_barrier", "Break the Sound Barrier", "Fly faster than the speed of sound in the lower atmosphere.", 1, 60, 3, "Pathfinder")
                    .Step(ObjKind.Mach, "Earth", 1.0, 0, "Reach Mach 1");
                M("reach_space", "Reach Space", "Cross the Kármán line at 100 km — the edge of space.", 1, 120, 6, "Pathfinder")
                    .Step(ObjKind.Altitude, "Earth", 100000, 0, "Reach 100 km");
                M("up_and_back", "Up and Back", "Fly above 100 km and bring the capsule back to the ground under parachute.", 1, 180, 8, "Pathfinder")
                    .Step(ObjKind.Altitude, "Earth", 100000, 0, "Reach 100 km")
                    .Step(ObjKind.ReturnEarth, "Earth", 0, 0, "Land safely on Earth");
                M("orbit", "Orbit the Earth", "Reach a stable orbit with periapsis above 140 km: about 7.8 km/s sideways.", 2, 400, 15, "Meridian")
                    .Step(ObjKind.Orbit, "Earth", 140000, 0, "Stable orbit, periapsis > 140 km");
                M("orbit_return", "Orbit and Return", "Orbit the Earth, then de-orbit, survive re-entry and land.", 2, 550, 20, "Meridian")
                    .Step(ObjKind.Orbit, "Earth", 140000, 0, "Stable orbit, periapsis > 140 km")
                    .Step(ObjKind.ReturnEarth, "Earth", 0, 0, "Re-enter and land");
                var st = M("station_dock", "Station Rendezvous", "Launch, catch up with the space station at 400 km and dock to deliver a new module.", 2, 700, 25, "StationShuttle")
                    .Step(ObjKind.Orbit, "Earth", 140000, 0, "Reach orbit")
                    .Step(ObjKind.Dock, "Earth", 0, 0, "Dock with the station");
                st.needsStation = true;
                M("geo", "Geostationary Orbit", "Put a satellite into a circular orbit at 35 786 km, where one orbit lasts one day.", 3, 900, 30, "MeridianHeavy")
                    .Step(ObjKind.OrbitRange, "Earth", 35286000, 36286000, "Periapsis and apoapsis within 35 286–36 286 km");
                M("moon_flyby", "Lunar Flyby", "Burn for the Moon (about 3.1 km/s from low orbit) and enter its sphere of influence.", 3, 900, 35, "Selene")
                    .Step(ObjKind.EnterSoi, "Moon", 0, 0, "Enter the Moon's sphere of influence");
                M("moon_orbit", "Lunar Orbit", "Capture into orbit around the Moon.", 3, 1200, 45, "Selene")
                    .Step(ObjKind.Orbit, "Moon", 10000, 0, "Moon orbit, periapsis > 10 km");
                M("tranquility", "Tranquility Base", "Land within 25 km of the Apollo 11 site in the Sea of Tranquility.", 4, 2500, 80, "Selene")
                    .Step(ObjKind.LandAt, "Moon", 25000, 0, "Land within 25 km of Tranquility Base", 1);
                M("moon_return", "Moon and Back", "Land anywhere on the Moon, take off again and bring the crew home to Earth.", 4, 4000, 120, "Selene")
                    .Step(ObjKind.Land, "Moon", 0, 0, "Land on the Moon")
                    .Step(ObjKind.ReturnEarth, "Earth", 0, 0, "Return to Earth and land");
                M("shackleton", "Lunar South Pole", "Land on the rim of Shackleton crater, where sunlight barely reaches.", 4, 3500, 100, "Selene")
                    .Step(ObjKind.LandAt, "Moon", 25000, 0, "Land within 25 km of Shackleton rim", 2);
                M("venus_flyby", "Venus Flyby", "Leave Earth's sphere of influence and reach Venus.", 4, 4500, 120, "Selene")
                    .Step(ObjKind.EnterSoi, "Venus", 0, 0, "Enter Venus's sphere of influence");
                M("mars_orbit", "Mars Orbit", "Cross interplanetary space and capture into orbit around Mars.", 5, 6000, 160, "Selene")
                    .Step(ObjKind.Orbit, "Mars", 130000, 0, "Mars orbit, periapsis > 130 km");
                M("jezero", "Jezero Crater", "Land within 50 km of the Perseverance landing site on Mars.", 5, 9000, 250, "Selene")
                    .Step(ObjKind.LandAt, "Mars", 50000, 0, "Land within 50 km of Jezero", 4);
                M("jupiter", "Grand Tour: Jupiter", "Reach the giant planet's sphere of influence.", 5, 9000, 250, "Selene")
                    .Step(ObjKind.EnterSoi, "Jupiter", 0, 0, "Enter Jupiter's sphere of influence");
                M("titan", "Titan Landing", "Land on Saturn's moon Titan, near the Huygens probe.", 5, 15000, 400, "Selene")
                    .Step(ObjKind.LandAt, "Titan", 100000, 0, "Land within 100 km of the Huygens site", 6);
                return all;
            }
        }

        public static Mission Get(string id)
        {
            foreach (var m in All) if (m.id == id) return m;
            return null;
        }

        public RocketBlueprint SuggestedRocket() { return ExampleByName(rocket); }

        public static RocketBlueprint ExampleByName(string n)
        {
            switch (n)
            {
                case "Pathfinder": return ExampleRockets.Pathfinder();
                case "MeridianHeavy": return ExampleRockets.MeridianHeavy();
                case "StationShuttle": return ExampleRockets.StationShuttle();
                case "Selene": return ExampleRockets.Selene();
                case "SeleneLander": return ExampleRockets.SeleneLander();
                case "AresLander": return ExampleRockets.AresLander();
                case "DockingTug": return ExampleRockets.DockingTug();
                default: return ExampleRockets.Meridian();
            }
        }
    }

    /// <summary>Follows the active mission during a flight, step by step.</summary>
    public class MissionTracker
    {
        public readonly Mission mission;
        public int step;
        public bool Complete { get { return step >= mission.steps.Count; } }
        public event Action<Objective> StepDone;
        public event Action Completed;

        public MissionTracker(Mission m) { mission = m; }

        public void Update(FlightVessel v)
        {
            if (Complete) return;
            var o = mission.steps[step];
            if (!o.Met(v)) return;
            step++;
            if (StepDone != null) StepDone(o);
            if (Complete && Completed != null) Completed();
        }

        public string Panel()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(mission.title).Append("</b>\n");
            for (int i = 0; i < mission.steps.Count; i++)
                sb.Append(i < step ? "<color=#7be08a>✓ " : i == step ? "<color=#ffd36b>▸ " : "<color=#8a99aa>· ").Append(mission.steps[i].text).Append("</color>\n");
            return sb.ToString().TrimEnd();
        }
    }

    // ====================================================================== tech tree & costs

    public class TechNode
    {
        public string id, name, desc;
        public int cost;            // science
        public string[] requires;
        public string[] parts;
    }

    public static class TechTree
    {
        public static readonly TechNode[] Nodes =
        {
            new TechNode { id = "start", name = "Start", desc = "Sounding rockets", cost = 0, requires = new string[0],
                parts = new[] { "capsule_mk1", "probe_core", "tank_mini", "tank_s", "tank_m", "eng_small", "srb_mini", "srb_small", "nose_cone", "fins", "parachute", "decoupler", "logo_panel", "battery" } },
            new TechNode { id = "basic", name = "Basic Rocketry", desc = "Bigger tanks, booster engine, radial staging", cost = 5, requires = new[] { "start" },
                parts = new[] { "tank_l", "eng_medium", "radial_decoupler", "winglet", "parachute_radial", "srb_large", "adapter_25_125", "tank_25" } },
            new TechNode { id = "orbital", name = "Orbital Flight", desc = "Vacuum engine, interstage, heat shield", cost = 15, requires = new[] { "basic" },
                parts = new[] { "eng_vac", "interstage", "heat_shield", "tank_25_l", "parachute_heavy", "fairing", "payload_container" } },
            new TechNode { id = "electric", name = "Electrics", desc = "Solar arrays, antennas, lights", cost = 12, requires = new[] { "basic" },
                parts = new[] { "solar_panel", "antenna", "antenna_omni", "light_spot", "nav_unit" } },
            new TechNode { id = "heavy", name = "Heavy Lift", desc = "Titan-H, 3.75 m and 5 m tanks", cost = 30, requires = new[] { "orbital" },
                parts = new[] { "eng_heavy", "tank_375", "adapter_375_25", "adapter_25_375", "tank_25_xl", "eng_condor" } },
            new TechNode { id = "docking", name = "Rendezvous & Docking", desc = "Docking port, RCS, monopropellant", cost = 25, requires = new[] { "orbital", "electric" },
                parts = new[] { "docking_port", "rcs_quad", "tank_mono", "eng_spark" } },
            new TechNode { id = "landing", name = "Landing", desc = "Legs, lander engine and tanks", cost = 35, requires = new[] { "orbital" },
                parts = new[] { "landing_legs", "eng_landing", "tank_sphere", "landing_legs_l", "ladder" } },
            new TechNode { id = "stations", name = "Space Stations", desc = "Habitat modules, trusses, large solar wings", cost = 50, requires = new[] { "docking" },
                parts = new[] { "crew_cabin", "cargo_module", "truss", "girder", "struct_panel", "ibeam", "strut_cube", "solar_large", "capsule_horizon" } },
            new TechNode { id = "highenergy", name = "High-Energy Propulsion", desc = "Hydrolox engines, super-heavy tank", cost = 70, requires = new[] { "heavy", "landing" },
                parts = new[] { "eng_hydra", "tank_5", "adapter_375_5", "les_tower", "eng_aerospike" } },
            new TechNode { id = "deepspace", name = "Deep Space", desc = "RTG, high-gain and relay dishes, science pod", cost = 90, requires = new[] { "highenergy", "electric" },
                parts = new[] { "rtg", "dish_hga", "dish_relay", "science_pod" } },
            new TechNode { id = "rovers", name = "Surface Mobility", desc = "Wheels for rovers", cost = 60, requires = new[] { "landing" },
                parts = new[] { "wheel_s", "wheel_l" } },
            new TechNode { id = "nuclear", name = "Nuclear Propulsion", desc = "Atom-N nuclear thermal engine", cost = 160, requires = new[] { "deepspace" },
                parts = new[] { "eng_nuclear" } },
        };

        static Dictionary<string, string> nodeOfPart;

        /// <summary>The tech node that unlocks a part (parts not listed come with "start").</summary>
        public static string NodeOf(string partId)
        {
            if (nodeOfPart == null)
            {
                nodeOfPart = new Dictionary<string, string>();
                foreach (var n in Nodes) foreach (var p in n.parts) nodeOfPart[p] = n.id;
            }
            string id;
            return nodeOfPart.TryGetValue(partId, out id) ? id : "start";
        }

        public static TechNode Get(string id)
        {
            foreach (var n in Nodes) if (n.id == id) return n;
            return null;
        }
    }

    public static class Costs
    {
        /// <summary>Price of a part in thousands of dollars (hardware plus propellant).</summary>
        public static float Part(PartInstance p)
        {
            var d = p.Def;
            float c = 1.5f + PartMass.Dry(p) * 6f + PartMass.Fuel(p) * 0.25f;
            if (d.IsEngine) c += d.thrustVac * 0.03f + Mathf.Max(0f, d.ispVac - 300f) * 0.15f;
            if (d.isCommand) c += d.crew > 0 ? 25f * d.crew : 12f;
            if (d.solarKW > 0f) c += d.solarKW * 4f;
            if (d.generatorKW > 0f) c += 60f;
            if (d.isRcs) c += 3f;
            return c;
        }

        public static float Rocket(RocketBlueprint bp)
        {
            float c = 0f;
            foreach (var p in bp.parts) c += Part(p);
            return c;
        }

        public static string Money(float k)
        {
            if (k >= 1000f) return "$" + (k / 1000f).ToString("0.00") + "M";
            return "$" + k.ToString("0") + "k";
        }
    }

    // ====================================================================== saved progress

    /// <summary>Career and mission progress, persisted in PlayerPrefs as JSON.</summary>
    [Serializable]
    public class Progress
    {
        const string Key = "apogee.progress.v1";

        // career
        public bool careerStarted;
        public float funds = 600f;               // k$
        public int science;
        public int reputation;
        public int flights;
        public List<string> careerDone = new List<string>();
        public List<string> unlocked = new List<string> { "start" };
        // mission mode (sandbox missions)
        public List<string> missionsDone = new List<string>();
        // station & explorer (shared across modes)
        public int stationModules;
        public List<string> sitesVisited = new List<string>();
        public List<string> bodiesLanded = new List<string>();

        static Progress current;
        public static Progress Current
        {
            get
            {
                if (current != null) return current;
                try
                {
                    string json = PlayerPrefs.GetString(Key, "");
                    if (!string.IsNullOrEmpty(json)) current = JsonUtility.FromJson<Progress>(json);
                }
                catch (Exception e) { Debug.LogWarning("Progress unreadable: " + e.Message); }
                if (current == null) current = new Progress();
                if (current.careerDone == null) current.careerDone = new List<string>();
                if (current.unlocked == null || current.unlocked.Count == 0) current.unlocked = new List<string> { "start" };
                if (current.missionsDone == null) current.missionsDone = new List<string>();
                if (current.sitesVisited == null) current.sitesVisited = new List<string>();
                if (current.bodiesLanded == null) current.bodiesLanded = new List<string>();
                return current;
            }
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public void ResetCareer()
        {
            careerStarted = true;
            funds = 600f; science = 0; reputation = 0; flights = 0;
            careerDone.Clear();
            unlocked.Clear(); unlocked.Add("start");
            Save();
        }

        public bool PartUnlocked(string partId) { return unlocked.Contains(TechTree.NodeOf(partId)); }

        public bool CanResearch(TechNode n)
        {
            if (unlocked.Contains(n.id)) return false;
            foreach (var r in n.requires) if (!unlocked.Contains(r)) return false;
            return science >= n.cost;
        }

        public bool Research(TechNode n)
        {
            if (!CanResearch(n)) return false;
            science -= n.cost;
            unlocked.Add(n.id);
            Save();
            return true;
        }

        /// <summary>Locked parts used by a design (career only).</summary>
        public List<string> LockedParts(RocketBlueprint bp)
        {
            var l = new List<string>();
            foreach (var p in bp.parts) if (!PartUnlocked(p.partId) && !l.Contains(p.Def.displayName)) l.Add(p.Def.displayName);
            return l;
        }

        /// <summary>Careers offer missions in order: the next few not yet done.</summary>
        public List<Mission> CareerOffers()
        {
            var l = new List<Mission>();
            foreach (var m in Mission.All)
            {
                if (careerDone.Contains(m.id)) continue;
                l.Add(m);
                if (l.Count >= 4) break;
            }
            return l;
        }
    }

    /// <summary>What the current flight is for (set by the menus, read by the builder and the flight scene).</summary>
    public static class Play
    {
        public static GameMode mode = GameMode.Sandbox;
        public static string missionId;
        public static int explorerSite = -1;       // LandingSite index to start on
        public static string explorerBody;         // body to start on / around
        public static bool explorerOrbit;          // start in low orbit instead of landed
        public static float launchCost;            // career: what this flight cost (for recovery)
        public static bool stationPractice;        // start in orbit just behind the station

        public static Mission Mission { get { return missionId != null ? Mission.Get(missionId) : null; } }
        public static bool Career { get { return mode == GameMode.Career; } }

        public static void Reset() { mode = GameMode.Sandbox; missionId = null; explorerSite = -1; explorerBody = null; explorerOrbit = false; stationPractice = false; launchCost = 0f; }
    }
}
