using System.Collections.Generic;
using UnityEngine;

namespace Apogee.Parts
{
    /// <summary>
    /// The full list of buildable parts. Values are modelled on real hardware classes
    /// (kerolox/methalox engines, steel/aluminium tanks) at real-world scale, because the
    /// game uses a full-size Earth: reaching orbit needs roughly 9.3 km/s of delta-v.
    /// </summary>
    public static class PartCatalog
    {
        /// <summary>Standard diameters a resizable part can snap to (metres).</summary>
        public static readonly float[] DiameterSteps = { 0.625f, 1.25f, 1.875f, 2.5f, 3.75f, 5.0f };

        /// <summary>Length multipliers available for stretchable tanks.</summary>
        public static readonly float[] LengthSteps = { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f };

        // Propellant bulk density (kerolox mix ≈ 1.03 t/m³) times a usable fill fraction.
        const float PropellantPerCubicMetre = 0.95f;
        // Tank structure as a fraction of the propellant it holds (aluminium-lithium class).
        const float TankDryFraction = 0.075f;

        static List<PartDefinition> all;
        static Dictionary<string, PartDefinition> byId;

        public static List<PartDefinition> All
        {
            get { Ensure(); return all; }
        }

        public static PartDefinition Get(string id)
        {
            Ensure();
            PartDefinition d;
            return id != null && byId.TryGetValue(id, out d) ? d : null;
        }

        public static List<PartDefinition> InCategory(PartCategory c)
        {
            Ensure();
            var list = new List<PartDefinition>();
            foreach (var p in all) if (p.category == c) list.Add(p);
            return list;
        }

        public static string CategoryLabel(PartCategory c)
        {
            switch (c)
            {
                case PartCategory.Command: return "Command";
                case PartCategory.Tanks: return "Fuel Tanks";
                case PartCategory.Engines: return "Engines";
                case PartCategory.Structural: return "Structure";
                case PartCategory.Coupling: return "Staging";
                case PartCategory.Aero: return "Aero";
                case PartCategory.Utility: return "Utility";
                case PartCategory.Science: return "Science";
            }
            return c.ToString();
        }

        /// <summary>Propellant a cylinder of the given size holds.</summary>
        public static float TankFuel(float diameter, float height)
        {
            float r = diameter * 0.5f;
            return Mathf.PI * r * r * height * PropellantPerCubicMetre;
        }

        static void Ensure()
        {
            if (all != null) return;
            all = new List<PartDefinition>();
            byId = new Dictionary<string, PartDefinition>();
            Build();
            foreach (var p in all) byId[p.id] = p;
        }

        static PartDefinition Add(PartDefinition p)
        {
            all.Add(p);
            return p;
        }

        static PartDefinition Tank(string id, string name, float d, float h, Finish finish, string desc)
        {
            float fuel = TankFuel(d, h);
            return Add(new PartDefinition
            {
                id = id, displayName = name, description = desc,
                category = PartCategory.Tanks, shape = PartShape.Tank, finish = finish,
                diameter = d, topDiameter = d, height = h,
                fuelMass = fuel, dryMass = fuel * TankDryFraction,
                acceptsRadial = true, resizableDiameter = true, resizableLength = true,
                scaleHeightWithDiameter = false, dragCoefficient = 0.8f
            });
        }

        static void Build()
        {
            // ---------------- Command ----------------
            Add(new PartDefinition
            {
                id = "capsule_mk1", displayName = "Mk1 Crew Capsule",
                description = "Single-seat capsule with ablative heat shield, windows and a top docking tunnel.",
                category = PartCategory.Command, shape = PartShape.Capsule,
                diameter = 1.25f, topDiameter = 0.55f, height = 1.3f, dryMass = 0.84f,
                isCommand = true, crew = 1, acceptsRadial = true, dragCoefficient = 0.55f, batteryKWh = 2f
            });
            Add(new PartDefinition
            {
                id = "capsule_horizon", displayName = "Horizon Command Module",
                description = "Three-crew deep-space command module. Heavy, but carries life support for long missions.",
                category = PartCategory.Command, shape = PartShape.CommandModule,
                diameter = 2.5f, topDiameter = 0.9f, height = 2.1f, dryMass = 4.1f,
                isCommand = true, crew = 3, acceptsRadial = true, dragCoefficient = 0.55f, batteryKWh = 8f
            });
            Add(new PartDefinition
            {
                id = "probe_core", displayName = "Guidance Probe Core",
                description = "Uncrewed flight computer. Lets you fly satellites and probes with no crew aboard.",
                category = PartCategory.Command, shape = PartShape.ProbeCore, finish = Finish.GoldFoil,
                diameter = 0.625f, topDiameter = 0.625f, height = 0.4f, dryMass = 0.1f,
                isCommand = true, acceptsRadial = true, resizableDiameter = true, batteryKWh = 0.5f
            });

            // ---------------- Fuel tanks ----------------
            Tank("tank_s", "FT-1 Small Tank", 1.25f, 1.0f, Finish.WhitePaint, "Short 1.25 m kerosene/LOX tank.");
            Tank("tank_m", "FT-2 Medium Tank", 1.25f, 2.0f, Finish.WhitePaint, "Standard 1.25 m kerosene/LOX tank.");
            Tank("tank_l", "FT-4 Long Tank", 1.25f, 4.0f, Finish.BlackWhite, "Long 1.25 m tank with roll-pattern paint.");
            Tank("tank_25", "HT-3 Core Tank", 2.5f, 3.0f, Finish.WhitePaint, "2.5 m core-stage tank.");
            Tank("tank_25_l", "HT-6 Long Core Tank", 2.5f, 6.0f, Finish.Stainless, "Stainless-steel 2.5 m tank, 6 m long.");
            Tank("tank_375", "XT-8 Heavy Tank", 3.75f, 8.0f, Finish.OrangeFoam, "Foam-insulated 3.75 m heavy-lift tank.");

            // ---------------- Engines ----------------
            Add(new PartDefinition
            {
                id = "eng_small", displayName = "Kestrel-S Small Engine",
                description = "Compact pressure-fed engine for small stages and landers.",
                category = PartCategory.Engines, shape = PartShape.Engine, finish = Finish.DarkComposite,
                diameter = 0.625f, topDiameter = 0.625f, height = 0.9f, dryMass = 0.18f,
                thrustSL = 60f, thrustVac = 70f, ispSL = 270f, ispVac = 315f, gimbalRange = 4f, minThrottle = 0.4f,
                dragCoefficient = 0.5f
            });
            Add(new PartDefinition
            {
                id = "eng_medium", displayName = "Vector-M Booster Engine",
                description = "Gas-generator kerolox engine. Workhorse first-stage engine for 1.25 m and clustered stages.",
                category = PartCategory.Engines, shape = PartShape.Engine, finish = Finish.DarkComposite,
                diameter = 1.25f, topDiameter = 1.25f, height = 1.8f, dryMass = 0.47f,
                thrustSL = 760f, thrustVac = 845f, ispSL = 282f, ispVac = 311f, gimbalRange = 5f, minThrottle = 0.4f
            });
            Add(new PartDefinition
            {
                id = "eng_heavy", displayName = "Titan-H Heavy-Lift Engine",
                description = "Full-flow methalox engine. High thrust and efficiency for 2.5 m core stages.",
                category = PartCategory.Engines, shape = PartShape.Engine, finish = Finish.Stainless,
                diameter = 2.5f, topDiameter = 2.5f, height = 3.2f, dryMass = 1.6f,
                thrustSL = 2256f, thrustVac = 2450f, ispSL = 327f, ispVac = 350f, gimbalRange = 6f, minThrottle = 0.4f
            });
            Add(new PartDefinition
            {
                id = "eng_vac", displayName = "Vector-V Vacuum Engine",
                description = "Vacuum-optimised engine with a huge nozzle bell. Very efficient in space, poor at sea level.",
                category = PartCategory.Engines, shape = PartShape.VacuumEngine, finish = Finish.DarkComposite,
                diameter = 1.25f, topDiameter = 1.25f, height = 3.0f, dryMass = 0.6f,
                thrustSL = 300f, thrustVac = 981f, ispSL = 110f, ispVac = 348f, gimbalRange = 4f, minThrottle = 0.4f
            });
            Add(new PartDefinition
            {
                id = "eng_landing", displayName = "Lander-L Landing Engine",
                description = "Deep-throttling engine (down to 10 %) for powered descent and vertical landings.",
                category = PartCategory.Engines, shape = PartShape.Engine, finish = Finish.GoldFoil,
                diameter = 1.25f, topDiameter = 1.25f, height = 1.0f, dryMass = 0.25f,
                thrustSL = 40f, thrustVac = 45f, ispSL = 290f, ispVac = 311f, gimbalRange = 6f, minThrottle = 0.1f
            });
            Add(new PartDefinition
            {
                id = "srb_small", displayName = "SRB-6 Solid Booster",
                description = "Solid rocket booster. Huge thrust, cannot be throttled or shut down once lit.",
                category = PartCategory.Engines, shape = PartShape.SolidBooster, finish = Finish.WhitePaint,
                diameter = 1.25f, topDiameter = 1.25f, height = 7.0f, dryMass = 1.5f, fuelMass = 9.5f, solidFuel = true,
                thrustSL = 500f, thrustVac = 550f, ispSL = 230f, ispVac = 250f, acceptsRadial = true
            });
            Add(new PartDefinition
            {
                id = "srb_large", displayName = "SRB-12 Heavy Solid Booster",
                description = "Large segmented solid booster for heavy launchers.",
                category = PartCategory.Engines, shape = PartShape.SolidBooster, finish = Finish.WhitePaint,
                diameter = 1.875f, topDiameter = 1.875f, height = 12.0f, dryMass = 5.5f, fuelMass = 40f, solidFuel = true,
                thrustSL = 1600f, thrustVac = 1750f, ispSL = 237f, ispVac = 262f, acceptsRadial = true
            });
            Add(new PartDefinition
            {
                id = "rcs_quad", displayName = "RCS Thruster Quad",
                description = "Four-nozzle cold-gas attitude thruster block. Mount radially in symmetry.",
                category = PartCategory.Engines, shape = PartShape.RcsBlock, finish = Finish.WhitePaint,
                diameter = 0.25f, topDiameter = 0.25f, height = 0.3f, dryMass = 0.04f,
                thrustVac = 1f, thrustSL = 0.8f, ispVac = 240f, ispSL = 100f, isRcs = true,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });

            // ---------------- Structural ----------------
            Add(new PartDefinition
            {
                id = "nose_cone", displayName = "Ogive Nose Cone",
                description = "Aerodynamic nose cone. Greatly reduces drag on top of a stack or booster.",
                category = PartCategory.Aero, shape = PartShape.NoseCone,
                diameter = 1.25f, topDiameter = 0f, height = 1.6f, dryMass = 0.06f,
                hasTopNode = false, resizableDiameter = true, dragCoefficient = 0.2f
            });
            Add(new PartDefinition
            {
                id = "adapter_25_125", displayName = "Adapter 2.5 m to 1.25 m",
                description = "Conical adapter joining a 2.5 m stack (bottom) to a 1.25 m stack (top).",
                category = PartCategory.Structural, shape = PartShape.Adapter,
                diameter = 2.5f, topDiameter = 1.25f, height = 1.2f, dryMass = 0.15f, acceptsRadial = true,
                dragCoefficient = 0.5f
            });
            Add(new PartDefinition
            {
                id = "adapter_375_25", displayName = "Adapter 3.75 m to 2.5 m",
                description = "Conical adapter joining a 3.75 m stack (bottom) to a 2.5 m stack (top).",
                category = PartCategory.Structural, shape = PartShape.Adapter,
                diameter = 3.75f, topDiameter = 2.5f, height = 1.6f, dryMass = 0.35f, acceptsRadial = true,
                dragCoefficient = 0.5f
            });
            Add(new PartDefinition
            {
                id = "truss", displayName = "Structural Truss",
                description = "Open lattice support section. Light spacer between stack parts.",
                category = PartCategory.Structural, shape = PartShape.Truss, finish = Finish.DarkComposite,
                diameter = 1.25f, topDiameter = 1.25f, height = 1.0f, dryMass = 0.08f, resizableDiameter = true,
                acceptsRadial = true
            });
            Add(new PartDefinition
            {
                id = "payload_container", displayName = "Payload Container",
                description = "Pressurised payload section. Set the payload mass it carries (satellites, cargo).",
                category = PartCategory.Structural, shape = PartShape.PayloadContainer,
                diameter = 2.5f, topDiameter = 2.5f, height = 2.0f, dryMass = 0.3f, isPayload = true,
                resizableDiameter = true, acceptsRadial = true
            });
            Add(new PartDefinition
            {
                id = "fairing", displayName = "Payload Fairing",
                description = "Ogive fairing that shields payloads during ascent. Jettisoned by staging.",
                category = PartCategory.Aero, shape = PartShape.Fairing,
                diameter = 2.5f, topDiameter = 0f, height = 4.5f, dryMass = 0.45f, isFairing = true,
                hasTopNode = false, resizableDiameter = true, dragCoefficient = 0.25f
            });

            // ---------------- Coupling / staging ----------------
            Add(new PartDefinition
            {
                id = "decoupler", displayName = "Separation Ring",
                description = "Stack decoupler. Fires pyrotechnic bolts to drop everything below it.",
                category = PartCategory.Coupling, shape = PartShape.Decoupler,
                diameter = 1.25f, topDiameter = 1.25f, height = 0.2f, dryMass = 0.05f,
                isDecoupler = true, resizableDiameter = true
            });
            Add(new PartDefinition
            {
                id = "interstage", displayName = "Interstage Connector",
                description = "Tall separating interstage that houses the upper-stage engine bell.",
                category = PartCategory.Coupling, shape = PartShape.Interstage, finish = Finish.DarkComposite,
                diameter = 2.5f, topDiameter = 2.5f, height = 2.0f, dryMass = 0.4f,
                isDecoupler = true, resizableDiameter = true, scaleHeightWithDiameter = true, acceptsRadial = true
            });
            Add(new PartDefinition
            {
                id = "radial_decoupler", displayName = "Radial Separator",
                description = "Attaches a side booster to a core stage and pushes it away when fired.",
                category = PartCategory.Coupling, shape = PartShape.RadialDecoupler,
                diameter = 0.4f, topDiameter = 0.4f, height = 0.9f, dryMass = 0.05f, isDecoupler = true,
                radialMount = true, stackOnly = false, hasOuterNode = true, hasTopNode = false, hasBottomNode = false
            });
            Add(new PartDefinition
            {
                id = "docking_port", displayName = "Docking Port",
                description = "Androgynous docking ring for rendezvous with stations and other craft.",
                category = PartCategory.Coupling, shape = PartShape.DockingPort,
                diameter = 1.25f, topDiameter = 1.0f, height = 0.35f, dryMass = 0.05f
            });

            // ---------------- Aero ----------------
            Add(new PartDefinition
            {
                id = "fins", displayName = "Stabiliser Fin",
                description = "Swept tail fin. Moves the centre of pressure aft for a stable ascent. Use 3 or 4 in symmetry.",
                category = PartCategory.Aero, shape = PartShape.Fins,
                diameter = 0.1f, topDiameter = 0.1f, height = 1.6f, dryMass = 0.05f, finArea = 1.2f,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });

            // ---------------- Utility ----------------
            Add(new PartDefinition
            {
                id = "parachute", displayName = "Main Parachute",
                description = "Drogue + main canopy pack. Mounts on top of a capsule or on its side.",
                category = PartCategory.Utility, shape = PartShape.Parachute,
                diameter = 0.55f, topDiameter = 0.3f, height = 0.4f, dryMass = 0.1f, isParachute = true,
                hasTopNode = false, radialMount = true, stackOnly = true, dragCoefficient = 0.5f
            });
            Add(new PartDefinition
            {
                id = "landing_legs", displayName = "Landing Leg",
                description = "Deployable landing leg with shock strut. Use 3 or 4 in symmetry.",
                category = PartCategory.Utility, shape = PartShape.LandingLegs,
                diameter = 0.3f, topDiameter = 0.3f, height = 2.0f, dryMass = 0.08f, isLeg = true,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });
            Add(new PartDefinition
            {
                id = "solar_panel", displayName = "Solar Array",
                description = "Fold-out photovoltaic array, 0.6 kW in full sunlight.",
                category = PartCategory.Utility, shape = PartShape.SolarPanel,
                diameter = 0.2f, topDiameter = 0.2f, height = 1.2f, dryMass = 0.03f, solarKW = 0.6f,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });
            Add(new PartDefinition
            {
                id = "battery", displayName = "Battery Pack",
                description = "Lithium-ion battery bank, 1.5 kWh.",
                category = PartCategory.Utility, shape = PartShape.Battery,
                diameter = 0.3f, topDiameter = 0.3f, height = 0.4f, dryMass = 0.02f, batteryKWh = 1.5f,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });
            Add(new PartDefinition
            {
                id = "antenna", displayName = "Communications Dish",
                description = "High-gain dish antenna for long-range links with mission control.",
                category = PartCategory.Utility, shape = PartShape.Antenna,
                diameter = 0.5f, topDiameter = 0.5f, height = 0.7f, dryMass = 0.03f, antennaRangeKm = 400000f,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });
            Add(new PartDefinition
            {
                id = "nav_unit", displayName = "Navigation Unit",
                description = "Inertial measurement + star tracker ring. Required for precise orbital manoeuvres.",
                category = PartCategory.Utility, shape = PartShape.NavComputer, finish = Finish.GoldFoil,
                diameter = 1.25f, topDiameter = 1.25f, height = 0.3f, dryMass = 0.1f, resizableDiameter = true,
                acceptsRadial = true
            });
            Add(new PartDefinition
            {
                id = "cargo_module", displayName = "Cargo Module",
                description = "Pressurised logistics module for station resupply. Set its cargo mass.",
                category = PartCategory.Utility, shape = PartShape.CargoModule,
                diameter = 2.5f, topDiameter = 2.5f, height = 3.0f, dryMass = 0.9f, isPayload = true,
                acceptsRadial = true
            });

            // ---------------- Science ----------------
            Add(new PartDefinition
            {
                id = "science_pod", displayName = "Science Instrument Pod",
                description = "Thermometer, barometer and magnetometer suite. Collects data in flight (Phase 5 career).",
                category = PartCategory.Science, shape = PartShape.ScienceInstrument,
                diameter = 0.3f, topDiameter = 0.3f, height = 0.35f, dryMass = 0.02f,
                radialMount = true, stackOnly = false, hasTopNode = false, hasBottomNode = false
            });
        }
    }
}
