using Apogee.Parts;
using Apogee.Rocket;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Rendering
{
    /// <summary>
    /// "Inside view" for one part: swaps the hull for a glass-like shell and shows what is really inside —
    /// propellant in separate fuel and oxidiser tanks (draining in flight), solid-fuel grain, combustion
    /// chambers and turbopumps, crew in their seats, flight computers, batteries and payload.
    /// </summary>
    public class XRay : MonoBehaviour
    {
        PartInstance part;
        string[] keys;
        Material[] original;
        Transform interior;
        Transform fuelLow, fuelHigh, grain;
        float lowBase, lowSpan, highBase, highSpan, innerR, grainR;
        bool on;

        static Material shell;
        static Mesh unitCylinder, unitSphere, unitCube;

        public static bool IsOn(GameObject go)
        {
            var x = go.GetComponent<XRay>();
            return x != null && x.on;
        }

        public static void Apply(GameObject go, PartInstance p, bool enable)
        {
            var x = go.GetComponent<XRay>();
            if (x == null)
            {
                if (!enable || p == null) return;
                x = go.AddComponent<XRay>();
                x.part = p;
                x.keys = PartMeshFactory.Get(p).materialKeys;
            }
            x.Set(enable);
        }

        /// <summary>Shows how full the part's propellant is (0..1).</summary>
        public static void SetFuel(GameObject go, float fraction)
        {
            var x = go.GetComponent<XRay>();
            if (x != null && x.on) x.Fill(Mathf.Clamp01(fraction));
        }

        void Set(bool enable)
        {
            var r = GetComponent<MeshRenderer>();
            if (r == null || on == enable) return;
            on = enable;
            var mpb = new MaterialPropertyBlock();
            if (enable)
            {
                original = r.sharedMaterials;
                var mats = new Material[original.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = Shell;
                r.sharedMaterials = mats;
                for (int i = 0; i < mats.Length; i++) { mpb.Clear(); r.SetPropertyBlock(mpb, i); }
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (interior == null) BuildInterior();
                if (interior != null) interior.gameObject.SetActive(true);
                Fill(1f);
            }
            else
            {
                if (original != null) r.sharedMaterials = original;
                r.shadowCastingMode = ShadowCastingMode.On;
                PartPaint.Apply(r, keys, part.paint, mpb);
                if (interior != null) interior.gameObject.SetActive(false);
            }
        }

        static Material Shell
        {
            get
            {
                if (shell == null)
                {
                    shell = MaterialLibrary.Custom("Ghost");
                    shell.SetColor("_Color", new Color(0.55f, 0.78f, 1f, 0.1f));
                }
                return shell;
            }
        }

        static void EnsureMeshes()
        {
            if (unitCylinder != null) return;
            var mb = new MeshBuilder();
            mb.Cylinder(1f, 0f, 1f, 28, 0);
            unitCylinder = mb.ToMesh("XRayCylinder");
            unitSphere = PartMeshFactory.UVSphere(14, 20, 1f);
            var cb = new MeshBuilder();
            cb.Box(Vector3.zero, Vector3.one, 0);
            unitCube = cb.ToMesh("XRayCube");
        }

        Transform Piece(Mesh mesh, string matKey, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(matKey);
            go.layer = gameObject.layer;
            go.transform.SetParent(interior, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Get(matKey);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        void BuildInterior()
        {
            EnsureMeshes();
            var def = part.Def;
            var d = PartDims.Of(part);
            float r = d.Radius, h = d.height;
            interior = new GameObject("Interior").transform;
            interior.SetParent(transform, false);
            interior.gameObject.layer = gameObject.layer;

            if (def.fuelMass > 0f && def.solidFuel)
            {
                // Solid motor: propellant grain around a hollow core; the grain thins as it burns.
                grainR = r * 0.9f;
                grain = Piece(unitCylinder, "fuel_solid", new Vector3(0f, -h * 0.46f, 0f), new Vector3(grainR, h * 0.86f, grainR));
                Piece(unitCylinder, "nozzle", new Vector3(0f, -h * 0.5f, 0f), new Vector3(r * 0.35f, h * 0.06f, r * 0.35f));
            }
            else if (def.fuelMass > 0f && !def.isRcs)
            {
                // Liquid stage: fuel tank below, oxidiser tank above, common bulkhead between, feed line through.
                innerR = r * 0.92f;
                float bottom = -h * 0.5f + 0.04f, top = h * 0.5f - 0.04f, span = top - bottom;
                lowBase = bottom; lowSpan = span * 0.4f;
                highBase = bottom + span * 0.43f; highSpan = span * 0.57f;
                fuelLow = Piece(unitCylinder, "fuel_rp1", new Vector3(0f, lowBase, 0f), new Vector3(innerR, lowSpan, innerR));
                fuelHigh = Piece(unitCylinder, "fuel_lox", new Vector3(0f, highBase, 0f), new Vector3(innerR, highSpan, innerR));
                Piece(unitCylinder, "dark_metal", new Vector3(0f, bottom + span * 0.405f, 0f), new Vector3(r * 0.95f, span * 0.02f, r * 0.95f));
                Piece(unitCylinder, "copper", new Vector3(r * 0.55f, bottom, 0f), new Vector3(Mathf.Max(0.03f, r * 0.06f), span, Mathf.Max(0.03f, r * 0.06f)));
            }
            else if (def.IsEngine)
            {
                // Turbopump, combustion chamber and propellant feeds above the nozzle.
                float cy = h * 0.5f - h * 0.38f;
                Piece(unitSphere, "copper", new Vector3(0f, cy, 0f), new Vector3(r * 0.32f, h * 0.16f, r * 0.32f));
                Piece(unitCube, "dark_metal", new Vector3(r * 0.42f, h * 0.5f - h * 0.18f, 0f), new Vector3(r * 0.3f, h * 0.22f, r * 0.3f));
                Piece(unitCylinder, "copper", new Vector3(-r * 0.3f, cy, 0f), new Vector3(r * 0.06f, h * 0.38f, r * 0.06f));
                Piece(unitCylinder, "light_red", new Vector3(0f, cy - h * 0.2f, 0f), new Vector3(r * 0.14f, h * 0.08f, r * 0.14f));
            }
            else if (def.isCommand && def.crew > 0)
            {
                // Crew seats with astronauts, and the instrument panel in front of them.
                for (int i = 0; i < def.crew; i++)
                {
                    float x = def.crew == 1 ? 0f : Mathf.Lerp(-r * 0.45f, r * 0.45f, i / (float)(def.crew - 1));
                    Piece(unitCube, "seat", new Vector3(x, -h * 0.22f, 0f), new Vector3(r * 0.32f, h * 0.08f, r * 0.45f));
                    Piece(unitCube, "suit", new Vector3(x, -h * 0.08f, 0f), new Vector3(r * 0.22f, h * 0.22f, r * 0.2f));
                    Piece(unitSphere, "suit", new Vector3(x, h * 0.1f, 0f), Vector3.one * r * 0.13f);
                    Piece(unitSphere, "glass", new Vector3(x, h * 0.1f, -r * 0.05f), Vector3.one * r * 0.1f);
                }
                Piece(unitCube, "screen_glow", new Vector3(0f, h * 0.18f, r * 0.3f), new Vector3(r * 0.9f, h * 0.12f, r * 0.05f));
            }
            else if (def.isCommand || def.shape == PartShape.NavComputer || def.batteryKWh > 0f)
            {
                Piece(unitCube, "screen_glow", Vector3.zero, new Vector3(r * 0.8f, h * 0.5f, r * 0.8f));
            }
            else if (def.isPayload)
            {
                float s = Mathf.Clamp01(0.35f + part.payloadMass / 20f);
                Piece(unitCube, "mli_white", Vector3.zero, new Vector3(r * 1.2f * s, h * 0.8f * s, r * 1.2f * s));
            }
        }

        void Fill(float f)
        {
            if (fuelLow != null)
            {
                fuelLow.localScale = new Vector3(innerR, Mathf.Max(0.0001f, lowSpan * f), innerR);
                fuelHigh.localScale = new Vector3(innerR, Mathf.Max(0.0001f, highSpan * f), innerR);
                fuelLow.gameObject.SetActive(f > 0.002f);
                fuelHigh.gameObject.SetActive(f > 0.002f);
            }
            if (grain != null)
            {
                float gr = grainR * Mathf.Sqrt(Mathf.Max(0.02f, f));
                grain.localScale = new Vector3(gr, grain.localScale.y, gr);
                grain.gameObject.SetActive(f > 0.002f);
            }
        }
    }
}
