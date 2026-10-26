using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>Opt-in scripted pilot: real movement, weapons, ammo, damage, rescues and servicing.
    /// Gives perfect map knowledge; uses actual bounded thrust/yaw inputs, never injected velocity.</summary>
    public sealed class MissionValidation : MonoBehaviour
    {
        [Serializable] sealed class MissionReport
        {
            public bool success, won;
            public string scope = "Informed route pilot using actual bounded thrust, yaw and altitude commands. Real ammo, damage, rescue, hook, fuel and purchases; no teleport, velocity injection, resource grants or direct objective damage. Perfect target knowledge is retained. This tests interactions and traversal, not human enjoyment.";
            public string failure, grade;
            public float seconds, minHull = 100, minFuel = 100, rocketUnlockedAt = -1, distanceFlown;
            public int peakEnemies, kills, rescued, lost, supplies, sorties, salvage, seals;
            public List<string> events = new List<string>();
            public List<string> errors = new List<string>();
        }
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly MethodInfo Fire = typeof(Arsenal).GetMethod("TryFire", Flags);
        Game game;
        MissionReport report = new MissionReport();
        readonly Vector3[] radar = new Vector3[1024];
        readonly Horde.ThreatRadarPoint[] threats = new Horde.ThreatRadarPoint[64];
        int airTarget = -1;
        Vector3 lastAirPosition;
        float dodgeUntil, nextDodge, dodgeClimb;
        int previousDives, previousHexes;
        int steps;
        float wallDeadline;
        const float Dt = 1f / 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-holy-mission-test") < 0) return;
            new GameObject("Opt-in simulated mission pilot").AddComponent<MissionValidation>();
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += CaptureError;
            while (Game.I == null) yield return null;
            game = Game.I; game.enabled = false; game.Begin(); game.SetCursor(true); game.Audio.Muted = true;
            // This pilot supplies desired angular displacement, rather than persistent reticle position.
            game.Flight.SetMode(FlightControlMode.Direct);
            wallDeadline = Time.realtimeSinceStartup + 300;
            // Evacuate the nearer settlement before the invasion reaches full strength.
            for (int i = 0; i < 8 && !game.GameOver; i++)
            {
                Survivor soul = game.Survivors[i];
                float deadline = game.Elapsed + 110;
                while (soul.Alive && game.Elapsed < deadline && !game.GameOver)
                {
                    Vector3 target = soul.Position + Vector3.up * 10;
                    Drive(target, SafeGroundTarget(soul.Position), 1, -1);
                    if (++steps % 80 == 0) yield return null;
                }
                Event("Rescue " + (i + 1) + ": aboard=" + soul.Rescued + ", health=" + soul.Health.ToString("F0"));
                if (game.Passengers >= 4) yield return ReturnHome();
            }
            yield return ReturnHome();
            for (int encounter = 0; encounter < 4; encounter++)
            {
                if (game.GameOver) break;
                Crypt crypt = null;
                float nearestCrypt = float.PositiveInfinity;
                foreach(var candidate in game.Crypts)
                {
                    float distance = Game.HorizontalDistance(candidate.Position,game.Flight.transform.position);
                    if(candidate.Active && distance < nearestCrypt) { crypt=candidate; nearestCrypt=distance; }
                }
                if (crypt == null) break;
                Vector3 approach = World.Ground(crypt.Position.x, crypt.Position.z - 85) + Vector3.up * 30;
                yield return Travel(approach);
                float deadline = game.Elapsed + 85;
                while (crypt.Active && game.Elapsed < deadline && !game.GameOver)
                {
                    Vector3 orbit = approach + Vector3.right * Mathf.Sin(game.Elapsed * .35f) * 32;
                    int weapon = game.Arsenal.Weapons[2].Unlocked && game.Arsenal.Weapons[2].Ammo >= 1 ? 2 : 0;
                    Drive(orbit, crypt.Position + Vector3.up * 3, weapon, weapon == 0 ? 1 : -1);
                    if (++steps % 80 == 0) yield return null;
                }
                Event(crypt.Name + " sealed=" + !crypt.Active + ", hull=" + game.Hull.ToString("F0"));
                if (crypt.Active) { report.failure = "Pilot could not suppress " + crypt.Name + " before its encounter deadline."; break; }
                yield return Collect(SupplyKind.Relic, 120);
                // Collect actual ordnance/ammo after a battle, with a moving hook pass.
                Supply cargo = NearestSupply(SupplyKind.Ammo);
                if (cargo != null && !game.GameOver)
                {
                    yield return Travel(cargo.Position + Vector3.up * 10);
                    for (int i = 0; i < 14 && !game.GameOver; i++) Drive(cargo.Position + Vector3.up * 10, cargo.Position, -1, -1);
                    Event("Ammo hook: crates=" + game.SuppliesCollected);
                }
                if (game.Hull < 85) yield return Collect(SupplyKind.Repair, 160);
                if (game.Fuel < 65) yield return Collect(SupplyKind.Fuel, 650);
                if (!game.Arsenal.Weapons[2].Unlocked || game.Fuel < 28 || game.Hull < 60)
                    yield return ReturnHome();
            }
            if (!game.GameOver) yield return ReturnHome();
            report.won = game.Won;
            report.seconds = game.Elapsed; report.kills = game.Kills; report.rescued = game.Rescued;
            report.lost = game.CivilianLosses; report.supplies = game.SuppliesCollected;
            report.sorties = game.Sortie; report.salvage = game.Salvage; report.grade = game.MissionGrade;
            report.seals = game.RelicsDelivered;
            if (game.GameOver && !game.Won) report.failure = game.DeathReason;
            report.success = game.Won && game.Rescued >= game.RequiredRescues && game.SuppliesCollected > 0 && report.rocketUnlockedAt > 0 && report.errors.Count == 0;
            if (!report.success && string.IsNullOrEmpty(report.failure)) report.failure = game.DeathReason + " Mission prerequisites or resource interactions incomplete.";
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-holy-report");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "HolyHelicopter-mission.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("HOLY MISSION " + (report.success ? "PASS" : "FAIL") + ": " + path);
            Application.logMessageReceived -= CaptureError;
            if (!Application.isEditor) Application.Quit(report.success ? 0 : 1);
        }

        IEnumerator Travel(Vector3 target)
        {
            float deadline = game.Elapsed + 150;
            while (Vector3.Distance(game.Flight.transform.position, target) > 3 && game.Elapsed < deadline && !game.GameOver)
            {
                Drive(target, null, -1, -1);
                if (++steps % 80 == 0) yield return null;
            }
        }

        IEnumerator ReturnHome()
        {
            if (game.GameOver) yield break;
            yield return Travel(World.BasePosition + Vector3.up * 7);
            for (int i = 0; i < 240 && !game.GameOver; i++) Drive(World.BasePosition + Vector3.up * 7, null, -1, -1);
            if (!game.GameOver && game.Arsenal.Unlock(2)) { report.rocketUnlockedAt = game.Elapsed; Event("Rockets purchased with earned salvage."); }
            Event("Home: delivered=" + game.Rescued + ", seals=" + game.RelicsDelivered + ", salvage=" + game.Salvage);
        }

        IEnumerator Collect(SupplyKind kind, float radius)
        {
            Supply cargo = NearestSupply(kind, radius);
            if (cargo == null || game.GameOver) yield break;
            yield return Travel(cargo.Position + Vector3.up * 9);
            for (int i=0;i<45 && !game.GameOver && !cargo.Collected;i++) Drive(cargo.Position+Vector3.up*9,null,-1,-1);
            Event(kind + " hook: " + cargo.Collected);
        }

        void Drive(Vector3 target, Vector3? aim, int primary, int secondary)
        {
            if (game.GameOver) return;
            game.Paused = false;
            if (game.Elapsed > 1200 || Time.realtimeSinceStartup > wallDeadline) { game.DamagePlayer(1000, "Scripted mission exceeded its time budget."); return; }
            var flight = game.Flight;
            Vector3 before = flight.transform.position;
            Vector3 delta = target - before; delta.y = 0;
            Vector3 desired = Vector3.ClampMagnitude(delta * .8f, flight.ForwardCruise);
            int count = game.Horde.GetThreatRadarPoints(before, 180, threats);
            float best = 130 * 130;
            int chosen = -1;
            for(int i=0;i<count;i++)
            {
                if (threats[i].Kind != EnemyKind.Gargoyle && threats[i].Kind != EnemyKind.Bat && threats[i].Kind != EnemyKind.Necromancer) continue;
                if (threats[i].Id == airTarget) { chosen=i; break; }
                float d = (threats[i].Position - before).sqrMagnitude;
                if (d < best) { best=d; chosen=i; }
            }
            if (chosen >= 0)
            {
                Vector3 lead = threats[chosen].Id == airTarget ? (threats[chosen].Position-lastAirPosition)/Dt*.18f : Vector3.zero;
                airTarget=threats[chosen].Id;lastAirPosition=threats[chosen].Position;
                aim=lastAirPosition+lead+Vector3.up*(threats[chosen].Heavy?0:1);
                primary=0;secondary=-1;
            }
            else airTarget=-1;
            Vector3 direction = aim.HasValue ? aim.Value - flight.AimOrigin : delta;
            float heading = direction.sqrMagnitude > .01f ? Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg : flight.Heading;
            float look = aim.HasValue ? Mathf.Clamp(-Mathf.Asin(direction.normalized.y)*Mathf.Rad2Deg,-28,78) : 15;
            float yaw = Mathf.Clamp(Mathf.DeltaAngle(flight.TargetHeading,heading),-100*Dt,100*Dt);
            Vector3 local = Quaternion.Euler(0,-flight.Heading,0)*desired;
            Vector2 command = new Vector2(local.x/flight.StrafeCruise,local.z/(local.z>=0?flight.ForwardCruise:flight.ReverseCruise));
            float climb = Mathf.Clamp(((target.y-before.y)*1.6f-flight.Velocity.y)/8,-1,1);
            if (aim.HasValue && direction.magnitude > 35 && flight.Altitude < 55)
            {
                Vector3 terrainHit=game.Arsenal.TraceAim(flight.AimOrigin,direction.normalized,direction.magnitude,false);
                if(Vector3.Distance(terrainHit,flight.AimOrigin)+6<direction.magnitude)climb=1;
            }
            if (game.Elapsed > nextDodge && (game.Horde.DivingGargoyles > previousDives || game.Horde.ActiveHexes > previousHexes))
            {
                dodgeUntil=game.Elapsed+1.25f; nextDodge=game.Elapsed+1.8f;
                dodgeClimb=flight.Altitude<36?1:-1;
            }
            previousDives=game.Horde.DivingGargoyles;previousHexes=game.Horde.ActiveHexes;
            if (game.Elapsed<dodgeUntil && !game.AtBase)
            {
                climb=dodgeClimb;
                command.x=Mathf.Clamp(command.x+(Mathf.Sin(game.Elapsed*.7f)>0?1:-1)*.7f,-1,1);
            }
            game.StepCommand(Dt,Vector2.ClampMagnitude(command,1),climb,yaw,Mathf.Clamp(look-flight.LookPitch,-90*Dt,90*Dt));
            if (!game.GameOver && aim.HasValue && Vector3.Angle(flight.AimForward,aim.Value-flight.AimOrigin)<(primary==0?13:3.5f))
            {
                if (primary >= 0) Fire.Invoke(game.Arsenal, new object[] { primary });
                if (secondary >= 0) Fire.Invoke(game.Arsenal, new object[] { secondary });
            }
            report.minHull = Mathf.Min(report.minHull, game.Hull);
            report.minFuel = Mathf.Min(report.minFuel, game.Fuel);
            report.peakEnemies = Mathf.Max(report.peakEnemies, game.Horde.Count);
            report.distanceFlown += Vector3.Distance(before, flight.transform.position);
        }

        Vector3? SafeGroundTarget(Vector3 origin)
        {
            int count = game.Horde.GetRadarPoints(origin, 85, radar);
            float best = float.PositiveInfinity; Vector3? result = null;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = radar[i];
                if (p.y > World.Height(p.x, p.z) + 4) continue;
                bool clear = true;
                foreach (var s in game.Survivors) if (s.Alive && Game.HorizontalDistance(p, s.Position) < 11) { clear = false; break; }
                float distance = (origin - p).sqrMagnitude;
                if (clear && distance < best) { best = distance; result = p + Vector3.up; }
            }
            return result;
        }

        Supply NearestSupply(SupplyKind kind, float radius = 150)
        {
            Supply nearest = null; float best = radius * radius;
            foreach (var s in game.Supplies)
            {
                float distance = (s.Position - game.Flight.transform.position).sqrMagnitude;
                if (!s.Collected && s.Kind == kind && distance < best) { nearest = s; best = distance; }
            }
            return nearest;
        }
        void Event(string value)
        {
            string line=game.Elapsed.ToString("F1")+"s: "+value;
            report.events.Add(line); Debug.Log("MISSION PILOT "+line);
        }
        void CaptureError(string value, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(value + "\n" + stack); }
    }
}
