using System.Collections.Generic;
using Apogee.Astro;
using Apogee.Rocket;
using Apogee.Simulation;
using UnityEngine;

namespace Apogee.Flight
{
    /// <summary>
    /// The space station: a second spacecraft coasting on its own Kepler orbit. The player's vessel can
    /// rendezvous with it and dock (docking port to docking port, slower than 0.6 m/s, aligned within ~20°).
    /// While docked the station rides along as extra mass rigidly joined at the port.
    /// </summary>
    public class Station
    {
        public readonly RocketBlueprint bp;
        public readonly Dictionary<int, PartPose> poses;
        public CelestialBody body;
        public Orbit orbit;
        public Vector3d pos, vel;                // body-centred frame
        public Quaternion rot = Quaternion.identity;
        public float mass;
        public Vector3 com;                       // station-local centre of mass
        public Vector3 portPoint, portAxis;       // station-local docking face centre and outward axis
        public bool docked;
        public Vector3 dockOffset;                // station origin in vessel-local coordinates while docked
        public Quaternion dockRot;                // station rotation relative to the vessel while docked
        public int dockedPortUid = -1;
        public double undockedAt = -1e9;

        public Station(RocketBlueprint blueprint, CelestialBody b, double altitude, double phaseFromVesselDeg, FlightVessel near)
        {
            bp = blueprint;
            poses = RocketLayout.Compute(bp);
            body = b;
            mass = 0f; com = Vector3.zero;
            foreach (var p in bp.parts)
            {
                float m = PartMass.Dry(p) + PartMass.Fuel(p);
                mass += m;
                PartPose pose;
                if (poses.TryGetValue(p.uid, out pose)) com += pose.position * m;
                if (p.Def.isDockingPort && poses.TryGetValue(p.uid, out pose))
                {
                    var dims = PartDims.Of(p);
                    portAxis = pose.rotation * (p.mode == AttachMode.Below ? Vector3.down : Vector3.up);
                    portPoint = pose.position + portAxis * dims.height * 0.5f;
                }
            }
            com /= Mathf.Max(0.001f, mass);

            // circular, equatorial, prograde (same sense as the planet's spin, like the vessel's launch)
            double r = b.radius + altitude;
            var refPos = near != null && near.body == b ? near.pos : new Vector3d(r, 0, 0);
            var radial = new Vector3d(refPos.x, 0, refPos.z).normalized;
            float ang = (float)phaseFromVesselDeg;
            var spin = new Vector3d(0, -1, 0);
            // rotate the reference point 'ang' degrees ahead along the direction of motion
            var east0 = Vector3d.Cross(spin, radial).normalized;
            double ar = ang * System.Math.PI / 180.0;
            radial = (radial * System.Math.Cos(ar) + east0 * System.Math.Sin(ar)).normalized;
            var east = Vector3d.Cross(spin, radial).normalized;
            pos = radial * r;
            vel = east * System.Math.Sqrt(b.mu / r);
            double t = near != null ? near.time : 0;
            orbit = new Orbit(b.mu, pos, vel, t);
            t0 = t;
            Orient();
        }

        /// <summary>Port-forward attitude: the docking port faces backwards along the orbit, the long axis is horizontal.</summary>
        void Orient()
        {
            var back = (-vel).normalized.ToFloat();
            var up = pos.normalized.ToFloat();
            rot = Quaternion.LookRotation(up, back);
        }

        double t0;
        bool dirty;

        /// <summary>Coasting with the same integrator as the player's vessel, so close-range motion matches exactly.</summary>
        public void StepPhysics(double dt)
        {
            if (docked) return;
            double r = pos.magnitude;
            vel = vel + pos * (-body.mu / (r * r * r) * dt);
            pos = pos + vel * dt;
            t0 += dt;
            dirty = true;
            Orient();
        }

        /// <summary>On rails (time warp): exact Kepler propagation from the current state.</summary>
        public void Propagate(double t)
        {
            if (docked) { t0 = t; return; }
            if (dirty) { orbit = new Orbit(body.mu, pos, vel, t0); dirty = false; }
            orbit.StateAt(t, out pos, out vel);
            t0 = t;
            Orient();
        }

        public void Release(Vector3d p, Vector3d v, double t)
        {
            docked = false;
            pos = p; vel = v;
            orbit = new Orbit(body.mu, pos, vel, t);
            t0 = t;
            dirty = false;
            undockedAt = t;
        }

        public Vector3d PortWorld { get { return pos + (Vector3d)(rot * portPoint); } }
        public Vector3 PortAxisWorld { get { return rot * portAxis; } }
    }
}
