using System.Collections.Generic;

namespace Apogee.Rocket
{
    /// <summary>Ready-made designs shipped with the game (also used for the main-menu backdrop).</summary>
    public static class ExampleRockets
    {
        public static List<RocketBlueprint> All()
        {
            return new List<RocketBlueprint> { Pathfinder(), Meridian(), MeridianHeavy() };
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
    }
}
