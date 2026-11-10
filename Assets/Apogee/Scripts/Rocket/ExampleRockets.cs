using System.Collections.Generic;

namespace Apogee.Rocket
{
    /// <summary>Ready-made designs shipped with the game (also used for the main-menu backdrop).</summary>
    public static class ExampleRockets
    {
        public static List<RocketBlueprint> All()
        {
            return new List<RocketBlueprint> { Pathfinder(), Meridian(), MeridianHeavy(), StationShuttle(), Selene(), SeleneLander(), AresLander(), DockingTug() };
        }

        /// <summary>Single-stage sounding rocket: capsule on a 1.25 m tank, small engine, four fins.</summary>
        public static RocketBlueprint Pathfinder()
        {
            var bp = new RocketBlueprint { name = "Pathfinder I (example)" };
            int capsule = RocketAssembly.AddRoot(bp, "capsule_mk1");
            RocketAssembly.AddChild(bp, capsule, "parachute", AttachMode.Above);
            int tank = RocketAssembly.AddChild(bp, capsule, "tank_m", AttachMode.Below);
            RocketAssembly.AddChild(bp, tank, "eng_small", AttachMode.Below);
            RocketAssembly.AddRadial(bp, tank, "fins", 4, -0.4f, 45f);
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>Two-stage orbital launcher on a 2.5 m core.</summary>
        public static RocketBlueprint Meridian()
        {
            var bp = new RocketBlueprint { name = "Meridian Orbital (example)" };
            int capsule = RocketAssembly.AddRoot(bp, "capsule_mk1");
            RocketAssembly.AddChild(bp, capsule, "parachute", AttachMode.Above);
            int adapter = RocketAssembly.AddChild(bp, capsule, "adapter_25_125", AttachMode.Below);
            int upperTank = RocketAssembly.AddChild(bp, adapter, "tank_25_l", AttachMode.Below);
            int vac = RocketAssembly.AddChild(bp, upperTank, "eng_vac", AttachMode.Below);
            int inter = RocketAssembly.AddChild(bp, vac, "interstage", AttachMode.Below);
            int t1 = RocketAssembly.AddChild(bp, inter, "tank_25_l", AttachMode.Below);
            int t2 = RocketAssembly.AddChild(bp, t1, "tank_25_l", AttachMode.Below);
            RocketAssembly.AddChild(bp, t2, "eng_heavy", AttachMode.Below);
            RocketAssembly.AddRadial(bp, t2, "fins", 4, -0.42f, 45f);
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>Meridian core with two solid boosters on radial separators.</summary>
        public static RocketBlueprint MeridianHeavy()
        {
            var bp = Meridian();
            bp.name = "Meridian Heavy (example)";
            int lowerTank = -1;
            foreach (var p in bp.parts)
                if (p.partId == "tank_25_l" && RocketAssembly.PartBelow(bp, p) != null && RocketAssembly.PartBelow(bp, p).partId == "eng_heavy")
                    lowerTank = p.uid;
            var seps = RocketAssembly.AddRadial(bp, lowerTank, "radial_decoupler", 2, 0.1f, 0f);
            if (seps.Count > 0)
            {
                int booster = RocketAssembly.AddChild(bp, seps[0], "srb_large", AttachMode.Side, 0.25f);
                if (booster > 0)
                {
                    int nose = RocketAssembly.AddChild(bp, booster, "nose_cone", AttachMode.Above);
                    if (nose > 0) RocketAssembly.Resize(bp, nose, +1); // 1.25 m → 1.875 m to match the booster
                }
            }
            StagingSystem.Restage(bp);
            return bp;
        }
    
        /// <summary>Lunar lander: crew capsule on a hydrolox descent/ascent stage. ~6 km/s — lands and flies home.</summary>
        static int LanderStack(RocketBlueprint bp, out int capsule)
        {
            capsule = RocketAssembly.AddRoot(bp, "capsule_mk1");
            RocketAssembly.AddChild(bp, capsule, "parachute", AttachMode.Above);
            int shield = RocketAssembly.AddChild(bp, capsule, "heat_shield", AttachMode.Below);
            RocketAssembly.Resize(bp, shield, -2);
            int sep = RocketAssembly.AddChild(bp, shield, "decoupler", AttachMode.Below);
            int adapter = RocketAssembly.AddChild(bp, sep, "adapter_25_125", AttachMode.Below);
            int tank = RocketAssembly.AddChild(bp, adapter, "tank_25", AttachMode.Below);
            int eng = RocketAssembly.AddChild(bp, tank, "eng_hydra", AttachMode.Below);
            RocketAssembly.AddRadial(bp, tank, "landing_legs", 4, -0.42f, 45f);
            RocketAssembly.AddRadial(bp, tank, "solar_panel", 2, 0.3f, 0f);
            RocketAssembly.AddRadial(bp, tank, "rcs_quad", 4, 0.45f, 45f);
            return eng;
        }

        public static RocketBlueprint SeleneLander()
        {
            var bp = new RocketBlueprint { name = "Selene Lander (example)" };
            int capsule;
            LanderStack(bp, out capsule);
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>Three-stage Moon rocket with the Selene lander on top: Earth orbit, trans-lunar injection, landing and return.</summary>
        public static RocketBlueprint Selene()
        {
            var bp = new RocketBlueprint { name = "Selene Moon Rocket (example)" };
            int capsule;
            int landerEng = LanderStack(bp, out capsule);
            // stage 3: hydrolox trans-lunar stage
            int i3 = RocketAssembly.AddChild(bp, landerEng, "interstage", AttachMode.Below);
            int t3 = RocketAssembly.AddChild(bp, i3, "tank_25_xl", AttachMode.Below);
            int e3 = RocketAssembly.AddChild(bp, t3, "eng_hydra", AttachMode.Below);
            // stage 2: 3.75 m kerolox upper stage
            int i2 = RocketAssembly.AddChild(bp, e3, "interstage", AttachMode.Below);
            int a2 = RocketAssembly.AddChild(bp, i2, "adapter_25_375", AttachMode.Below);
            int t2 = RocketAssembly.AddChild(bp, a2, "tank_375", AttachMode.Below);
            int e2 = RocketAssembly.AddChild(bp, t2, "eng_vac", AttachMode.Below);
            // stage 1: 5 m core, five Titan-H engines, four solid boosters
            int i1 = RocketAssembly.AddChild(bp, e2, "interstage", AttachMode.Below);
            RocketAssembly.Resize(bp, i1, +2);
            int a1 = RocketAssembly.AddChild(bp, i1, "adapter_375_5", AttachMode.Below);
            int t1a = RocketAssembly.AddChild(bp, a1, "tank_5", AttachMode.Below);
            int t1b = RocketAssembly.AddChild(bp, t1a, "tank_5", AttachMode.Below);
            RocketAssembly.AddChild(bp, t1b, "eng_heavy", AttachMode.Below);
            RocketAssembly.AddRadial(bp, t1b, "eng_heavy", 4, -0.47f, 45f);
            RocketAssembly.AddRadial(bp, t1b, "fins", 4, -0.4f, 0f);
            var seps = RocketAssembly.AddRadial(bp, t1b, "radial_decoupler", 4, 0.2f, 0f);
            foreach (int s in seps)
            {
                int b = RocketAssembly.AddChild(bp, s, "srb_large", AttachMode.Side, 0.25f);
                if (b > 0) { int n = RocketAssembly.AddChild(bp, b, "nose_cone", AttachMode.Above); if (n > 0) RocketAssembly.Resize(bp, n, +1); }
            }
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>Mars hopper: lands with chutes and engine, and can climb back to Mars orbit (~5.5 km/s).</summary>
        public static RocketBlueprint AresLander()
        {
            var bp = new RocketBlueprint { name = "Ares Mars Hopper (example)" };
            int capsule = RocketAssembly.AddRoot(bp, "capsule_mk1");
            RocketAssembly.AddChild(bp, capsule, "parachute_heavy", AttachMode.Above);
            int adapter = RocketAssembly.AddChild(bp, capsule, "adapter_25_125", AttachMode.Below);
            int tank = RocketAssembly.AddChild(bp, adapter, "tank_25_l", AttachMode.Below);
            RocketAssembly.AddChild(bp, tank, "eng_aerospike", AttachMode.Below);
            RocketAssembly.AddRadial(bp, tank, "landing_legs_l", 4, -0.45f, 45f);
            RocketAssembly.AddRadial(bp, tank, "solar_panel", 2, 0.35f, 0f);
            RocketAssembly.AddRadial(bp, tank, "rcs_quad", 4, 0.46f, 45f);
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>Meridian with a docking port on the nose: flies up to the space station.</summary>
        public static RocketBlueprint StationShuttle()
        {
            var bp = new RocketBlueprint { name = "Station Shuttle (example)" };
            int capsule = RocketAssembly.AddRoot(bp, "capsule_mk1");
            int port = RocketAssembly.AddChild(bp, capsule, "docking_port", AttachMode.Above);
            RocketAssembly.AddRadial(bp, capsule, "parachute_radial", 2, 0.1f, 0f);
            RocketAssembly.AddRadial(bp, capsule, "rcs_quad", 4, 0f, 45f);
            int adapter = RocketAssembly.AddChild(bp, capsule, "adapter_25_125", AttachMode.Below);
            int upperTank = RocketAssembly.AddChild(bp, adapter, "tank_25_l", AttachMode.Below);
            RocketAssembly.AddRadial(bp, upperTank, "solar_panel", 2, 0.4f, 0f);
            int vac = RocketAssembly.AddChild(bp, upperTank, "eng_vac", AttachMode.Below);
            int inter = RocketAssembly.AddChild(bp, vac, "interstage", AttachMode.Below);
            int t1 = RocketAssembly.AddChild(bp, inter, "tank_25_xl", AttachMode.Below);
            int t2 = RocketAssembly.AddChild(bp, t1, "tank_25_l", AttachMode.Below);
            RocketAssembly.AddChild(bp, t2, "eng_heavy", AttachMode.Below);
            RocketAssembly.AddRadial(bp, t2, "fins", 4, -0.42f, 45f);
            var seps = RocketAssembly.AddRadial(bp, t2, "radial_decoupler", 2, 0.1f, 0f);
            foreach (int s in seps)
            {
                int b = RocketAssembly.AddChild(bp, s, "srb_large", AttachMode.Side, 0.25f);
                if (b > 0) { int n = RocketAssembly.AddChild(bp, b, "nose_cone", AttachMode.Above); if (n > 0) RocketAssembly.Resize(bp, n, +1); }
            }
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>Small RCS tug for docking practice: probe, docking port, monopropellant, kick engine, solar.</summary>
        public static RocketBlueprint DockingTug()
        {
            var bp = new RocketBlueprint { name = "Docking Tug (example)" };
            int core = RocketAssembly.AddRoot(bp, "probe_core");
            RocketAssembly.AddChild(bp, core, "docking_port", AttachMode.Above);
            int mono = RocketAssembly.AddChild(bp, core, "tank_mono", AttachMode.Below);
            int tank = RocketAssembly.AddChild(bp, mono, "tank_s", AttachMode.Below);
            RocketAssembly.AddChild(bp, tank, "eng_spark", AttachMode.Below);
            RocketAssembly.AddRadial(bp, mono, "rcs_quad", 4, 0f, 45f);
            RocketAssembly.AddRadial(bp, tank, "solar_panel", 2, 0f, 0f);
            StagingSystem.Restage(bp);
            return bp;
        }

        /// <summary>The space station, grown by one module per successful delivery (0..8).</summary>
        public static RocketBlueprint Station(int modules)
        {
            var bp = new RocketBlueprint { name = "Orbital Station" };
            int core = RocketAssembly.AddRoot(bp, "cargo_module");
            RocketAssembly.AddChild(bp, core, "docking_port", AttachMode.Above);
            RocketAssembly.AddRadial(bp, core, "solar_large", 2, 0f, 0f);
            RocketAssembly.AddRadial(bp, core, "antenna_omni", 1, 0.4f, 90f);
            int last = core;
            if (modules >= 1) { last = RocketAssembly.AddChild(bp, last, "crew_cabin", AttachMode.Below); }
            if (modules >= 2) { RocketAssembly.AddRadial(bp, last, "science_pod", 2, 0f, 90f); }
            if (modules >= 3) { last = RocketAssembly.AddChild(bp, last, "girder", AttachMode.Below); RocketAssembly.AddRadial(bp, last, "solar_large", 4, 0f, 45f); }
            if (modules >= 4) { last = RocketAssembly.AddChild(bp, last, "cargo_module", AttachMode.Below); RocketAssembly.AddRadial(bp, last, "dish_hga", 1, 0.2f, 0f); }
            if (modules >= 5) { last = RocketAssembly.AddChild(bp, last, "crew_cabin", AttachMode.Below); RocketAssembly.AddRadial(bp, last, "light_spot", 4, 0.3f, 0f); }
            if (modules >= 6) { last = RocketAssembly.AddChild(bp, last, "girder", AttachMode.Below); RocketAssembly.AddRadial(bp, last, "solar_large", 4, 0f, 0f); }
            if (modules >= 7) { last = RocketAssembly.AddChild(bp, last, "cargo_module", AttachMode.Below); RocketAssembly.AddRadial(bp, last, "dish_relay", 1, 0f, 180f); }
            if (modules >= 8) { last = RocketAssembly.AddChild(bp, last, "crew_cabin", AttachMode.Below); }
            StagingSystem.Restage(bp);
            return bp;
        }
    }
}
