using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>Opt-in adversarial encounters. Every trajectory uses the actual flight input path.</summary>
    public static class CombatOppositionValidation
    {
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const float Step = 1f / 60f;

        public static void Run(Game game, Action<string, bool, string> result)
        {
            if (game == null || game.Horde == null || game.Flight == null)
            { result("Combat opposition fixture", false, "An initialized game is required."); return; }
            var snapshots = new List<Snapshot>
            {
                new Snapshot(game), new Snapshot(game.Horde), new Snapshot(game.Flight),
                new Snapshot(game.Arsenal), new Snapshot(game.Effects)
            };
            foreach (WeaponState weapon in game.Arsenal.Weapons) snapshots.Add(new Snapshot(weapon));
            Vector3 position = game.Flight.transform.position;
            Quaternion rotation = game.Flight.transform.rotation;
            Camera camera = game.Flight.Camera;
            Vector3 cameraPosition = camera.transform.localPosition;
            Quaternion cameraRotation = camera.transform.localRotation;
            float cameraFov = camera.fieldOfView;
            float[] cryptHealth = new float[game.Crypts.Count];
            for (int i = 0; i < cryptHealth.Length; i++) cryptHealth[i] = game.Crypts[i].Health;
            var random = UnityEngine.Random.state;
            bool muted = game.Audio.Muted;
            int supplies = game.Supplies.Count;
            var fixture = new Fixture(game);
            game.Audio.Muted = true;
            try
            {
                Check(result, "Airborne aiming query is nonmutating", () =>
                {
                    fixture.Reset();
                    Vector3 target = game.Flight.transform.position + Vector3.forward * 40f;
                    fixture.Add(EnemyKind.Gargoyle, target, 3, .3f, 10f);
                    int count = game.Horde.Count;
                    object before = fixture.Enemies.GetValue(0);
                    float health = (float)Get(before, "Health");
                    for (int i = 0; i < 40; i++)
                    {
                        Require(game.Horde.RaycastThreat(game.Flight.AimOrigin, Vector3.forward, 100f, .5f, out Vector3 hit),
                            "The hovering gargoyle was invisible to the aiming query.");
                        Require(Vector3.Distance(hit, target) < 3f, "The air target solution was not near the gargoyle.");
                    }
                    object after = fixture.Enemies.GetValue(0);
                    Require(game.Horde.Count == count && (float)Get(after, "Health") == health && (float)Get(after, "Flash") == 0f,
                        "A nonmutating aiming query changed an enemy.");
                    Require(game.Horde.SampleThreat(target, 10f, out _, out _) == 0,
                        "Civilian melee sampling included a gargoyle.");
                    return "Forty real air-target queries preserved health, flash, population, and ground-only sampling.";
                });

                Check(result, "Crypt garrison holds its generator", () =>
                {
                    fixture.Reset();
                    MethodInfo spawn = typeof(Horde).GetMethod("SpawnGarrison", Fields);
                    spawn.Invoke(game.Horde, new object[] { 0 });
                    Vector3 home = game.Crypts[0].Position;
                    game.Flight.Teleport(World.BasePosition + Vector3.up * 30f, 0f, 0f);
                    for (int i = 0; i < 8f / Step; i++) fixture.StepPilot(Vector2.zero, 0f, 0f);
                    Require(game.Horde.Count == 4, "Expected two sentinels and two winged defenders.");
                    for (int i = 0; i < 4; i++)
                    {
                        object enemy = fixture.Enemies.GetValue(i);
                        Vector3 offset = (Vector3)Get(enemy, "Position") - home; offset.y = 0f;
                        Require(offset.magnitude < 38f, "A territorial defender abandoned its crypt for a distant village.");
                    }
                    game.Horde.OnCryptDestroyed(game.Crypts[0]);
                    Require(game.Horde.Count == 6, "Sealing a guarded crypt did not launch two physical pursuers.");
                    int pursuing = 0;
                    for (int i = 0; i < game.Horde.Count; i++)
                        if ((byte)Get(fixture.Enemies.GetValue(i), "Role") == 3) pursuing++;
                    Require(pursuing == 4, "Surviving winged defenders did not join the retaliation.");
                    return "Four defenders stayed at home; sealing converted two survivors and spawned two pursuers.";
                });

                Check(result, "Sentinel damage interrupts a live charged salvo", () =>
                {
                    fixture.Reset();
                    Vector3 point = World.Ground(60f, 30f);
                    fixture.Add(EnemyKind.Necromancer, point, 1, .2f, 0f);
                    for (int i = 0; i < 20; i++) fixture.StepPilot(Vector2.zero, 0f, 0f);
                    Require(game.Horde.ChargingHexes == 3, "The sentinel did not charge its three-bolt pattern.");
                    game.Horde.DamageArea(point + Vector3.up, 4f, 10f, DamageKind.Silver);
                    for (int i = 0; i < 55; i++) fixture.StepPilot(Vector2.zero, 0f, 0f);
                    Require(game.Horde.Count == 1 && game.Horde.ActiveHexes == 0 && game.Horde.ChargingHexes == 0,
                        "Damaging, but not killing, a sentinel failed to interrupt its pending salvo.");
                    return "A 10-damage hit canceled all three pending bolts while the sentinel remained alive.";
                });

                Check(result, "Crypt retaliation survives the population cap", () =>
                {
                    fixture.Reset();
                    game.Horde.SpawnHorde(World.Ground(0f, 0f), game.Horde.Capacity);
                    Require(game.Horde.Count == game.Horde.Capacity, "The cap fixture failed to fill the population.");
                    int kills = game.Kills, salvage = game.Salvage;
                    game.Horde.OnCryptDestroyed(game.Crypts[0]);
                    int pursuers = 0;
                    for (int i = 0; i < (int)Get(game.Horde, "_count"); i++)
                        if ((byte)Get(fixture.Enemies.GetValue(i), "Role") == 3) pursuers++;
                    Require(pursuers == 2 && game.Horde.Count == game.Horde.Capacity,
                        "A full population suppressed retaliation or exceeded its cap.");
                    Require(game.Kills == kills && game.Salvage == salvage, "Recycling infantry awarded free kills or salvage.");
                    return "At 6,000 actors, two infantry slots became physical pursuers without changing count, kills, or salvage.";
                });

                Outcome hover = fixture.Encounter(Pilot.Hover);
                Outcome straight = fixture.Encounter(Pilot.Straight);
                Outcome evasive = fixture.Encounter(Pilot.Evasive);
                Outcome clearance = fixture.Encounter(Pilot.Clearance);
                result("Hovering receives committed aerial attacks", hover.Damage >= 14f && hover.DiveFrames > 0,
                    hover.Describe());
                result("Full-speed straight flight cannot ignore interceptors", straight.Damage >= 14f && straight.PeakSpeed >= 22f && straight.Distance > 100f,
                    straight.Describe());
                result("Warning-driven evasive flight reduces incoming damage",
                    evasive.Damage < straight.Damage && evasive.Damage < hover.Damage && evasive.DodgeCommands > 0,
                    "Hover: " + hover.Describe() + " Straight: " + straight.Describe() + " Evasive: " + evasive.Describe());
                result("Deliberate defender clearance improves survival",
                    clearance.Kills >= 4 && clearance.Damage < hover.Damage && clearance.Rounds > 0,
                    clearance.Describe() + " Compared with unarmed hover damage " + hover.Damage.ToString("F1") + ".");
            }
            catch (Exception error)
            {
                result("Combat opposition execution", false, error.GetBaseException().Message);
            }
            finally
            {
                for (int i = game.Supplies.Count - 1; i >= supplies; i--)
                {
                    if (game.Supplies[i].Visual) UnityEngine.Object.Destroy(game.Supplies[i].Visual.gameObject);
                    game.Supplies.RemoveAt(i);
                }
                foreach (Snapshot snapshot in snapshots) snapshot.Restore();
                for (int i = 0; i < cryptHealth.Length; i++) game.Crypts[i].Health = cryptHealth[i];
                game.Flight.transform.SetPositionAndRotation(position, rotation);
                camera.transform.localPosition = cameraPosition;
                camera.transform.localRotation = cameraRotation;
                camera.fieldOfView = cameraFov;
                game.Audio.Muted = muted;
                UnityEngine.Random.state = random;
                foreach (LineRenderer beam in (LineRenderer[])Get(game.Effects, "beams")) if (beam) beam.enabled = false;
            }
        }

        enum Pilot { Hover, Straight, Evasive, Clearance }
        struct Outcome
        {
            public float Damage, Distance, PeakSpeed, Duration, LastAimError, LastTargetRange;
            public int DiveFrames, DodgeCommands, Kills, Rounds;
            public string Describe() => "damage=" + Damage.ToString("F1") + ", kills=" + Kills +
                ", traveled=" + Distance.ToString("F0") + "m, peak=" + PeakSpeed.ToString("F1") +
                "m/s, dives=" + DiveFrames + " frames, dodge commands=" + DodgeCommands +
                ", rounds=" + Rounds + ", duration=" + Duration.ToString("F1") + "s" +
                (Rounds > 0 ? ", last aim error=" + LastAimError.ToString("F1") + "deg, target range=" + LastTargetRange.ToString("F1") : "");
        }

        sealed class Fixture
        {
            readonly Game game;
            readonly Horde horde;
            readonly Type enemyType;
            readonly Horde.ThreatRadarPoint[] radar = new Horde.ThreatRadarPoint[32];
            public readonly Array Enemies;

            public Fixture(Game owner)
            {
                game = owner; horde = owner.Horde;
                Enemies = (Array)Get(horde, "_enemies");
                enemyType = Enemies.GetType().GetElementType();
            }

            public void Reset()
            {
                UnityEngine.Random.InitState(74821);
                Array.Clear(Enemies, 0, Enemies.Length);
                foreach (string name in new[] { "_hexes", "_cryptSpawnBudgets" })
                { Array array = (Array)Get(horde, name); Array.Clear(array, 0, array.Length); }
                Set(horde, "_count", 0); Set(horde, "_nextEnemyId", 0); Set(horde, "_frame", 0);
                foreach (string name in new[] { "_targetClock", "_towerClock", "_stillTime", "_towerIntegrity", "_boardClock",
                    "_shakeClock", "_hexCastClock", "_hexHitClock", "_batHitClock", "_heavyHitClock", "_hexVisualClock", "_towerFlash" })
                    Set(horde, name, 0f);
                Set(horde, "_nextAssaultTime", float.MaxValue);
                foreach (string name in new[] { "Boarders", "TowerCount", "ChargingHexes", "ActiveHexes", "DivingBats",
                    "EngagedGargoyles", "DivingGargoyles", "ChargingGargoyles" }) Set(horde, "<" + name + ">k__BackingField", 0);
                foreach (Crypt crypt in game.Crypts) crypt.Health = 0f;
                game.Started = true; game.GameOver = game.Won = game.Paused = false;
                game.Hull = game.Fuel = 100f; game.Elapsed = 150f; game.Kills = game.Combo = 0;
                game.Flight.Teleport(World.Ground(0f, 0f) + Vector3.up * 28f, 0f, 0f);
                foreach (WeaponState weapon in game.Arsenal.Weapons)
                { weapon.Ammo = weapon.MaxAmmo; weapon.Cooldown = 0f; weapon.Level = 1; }
                game.Arsenal.LeftSlot = 0;
            }

            public void Add(EnemyKind kind, Vector3 position, byte role, float phase, float cooldown)
            {
                object enemy = Activator.CreateInstance(enemyType);
                Set(enemy, "Position", position); Set(enemy, "Home", position);
                Set(enemy, "Ground", World.Height(position.x, position.z));
                Set(enemy, "Kind", kind); Set(enemy, "Role", role); Set(enemy, "Phase", phase);
                Set(enemy, "Health", kind == EnemyKind.Gargoyle ? 210f : 180f);
                Set(enemy, "Speed", kind == EnemyKind.Gargoyle ? 28f : 2.5f);
                Set(enemy, "AttackTime", cooldown); Set(enemy, "CryptIndex", -1);
                int count = (int)Get(horde, "_count");
                Set(enemy, "Id", count + 1); Set(horde, "_nextEnemyId", count + 1);
                Enemies.SetValue(enemy, count); Set(horde, "_count", count + 1);
            }

            public void StepPilot(Vector2 movement, float climb, float yaw, float look = 0f, bool fire = false)
            {
                game.Flight.TickCommand(Step, movement, climb, yaw, look);
                game.Arsenal.Tick(Step, false);
                if (fire) game.Arsenal.TryFire(0);
                game.Elapsed += Step;
                horde.Tick(Step);
            }

            public Outcome Encounter(Pilot pilot)
            {
                Reset();
                Vector3 start = game.Flight.transform.position;
                Add(EnemyKind.Gargoyle, start + new Vector3(-34f, 3f, -34f), 3, .15f, .6f);
                Add(EnemyKind.Gargoyle, start + new Vector3(38f, -2f, 34f), 3, .55f, 1.2f);
                Add(EnemyKind.Gargoyle, start + new Vector3(4f, 7f, -58f), 3, .85f, 2.1f);
                Add(EnemyKind.Necromancer, World.Ground(-88f, 45f), 1, .25f, 1.2f);
                Add(EnemyKind.Necromancer, World.Ground(86f, -38f), 1, .75f, 2.5f);
                Outcome outcome = new Outcome();
                float dodgeTime = 0f, dodgeDirection = 1f, dodgeClimb = 1f;
                int previousDives = 0;
                int lockedTarget = -1;
                Vector3 lastTargetPosition = Vector3.zero;
                for (int tick = 0; tick < 24f / Step && !game.GameOver; tick++)
                {
                    Vector2 movement = pilot == Pilot.Straight || pilot == Pilot.Evasive ? Vector2.up : Vector2.zero;
                    float climb = 0f, yaw = 0f, look = 0f;
                    bool fire = false;
                    if (pilot == Pilot.Evasive)
                    {
                        if (horde.DivingGargoyles > previousDives)
                        {
                            dodgeTime = 1.65f; dodgeDirection = -dodgeDirection;
                            dodgeClimb = -dodgeClimb;
                            if (game.Flight.Altitude < 11f) dodgeClimb = 1f;
                            else if (game.Flight.Altitude > 47f) dodgeClimb = -1f;
                        }
                        previousDives = horde.DivingGargoyles;
                        if (dodgeTime > 0f)
                        {
                            dodgeTime -= Step; movement = new Vector2(dodgeDirection, .55f);
                            climb = dodgeClimb; yaw = dodgeDirection * 45f * Step;
                            outcome.DodgeCommands++;
                        }
                    }
                    if (pilot == Pilot.Clearance)
                    {
                        int contacts = horde.GetThreatRadarPoints(game.Flight.transform.position, 190f, radar);
                        int target = -1; float best = float.PositiveInfinity;
                        for (int i = 0; i < contacts; i++)
                            if (radar[i].Id == lockedTarget) { target = i; break; }
                        bool retained = target >= 0;
                        for (int i = 0; i < contacts; i++)
                        {
                            if (retained) break;
                            float priority = Vector3.Distance(radar[i].Position, game.Flight.transform.position);
                            if (radar[i].Kind == EnemyKind.Necromancer) priority += 90f;
                            if (priority < best) { best = priority; target = i; }
                        }
                        if (target >= 0)
                        {
                            Vector3 targetVelocity = retained ? (radar[target].Position - lastTargetPosition) / Step : Vector3.zero;
                            lockedTarget = radar[target].Id;
                            lastTargetPosition = radar[target].Position;
                            // Lead the angular servo slightly, just as a pilot tracks a moving target with mouse input.
                            Vector3 aim = radar[target].Position + targetVelocity * .18f +
                                Vector3.up * (radar[target].Heavy ? 0f : 1.5f);
                            Vector3 delta = aim - game.Flight.AimOrigin;
                            float heading = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                            float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                            yaw = Mathf.Clamp(Mathf.DeltaAngle(game.Flight.TargetHeading, heading), -95f * Step, 95f * Step);
                            look = Mathf.Clamp(pitch - game.Flight.LookPitch, -80f * Step, 80f * Step);
                            outcome.LastAimError = Vector3.Angle(game.Flight.AimForward, delta);
                            outcome.LastTargetRange = delta.magnitude;
                            Vector3 visibleTarget = radar[target].Position + Vector3.up * (radar[target].Heavy ? 0f : 1.5f);
                            Vector3 sight = visibleTarget - game.Flight.AimOrigin;
                            Vector3 terrain = game.Arsenal.TraceAim(game.Flight.AimOrigin, sight.normalized, sight.magnitude, false);
                            if (Vector3.Distance(game.Flight.AimOrigin, terrain) < sight.magnitude - 4f)
                            {
                                // A deliberate pilot climbs or closes the distance to reveal a guard behind a ridge.
                                climb = game.Flight.Altitude < 51f ? 1f : 0f;
                                movement = game.Flight.Altitude >= 48f ? new Vector2(0f, .6f) : Vector2.zero;
                            }
                            fire = Vector3.Dot(game.Flight.AimForward, delta.normalized) > .975f;
                        }
                    }
                    Vector3 previous = game.Flight.transform.position;
                    StepPilot(movement, climb, yaw, look, fire);
                    outcome.Distance += Vector3.Distance(previous, game.Flight.transform.position);
                    outcome.PeakSpeed = Mathf.Max(outcome.PeakSpeed, game.Flight.PlanarSpeed);
                    if (horde.DivingGargoyles > 0) outcome.DiveFrames++;
                    outcome.Duration += Step;
                }
                outcome.Damage = 100f - game.Hull;
                outcome.Kills = game.Kills;
                outcome.Rounds = Mathf.RoundToInt(game.Arsenal.Weapons[0].MaxAmmo - game.Arsenal.Weapons[0].Ammo);
                return outcome;
            }
        }

        static void Check(Action<string, bool, string> result, string name, Func<string> test)
        {
            try { result(name, true, test()); }
            catch (Exception error) { result(name, false, error.GetBaseException().Message); }
        }
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
        static object Get(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);

        sealed class Snapshot
        {
            readonly object target;
            readonly List<FieldInfo> fields = new List<FieldInfo>();
            readonly List<object> values = new List<object>();
            public Snapshot(object instance)
            {
                target = instance;
                foreach (FieldInfo field in instance.GetType().GetFields(Fields | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic) continue;
                    object value = field.GetValue(instance);
                    if (field.IsInitOnly && !(value is Array)) continue;
                    fields.Add(field); values.Add(value is Array array ? array.Clone() : value);
                }
            }
            public void Restore()
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    if (values[i] is Array saved && fields[i].GetValue(target) is Array current && saved.Length == current.Length)
                        Array.Copy(saved, current, saved.Length);
                    else if (!fields[i].IsInitOnly) fields[i].SetValue(target, values[i]);
                }
            }
        }
    }
}
