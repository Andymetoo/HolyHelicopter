using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>Opt-in scripted pilot: real movement, weapons, ammo, damage, rescues and servicing.
    /// Gives perfect map knowledge and steering; measures a route, not human completion time.</summary>
    public sealed class MissionValidation : MonoBehaviour
    {
        [Serializable] sealed class MissionReport
        {
            public bool success, won;
            public string scope = "Scripted pilot with perfect target knowledge and ideal immediate steering/braking. Real Game.Step at 60 Hz, terrain integration, ammunition, weapon cooldowns, enemy attacks, civilians, cargo and base economy; no resource grants or instant objective damage. This is an optimistic route test, not a human balance benchmark.";
            public string failure, grade;
            public float seconds, minHull = 100, minFuel = 100, rocketUnlockedAt = -1;
            public int peakEnemies, kills, rescued, lost, supplies, sorties, salvage;
            public List<string> events = new List<string>();
            public List<string> errors = new List<string>();
        }
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly PropertyInfo Velocity = typeof(FlightController).GetProperty("Velocity", Flags);
        static readonly FieldInfo Height = typeof(FlightController).GetField("holdHeight", Flags);
        static readonly FieldInfo Yaw = typeof(FlightController).GetField("yaw", Flags);
        static readonly FieldInfo Pitch = typeof(FlightController).GetField("pitch", Flags);
        static readonly MethodInfo Fire = typeof(Arsenal).GetMethod("TryFire", Flags);
        Game game;
        MissionReport report = new MissionReport();
        readonly Vector3[] radar = new Vector3[1024];
        int steps;
        float wallDeadline;
        const float Dt = 1f / 60;

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
            wallDeadline = Time.realtimeSinceStartup + 180;
            // Evacuate the nearer settlement before the invasion reaches full strength.
            for (int i = 0; i < 4 && !game.GameOver; i++)
            {
                Survivor soul = game.Survivors[i];
                float deadline = game.Elapsed + 55;
                while (soul.Alive && game.Elapsed < deadline && !game.GameOver)
                {
                    Vector3 target = soul.Position + Vector3.up * 10;
                    Drive(target, SafeGroundTarget(soul.Position), 1, -1);
                    if (++steps % 80 == 0) yield return null;
                }
                Event("Rescue " + (i + 1) + ": aboard=" + soul.Rescued + ", health=" + soul.Health.ToString("F0"));
            }
            yield return ReturnHome();
            foreach (Crypt crypt in game.Crypts)
            {
                if (game.GameOver) break;
                Vector3 approach = World.Ground(crypt.Position.x, crypt.Position.z - 65) + Vector3.up * 25;
                yield return Travel(approach);
                float deadline = game.Elapsed + 70;
                while (crypt.Active && game.Elapsed < deadline && !game.GameOver)
                {
                    Vector3 orbit = approach + Vector3.right * Mathf.Sin(game.Elapsed * .7f) * 19;
                    int weapon = game.Arsenal.Weapons[2].Unlocked && game.Arsenal.Weapons[2].Ammo >= 1 ? 2 : 0;
                    Drive(orbit, crypt.Position + Vector3.up * 3, weapon, weapon == 0 ? 1 : -1);
                    if (++steps % 80 == 0) yield return null;
                }
                Event(crypt.Name + " sealed=" + !crypt.Active + ", hull=" + game.Hull.ToString("F0"));
                if (crypt.Active) { report.failure = "Pilot could not suppress " + crypt.Name + " before its encounter deadline."; break; }
                // Collect actual ordnance/ammo after a battle, with a moving hook pass.
                Supply cargo = NearestSupply(SupplyKind.Ammo);
                if (cargo != null && !game.GameOver)
                {
                    yield return Travel(cargo.Position + Vector3.up * 10);
                    for (int i = 0; i < 14 && !game.GameOver; i++) Drive(cargo.Position + Vector3.up * 10, cargo.Position, -1, -1);
                    Event("Ammo hook: crates=" + game.SuppliesCollected);
                }
                if (!game.Arsenal.Weapons[2].Unlocked || game.Fuel < 28 || game.Hull < 60)
                    yield return ReturnHome();
            }
            if (!game.GameOver) yield return ReturnHome();
            report.won = game.Won;
            report.seconds = game.Elapsed; report.kills = game.Kills; report.rescued = game.Rescued;
            report.lost = game.CivilianLosses; report.supplies = game.SuppliesCollected;
            report.sorties = game.Sortie; report.salvage = game.Salvage; report.grade = game.MissionGrade;
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
            float deadline = game.Elapsed + 45;
            while (Vector3.Distance(game.Flight.transform.position, target) > 3 && game.Elapsed < deadline && !game.GameOver)
            {
                Drive(target, SafeGroundTarget(game.Flight.transform.position), 1, -1);
                if (++steps % 80 == 0) yield return null;
            }
        }

        IEnumerator ReturnHome()
        {
            if (game.GameOver) yield break;
            yield return Travel(World.BasePosition + Vector3.up * 7);
            for (int i = 0; i < 450 && !game.GameOver; i++) Drive(World.BasePosition + Vector3.up * 7, null, -1, -1);
            if (!game.GameOver && game.Arsenal.Unlock(2)) { report.rocketUnlockedAt = game.Elapsed; Event("Rockets purchased with earned salvage."); }
            Event("Home: delivered=" + game.Rescued + ", salvage=" + game.Salvage);
        }

        void Drive(Vector3 target, Vector3? aim, int primary, int secondary)
        {
            if (game.GameOver) return;
            game.Paused = false;
            if (game.Elapsed > 800 || Time.realtimeSinceStartup > wallDeadline) { game.DamagePlayer(1000, "Scripted mission exceeded its time budget."); return; }
            Vector3 delta = target - game.Flight.transform.position; delta.y = 0;
            Vector3 speed = Vector3.ClampMagnitude(delta * 2.5f, game.Fuel > 0 ? 29 : 8.7f);
            // Feed planar velocity before the same damping/terrain integration used during play.
            Velocity.SetValue(game.Flight, speed * Mathf.Exp(Dt * 4.8f));
            Height.SetValue(game.Flight, target.y);
            if (aim.HasValue)
            {
                Vector3 direction = (aim.Value - game.Flight.transform.position).normalized;
                Yaw.SetValue(game.Flight, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                Pitch.SetValue(game.Flight, Mathf.Clamp(-Mathf.Asin(direction.y) * Mathf.Rad2Deg, -28, 78));
            }
            game.Step(Dt);
            if (!game.GameOver && aim.HasValue)
            {
                if (primary >= 0) Fire.Invoke(game.Arsenal, new object[] { primary });
                if (secondary >= 0) Fire.Invoke(game.Arsenal, new object[] { secondary });
            }
            report.minHull = Mathf.Min(report.minHull, game.Hull);
            report.minFuel = Mathf.Min(report.minFuel, game.Fuel);
            report.peakEnemies = Mathf.Max(report.peakEnemies, game.Horde.Count);
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

        Supply NearestSupply(SupplyKind kind)
        {
            Supply nearest = null; float best = 150 * 150;
            foreach (var s in game.Supplies)
            {
                float distance = (s.Position - game.Flight.transform.position).sqrMagnitude;
                if (!s.Collected && s.Kind == kind && distance < best) { nearest = s; best = distance; }
            }
            return nearest;
        }
        void Event(string value) => report.events.Add(game.Elapsed.ToString("F1") + "s: " + value);
        void CaptureError(string value, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(value + "\n" + stack); }
    }
}
