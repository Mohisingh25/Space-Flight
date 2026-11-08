using System.Collections.Generic;
using Apogee.Parts;
using Apogee.Rocket;
using UnityEngine;

namespace Apogee.Rendering
{
    /// <summary>Mesh plus the material for each submesh.</summary>
    public class PartModel
    {
        public Mesh mesh;
        public Material[] materials;
        public string[] materialKeys;
    }

    /// <summary>
    /// Builds detailed procedural 3D models for every part type: lathed tanks with weld rings and
    /// roll patterns, engines with thrust structures, turbopumps, plumbing and regeneratively cooled
    /// bells, capsules with heat shields and windows, and so on. Stack parts are centred on the origin
    /// with their axis on +Y; radial parts attach at the origin and extend along +X.
    /// </summary>
    public static class PartMeshFactory
    {
        static readonly Dictionary<string, PartModel> cache = new Dictionary<string, PartModel>();

        class Mats
        {
            public readonly List<string> keys = new List<string>();
            public int this[string key]
            {
                get
                {
                    int i = keys.IndexOf(key);
                    if (i < 0) { keys.Add(key); i = keys.Count - 1; }
                    return i;
                }
            }
        }

        public static PartModel Get(PartInstance p)
        {
            var d = PartDims.Of(p);
            return Get(p.Def, d);
        }

        public static PartModel Get(PartDefinition def, PartDims dims)
        {
            string key = def.id + "|" + dims.diameter.ToString("0.000") + "|" + dims.height.ToString("0.000");
            PartModel model;
            if (cache.TryGetValue(key, out model) && model.mesh != null) return model;

            var mb = new MeshBuilder();
            var mats = new Mats();
            Build(def, dims, mb, mats);
            model = new PartModel();
            model.mesh = mb.ToMesh(key);
            model.materialKeys = mats.keys.ToArray();
            model.materials = new Material[mats.keys.Count];
            for (int i = 0; i < mats.keys.Count; i++) model.materials[i] = MaterialLibrary.Get(mats.keys[i]);
            cache[key] = model;
            return model;
        }

        static int Segs(float r) { return Mathf.Clamp(Mathf.RoundToInt(20f + r * 14f), 20, 64); }
        static float Circ(float r, float tile) { return Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * r / tile)); }

        static string BodyKey(Finish f)
        {
            switch (f)
            {
                case Finish.Stainless: return "stainless";
                case Finish.OrangeFoam: return "foam";
                case Finish.DarkComposite: return "carbon";
                case Finish.GoldFoil: return "gold";
                default: return "white";
            }
        }

        static void Build(PartDefinition def, PartDims d, MeshBuilder mb, Mats m)
        {
            float r = d.Radius, rt = d.TopRadius, h = d.height;
            switch (def.shape)
            {
                case PartShape.Tank: Tank(def, r, h, mb, m); break;
                case PartShape.NoseCone: NoseCone(r, h, mb, m); break;
                case PartShape.Adapter: Adapter(r, rt, h, mb, m); break;
                case PartShape.Decoupler: Decoupler(r, h, mb, m); break;
                case PartShape.RadialDecoupler: RadialDecoupler(d, mb, m); break;
                case PartShape.Interstage: Interstage(r, h, mb, m); break;
                case PartShape.Fairing: Fairing(r, h, mb, m); break;
                case PartShape.PayloadContainer: PayloadContainer(r, h, mb, m); break;
                case PartShape.Truss: Truss(r, h, mb, m); break;
                case PartShape.Engine: Engine(def, r, h, mb, m, false); break;
                case PartShape.VacuumEngine: Engine(def, r, h, mb, m, true); break;
                case PartShape.SolidBooster: SolidBooster(r, h, mb, m); break;
                case PartShape.RcsBlock: Rcs(mb, m); break;
                case PartShape.Fins: Fin(h, mb, m); break;
                case PartShape.LandingLegs: Leg(h, mb, m); break;
                case PartShape.Parachute: Parachute(r, rt, h, mb, m); break;
                case PartShape.SolarPanel: Solar(h, mb, m); break;
                case PartShape.Battery: Battery(h, mb, m); break;
                case PartShape.Antenna: Antenna(d.diameter, mb, m); break;
                case PartShape.NavComputer: NavUnit(r, h, mb, m); break;
                case PartShape.DockingPort: DockingPort(r, rt, h, mb, m); break;
                case PartShape.CargoModule: Cargo(r, h, mb, m); break;
                case PartShape.ScienceInstrument: Science(h, mb, m); break;
                case PartShape.Capsule: Capsule(r, rt, h, mb, m, 2); break;
                case PartShape.CommandModule: Capsule(r, rt, h, mb, m, 4); break;
                case PartShape.ProbeCore: Probe(r, h, mb, m); break;
                case PartShape.Wheel: Wheel(h, mb, m); break;
                case PartShape.Girder: Girder(r, h, mb, m); break;
                case PartShape.StructuralPanel: Panel(d.diameter, h, mb, m); break;
                case PartShape.HeatShield: HeatShield(r, h, mb, m); break;
                case PartShape.Aerospike: Aerospike(r, h, mb, m); break;
                case PartShape.SphereTank: SphereTank(r, d, mb, m); break;
                case PartShape.IBeam: IBeam(r, h, mb, m); break;
                case PartShape.Dish: Dish(d.diameter, h, mb, m); break;
                case PartShape.Rtg: Rtg(r, h, mb, m); break;
                case PartShape.Light: Lamp(r, mb, m); break;
                case PartShape.Ladder: Ladder(h, mb, m); break;
                case PartShape.EscapeTower: EscapeTower(r, h, mb, m); break;
                case PartShape.Decal: DecalPanel(d.diameter, h, mb, m); break;
                case PartShape.CrewCabin: CrewCabin(r, h, mb, m); break;
                case PartShape.RadialChute:
                {
                    var saved = mb.transform;
                    mb.transform = saved * Matrix4x4.TRS(new Vector3(r + 0.06f, 0f, 0f), Quaternion.identity, Vector3.one);
                    Parachute(r, r * 0.6f, h, mb, m);
                    mb.transform = saved;
                    mb.Box(new Vector3(0.04f, 0f, 0f), new Vector3(0.1f, h * 0.5f, r), m["dark_metal"]);
                    break;
                }
                default: mb.Cylinder(r, -h / 2, h / 2, Segs(r), m["white"]); break;
            }
        }

        static List<Vector2> P(params float[] xy)
        {
            var l = new List<Vector2>();
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(new Vector2(xy[i], xy[i + 1]));
            return l;
        }

        // ------------------------------------------------------------------ structures

        static void Tank(PartDefinition def, float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float y0 = -h / 2, y1 = h / 2, lip = Mathf.Min(0.06f, h * 0.05f);
            string body = BodyKey(def.finish);
            float uRep = Circ(r, 1.6f);
            if (def.finish == Finish.BlackWhite)
            {
                // Classic quadrant roll pattern: lower and upper halves offset by 90°.
                float ym = 0f;
                for (int q = 0; q < 4; q++)
                {
                    mb.Lathe(P(r, y0 + lip, r, ym), seg / 4 + 2, m[q % 2 == 0 ? "white" : "black"], uRep / 4f, 0.5f, q * 90f, 90f);
                    mb.Lathe(P(r, ym, r, y1 - lip), seg / 4 + 2, m[q % 2 == 0 ? "black" : "white"], uRep / 4f, 0.5f, q * 90f, 90f);
                }
            }
            else
            {
                mb.Lathe(P(r, y0 + lip, r, y1 - lip), seg, m[body], uRep, 0.5f);
            }
            // Machined end rings with a small chamfer
            mb.Lathe(P(r * 0.96f, y0, r * 1.008f, y0 + lip * 0.4f, r * 1.008f, y0 + lip, r, y0 + lip), seg, m["aluminium"]);
            mb.Lathe(P(r, y1 - lip, r * 1.008f, y1 - lip, r * 1.008f, y1 - lip * 0.4f, r * 0.96f, y1), seg, m["aluminium"]);
            mb.Lathe(P(0f, y0, r * 0.96f, y0), seg, m["dark_metal"]);          // bottom bulkhead (faces down)
            mb.Lathe(P(r * 0.96f, y1, 0f, y1), seg, m["dark_metal"]);          // top bulkhead (faces up)
            // Stiffening rings every ~1.5 m on painted tanks
            if (def.finish != Finish.OrangeFoam)
            {
                int rings = Mathf.FloorToInt(h / 1.5f);
                for (int i = 1; i <= rings; i++)
                {
                    float y = y0 + h * i / (rings + 1);
                    mb.Lathe(P(r, y - 0.025f, r * 1.006f, y - 0.015f, r * 1.006f, y + 0.015f, r, y + 0.025f), seg, m[def.finish == Finish.Stainless ? "stainless" : "aluminium"]);
                }
            }
            // External cable / pressurisation raceway on longer tanks
            if (h >= 1.8f)
            {
                string tray = def.finish == Finish.OrangeFoam ? "foam" : "aluminium";
                mb.Box(new Vector3(r + 0.035f, 0f, 0f), new Vector3(0.07f, h - lip * 4f, Mathf.Clamp(r * 0.12f, 0.08f, 0.22f)), m[tray]);
            }
        }

        static void NoseCone(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            var prof = new List<Vector2>();
            prof.Add(new Vector2(0f, -h / 2));
            prof.Add(new Vector2(r * 0.98f, -h / 2));
            prof.Add(new Vector2(r, -h / 2 + 0.04f));
            // Tangent ogive
            float L = h - 0.04f;
            float rho = (r * r + L * L) / (2f * r);
            int n = 18;
            for (int i = 1; i <= n; i++)
            {
                float y = L * i / n;
                float rad = Mathf.Sqrt(Mathf.Max(0f, rho * rho - (L - y) * (L - y))) + r - rho;
                if (i == n) rad = 0f;
                prof.Add(new Vector2(Mathf.Max(0f, rad), -h / 2 + 0.04f + y));
            }
            mb.Lathe(prof.GetRange(0, 2), seg, m["dark_metal"]);
            mb.Lathe(prof.GetRange(1, prof.Count - 1), seg, m["white"], Circ(r, 1.6f), 0.4f);
        }

        static void Adapter(float rb, float rt, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(rb);
            float lip = 0.05f;
            mb.Lathe(P(0f, -h / 2, rb * 0.97f, -h / 2, rb * 1.006f, -h / 2 + lip * 0.4f, rb * 1.006f, -h / 2 + lip), seg, m["aluminium"]);
            mb.Lathe(P(rb * 1.006f, -h / 2 + lip, rt * 1.006f, h / 2 - lip), seg, m["white"], Circ(rb, 1.6f), 0.5f);
            mb.Lathe(P(rt * 1.006f, h / 2 - lip, rt * 1.006f, h / 2 - lip * 0.4f, rt * 0.97f, h / 2, 0f, h / 2), seg, m["aluminium"]);
        }

        static void Decoupler(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float band = h * 0.3f;
            mb.Lathe(P(0f, -h / 2, r * 0.99f, -h / 2, r * 0.99f, -band / 2), seg, m["dark_metal"]);
            mb.Lathe(P(r * 0.995f, -band / 2, r * 0.995f, band / 2), seg, m["hazard"], Circ(r, 0.6f), 1f / Mathf.Max(band, 0.01f));
            mb.Lathe(P(r * 0.99f, band / 2, r * 0.99f, h / 2, 0f, h / 2), seg, m["dark_metal"]);
            int bolts = Mathf.Clamp(Mathf.RoundToInt(r * 12f), 8, 32);
            for (int i = 0; i < bolts; i++)
            {
                float a = i * Mathf.PI * 2f / bolts;
                var c = new Vector3(Mathf.Cos(a) * r, h * 0.32f, Mathf.Sin(a) * r);
                mb.Box(c, new Vector3(0.04f, 0.03f, 0.04f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), m["aluminium"]);
                c.y = -h * 0.32f;
                mb.Box(c, new Vector3(0.04f, 0.03f, 0.04f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), m["aluminium"]);
            }
        }

        static void RadialDecoupler(PartDims d, MeshBuilder mb, Mats m)
        {
            float depth = d.diameter, h = d.height;
            mb.Box(new Vector3(0.025f, 0f, 0f), new Vector3(0.05f, h * 0.9f, 0.3f), m["dark_metal"]);
            mb.Box(new Vector3(depth * 0.5f, 0f, 0f), new Vector3(depth - 0.08f, h * 0.35f, 0.14f), m["aluminium"]);
            mb.Box(new Vector3(depth * 0.5f, h * 0.25f, 0f), new Vector3(depth - 0.1f, 0.05f, 0.08f), m["dark_metal"]);
            mb.Box(new Vector3(depth * 0.5f, -h * 0.25f, 0f), new Vector3(depth - 0.1f, 0.05f, 0.08f), m["dark_metal"]);
            mb.Box(new Vector3(depth - 0.03f, 0f, 0f), new Vector3(0.06f, h * 0.7f, 0.32f), m["hazard"]);
        }

        static void Interstage(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float lip = 0.06f;
            mb.Lathe(P(r, -h / 2 + lip, r, h / 2 - lip * 2f), seg, m["carbon"], Circ(r, 1f), 1f);
            mb.Lathe(P(r * 0.97f, h / 2 - lip * 2f, r * 0.97f, -h / 2 + lip), seg, m["dark_metal"]); // inner wall
            mb.Lathe(P(r * 0.97f, -h / 2, r * 1.008f, -h / 2, r * 1.008f, -h / 2 + lip, r, -h / 2 + lip), seg, m["aluminium"]);
            mb.Lathe(P(r, h / 2 - lip * 2f, r * 1.006f, h / 2 - lip * 2f, r * 1.006f, h / 2, r * 0.97f, h / 2), seg, m["hazard"], Circ(r, 0.6f), 8f);
            // Vent grilles
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f);
                float ar = a * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(ar) * r, 0f, Mathf.Sin(ar) * r), new Vector3(0.03f, h * 0.25f, r * 0.35f), Quaternion.Euler(0f, -a, 0f), m["dark_metal"]);
            }
        }

        static void Fairing(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float cyl = h * 0.42f;
            var prof = new List<Vector2> { new Vector2(0f, -h / 2), new Vector2(r * 0.98f, -h / 2), new Vector2(r, -h / 2 + 0.05f), new Vector2(r, -h / 2 + cyl) };
            float L = h - cyl;
            float rho = (r * r + L * L) / (2f * r);
            for (int i = 1; i <= 20; i++)
            {
                float y = L * i / 20f;
                float rad = i == 20 ? 0f : Mathf.Sqrt(Mathf.Max(0f, rho * rho - (L - y) * (L - y))) + r - rho;
                prof.Add(new Vector2(Mathf.Max(0f, rad), -h / 2 + cyl + y));
            }
            mb.Lathe(prof, seg, m["white"], Circ(r, 1.6f), 0.4f);
            // Separation seam lines
            for (int i = 0; i < 2; i++)
            {
                float a = i * 180f;
                float ar = a * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(ar) * (r + 0.005f), -h / 2 + cyl * 0.5f, Mathf.Sin(ar) * (r + 0.005f)), new Vector3(0.012f, cyl, 0.03f), Quaternion.Euler(0f, -a, 0f), m["black"]);
            }
            mb.Lathe(P(r * 1.01f, -h / 2, r * 1.01f, -h / 2 + 0.08f), seg, m["aluminium"]);
        }

        static void PayloadContainer(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float lip = 0.08f;
            mb.Lathe(P(0f, -h / 2, r * 0.97f, -h / 2, r * 1.005f, -h / 2 + 0.03f, r * 1.005f, -h / 2 + lip), seg, m["aluminium"]);
            mb.Lathe(P(r * 0.98f, -h / 2 + lip, r * 0.98f, h / 2 - lip), seg, m["silver_foil"], Circ(r, 1.2f), 0.7f);
            mb.Lathe(P(r * 1.005f, h / 2 - lip, r * 1.005f, h / 2 - 0.03f, r * 0.97f, h / 2, 0f, h / 2), seg, m["aluminium"]);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f, ar = a * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(ar) * r, 0f, Mathf.Sin(ar) * r), new Vector3(0.05f, h - lip * 2f, 0.06f), Quaternion.Euler(0f, -a, 0f), m["aluminium"]);
            }
        }

        static void Truss(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float plate = 0.06f;
            mb.Cylinder(r * 0.98f, -h / 2, -h / 2 + plate, seg, m["dark_metal"]);
            mb.Cylinder(r * 0.98f, h / 2 - plate, h / 2, seg, m["dark_metal"]);
            float br = Mathf.Max(0.03f, r * 0.06f);
            float rr = r * 0.82f;
            for (int i = 0; i < 4; i++)
            {
                float a0 = (i * 90f + 45f) * Mathf.Deg2Rad, a1 = ((i + 1) * 90f + 45f) * Mathf.Deg2Rad;
                var p0 = new Vector3(Mathf.Cos(a0) * rr, 0f, Mathf.Sin(a0) * rr);
                var p1 = new Vector3(Mathf.Cos(a1) * rr, 0f, Mathf.Sin(a1) * rr);
                mb.Box(p0, new Vector3(br * 2f, h - plate * 2f, br * 2f), Quaternion.Euler(0f, -(i * 90f + 45f), 0f), m["carbon"]);
                mb.Tube(p0 + Vector3.up * (-h / 2 + plate), p1 + Vector3.up * (h / 2 - plate), br * 0.6f, 8, m["aluminium"]);
                mb.Tube(p1 + Vector3.up * (-h / 2 + plate), p0 + Vector3.up * (h / 2 - plate), br * 0.6f, 8, m["aluminium"]);
            }
            mb.Cylinder(r * 0.25f, -h / 2 + plate, h / 2 - plate, 16, m["copper"], false); // central feed line
        }

        static void Aerospike(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float y0 = -h / 2, y1 = h / 2;
            mb.Lathe(P(0f, y1, r, y1, r, y1 - h * 0.25f, r * 0.92f, y1 - h * 0.3f), seg, m["dark_metal"]);
            // truncated plug: the exhaust expands against the spike instead of a bell
            mb.Lathe(P(r * 0.9f, y1 - h * 0.3f, r * 0.62f, y0 + h * 0.25f, r * 0.3f, y0 + h * 0.02f, 0f, y0), seg, m["nozzle"]);
            mb.Lathe(P(r * 0.94f, y1 - h * 0.28f, r * 0.96f, y1 - h * 0.45f), seg, m["copper"]);
        }

        static void SphereTank(float r, PartDims d, MeshBuilder mb, Mats m)
        {
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.Scale(new Vector3(r, d.height * 0.5f, r));
            var prof = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float a = -Mathf.PI / 2 + Mathf.PI * i / 16f;
                prof.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            prof[0] = new Vector2(0f, -1f); prof[16] = new Vector2(0f, 1f);
            mb.Lathe(prof, 28, m[d.diameter < 1f ? "white" : "silver_foil"]);
            mb.transform = saved;
            mb.Lathe(P(r * 1.02f, -0.06f, r * 1.02f, 0.06f), 28, m["dark_metal"]);                 // equator ring
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                mb.Tube(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), new Vector3(Mathf.Cos(a) * r * 0.6f, -d.height * 0.5f, Mathf.Sin(a) * r * 0.6f), 0.03f, 6, m["aluminium"]);
            }
        }

        static void IBeam(float r, float h, MeshBuilder mb, Mats m)
        {
            float w = r * 1.6f, t = Mathf.Max(0.04f, r * 0.12f);
            mb.Box(new Vector3(0f, 0f, w * 0.5f - t * 0.5f), new Vector3(w, h, t), m["dark_metal"]);
            mb.Box(new Vector3(0f, 0f, -w * 0.5f + t * 0.5f), new Vector3(w, h, t), m["dark_metal"]);
            mb.Box(Vector3.zero, new Vector3(t, h, w), m["dark_metal"]);
            for (float y = -h / 2 + 0.3f; y < h / 2; y += 0.6f)
                mb.Box(new Vector3(t * 0.6f, y, w * 0.25f), new Vector3(0.03f, 0.05f, 0.05f), m["aluminium"]);
        }

        static void Dish(float dd, float h, MeshBuilder mb, Mats m)
        {
            float R = dd * 0.5f;
            mb.Box(new Vector3(0.05f, 0f, 0f), new Vector3(0.1f, 0.2f, 0.2f), m["dark_metal"]);
            mb.Tube(new Vector3(0.1f, 0f, 0f), new Vector3(h * 0.55f, 0f, 0f), 0.035f, 8, m["aluminium"]);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(new Vector3(h * 0.55f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            var prof = new List<Vector2>();
            for (int i = 0; i <= 10; i++) { float x = R * i / 10f; prof.Add(new Vector2(x, x * x / (4f * R * 0.45f))); }
            mb.Lathe(prof, 32, m["white"]);
            var back = new List<Vector2>();
            for (int i = 10; i >= 0; i--) { float x = R * i / 10f; back.Add(new Vector2(x, x * x / (4f * R * 0.45f) - 0.02f)); }
            mb.Lathe(back, 32, m["aluminium"]);
            float focus = R * 0.45f;
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f;
                mb.Tube(new Vector3(Mathf.Cos(a) * R * 0.95f, R * 0.5f, Mathf.Sin(a) * R * 0.95f), new Vector3(0f, focus, 0f), 0.012f, 4, m["aluminium"]);
            }
            mb.Lathe(P(0f, focus - 0.05f, R * 0.06f, focus - 0.05f, R * 0.06f, focus + 0.05f, 0f, focus + 0.05f), 12, m["gold"]);
            mb.transform = saved;
        }

        static void Rtg(float r, float h, MeshBuilder mb, Mats m)
        {
            mb.Tube(new Vector3(0.02f, 0f, 0f), new Vector3(0.25f + r * 0.3f, 0f, 0f), 0.04f, 8, m["aluminium"]);
            var c = new Vector3(0.25f + r * 0.6f, 0f, 0f);
            mb.Tube(c + Vector3.down * h * 0.45f, c + Vector3.up * h * 0.45f, r * 0.35f, 16, m["dark_metal"]);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mb.Box(c + dir * r * 0.6f, new Vector3(Mathf.Abs(dir.x) * r * 0.5f + 0.02f, h * 0.85f, Mathf.Abs(dir.z) * r * 0.5f + 0.02f), m["dark_metal"]);
            }
        }

        static void Lamp(float r, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.03f, 0f, 0f), new Vector3(0.06f, 0.15f, 0.15f), m["dark_metal"]);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(new Vector3(0.08f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            mb.Lathe(P(0f, 0f, r * 0.5f, 0f, r * 0.7f, r * 0.8f), 16, m["dark_metal"]);
            mb.Lathe(P(r * 0.68f, r * 0.8f, 0f, r * 0.82f), 16, m["screen_glow"]);
            mb.transform = saved;
        }

        static void Ladder(float h, MeshBuilder mb, Mats m)
        {
            float off = 0.12f, w = 0.22f;
            mb.Box(new Vector3(off, 0f, -w), new Vector3(0.04f, h, 0.04f), m["aluminium"]);
            mb.Box(new Vector3(off, 0f, w), new Vector3(0.04f, h, 0.04f), m["aluminium"]);
            for (float y = -h / 2 + 0.15f; y < h / 2; y += 0.3f) mb.Box(new Vector3(off, y, 0f), new Vector3(0.03f, 0.03f, w * 2f), m["aluminium"]);
            mb.Box(new Vector3(0.05f, h * 0.4f, 0f), new Vector3(0.1f, 0.06f, 0.1f), m["dark_metal"]);
            mb.Box(new Vector3(0.05f, -h * 0.4f, 0f), new Vector3(0.1f, 0.06f, 0.1f), m["dark_metal"]);
        }

        static void EscapeTower(float r, float h, MeshBuilder mb, Mats m)
        {
            float y0 = -h / 2;
            // truss legs onto the capsule nose
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f;
                var foot = new Vector3(Mathf.Cos(a) * r, y0, Mathf.Sin(a) * r);
                var top = new Vector3(Mathf.Cos(a) * r * 0.35f, y0 + h * 0.35f, Mathf.Sin(a) * r * 0.35f);
                mb.Tube(foot, top, 0.03f, 6, m["tower_steel"]);
                mb.Tube(foot, new Vector3(Mathf.Cos(a + Mathf.PI / 2) * r * 0.35f, y0 + h * 0.35f, Mathf.Sin(a + Mathf.PI / 2) * r * 0.35f), 0.02f, 6, m["tower_steel"]);
            }
            float rr = r * 0.4f;
            mb.Lathe(P(rr * 1.2f, y0 + h * 0.35f, rr, y0 + h * 0.4f, rr, y0 + h * 0.82f, rr * 0.6f, y0 + h * 0.92f, 0.04f, y0 + h), 16, m["white"]);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mb.Tube(n * rr * 0.6f + Vector3.up * (y0 + h * 0.43f), n * rr * 1.3f + Vector3.up * (y0 + h * 0.36f), 0.05f, 8, m["nozzle"]);
            }
            mb.Lathe(P(rr * 1.02f, y0 + h * 0.62f, rr * 1.02f, y0 + h * 0.66f), 16, m["light_red"]);
        }

        static void DecalPanel(float w, float h, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.015f, 0f, 0f), new Vector3(0.03f, h, w), m["white"]);
        }

        static void CrewCabin(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            mb.Lathe(P(r, -h / 2, r, h / 2), seg, m["white"]);
            mb.Lathe(P(r * 1.01f, -h / 2, r * 1.01f, -h / 2 + 0.12f), seg, m["dark_metal"]);
            mb.Lathe(P(r * 1.01f, h / 2 - 0.12f, r * 1.01f, h / 2), seg, m["dark_metal"]);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mb.Box(n * (r + 0.01f), new Vector3(0.05f + Mathf.Abs(n.x) * 0.02f, 0.45f, 0.45f), m["glass"]);
            }
        }

        /// <summary>Square lattice beam: four longerons with X-bracing on every face.</summary>
        static void Girder(float r, float h, MeshBuilder mb, Mats m)
        {
            float s = r * 0.85f, br = Mathf.Max(0.025f, r * 0.08f);
            var corners = new[] { new Vector3(s, 0f, s), new Vector3(-s, 0f, s), new Vector3(-s, 0f, -s), new Vector3(s, 0f, -s) };
            foreach (var c in corners) mb.Box(c, new Vector3(br * 2f, h, br * 2f), m["carbon"]);
            mb.Box(new Vector3(0f, -h / 2 + 0.03f, 0f), new Vector3(s * 2.2f, 0.06f, s * 2.2f), m["dark_metal"]);
            mb.Box(new Vector3(0f, h / 2 - 0.03f, 0f), new Vector3(s * 2.2f, 0.06f, s * 2.2f), m["dark_metal"]);
            int bays = Mathf.Max(1, Mathf.RoundToInt(h / (s * 2.2f)));
            for (int b = 0; b < bays; b++)
            {
                float y0 = -h / 2 + h * b / bays, y1 = -h / 2 + h * (b + 1) / bays;
                for (int i = 0; i < 4; i++)
                {
                    var a = corners[i];
                    var c = corners[(i + 1) % 4];
                    mb.Tube(a + Vector3.up * y0, c + Vector3.up * y1, br * 0.55f, 6, m["aluminium"]);
                    mb.Tube(c + Vector3.up * y0, a + Vector3.up * y1, br * 0.55f, 6, m["aluminium"]);
                }
            }
        }

        static void Panel(float w, float h, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(w * 0.5f, 0f, 0f), new Vector3(w, h, 0.05f), m["aluminium"]);
            mb.Box(new Vector3(w * 0.5f, 0f, 0.03f), new Vector3(w * 0.9f, h * 0.9f, 0.02f), m["white"]);
            mb.Box(new Vector3(0.03f, 0f, 0f), new Vector3(0.06f, h, 0.12f), m["dark_metal"]);
        }

        static void HeatShield(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float y0 = -h / 2, y1 = h / 2;
            mb.Lathe(P(0f, y0, r * 0.6f, y0 + h * 0.08f, r * 0.95f, y0 + h * 0.35f, r, y0 + h * 0.6f), seg, m["heatshield"]);
            mb.Lathe(P(r, y0 + h * 0.6f, r, y1, 0f, y1), seg, m["dark_metal"]);
        }

        /// <summary>Strut out from the hull, with a tyre whose axle is parallel to the hull's Z axis.</summary>
        static void Wheel(float h, MeshBuilder mb, Mats m)
        {
            float R = h * 0.5f, w = R * 0.55f;
            var hub = new Vector3(0.18f + R * 0.75f, -R * 0.35f, 0f);
            mb.Box(new Vector3(0.05f, 0f, 0f), new Vector3(0.1f, 0.3f, 0.25f), m["dark_metal"]);
            mb.Tube(new Vector3(0.1f, 0.05f, 0f), hub + new Vector3(0f, 0f, -w * 0.6f), 0.045f, 8, m["aluminium"]);
            mb.Tube(new Vector3(0.1f, -0.05f, 0f), hub + new Vector3(0f, 0f, -w * 0.6f), 0.035f, 8, m["dark_metal"]);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(hub, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            mb.Lathe(P(R * 0.6f, -w / 2, R * 0.92f, -w / 2, R, -w * 0.3f, R, w * 0.3f, R * 0.92f, w / 2, R * 0.6f, w / 2), 28, m["rubber"]);
            mb.Lathe(P(0f, -w * 0.42f, R * 0.6f, -w * 0.42f, R * 0.6f, w * 0.42f, 0f, w * 0.42f), 20, m["aluminium"]);
            mb.transform = saved;
        }

        // ------------------------------------------------------------------ propulsion

        static void Engine(PartDefinition def, float r, float h, MeshBuilder mb, Mats m, bool vacuum)
        {
            int seg = Segs(r);
            float top = h / 2;
            string frame = def.finish == Finish.Stainless ? "stainless" : def.finish == Finish.GoldFoil ? "gold" : "dark_metal";

            float mountH = h * 0.05f;
            float structH = vacuum ? h * 0.18f : h * 0.28f;
            float chamberH = vacuum ? h * 0.1f : h * 0.14f;
            float throatR = r * (vacuum ? 0.12f : 0.17f);
            float chamberR = throatR * 1.75f;
            float exitR = vacuum ? r * 1.32f : def.minThrottle <= 0.15f ? r * 0.82f : r * 0.88f;

            float yMount = top - mountH;
            float yStruct = yMount - structH;
            float yChamber = yStruct - chamberH;
            float yThroat = yChamber - chamberH * 0.35f;
            float yExit = -h / 2;

            // Mounting plate and thrust structure
            mb.Lathe(P(0f, yMount, r * 0.98f, yMount, r * 0.98f, top, 0f, top), seg, m["dark_metal"]);
            mb.Lathe(P(chamberR * 1.3f, yStruct, r * 0.9f, yMount), seg, m[frame], Circ(r, 1.2f), 1f);
            mb.Lathe(P(r * 0.9f, yMount, chamberR * 1.3f, yStruct), seg, m["dark_metal"]); // underside

            // Injector dome, chamber and throat
            mb.Lathe(P(chamberR * 1.1f, yChamber, chamberR, yChamber + 0.02f, chamberR, yStruct, chamberR * 0.6f, yStruct + chamberH * 0.2f, 0f, yStruct + chamberH * 0.25f), 24, m["dark_metal"]);
            mb.Lathe(P(throatR, yThroat, chamberR * 1.1f, yChamber), 24, m["copper"]);

            // Bell nozzle (outer + inner surface, v=0 at the throat for the heat gradient texture)
            int n = 16;
            var outer = new List<Vector2>();
            var inner = new List<Vector2>();
            float bellLen = yThroat - yExit;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float rad = throatR + (exitR - throatR) * (1f - Mathf.Pow(1f - t, 2.2f));
                float y = yThroat - bellLen * t;
                inner.Add(new Vector2(rad * 0.985f, y));
                outer.Insert(0, new Vector2(rad, y));
            }
            float len = 0f;
            for (int i = 1; i < outer.Count; i++) len += (outer[i] - outer[i - 1]).magnitude;
            int bellSeg = Mathf.Max(seg, Segs(exitR));
            mb.Lathe(outer, bellSeg, m["nozzle"], Circ(exitR, 0.15f) / 4f, -1f / len, 0f, 360f, 1f);
            mb.Lathe(inner, bellSeg, m["nozzle"], Circ(exitR, 0.15f) / 4f, 1f / len);
            mb.Lathe(P(exitR * 0.985f, yExit, exitR * 1.01f, yExit, exitR * 1.01f, yExit + 0.03f, exitR, yExit + 0.03f), bellSeg, m["dark_metal"]); // exit lip
            if (!vacuum)
            {
                // Cooling manifold ring a third of the way down the bell
                float tm = 0.33f;
                float radm = throatR + (exitR - throatR) * (1f - Mathf.Pow(1f - tm, 2.2f));
                float ym = yThroat - bellLen * tm;
                mb.Lathe(P(radm * 1.0f, ym - 0.03f, radm * 1.06f, ym - 0.015f, radm * 1.06f, ym + 0.015f, radm, ym + 0.03f), bellSeg, m["copper"]);
            }

            // Turbopump, gas generator and plumbing
            float pumpR = Mathf.Max(0.05f, r * 0.16f);
            var pump = new Vector3(chamberR + pumpR * 1.6f, yStruct - chamberH * 0.2f, 0f);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(pump, Quaternion.identity, Vector3.one);
            mb.Cylinder(pumpR, -chamberH * 0.6f, chamberH * 0.6f, 16, m["aluminium"]);
            mb.Cylinder(pumpR * 1.15f, chamberH * 0.25f, chamberH * 0.4f, 16, m["dark_metal"]);
            mb.transform = saved;
            float pipe = Mathf.Max(0.015f, r * 0.035f);
            mb.Tube(pump + new Vector3(0f, chamberH * 0.6f, 0f), new Vector3(chamberR * 0.5f, yStruct + chamberH * 0.2f, 0f), pipe, 8, m["copper"]);
            mb.Tube(pump + new Vector3(-pumpR, 0f, 0f), new Vector3(chamberR, yChamber + chamberH * 0.5f, pumpR * 0.5f), pipe, 8, m["aluminium"]);
            mb.Tube(pump + new Vector3(0f, -chamberH * 0.6f, 0f), new Vector3(pump.x * 1.05f, yThroat - bellLen * 0.25f, 0f), pipe * 1.2f, 8, m["dark_metal"]); // turbine exhaust
            mb.Tube(new Vector3(-chamberR * 0.8f, yStruct, chamberR * 0.8f), new Vector3(-chamberR * 1.1f, yThroat, chamberR * 1.1f), pipe, 8, m["copper"]);

            // Gimbal actuators
            if (def.gimbalRange > 0f)
            {
                for (int i = 0; i < 2; i++)
                {
                    float a = (i == 0 ? 135f : 225f) * Mathf.Deg2Rad;
                    var a0 = new Vector3(Mathf.Cos(a) * r * 0.75f, yMount - structH * 0.1f, Mathf.Sin(a) * r * 0.75f);
                    var a1 = new Vector3(Mathf.Cos(a) * chamberR * 1.2f, yThroat - bellLen * 0.08f, Mathf.Sin(a) * chamberR * 1.2f);
                    mb.Tube(a0, a1, pipe * 1.4f, 8, m["aluminium"]);
                }
            }
        }

        static void SolidBooster(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float skirtH = h * 0.1f;
            float yBody0 = -h / 2 + skirtH;
            mb.Lathe(P(r * 0.97f, h / 2, 0f, h / 2), seg, m["dark_metal"]);
            mb.Lathe(P(r, yBody0, r, h / 2 - 0.04f, r * 0.97f, h / 2), seg, m["white"], Circ(r, 1.6f), 0.3f);
            // Field joints between casing segments
            int segs = Mathf.Max(2, Mathf.RoundToInt(h / 3f));
            for (int i = 1; i < segs; i++)
            {
                float y = yBody0 + (h / 2 - yBody0) * i / segs;
                mb.Lathe(P(r, y - 0.06f, r * 1.015f, y - 0.05f, r * 1.015f, y + 0.05f, r, y + 0.06f), seg, m["black"]);
            }
            // Aft skirt flare and nozzle
            mb.Lathe(P(r * 1.12f, -h / 2, r, yBody0), seg, m["white"], Circ(r, 1.6f), 0.5f);
            mb.Lathe(P(0f, -h / 2 + skirtH * 0.05f, r * 1.12f, -h / 2), seg, m["dark_metal"]); // aft closure (faces down)
            float exitR = r * 0.6f;
            mb.Lathe(P(exitR, -h / 2 - 0.001f, r * 0.3f, -h / 2 + skirtH * 0.6f), seg, m["carbon"]);
            mb.Lathe(P(r * 0.29f, -h / 2 + skirtH * 0.6f, exitR * 0.97f, -h / 2), seg, m["dark_metal"]);
            // Attach rails
            mb.Box(new Vector3(-r - 0.02f, h * 0.1f, 0f), new Vector3(0.05f, h * 0.6f, 0.12f), m["aluminium"]);
        }

        static void Rcs(MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.02f, 0f, 0f), new Vector3(0.04f, 0.26f, 0.2f), m["dark_metal"]);
            mb.Box(new Vector3(0.12f, 0f, 0f), new Vector3(0.16f, 0.18f, 0.16f), m["white"]);
            var c = new Vector3(0.14f, 0f, 0f);
            Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right };
            foreach (var dir in dirs)
            {
                var a = c + dir * 0.08f;
                mb.Tube(a, a + dir * 0.06f, 0.022f, 8, m["dark_metal"]);
            }
        }

        // ------------------------------------------------------------------ aero & utility (radial parts extend along +X)

        static void Fin(float h, MeshBuilder mb, Mats m)
        {
            float span = 1.0f;
            var poly = new List<Vector2> { new Vector2(0f, -h / 2), new Vector2(span, -h / 2), new Vector2(span, -h / 2 + h * 0.32f), new Vector2(0f, h / 2) };
            mb.ExtrudedPolygon(poly, 0.06f, m["white"]);
            mb.Box(new Vector3(0.03f, 0f, 0f), new Vector3(0.06f, h * 0.95f, 0.14f), m["dark_metal"]);
            // leading-edge cap
            var le = new Vector2(span, -h / 2 + h * 0.32f) - new Vector2(0f, h / 2);
            mb.Tube(new Vector3(0f, h / 2, 0f), new Vector3(le.x, h / 2 + le.y, 0f), 0.032f, 8, m["black"]);
        }

        static void Leg(float h, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.06f, h * 0.3f, 0f), new Vector3(0.12f, 0.35f, 0.3f), m["dark_metal"]);
            var hip = new Vector3(0.12f, h * 0.3f, 0f);
            var foot = new Vector3(0.95f, -h / 2 + 0.12f, 0f);
            mb.Tube(hip, foot, 0.07f, 12, m["carbon"]);
            mb.Tube(hip + (foot - hip) * 0.15f, hip + (foot - hip) * 0.6f, 0.085f, 12, m["aluminium"]); // shock absorber sleeve
            var knee = new Vector3(0.1f, -h * 0.15f, 0f);
            mb.Tube(knee, hip + (foot - hip) * 0.55f, 0.04f, 8, m["aluminium"]);
            mb.Tube(knee + Vector3.forward * 0.1f, knee - Vector3.forward * 0.1f, 0.05f, 8, m["dark_metal"]);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(new Vector3(0.95f, -h / 2, 0f), Quaternion.identity, Vector3.one);
            mb.Lathe(P(0f, 0f, 0.26f, 0f, 0.24f, 0.08f, 0.08f, 0.12f), 20, m["aluminium"]);
            mb.transform = saved;
        }

        static void Parachute(float r, float rt, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            float yb = -h / 2;
            mb.Lathe(P(0f, yb, r, yb, r, yb + h * 0.15f), seg, m["dark_metal"]);
            mb.Lathe(P(r * 0.95f, yb + h * 0.15f, r * 0.9f, yb + h * 0.6f), seg, m["white"]);
            var dome = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                dome.Add(new Vector2(Mathf.Lerp(r * 0.9f, 0f, Mathf.Sin(t * Mathf.PI * 0.5f)), yb + h * 0.6f + h * 0.4f * Mathf.Sin(t * Mathf.PI * 0.5f) * 0.999f));
            }
            dome[dome.Count - 1] = new Vector2(0f, h / 2);
            mb.Lathe(dome, seg, m["chute"]);
        }

        static void Solar(float h, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.05f, 0f, 0f), new Vector3(0.1f, 0.25f, 0.2f), m["dark_metal"]);
            mb.Tube(new Vector3(0.1f, 0f, 0f), new Vector3(0.38f, 0f, 0f), 0.03f, 10, m["aluminium"]);
            float len = 1.5f, w = h;
            mb.Box(new Vector3(0.4f + len / 2, 0f, 0f), new Vector3(len, w, 0.025f), m["solar"]);
            mb.Box(new Vector3(0.4f + len / 2, w / 2, 0f), new Vector3(len, 0.03f, 0.035f), m["aluminium"]);
            mb.Box(new Vector3(0.4f + len / 2, -w / 2, 0f), new Vector3(len, 0.03f, 0.035f), m["aluminium"]);
            mb.Box(new Vector3(0.4f + len / 2, 0f, 0f), new Vector3(0.02f, w, 0.035f), m["aluminium"]);
        }

        static void Battery(float h, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.1f, 0f, 0f), new Vector3(0.2f, h, 0.3f), m["dark_metal"]);
            mb.Box(new Vector3(0.205f, h * 0.25f, 0f), new Vector3(0.01f, h * 0.15f, 0.26f), m["hazard"]);
            mb.Box(new Vector3(0.205f, -h * 0.1f, 0f), new Vector3(0.012f, h * 0.35f, 0.2f), m["screen_glow"]);
        }

        static void Antenna(float dishD, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.04f, 0f, 0f), new Vector3(0.08f, 0.2f, 0.16f), m["dark_metal"]);
            mb.Tube(new Vector3(0.08f, 0f, 0f), new Vector3(0.4f, 0f, 0f), 0.025f, 8, m["aluminium"]);
            var saved = mb.transform;
            mb.transform = saved * Matrix4x4.TRS(new Vector3(0.4f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            float R = dishD * 0.5f, depth = R * 0.35f;
            var back = new List<Vector2>();
            var front = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float rr = R * i / 10f;
                float y = depth * (rr / R) * (rr / R);
                back.Add(new Vector2(rr, y - 0.015f));
                front.Insert(0, new Vector2(rr, y));
            }
            mb.Lathe(back, 32, m["aluminium"]);
            mb.Lathe(front, 32, m["white"]);
            mb.Tube(Vector3.zero, new Vector3(0f, depth * 1.4f, 0f), 0.012f, 6, m["aluminium"]);
            mb.Cylinder(0.035f, depth * 1.4f, depth * 1.4f + 0.06f, 10, m["dark_metal"]);
            mb.transform = saved;
        }

        static void NavUnit(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            mb.Lathe(P(0f, -h / 2, r, -h / 2, r, -h / 2 + 0.05f), seg, m["dark_metal"]);
            mb.Lathe(P(r * 0.99f, -h / 2 + 0.05f, r * 0.99f, h / 2 - 0.05f), seg, m["gold"], Circ(r, 0.8f), 2f);
            mb.Lathe(P(r, h / 2 - 0.05f, r, h / 2, 0f, h / 2), seg, m["dark_metal"]);
            for (int i = 0; i < 2; i++)
            {
                float a = i * 180f + 30f, ar = a * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(ar) * (r + 0.06f), 0f, Mathf.Sin(ar) * (r + 0.06f));
                mb.Box(c, new Vector3(0.12f, h * 0.6f, 0.12f), Quaternion.Euler(0f, -a, 0f), m["white"]);
                mb.Box(c + new Vector3(Mathf.Cos(ar), 0f, Mathf.Sin(ar)) * 0.065f, new Vector3(0.01f, h * 0.35f, 0.08f), Quaternion.Euler(0f, -a, 0f), m["glass"]);
            }
        }

        static void DockingPort(float r, float rt, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            mb.Lathe(P(0f, -h / 2, r, -h / 2, r, -h / 2 + h * 0.3f, rt, h * 0.1f, rt, h / 2 - 0.06f), seg, m["aluminium"]);
            // Soft-capture ring with rounded lip
            mb.Lathe(P(rt, h / 2 - 0.06f, rt * 1.04f, h / 2 - 0.04f, rt * 1.04f, h / 2, rt * 0.8f, h / 2, rt * 0.8f, h / 2 - 0.04f, 0f, h / 2 - 0.04f), seg, m["white"]);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f, ar = a * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(ar) * rt * 0.88f, h / 2 + 0.08f, Mathf.Sin(ar) * rt * 0.88f);
                mb.Box(c, new Vector3(0.04f, 0.18f, rt * 0.4f), Quaternion.Euler(0f, -a, -12f), m["dark_metal"]);
            }
        }

        static void Cargo(float r, float h, MeshBuilder mb, Mats m)
        {
            int seg = Segs(r);
            mb.Lathe(P(0f, -h / 2, r * 0.97f, -h / 2, r, -h / 2 + 0.05f, r, -h / 2 + 0.12f), seg, m["aluminium"]);
            mb.Lathe(P(r * 0.99f, -h / 2 + 0.12f, r * 0.99f, h / 2 - 0.12f), seg, m["mli_white"], Circ(r, 1.4f), 0.6f);
            mb.Lathe(P(r, h / 2 - 0.12f, r, h / 2 - 0.05f, r * 0.97f, h / 2, 0f, h / 2), seg, m["aluminium"]);
            mb.Box(new Vector3(r + 0.01f, 0f, 0f), new Vector3(0.03f, h * 0.45f, r * 0.55f), m["aluminium"]); // hatch
            for (int i = 0; i < 3; i++)
            {
                float a = 60f + i * 100f, ar = a * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(ar) * (r + 0.05f), 0f, Mathf.Sin(ar) * (r + 0.05f)), new Vector3(0.03f, h * 0.7f, 0.04f), Quaternion.Euler(0f, -a, 0f), m["gold"]); // handrails
            }
        }

        static void Science(float h, MeshBuilder mb, Mats m)
        {
            mb.Box(new Vector3(0.1f, 0f, 0f), new Vector3(0.2f, h, 0.25f), m["gold"]);
            mb.Tube(new Vector3(0.2f, h * 0.2f, 0f), new Vector3(0.45f, h * 0.45f, 0f), 0.012f, 6, m["aluminium"]);
            mb.Box(new Vector3(0.45f, h * 0.45f, 0f), new Vector3(0.05f, 0.05f, 0.05f), m["white"]);
            mb.Box(new Vector3(0.205f, -h * 0.15f, 0f), new Vector3(0.01f, h * 0.3f, 0.14f), m["glass"]);
        }

        // ------------------------------------------------------------------ command

        static void Capsule(float r, float rt, float h, MeshBuilder mb, Mats m, int windows)
        {
            int seg = Segs(r);
            float yb = -h / 2;
            float shieldH = h * 0.08f;
            float tunnelH = h * 0.16f;
            float yConeTop = h / 2 - tunnelH;
            // Heat shield: shallow dome
            mb.Lathe(P(0f, yb, r * 0.6f, yb + shieldH * 0.35f, r * 0.95f, yb + shieldH * 0.8f, r, yb + shieldH), seg, m["heatshield"], Circ(r, 0.8f), 1f);
            // Conical pressure vessel with ablative backshell
            mb.Lathe(P(r, yb + shieldH, r * 0.985f, yb + shieldH + 0.04f, rt * 1.35f, yConeTop - 0.06f, rt * 1.25f, yConeTop), seg, m["silver_foil"], Circ(r, 1.2f), 0.8f);
            mb.Lathe(P(rt * 1.25f, yConeTop, rt, yConeTop + 0.02f, rt, h / 2 - 0.03f, rt * 0.94f, h / 2, 0f, h / 2), Segs(rt), m["dark_metal"]);
            // Windows on the cone
            float slope = Mathf.Atan2(r - rt * 1.35f, yConeTop - (yb + shieldH)) * Mathf.Rad2Deg;
            for (int i = 0; i < windows; i++)
            {
                float a = (windows == 2 ? -25f + i * 50f : i * 90f + 15f);
                float t = 0.62f;
                float rad = Mathf.Lerp(r, rt * 1.35f, t);
                float y = Mathf.Lerp(yb + shieldH, yConeTop, t);
                float ar = a * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(ar) * rad, y, Mathf.Sin(ar) * rad);
                var rot = Quaternion.Euler(0f, -a, 0f) * Quaternion.Euler(0f, 0f, slope);
                float ws = r * 0.22f;
                mb.Box(c, new Vector3(0.03f, ws * 1.1f, ws * 1.1f), rot, m["dark_metal"]);
                mb.Box(c + rot * new Vector3(0.012f, 0f, 0f), new Vector3(0.012f, ws * 0.85f, ws * 0.85f), rot, m["glass"]);
            }
            // Hatch and RCS ports
            {
                float a = 180f, ar = a * Mathf.Deg2Rad, t = 0.45f;
                float rad = Mathf.Lerp(r, rt * 1.35f, t);
                float y = Mathf.Lerp(yb + shieldH, yConeTop, t);
                var rot = Quaternion.Euler(0f, -a, 0f) * Quaternion.Euler(0f, 0f, slope);
                mb.Box(new Vector3(Mathf.Cos(ar) * rad, y, Mathf.Sin(ar) * rad), new Vector3(0.025f, r * 0.42f, r * 0.36f), rot, m["aluminium"]);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f, ar = a * Mathf.Deg2Rad, t = 0.85f;
                float rad = Mathf.Lerp(r, rt * 1.35f, t);
                float y = Mathf.Lerp(yb + shieldH, yConeTop, t);
                var rot = Quaternion.Euler(0f, -a, 0f) * Quaternion.Euler(0f, 0f, slope);
                mb.Box(new Vector3(Mathf.Cos(ar) * rad, y, Mathf.Sin(ar) * rad), new Vector3(0.05f, 0.1f, 0.1f), rot, m["dark_metal"]);
            }
        }

        static void Probe(float r, float h, MeshBuilder mb, Mats m)
        {
            mb.Lathe(P(0f, -h / 2, r, -h / 2, r, -h / 2 + 0.04f), 8, m["dark_metal"], 1f, 1f, 22.5f);
            mb.Lathe(P(r * 0.97f, -h / 2 + 0.04f, r * 0.97f, h / 2 - 0.04f), 8, m["gold"], 4f, 2f, 22.5f);
            mb.Lathe(P(r, h / 2 - 0.04f, r, h / 2, 0f, h / 2), 8, m["dark_metal"], 1f, 1f, 22.5f);
            mb.Box(new Vector3(r * 0.97f, 0f, 0f), new Vector3(0.01f, h * 0.4f, r * 0.5f), m["screen_glow"]);
            mb.Tube(new Vector3(0f, h / 2, r * 0.5f), new Vector3(0f, h / 2 + 0.25f, r * 0.5f), 0.01f, 6, m["aluminium"]);
        }

        // ------------------------------------------------------------------ helpers for other systems

        /// <summary>A UV sphere with equirectangular UVs (seam duplicated) for planets.</summary>
        public static Mesh UVSphere(int lat, int lon, float radius)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat;
                float th = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon;
                    float ph = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), -Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    verts.Add(n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(1f - u, v));
                }
            }
            int row = lon + 1;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * row + x, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
            var mesh = new Mesh();
            mesh.name = "UVSphere";
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
