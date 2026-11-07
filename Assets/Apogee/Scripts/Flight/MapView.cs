using System;
using System.Collections.Generic;
using Apogee.Rendering;
using Apogee.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Flight
{
    /// <summary>
    /// Orbital map: the planet and its atmosphere, the predicted trajectory (two-body conic, cut where it
    /// meets the ground), the vessel marker and apoapsis / periapsis markers. Drawn on its own layer with
    /// an orthographic camera; 1 unit = 1 km.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        public const int Layer = 29;
        const double Scale = 0.001;

        public Camera MapCamera { get; private set; }
        public bool Active { get; private set; }

        Transform root, vesselMarker, apMarker, peMarker;
        Mesh orbitMesh;
        Material lineMat;
        float orthoSize = 9500f;
        Vector2 pan;
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Vector3> lv = new List<Vector3>();
        readonly List<Color> lc = new List<Color>();
        readonly List<int> lt = new List<int>();
        public bool HasApoapsis, HasPeriapsis;
        public Vector3 ApoWorld, PeriWorld;

        public void Init(EquatorBand band)
        {
            root = new GameObject("Map").transform;
            var camGo = new GameObject("Map Camera");
            MapCamera = camGo.AddComponent<Camera>();
            MapCamera.orthographic = true;
            MapCamera.orthographicSize = orthoSize;
            MapCamera.cullingMask = 1 << Layer;
            MapCamera.clearFlags = CameraClearFlags.SolidColor;
            MapCamera.backgroundColor = new Color(0.01f, 0.015f, 0.03f);
            MapCamera.nearClipPlane = 1f;
            MapCamera.farClipPlane = 1000f;
            camGo.transform.position = new Vector3(0f, 0f, -500f);
            MapCamera.enabled = false;

            lineMat = MaterialLibrary.Custom("VertexColor");
            float R = (float)(FlightVessel.R * Scale);

            // Planet disk coloured from the same surface band as the flight view, with a soft limb
            var mesh = new Mesh { name = "MapPlanet" };
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { new Color(0.05f, 0.08f, 0.12f) };
            var t = new List<int>();
            int n = 360;
            for (int i = 0; i <= n; i++)
            {
                double phi = 2 * Math.PI * i / n;
                v.Add(new Vector3((float)Math.Cos(phi) * R, (float)Math.Sin(phi) * R, 0f));
                var col = band.Sample(phi);
                c.Add(col);
                if (i < n) { t.Add(0); t.Add(i + 2); t.Add(i + 1); }
            }
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            Make("Planet", mesh, 0f);

            // Atmosphere ring (to 70 km)
            var ring = new Mesh { name = "MapAtmosphere" };
            var rv = new List<Vector3>();
            var rc = new List<Color>();
            var rt = new List<int>();
            float top = (float)((FlightVessel.R + 140000) * Scale);
            for (int i = 0; i <= n; i++)
            {
                double phi = 2 * Math.PI * i / n;
                float cs = (float)Math.Cos(phi), sn = (float)Math.Sin(phi);
                rv.Add(new Vector3(cs * R, sn * R, 0f));
                rv.Add(new Vector3(cs * top, sn * top, 0f));
                rc.Add(new Color(0.4f, 0.65f, 1f, 0.55f));
                rc.Add(new Color(0.3f, 0.5f, 1f, 0f));
                if (i < n) { int b = i * 2; rt.Add(b); rt.Add(b + 1); rt.Add(b + 2); rt.Add(b + 1); rt.Add(b + 3); rt.Add(b + 2); }
            }
            ring.SetVertices(rv);
            ring.SetColors(rc);
            ring.SetTriangles(rt, 0);
            ring.RecalculateBounds();
            Make("Atmosphere", ring, -1f);

            orbitMesh = new Mesh { name = "Orbit" };
            orbitMesh.MarkDynamic();
            Make("Orbit", orbitMesh, -2f);

            vesselMarker = Make("Vessel", Triangle(new Color(1f, 1f, 1f, 1f)), -3f).transform;
            apMarker = Make("Ap", Diamond(new Color(0.4f, 0.85f, 1f, 1f)), -3f).transform;
            peMarker = Make("Pe", Diamond(new Color(1f, 0.75f, 0.3f, 1f)), -3f).transform;
            root.gameObject.SetActive(false);
        }

        GameObject Make(string name, Mesh mesh, float z)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lineMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static Mesh Triangle(Color c)
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(0f, 1f, 0f), new Vector3(-0.6f, -0.7f, 0f), new Vector3(0.6f, -0.7f, 0f) });
            m.SetColors(new List<Color> { c, c, c });
            m.SetTriangles(new List<int> { 0, 2, 1 }, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Diamond(Color c)
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, -1f, 0f), new Vector3(-1f, 0f, 0f) });
            m.SetColors(new List<Color> { c, c, c, c });
            m.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }

        public void SetActive(bool on)
        {
            Active = on;
            root.gameObject.SetActive(on);
            MapCamera.enabled = on;
        }

        public void Zoom(float factor)
        {
            orthoSize = Mathf.Clamp(orthoSize * factor, 30f, 2000000f);
        }

        public void Pan(Vector2 pixelDelta)
        {
            float unitsPerPixel = orthoSize * 2f / Mathf.Max(1, Screen.height);
            pan -= pixelDelta * unitsPerPixel;
        }

        public void Recenter() { pan = Vector2.zero; }

        public void UpdateMap(FlightVessel v)
        {
            if (!Active) return;
            MapCamera.orthographicSize = Mathf.Lerp(MapCamera.orthographicSize, orthoSize, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            float size = MapCamera.orthographicSize;
            var vp = new Vector3((float)(v.pos.x * Scale), (float)(v.pos.y * Scale), 0f);
            // Follow the vessel when zoomed in close, the planet when zoomed far out
            float follow = Mathf.InverseLerp(9000f, 2500f, size);
            var focus = Vector3.Lerp(Vector3.zero, vp, follow) + new Vector3(pan.x, pan.y, 0f);
            MapCamera.transform.position = new Vector3(focus.x, focus.y, -500f);

            float marker = size * 0.035f;
            vesselMarker.localPosition = new Vector3(vp.x, vp.y, -3f);
            vesselMarker.localRotation = Quaternion.Euler(0f, 0f, (float)(v.angle * Mathf.Rad2Deg) - 90f);
            vesselMarker.localScale = Vector3.one * marker;

            BuildOrbit(v, size * 0.004f);
            apMarker.gameObject.SetActive(HasApoapsis);
            peMarker.gameObject.SetActive(HasPeriapsis);
            apMarker.localPosition = new Vector3(ApoWorld.x, ApoWorld.y, -3f);
            peMarker.localPosition = new Vector3(PeriWorld.x, PeriWorld.y, -3f);
            apMarker.localScale = peMarker.localScale = Vector3.one * marker * 0.5f;
        }

        void BuildOrbit(FlightVessel v, float width)
        {
            pts.Clear();
            HasApoapsis = HasPeriapsis = false;
            double mu = FlightVessel.MU, R = FlightVessel.R;
            var r = v.pos;
            var vel = v.vel;
            double h = Vector2d.Cross(r, vel);
            if (Math.Abs(h) > 1e3 && v.status != FlightStatus.Crashed)
            {
                var o = Orbit2D.From(r, vel, mu);
                double p = h * h / mu, e = o.eccentricity, w = o.argumentOfPeriapsis;
                double dir = h >= 0 ? 1 : -1;
                double nu0 = Math.Atan2(r.y, r.x) - w;
                double step = o.bound ? 2 * Math.PI / 720 : 0.004;
                int max = o.bound ? 721 : 4000;
                for (int k = 0; k < max; k++)
                {
                    double nu = nu0 + dir * k * step;
                    double denom = 1 + e * Math.Cos(nu);
                    if (denom <= 1e-6) break;
                    double rr = p / denom;
                    if (rr > R * 60) break;
                    pts.Add(new Vector3((float)(Math.Cos(nu + w) * rr * Scale), (float)(Math.Sin(nu + w) * rr * Scale), 0f));
                    if (rr < R && k > 0) break; // impact
                }
                if (o.bound && o.apoapsis > R && o.apoapsis < R * 60)
                {
                    HasApoapsis = true;
                    ApoWorld = new Vector3((float)(Math.Cos(Math.PI + w) * o.apoapsis * Scale), (float)(Math.Sin(Math.PI + w) * o.apoapsis * Scale), 0f);
                }
                if (o.periapsis > R && e > 1e-3)
                {
                    HasPeriapsis = true;
                    PeriWorld = new Vector3((float)(Math.Cos(w) * o.periapsis * Scale), (float)(Math.Sin(w) * o.periapsis * Scale), 0f);
                }
            }

            lv.Clear(); lc.Clear(); lt.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 a = pts[Mathf.Max(0, i - 1)], b = pts[Mathf.Min(pts.Count - 1, i + 1)];
                Vector3 d = (b - a);
                Vector3 nrm = new Vector3(-d.y, d.x, 0f).normalized * width;
                float fade = 1f - (float)i / Mathf.Max(1, pts.Count) * 0.4f;
                var col = new Color(0.35f, 0.85f, 1f, fade);
                lv.Add(pts[i] + nrm);
                lv.Add(pts[i] - nrm);
                lc.Add(col);
                lc.Add(col);
                if (i < pts.Count - 1)
                {
                    int q = i * 2;
                    lt.Add(q); lt.Add(q + 2); lt.Add(q + 1);
                    lt.Add(q + 1); lt.Add(q + 2); lt.Add(q + 3);
                }
            }
            orbitMesh.Clear();
            if (lv.Count >= 4)
            {
                orbitMesh.SetVertices(lv);
                orbitMesh.SetColors(lc);
                orbitMesh.SetTriangles(lt, 0);
                orbitMesh.RecalculateBounds();
            }
        }
    }
}
