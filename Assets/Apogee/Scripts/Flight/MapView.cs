using System;
using System.Collections.Generic;
using Apogee.Simulation;
using Apogee.Astro;
using UnityEngine;
using UnityEngine.UI;

namespace Apogee.Flight
{
    /// <summary>One leg of a predicted trajectory (patched conics).</summary>
    public class Patch
    {
        public CelestialBody body;
        public Orbit orbit;
        public double start, end;           // time span of this leg
        public List<Vector3d> points = new List<Vector3d>();  // relative to body
        public string endReason;            // "encounter", "escape", "impact", null
    }

    /// <summary>Trajectory prediction and transfer planning (pure maths; used by the map and tests).</summary>
    public static class NavPlanner
    {
        /// <summary>Predicts up to 3 legs: current orbit, then any moon/planet encounter or escape.</summary>
        public static List<Patch> Predict(CelestialBody body, Vector3d pos, Vector3d vel, double t, int samples = 180)
        {
            var result = new List<Patch>();
            for (int leg = 0; leg < 3; leg++)
            {
                var o = new Orbit(body.mu, pos, vel, t);
                var patch = new Patch { body = body, orbit = o, start = t };
                double span = o.e < 1 ? o.Period : 40 * 86400;
                if (o.e < 1 && o.Apoapsis > body.soi) span = Math.Min(span, 400 * 86400);
                span = Math.Min(span, body.parent == null ? 3e8 : 120 * 86400);
                int n = 900;
                double dt = span / n;
                Vector3d r = pos, v = vel;
                double tt = t;
                patch.points.Add(r);
                for (int i = 1; i <= n; i++)
                {
                    tt = t + dt * i;
                    o.StateAt(tt, out r, out v);
                    if (i % Math.Max(1, n / samples) == 0) patch.points.Add(r);
                    if (r.magnitude < body.radius) { patch.endReason = "impact"; break; }
                    if (body.parent != null && r.magnitude > body.soi) { patch.endReason = "escape"; break; }
                    CelestialBody hit = null;
                    foreach (var c in body.children)
                        if ((r - c.LocalPosition(tt)).magnitude < c.soi) { hit = c; break; }
                    if (hit != null)
                    {
                        patch.endReason = "encounter";
                        patch.end = tt;
                        patch.points.Add(r);
                        result.Add(patch);
                        Vector3d cr, cv;
                        hit.LocalState(tt, out cr, out cv);
                        pos = r - cr; vel = v - cv; t = tt; body = hit;
                        goto next;
                    }
                }
                patch.end = tt;
                if (patch.endReason == "escape")
                {
                    result.Add(patch);
                    Vector3d br, bv;
                    body.LocalState(tt, out br, out bv);
                    pos = r + br; vel = v + bv; t = tt; body = body.parent;
                    continue;
                }
                result.Add(patch);
                break;
                next:;
            }
            return result;
        }

        /// <summary>
        /// Hohmann transfer helper from the vessel's current orbit to 'target' (a moon of the current body,
        /// or a planet sharing the current body's parent). Returns a readable instruction, or null.
        /// </summary>
        public static string TransferAdvice(CelestialBody body, Vector3d pos, Vector3d vel, double t, CelestialBody target)
        {
            if (target == null || target == body) return null;
            double r1, r2, mu, phaseNow;
            bool interplanetary = false;
            if (target.parent == body)
            {
                mu = body.mu;
                r1 = pos.magnitude;
                var tp = target.LocalPosition(t);
                r2 = tp.magnitude;
                phaseNow = SignedAngle(pos, tp, Vector3d.Cross(pos, vel));
            }
            else if (body.parent != null && target.parent == body.parent)
            {
                interplanetary = true;
                mu = body.parent.mu;
                var bp = body.LocalPosition(t);
                Vector3d bpos, bvel;
                body.LocalState(t, out bpos, out bvel);
                r1 = bp.magnitude;
                var tp = target.LocalPosition(t);
                r2 = tp.magnitude;
                phaseNow = SignedAngle(bp, tp, Vector3d.Cross(bpos, bvel));
            }
            else return "Target: " + target.name + " — get into orbit around " + (target.parent != null ? target.parent.name : "the Sun") + " first.";

            double a = (r1 + r2) / 2;
            double tof = Math.PI * Math.Sqrt(a * a * a / mu);
            double need = 180 - (tof / (2 * Math.PI * Math.Sqrt(r2 * r2 * r2 / mu))) * 360;
            need = Wrap(need);
            double dv;
            if (!interplanetary)
            {
                double v1 = Math.Sqrt(mu / r1);
                dv = v1 * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1);
            }
            else
            {
                double vinf = Math.Sqrt(mu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1);
                double rp = pos.magnitude;
                dv = Math.Sqrt(vinf * vinf + 2 * body.mu / rp) - Math.Sqrt(body.mu / rp);
            }
            double diff = Wrap(phaseNow - need);
            string when;
            if (Math.Abs(diff) < 3) when = "<b>BURN NOW</b>";
            else
            {
                // how long until the phase angle comes round
                double w1 = interplanetary ? Math.Sqrt(mu / (r1 * r1 * r1)) : Math.Sqrt(mu / Math.Pow(r1, 3));
                double w2 = Math.Sqrt(mu / (r2 * r2 * r2));
                double rel = (w2 - w1) * 180 / Math.PI;   // deg/s, negative: target falls behind
                double wait = rel < 0 ? ((diff + 360) % 360) / -rel : ((360 - diff) % 360) / rel;
                when = "in " + FlightHUD.FormatTime(wait);
            }
            return string.Format("{0} transfer: burn prograde {1:N0} m/s at phase {2:0}° (now {3:0}°) — {4}\nCoast ≈ {5}",
                target.name, dv, need, phaseNow, when, FlightHUD.FormatTime(tof));
        }

        static double Wrap(double deg) { deg %= 360; if (deg > 180) deg -= 360; if (deg < -180) deg += 360; return deg; }

        static double SignedAngle(Vector3d from, Vector3d to, Vector3d normal)
        {
            var f = from.normalized;
            var tt = to - normal.normalized * Vector3d.Dot(to, normal.normalized);
            double ang = Vector3d.Angle(f, tt) * 180 / Math.PI;
            return Vector3d.Dot(Vector3d.Cross(f, tt), normal) < 0 ? -ang : ang;
        }
    }

    /// <summary>
    /// 3D map of the Solar System: drag to rotate, pinch to zoom, tap a planet or moon's name to focus it,
    /// "Target" to navigate to it. Shows the predicted trajectory with encounters, apoapsis/periapsis.
    /// </summary>
    public class MapView
    {
        public bool Active;
        public CelestialBody focus;
        public float yaw = 30f, pitch = 50f;
        public double viewDistance;
        readonly SpaceView space;
        readonly FlightVessel vessel;
        readonly FlightHUD hud;
        readonly Dictionary<CelestialBody, Text> labels = new Dictionary<CelestialBody, Text>();
        Text shipLabel, apLabel, peLabel;
        readonly Dictionary<LandingSite, Text> siteLabels = new Dictionary<LandingSite, Text>();
        readonly List<Text> encounterLabels = new List<Text>();
        public List<Patch> prediction = new List<Patch>();
        float predictTimer;

        public MapView(SpaceView space, FlightVessel vessel, FlightHUD hud)
        {
            this.space = space;
            this.vessel = vessel;
            this.hud = hud;
            foreach (var b in SolarSystem.Bodies)
            {
                var t = hud.MakeMapLabel(b.name, b.star ? new Color(1f, 0.9f, 0.5f) : Color.white);
                var btn = t.gameObject.AddComponent<Button>();
                var body = b;
                btn.onClick.AddListener(() => Focus(body));
                labels[b] = t;
            }
            foreach (var site in LandingSite.All)
                siteLabels[site] = hud.MakeMapLabel("x " + site.name, new Color(1f, 0.85f, 0.35f));
            shipLabel = hud.MakeMapLabel("▲ You", new Color(0.4f, 1f, 0.6f));
            apLabel = hud.MakeMapLabel("Ap", new Color(0.45f, 0.8f, 1f));
            peLabel = hud.MakeMapLabel("Pe", new Color(0.45f, 0.8f, 1f));
            for (int i = 0; i < 2; i++) encounterLabels.Add(hud.MakeMapLabel("", new Color(1f, 0.75f, 0.3f)));
            hud.mapLabels.gameObject.SetActive(false);
        }

        public void Open()
        {
            Active = true;
            Focus(vessel.body);
            hud.mapLabels.gameObject.SetActive(true);
            predictTimer = 0f;
        }

        public void Close()
        {
            Active = false;
            hud.mapLabels.gameObject.SetActive(false);
            space.HideAllLines();
        }

        public void Focus(CelestialBody b)
        {
            focus = b;
            viewDistance = b.star ? 6 * SolarSystem.AU : b == vessel.body ? Math.Max(b.radius * 4, vessel.pos.magnitude * 3) : b.radius * 6;
        }

        public void Rotate(Vector2 delta) { yaw += delta.x * 0.25f; pitch = Mathf.Clamp(pitch - delta.y * 0.25f, -89f, 89f); }
        public void Zoom(float factor) { viewDistance = Math.Max(focus.radius * 1.3, Math.Min(80 * SolarSystem.AU, viewDistance * factor)); }

        public void Update(float dt)
        {
            double t = vessel.time;
            predictTimer -= dt;
            if (predictTimer <= 0f)
            {
                predictTimer = 0.5f;
                prediction = vessel.status == FlightStatus.Flying ? NavPlanner.Predict(vessel.body, vessel.pos, vessel.vel, t) : new List<Patch>();
                // orbit lines
                for (int i = 0; i < 3; i++)
                {
                    if (i < prediction.Count)
                        space.SetLine("patch" + i, prediction[i].body, prediction[i].points, i == 0 ? new Color(0.35f, 0.85f, 1f, 0.95f) : new Color(1f, 0.7f, 0.25f, 0.95f));
                    else space.HideLine("patch" + i);
                }
                // orbits of the bodies around the focus and its parent
                foreach (var b in SolarSystem.Bodies)
                {
                    if (b.parent == null) continue;
                    bool show = b.parent == focus || b.parent == focus.parent || (focus.star && b.parent.star);
                    if (!show) { space.HideLine("orbit" + b.name); continue; }
                    var pts = new List<Vector3d>();
                    double period = b.orbit.Period;
                    for (int i = 0; i <= 128; i++) pts.Add(b.orbit.PositionAt(t + period * i / 128.0));
                    space.SetLine("orbit" + b.name, b.parent, pts, new Color(1f, 1f, 1f, 0.22f));
                }
            }

            var focusAbs = focus.AbsolutePosition(t);
            var q = Quaternion.Euler(pitch, yaw, 0f);
            double metersPerUnit = viewDistance / 100.0;
            var camAbs = focusAbs + Vector3d.Rotate(q, new Vector3d(0, 0, -viewDistance));
            space.SetSky(Vector3.up, Vector3.up, Color.black, Color.black, Color.black, Color.black, 0f);
            space.Render(camAbs, q, 50f, t, metersPerUnit, 1f, Color.black, 0.35f);

            // labels
            var cam = space.cam;
            foreach (var kv in labels)
            {
                var b = kv.Key;
                bool relevant = b == focus || b.parent == focus || b == focus.parent || (b.parent != null && b.parent == focus.parent) || (focus.star && b.parent == focus);
                Place(kv.Value, b.AbsolutePosition(t), relevant);
            }
            foreach (var kv in siteLabels)
            {
                var b = SolarSystem.Get(kv.Key.body);
                bool show = b != null && b == focus;
                if (!show) { if (kv.Value.gameObject.activeSelf) kv.Value.gameObject.SetActive(false); continue; }
                var dirFixed = b.SurfaceDirection(kv.Key.lat, kv.Key.lon, t);
                // only label sites on the side facing the camera
                var abs = b.AbsolutePosition(t) + dirFixed * b.radius;
                var toCam = (camAbs - abs).normalized;
                Place(kv.Value, abs, Vector3d.Dot(dirFixed, toCam) > 0.05);
            }
            var vesselAbs = vessel.body.AbsolutePosition(t) + vessel.pos;
            Place(shipLabel, vesselAbs, true);
            if (prediction.Count > 0 && prediction[0].orbit.e < 1 && prediction[0].endReason == null)
            {
                var o = prediction[0].orbit;
                var b = prediction[0].body.AbsolutePosition(t);
                apLabel.text = "Ap " + FlightHUD.Dist(o.Apoapsis - prediction[0].body.radius);
                peLabel.text = "Pe " + FlightHUD.Dist(o.Periapsis - prediction[0].body.radius);
                Place(apLabel, b + o.PositionAtTrue(Math.PI), true);
                Place(peLabel, b + o.PositionAtTrue(0), true);
            }
            else { apLabel.gameObject.SetActive(false); peLabel.gameObject.SetActive(false); }
            for (int i = 0; i < encounterLabels.Count; i++)
            {
                if (i + 1 < prediction.Count)
                {
                    var p = prediction[i + 1];
                    var o = p.orbit;
                    encounterLabels[i].text = (prediction[i].endReason == "escape" ? "Escape → " : "Encounter ") + p.body.name + "\nPe " + FlightHUD.Dist(o.Periapsis - p.body.radius);
                    Place(encounterLabels[i], p.body.AbsolutePosition(t) + p.points[0], true);
                }
                else encounterLabels[i].gameObject.SetActive(false);
            }
        }

        void Place(Text label, Vector3d abs, bool show)
        {
            var sp = space.cam.WorldToScreenPoint(space.ToScaled(abs));
            bool vis = show && sp.z > 0;
            if (label.gameObject.activeSelf != vis) label.gameObject.SetActive(vis);
            if (!vis) return;
            var canvasRt = (RectTransform)hud.mapLabels;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out local);
            label.rectTransform.anchoredPosition = local + canvasRt.rect.size * 0.5f + new Vector2(0, 10);
        }

        /// <summary>Text summary of what's ahead, for the HUD.</summary>
        public static string Summary(FlightVessel v, List<Patch> pred)
        {
            if (pred == null || pred.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pred.Count - 1 && i < 2; i++)
            {
                var next = pred[i + 1];
                sb.Append(pred[i].endReason == "escape" ? "Escape " + pred[i].body.name : next.body.name + " encounter");
                sb.Append(" in ").Append(FlightHUD.FormatTime(pred[i].end - v.time));
                sb.Append(" · Pe ").Append(FlightHUD.Dist(next.orbit.Periapsis - next.body.radius)).Append('\n');
            }
            if (pred[pred.Count - 1].endReason == "impact" && v.status == FlightStatus.Flying)
                sb.Append("<color=#ff8866>Impact course: ").Append(pred[pred.Count - 1].body.name).Append("</color>\n");
            return sb.ToString();
        }
    }
}
