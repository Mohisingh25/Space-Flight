using System;

namespace Apogee.Parts
{
    public enum PartCategory
    {
        Command,
        Tanks,
        Engines,
        Structural,
        Coupling,
        Aero,
        Utility,
        Science
    }

    public enum PartShape
    {
        Capsule,
        CommandModule,
        ProbeCore,
        Tank,
        NoseCone,
        Adapter,
        Decoupler,
        RadialDecoupler,
        Interstage,
        Fairing,
        PayloadContainer,
        Truss,
        Engine,
        VacuumEngine,
        SolidBooster,
        RcsBlock,
        Fins,
        LandingLegs,
        Parachute,
        SolarPanel,
        Battery,
        Antenna,
        NavComputer,
        DockingPort,
        CargoModule,
        ScienceInstrument
    }

    /// <summary>Paint / material scheme used to build a part's look.</summary>
    public enum Finish
    {
        WhitePaint,
        BlackWhite,
        Stainless,
        OrangeFoam,
        DarkComposite,
        GoldFoil
    }

    /// <summary>
    /// Static description of a part type. All masses in tonnes, thrust in kN, sizes in metres.
    /// Instances in a rocket reference a definition by <see cref="id"/>.
    /// </summary>
    [Serializable]
    public class PartDefinition
    {
        public string id;
        public string displayName;
        public string description;
        public PartCategory category;
        public PartShape shape;
        public Finish finish = Finish.WhitePaint;

        // Geometry (base size; instances may rescale)
        public float diameter = 1.25f;     // bottom diameter
        public float topDiameter = 1.25f;  // top diameter (cones/adapters/capsules)
        public float height = 1f;

        // Mass
        public float dryMass;              // t
        public float fuelMass;             // t of propellant (liquid or solid)
        public bool solidFuel;

        // Propulsion
        public float thrustVac;            // kN
        public float thrustSL;             // kN
        public float ispVac;               // s
        public float ispSL;                // s
        public float gimbalRange;          // degrees
        public float minThrottle = 1f;     // 1 = not throttleable
        public bool isRcs;

        // Attachment rules
        public bool hasTopNode = true;
        public bool hasBottomNode = true;
        public bool radialMount;           // can be attached to the surface of another part
        public bool acceptsRadial;         // other parts can be attached to its surface
        public bool hasOuterNode;          // radial decoupler: a stack part hangs off its outer face
        public bool stackOnly = true;      // false for radial-only parts

        // Resizing
        public bool resizableDiameter;
        public bool resizableLength;
        public bool scaleHeightWithDiameter = true; // e.g. nose cones keep their proportions

        // Roles
        public bool isDecoupler;
        public bool isCommand;
        public int crew;
        public bool isParachute;
        public bool isFairing;
        public bool isLeg;
        public bool isPayload;             // carries an adjustable payload mass

        // Aerodynamics
        public float dragCoefficient = 0.8f;
        public float finArea;              // m² of lifting surface (fins)

        // Electrical / other (used by later phases, displayed in the builder now)
        public float batteryKWh;
        public float solarKW;
        public float antennaRangeKm;

        public bool IsEngine { get { return thrustVac > 0f && !isRcs; } }
        public bool HasFuel { get { return fuelMass > 0f; } }
        public bool IsStaged { get { return IsEngine || isDecoupler || isParachute || isFairing; } }

        public string ShortStats()
        {
            string s = FormatMass(dryMass + fuelMass);
            if (IsEngine) s += "  ·  " + thrustVac.ToString("0") + " kN  ·  Isp " + ispVac.ToString("0") + " s";
            else if (fuelMass > 0) s += "  ·  " + fuelMass.ToString("0.0") + " t fuel";
            if (crew > 0) s += "  ·  " + crew + " crew";
            return s;
        }

        public static string FormatMass(float t)
        {
            if (t < 1f) return (t * 1000f).ToString("0") + " kg";
            return t.ToString("0.00") + " t";
        }
    }
}
