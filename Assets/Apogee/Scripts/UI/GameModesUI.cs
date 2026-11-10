using System;
using System.Collections.Generic;
using Apogee.Astro;
using Apogee.Core;
using Apogee.Flight;
using Apogee.Game;
using Apogee.Rocket;
using UnityEngine;
using UnityEngine.UI;

namespace Apogee.UI
{
    /// <summary>Menus for Career, Missions, Space Station, Planet Explorer and the Cheats panel.</summary>
    public static class GameModesUI
    {
        static Dialogs D { get { return AppRoot.Instance.Dialogs; } }
        static float W { get { return UIFactory.Portrait ? 1000 : 1400; } }
        static float H { get { return UIFactory.Portrait ? 1500 : 940; } }

        // ------------------------------------------------------------------ launching

        public static void Fly(RocketBlueprint bp)
        {
            if (Play.Career && !ChargeLaunch(bp)) return;
            StagingSystem.Restage(bp);
            Session.flightBlueprint = bp;
            AppRoot.Instance.LoadScene(Scenes.Flight);
        }

        public static void Build(RocketBlueprint bp)
        {
            if (bp != null)
            {
                Session.builderEntry = BuilderEntry.OpenBlueprint;
                Session.blueprintToOpen = bp;
                Session.blueprintPath = null;
            }
            else Session.builderEntry = BuilderEntry.Continue;
            AppRoot.Instance.LoadScene(Scenes.Builder);
        }

        /// <summary>Career: checks the design is researched and affordable, then pays for it. False = blocked.</summary>
        public static bool ChargeLaunch(RocketBlueprint bp)
        {
            var pr = Progress.Current;
            var s = AppRoot.Instance.Settings;
            if (!s.cheatUnlockAll)
            {
                var locked = pr.LockedParts(bp);
                if (locked.Count > 0)
                {
                    D.Toast("Not researched yet: " + string.Join(", ", locked.ToArray()), Theme.Warn, 4f);
                    return false;
                }
            }
            float cost = Costs.Rocket(bp);
            if (!s.cheatFreeLaunch)
            {
                if (cost > pr.funds) { D.Toast("Launch costs " + Costs.Money(cost) + " — you have " + Costs.Money(pr.funds), Theme.Bad, 4f); return false; }
                pr.funds -= cost;
            }
            pr.flights++;
            pr.Save();
            Play.launchCost = cost;
            return true;
        }

        static RectTransform Card(RectTransform parent, float h)
        {
            var c = UIFactory.Panel(parent, Theme.PanelLight, true, "Card");
            c.sprite = UIFactory.RoundedSmall;
            UIFactory.Size(c, -1, h);
            var rt = c.rectTransform;
            UIFactory.Vertical(rt, 6, 16, true);
            return rt;
        }

        static RectTransform Row(RectTransform parent, float h)
        {
            var r = UIFactory.Rect("Row", parent);
            UIFactory.Size(r, -1, h);
            var hl = UIFactory.Horizontal(r, 10, 0, true);
            hl.childForceExpandWidth = true;
            return r;
        }

        static Text Line(RectTransform parent, string text, int size, Color col, float h, FontStyle st = FontStyle.Normal)
        {
            var t = UIFactory.Label(parent, text, size, col, TextAnchor.MiddleLeft, st);
            t.supportRichText = true;
            UIFactory.Size(t, -1, h);
            return t;
        }

        static RectTransform Scroll(RectTransform body)
        {
            RectTransform c;
            var sc = UIFactory.ScrollList(body, out c, 10, 6);
            UIFactory.Size(sc, -1, -1, -1, 1);
            return c;
        }

        static void Footer(RectTransform body, Action close, string extraLabel = null, Action extra = null)
        {
            var row = UIFactory.Rect("Footer", body);
            UIFactory.Size(row, -1, 80);
            var h = UIFactory.Horizontal(row, 12, 0, true);
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleRight;
            var spacer = UIFactory.Rect("Spacer", row);
            UIFactory.Size(spacer, -1, -1, 1);
            if (extra != null) UIFactory.Size(UIFactory.Button(row, extraLabel, extra, ButtonStyle.Danger, 22), 280, 72);
            UIFactory.Size(UIFactory.Button(row, "Close", close, ButtonStyle.Primary), 200, 72);
        }

        static string Steps(Mission m)
        {
            var parts = new List<string>();
            foreach (var o in m.steps) parts.Add(o.text);
            return string.Join("  →  ", parts.ToArray());
        }

        // ------------------------------------------------------------------ Missions

        public static void OpenMissions()
        {
            D.Custom("Missions", W, H, (body, close) =>
            {
                var c = Scroll(body);
                var pr = Progress.Current;
                Line(c, "Real-scale objectives from first hop to Titan. Fly the suggested rocket or build your own. All parts are available. Done: <b>" + pr.missionsDone.Count + " / " + Mission.All.Count + "</b>", 21, Theme.Muted, 64);
                int tier = 0;
                foreach (var m in Mission.All)
                {
                    if (m.tier != tier) { tier = m.tier; UIFactory.Header(c, TierName(tier)); }
                    var mm = m;
                    bool done = pr.missionsDone.Contains(m.id);
                    var card = Card(c, 220);
                    Line(card, (done ? "<color=#7be08a>✓</color>  " : "") + "<b>" + m.title + "</b>", 28, Theme.Text, 38);
                    Line(card, m.brief, 20, Theme.Muted, 30);
                    Line(card, "<color=#ffd36b>" + Steps(m) + "</color>", 19, Theme.Muted, 30);
                    var r = Row(card, 66);
                    UIFactory.Button(r, "Fly " + Mission.ExampleByName(m.rocket).name.Replace(" (example)", ""), () =>
                    {
                        close();
                        Play.Reset(); Play.mode = GameMode.Mission; Play.missionId = mm.id;
                        Fly(mm.SuggestedRocket());
                    }, ButtonStyle.Primary, 21);
                    UIFactory.Button(r, "Edit it first", () =>
                    {
                        close();
                        Play.Reset(); Play.mode = GameMode.Mission; Play.missionId = mm.id;
                        Build(mm.SuggestedRocket());
                    }, ButtonStyle.Normal, 21);
                    UIFactory.Button(r, "My design", () =>
                    {
                        close();
                        Play.Reset(); Play.mode = GameMode.Mission; Play.missionId = mm.id;
                        Build(null);
                    }, ButtonStyle.Normal, 21);
                }
                Footer(body, close);
            });
        }

        static string TierName(int t)
        {
            switch (t)
            {
                case 1: return "Tier 1 · First steps";
                case 2: return "Tier 2 · Earth orbit";
                case 3: return "Tier 3 · High orbits and the Moon";
                case 4: return "Tier 4 · Landing on other worlds";
                default: return "Tier 5 · Interplanetary";
            }
        }

        // ------------------------------------------------------------------ Career

        public static void OpenCareer()
        {
            var pr = Progress.Current;
            if (!pr.careerStarted)
            {
                D.Confirm("New career", "Run a space agency: start with " + Costs.Money(600) + ", basic parts and no science. Accept contracts to earn money and science, research new technology, and recover capsules to get money back.", "Start career", () =>
                {
                    pr.ResetCareer();
                    OpenCareer();
                });
                return;
            }
            D.Custom("Career", W, H, (body, close) =>
            {
                var c = Scroll(body);
                Action refresh = null;
                refresh = () =>
                {
                    for (int i = c.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(c.GetChild(i).gameObject);
                    var head = Card(c, 110);
                    Line(head, "<b>Funds " + Costs.Money(pr.funds) + "</b>    Science <b>" + pr.science + "</b>    Reputation <b>" + pr.reputation + "</b>", 28, Theme.Text, 40);
                    Line(head, pr.flights + " launches · " + pr.careerDone.Count + " contracts complete · tech " + pr.unlocked.Count + " / " + TechTree.Nodes.Length, 20, Theme.Muted, 30);

                    UIFactory.Header(c, "Contracts");
                    var offers = pr.CareerOffers();
                    if (offers.Count == 0) Line(c, "Every contract is complete — the Solar System is yours.", 22, Theme.Good, 40);
                    foreach (var m in offers)
                    {
                        var mm = m;
                        var card = Card(c, 190);
                        Line(card, "<b>" + m.title + "</b>   <color=#7be08a>+" + Costs.Money(m.reward) + "</color>  <color=#7fd0ff>+" + m.science + " science</color>", 26, Theme.Text, 38);
                        Line(card, m.brief, 20, Theme.Muted, 30);
                        var r = Row(card, 66);
                        UIFactory.Button(r, "Accept & build", () =>
                        {
                            close();
                            Play.Reset(); Play.mode = GameMode.Career; Play.missionId = mm.id;
                            Build(null);
                        }, ButtonStyle.Primary, 21);
                        var ex = mm.SuggestedRocket();
                        bool can = pr.LockedParts(ex).Count == 0 || AppRoot.Instance.Settings.cheatUnlockAll;
                        var fb = UIFactory.Button(r, can ? "Fly " + ex.name.Replace(" (example)", "") + " (" + Costs.Money(Costs.Rocket(ex)) + ")" : "Suggested rocket needs research", () =>
                        {
                            Play.Reset(); Play.mode = GameMode.Career; Play.missionId = mm.id;
                            if (ChargeLaunchPreview(ex)) { close(); Fly(ex); }
                        }, ButtonStyle.Normal, 19);
                        fb.interactable = can;
                    }

                    UIFactory.Header(c, "Research");
                    foreach (var n in TechTree.Nodes)
                    {
                        if (n.id == "start") continue;
                        var nn = n;
                        bool have = pr.unlocked.Contains(n.id);
                        bool reqOk = true;
                        foreach (var q in n.requires) if (!pr.unlocked.Contains(q)) reqOk = false;
                        var card = Card(c, 150);
                        Line(card, (have ? "<color=#7be08a>✓</color> " : "") + "<b>" + n.name + "</b>  <color=#7fd0ff>" + n.cost + " science</color>", 25, have ? Theme.Good : Theme.Text, 36);
                        Line(card, n.desc + (reqOk ? "" : "   <color=#ff8a7a>(needs " + string.Join(", ", Names(n.requires)) + ")</color>"), 19, Theme.Muted, 28);
                        if (!have)
                        {
                            var b = UIFactory.Button(card, pr.science >= n.cost && reqOk ? "Research" : "Locked", () =>
                            {
                                if (pr.Research(nn)) { AppRoot.Instance.Audio.Play(Sfx.Snap); D.Toast(nn.name + " researched!", Theme.Good); refresh(); }
                            }, ButtonStyle.Primary, 20);
                            UIFactory.Size(b, -1, 54);
                            b.interactable = pr.CanResearch(n);
                        }
                    }
                };
                refresh();

                var row = UIFactory.Rect("Footer", body);
                UIFactory.Size(row, -1, 80);
                var h = UIFactory.Horizontal(row, 12, 0, true);
                h.childForceExpandWidth = true;
                UIFactory.Button(row, "Free flight (builder)", () => { close(); Play.Reset(); Play.mode = GameMode.Career; Build(null); }, ButtonStyle.Normal, 21);
                UIFactory.Button(row, "Restart career", () => D.Confirm("Restart career?", "Funds, science, research and contracts start over.", "Restart", () => { pr.ResetCareer(); refresh(); }, true), ButtonStyle.Danger, 21);
                UIFactory.Button(row, "Close", close, ButtonStyle.Primary, 22);
            });
        }

        static bool ChargeLaunchPreview(RocketBlueprint bp) { return true; }

        static string[] Names(string[] ids)
        {
            var l = new List<string>();
            foreach (var i in ids) { var n = TechTree.Get(i); l.Add(n != null ? n.name : i); }
            return l.ToArray();
        }

        // ------------------------------------------------------------------ Space station

        public static void OpenStation()
        {
            D.Custom("Space Station", W, UIFactory.Portrait ? 1250 : 860, (body, close) =>
            {
                var c = Scroll(body);
                var pr = Progress.Current;
                int n = Mathf.Clamp(pr.stationModules, 0, 8);
                var head = Card(c, 170);
                Line(head, "<b>Orbital Station</b> · 400 km circular equatorial orbit · " + (n + 1) + " / 9 modules", 26, Theme.Text, 38);
                Line(head, "Rendezvous: launch due east into a lower orbit (≈250 km), wait until the station is ~20° ahead, then raise your orbit to 400 km. Near the station Pro/Retro follow your speed relative to it and Tgt points at it. Dock below 0.6 m/s.", 19, Theme.Muted, 100);
                UIFactory.Header(c, "Modules");
                string[] mods = { "Core hub with docking port, solar wings", "Crew habitat", "Science lab", "Solar truss", "Logistics module + high-gain dish", "Second habitat with floodlights", "Power truss", "Storage + relay dish", "Third habitat" };
                for (int i = 0; i < mods.Length; i++)
                    Line(c, (i <= n ? "<color=#7be08a>✓</color> " : "<color=#8a99aa>○</color> ") + mods[i], 22, i <= n ? Theme.Text : Theme.Muted, 34);
                Line(c, "Each docking delivers the next module.", 19, Theme.Muted, 34);

                UIFactory.Header(c, "Fly");
                var r1 = Row(c, 80);
                UIFactory.Button(r1, "Launch Station Shuttle", () => { close(); Play.Reset(); Play.mode = GameMode.Station; Fly(ExampleRockets.StationShuttle()); }, ButtonStyle.Primary, 22);
                UIFactory.Button(r1, "Build my own (needs a docking port)", () => { close(); Play.Reset(); Play.mode = GameMode.Station; Build(null); }, ButtonStyle.Normal, 20);
                var r2 = Row(c, 80);
                UIFactory.Button(r2, "Docking practice (start 120 m away)", () => { close(); Play.Reset(); Play.mode = GameMode.Station; Play.stationPractice = true; Fly(ExampleRockets.DockingTug()); }, ButtonStyle.Normal, 21);
                Footer(body, close, "Reset station", () => D.Confirm("Reset station?", "Removes every delivered module.", "Reset", () => { pr.stationModules = 0; pr.Save(); close(); OpenStation(); }, true));
            });
        }

        // ------------------------------------------------------------------ Planet explorer

        public static void OpenExplorer()
        {
            D.Custom("Planet Explorer", W, H, (body, close) =>
            {
                var c = Scroll(body);
                var pr = Progress.Current;
                SolarSystem.Build();
                Line(c, "Start on the surface of another world, or in low orbit around it. Landing within 25 km of a marked site logs it. Sites explored: <b>" + pr.sitesVisited.Count + " / " + (LandingSite.All.Count - 1) + "</b>", 21, Theme.Muted, 64);
                string[] order = { "Moon", "Mars", "Titan", "Mercury", "Venus", "Phobos", "Deimos", "Io", "Europa", "Ganymede", "Callisto", "Jupiter", "Saturn", "Uranus", "Neptune" };
                foreach (var name in order)
                {
                    var b = SolarSystem.Get(name);
                    if (b == null) continue;
                    var bb = b;
                    var sites = new List<int>();
                    for (int i = 0; i < LandingSite.All.Count; i++) if (LandingSite.All[i].body == name) sites.Add(i);
                    bool gas = name == "Jupiter" || name == "Saturn" || name == "Uranus" || name == "Neptune";
                    var card = Card(c, 170 + sites.Count * 74);
                    bool landed = pr.bodiesLanded.Contains(name);
                    Line(card, (landed ? "<color=#7be08a>✓</color> " : "") + "<b>" + name + "</b>", 28, Theme.Text, 36);
                    Line(card, Facts(b), 19, Theme.Muted, 52);
                    var r = Row(card, 62);
                    UIFactory.Button(r, "Start in low orbit", () => { close(); StartExplorer(bb, -1, gas ? ExampleRockets.SeleneLander() : Lander(bb)); }, ButtonStyle.Normal, 20);
                    if (!gas)
                        UIFactory.Button(r, "Land: my design", () => { close(); Play.Reset(); Play.mode = GameMode.Explorer; Play.explorerBody = bb.name; Play.explorerSite = sites.Count > 0 ? sites[0] : -1; Build(null); }, ButtonStyle.Normal, 20);
                    foreach (int si in sites)
                    {
                        int s2 = si;
                        var sr = Row(card, 62);
                        var site = LandingSite.All[si];
                        bool v = pr.sitesVisited.Contains(site.name);
                        UIFactory.Button(sr, (v ? "✓ " : "") + "Start at " + site.name, () => { close(); StartExplorer(bb, s2, Lander(bb)); }, ButtonStyle.Primary, 20);
                    }
                }
                Footer(body, close);
            });
        }

        static RocketBlueprint Lander(CelestialBody b)
        {
            return b.HasAtmosphere || b.radius > 2500000 ? ExampleRockets.AresLander() : ExampleRockets.SeleneLander();
        }

        static void StartExplorer(CelestialBody b, int site, RocketBlueprint bp)
        {
            Play.Reset();
            Play.mode = GameMode.Explorer;
            Play.explorerBody = b.name;
            Play.explorerSite = site;
            Play.explorerOrbit = site < 0;
            Fly(bp);
        }

        public static string Facts(CelestialBody b)
        {
            double g = b.mu / (b.radius * b.radius);
            double vOrb = Math.Sqrt(b.mu / b.radius), vEsc = Math.Sqrt(2 * b.mu / b.radius);
            string atm = b.HasAtmosphere ? "atmosphere to " + (b.atmosphere.height / 1000).ToString("0") + " km" : "no air";
            return "Radius " + (b.radius / 1000).ToString("N0") + " km · gravity " + g.ToString("0.00") + " m/s² (" + (g / 9.80665).ToString("0.00") + " g) · " + atm +
                   "\nLow orbit " + (vOrb / 1000).ToString("0.00") + " km/s · escape " + (vEsc / 1000).ToString("0.00") + " km/s · day " + Day(b.rotationPeriod);
        }

        static string Day(double s)
        {
            s = Math.Abs(s);
            if (s <= 0) return "—";
            if (s < 86400 * 2) return (s / 3600).ToString("0.0") + " h";
            return (s / 86400).ToString("0.0") + " d";
        }

        // ------------------------------------------------------------------ Cheats

        public static void OpenCheats(FlightCheats live = null, Action changed = null)
        {
            D.Custom("Cheats", UIFactory.Portrait ? 1000 : 1100, UIFactory.Portrait ? 1400 : 900, (body, close) =>
            {
                var c = Scroll(body);
                BuildCheats(c, live, changed);
                Footer(body, close, "Turn all off", () =>
                {
                    var s = AppRoot.Instance.Settings;
                    s.ClearCheats();
                    s.Save();
                    if (live != null) s.ApplyCheats(live);
                    if (changed != null) changed();
                    close();
                    OpenCheats(live, changed);
                });
            });
        }

        /// <summary>One switch per cheat. Changes save immediately and, in flight, apply at once.</summary>
        public static void BuildCheats(RectTransform c, FlightCheats live, Action changed)
        {
            var s = AppRoot.Instance.Settings;
            Action save = () => { s.Save(); if (live != null) s.ApplyCheats(live); if (changed != null) changed(); };
            var warn = UIFactory.Label(c, "Each switch works on its own and can be flipped any time — even mid-flight from the pause menu. All off = real physics.", 20, Theme.Muted);
            UIFactory.Size(warn, -1, 56);
            UIFactory.Header(c, "Physics");
            UIFactory.ToggleRow(c, "Unlimited fuel", s.cheatInfiniteFuel, on => { s.cheatInfiniteFuel = on; save(); });
            UIFactory.ToggleRow(c, "Zero gravity", s.cheatNoGravity, on => { s.cheatNoGravity = on; save(); });
            UIFactory.ToggleRow(c, "No air drag", s.cheatNoDrag, on => { s.cheatNoDrag = on; save(); });
            UIFactory.ToggleRow(c, "Super thrust (5× engines)", s.cheatSuperThrust, on => { s.cheatSuperThrust = on; save(); });
            UIFactory.ToggleRow(c, "Unlimited electricity", s.cheatInfinitePower, on => { s.cheatInfinitePower = on; save(); });
            UIFactory.Header(c, "Damage");
            UIFactory.ToggleRow(c, "Zero damage (parts never break or crash)", s.cheatUnbreakable, on => { s.cheatUnbreakable = on; save(); });
            UIFactory.ToggleRow(c, "No overheating", s.cheatNoHeat, on => { s.cheatNoHeat = on; save(); });
            UIFactory.ToggleRow(c, "No g-force limit", s.cheatNoGLimit, on => { s.cheatNoGLimit = on; save(); });
            UIFactory.Header(c, "Career");
            UIFactory.ToggleRow(c, "Free launches", s.cheatFreeLaunch, on => { s.cheatFreeLaunch = on; save(); });
            UIFactory.ToggleRow(c, "All parts unlocked", s.cheatUnlockAll, on => { s.cheatUnlockAll = on; save(); });
            UIFactory.Header(c, "Start position (sandbox)");
            Button startBtn = null;
            startBtn = UIFactory.Button(c, "Start: " + GameSettings.StartNames[Mathf.Clamp(s.cheatStart, 0, GameSettings.StartNames.Length - 1)], () =>
            {
                s.cheatStart = (s.cheatStart + 1) % GameSettings.StartNames.Length;
                s.Save();
                UIFactory.SetLabel(startBtn, "Start: " + GameSettings.StartNames[s.cheatStart]);
            });
            UIFactory.Size(startBtn, -1, 70);
        }
    }
}
