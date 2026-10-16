using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>
    /// Opt-in deterministic checks, called by the standalone balance suite on a fresh game.
    /// Reflection supplies isolated enemies and pilot trajectories; damage, attacks, projectiles,
    /// tower recruitment and boarding still run through the real Horde.Tick implementation.
    /// </summary>
    public static class ThreatValidation
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const float Step = 1f / 60f;

        public static void Run(Game game, Action<string, bool, string> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (game == null || game.Horde == null || game.Flight == null)
            {
                result("Threat fixture initialization", false, "A fully initialized game is required.");
                return;
            }

            var hordeState = new StateSnapshot(game.Horde);
            var gameState = new StateSnapshot(game);
            var flightState = new StateSnapshot(game.Flight);
            Vector3 flightPosition = game.Flight.transform.position;
            Quaternion flightRotation = game.Flight.transform.rotation;
            var randomState = UnityEngine.Random.state;
            float[] cryptHealth = new float[game.Crypts.Count];
            for (int i = 0; i < cryptHealth.Length; i++) cryptHealth[i] = game.Crypts[i].Health;
            bool muted = game.Audio.Muted;
            int suppliesBefore = game.Supplies.Count;
            var fixture = new Fixture(game);
            UnityEngine.Random.InitState(9081);
            game.Audio.Muted = true;
            try
            {
                Check(result, "Threat sample excludes airborne monsters", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 20f);
                    fixture.Add(EnemyKind.Skeleton, World.Ground(center.x + 3, center.z), .1f, 20f);
                    fixture.Add(EnemyKind.Zombie, World.Ground(center.x - 8, center.z), .2f, 20f);
                    fixture.Add(EnemyKind.Necromancer, World.Ground(center.x + 12, center.z), .3f, 20f);
                    fixture.Add(EnemyKind.Bat, center + new Vector3(.5f, 12f, 0), .4f, 20f);
                    fixture.Add(EnemyKind.Bat, center + new Vector3(-.5f, 25f, 0), .5f, 20f);
                    int count = game.Horde.SampleThreat(center, 10f, out Vector3 away, out float nearest);
                    Require(count == 2, "Expected two ground contacts; got " + count + ".");
                    Require(Mathf.Abs(nearest - 3f) < .01f, "Nearest contact should be the skeleton at 3 m.");
                    Require(away.sqrMagnitude > .99f && away.x < 0f, "Escape direction must be normalized and away from the nearest threat.");
                    int empty = game.Horde.SampleThreat(center + Vector3.forward * 150f, 5f, out away, out nearest);
                    Require(empty == 0 && float.IsPositiveInfinity(nearest) && away == Vector3.zero,
                        "Empty samples must return zero, zero direction, and infinite nearest distance.");
                    return "Ground-only counts and nearest contact verified, including the empty result contract.";
                });

                Check(result, "Necromancer charge precedes real projectile damage", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 20f);
                    fixture.Add(EnemyKind.Necromancer, World.Ground(center.x + 24f, center.z), .1f, 0f);
                    fixture.Advance(.8f);
                    Require(game.Horde.ChargingHexes > 0, "The caster did not enter its charge phase.");
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "The charge applied immediate damage.");
                    bool sawProjectile = false;
                    fixture.Advance(1.8f, null, () => sawProjectile |= game.Horde.ActiveHexes > 0);
                    Require(sawProjectile, "No traveling projectile was observed after the charge.");
                    Require(game.Hull < 100f && game.Hull >= 94f, "A stationary pilot should take one delayed 5.5-point hit; hull=" + game.Hull);
                    return "Charge remained harmless for 0.8 s; a traveling hex subsequently hit the stationary aircraft.";
                });

                Check(result, "Moving after a hex warning dodges the projectile", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 20f);
                    fixture.Add(EnemyKind.Necromancer, World.Ground(center.x + 24f, center.z), .1f, 0f);
                    fixture.Advance(.8f);
                    Require(game.Horde.ChargingHexes > 0, "The dodge fixture never received a warning.");
                    bool sawProjectile = false;
                    fixture.Advance(2.4f, dt => fixture.MovePilot(Vector3.forward * 18f, dt),
                        () => sawProjectile |= game.Horde.ActiveHexes > 0);
                    Require(sawProjectile, "The shot failed to launch, so this was not a dodge test.");
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "A pilot moving perpendicular to the locked aim took damage; hull=" + game.Hull);
                    return "The same charged attack missed after a perpendicular 18 m/s trajectory began during the warning.";
                });

                Check(result, "Killing a charging caster interrupts its hex", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    Vector3 caster = World.Ground(center.x + 24f, center.z);
                    fixture.Reset(center, 20f);
                    fixture.Add(EnemyKind.Necromancer, caster, .1f, 0f);
                    fixture.Advance(.25f);
                    Require(game.Horde.ChargingHexes > 0, "The cancellation fixture never began charging.");
                    int killed = game.Horde.DamageArea(caster + Vector3.up, 5f, 10000f, DamageKind.Silver);
                    Require(killed == 1, "The controlled caster was not killed.");
                    fixture.Advance(2.5f);
                    Require(game.Horde.ActiveHexes == 0 && game.Horde.ChargingHexes == 0,
                        "The dead caster's charge remained in the projectile pool.");
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "An interrupted cast still damaged the aircraft.");
                    // Reuse the same pool after interruption; a fresh actor must be able to cast normally.
                    fixture.Add(EnemyKind.Necromancer, caster, .2f, 0f);
                    fixture.Advance(.3f);
                    Require(game.Horde.ChargingHexes > 0, "The interrupted pool slot did not accept a fresh caster.");
                    return "Caster death canceled the pending attack; the recycled projectile slot accepted a new cast.";
                });

                Check(result, "Bat orbit is harmless until a contact dive", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 20f);
                    fixture.Add(EnemyKind.Bat, game.Flight.transform.position + Vector3.right * 10f, .2f, 10f);
                    fixture.Advance(2f);
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "An orbiting bat damaged the helicopter without diving.");
                    fixture.Reset(center, 20f);
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 16f;
                        fixture.Add(EnemyKind.Bat, game.Flight.transform.position +
                            new Vector3(Mathf.Cos(angle) * 12f, 1f, Mathf.Sin(angle) * 12f), i / 20f, 0f);
                    }
                    fixture.Advance(.5f);
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "Bat wind-up caused immediate damage.");
                    bool sawDive = false;
                    fixture.Advance(1.7f, null, () => sawDive |= game.Horde.DivingBats > 0);
                    Require(sawDive && game.Hull < 100f, "The bats never completed a damaging contact dive.");
                    Require(game.Hull >= 92.1f, "Simultaneous bats exceeded the shared 0.7 s damage gate; hull=" + game.Hull);
                    return "Orbit and wind-up caused no damage; sixteen simultaneous divers remained within the shared damage cap.";
                });

                Check(result, "Low hover builds a tower, boards, and damages hull", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 7f);
                    fixture.GroundCrowd(center, 100);
                    fixture.Advance(7f);
                    Require(game.Horde.TowerCount > 0 && game.Horde.TowerHeight > 4f, "The stationary ground swarm failed to form a tower.");
                    Require(game.Horde.Boarders > 0, "The tower reached low altitude without boarding.");
                    Require(game.Hull < 100f, "Attached boarders failed to cause hull damage.");
                    return "After 7 s of low hovering: tower " + game.Horde.TowerHeight.ToString("F1") +
                        " m, boarders " + game.Horde.Boarders + ", hull " + game.Hull.ToString("F1") + ".";
                });

                Check(result, "Climbing shakes real tower boarders loose", () =>
                {
                    // Continue the previous real boarding encounter; no clear-boarder helper is used.
                    Require(game.Horde.Boarders > 0, "The preceding tower fixture produced no boarders.");
                    fixture.Advance(5f, dt => fixture.MovePilot(Vector3.up * 8f, dt));
                    Require(game.Horde.Boarders == 0, "A sustained climb did not remove the boarders.");
                    return "An 8 m/s climb escaped the stack and cleared every attached boarder through TickBoarders.";
                });

                Check(result, "Fast lateral flight also shakes boarders loose", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 16f);
                    Set(game.Horde, "<Boarders>k__BackingField", 6);
                    fixture.Advance(2f, dt => fixture.MovePilot(Vector3.right * 25f, dt));
                    Require(game.Horde.Boarders == 0, "Six boarders survived sustained lateral flight.");
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "Actively shaken boarders caused passive boarding damage.");
                    return "Six attached boarders were shed during 2 s at 25 m/s without invoking ClearBoarders.";
                });

                Check(result, "Ground melee cannot damage a high helicopter", () =>
                {
                    Vector3 center = World.VillagePosition(0);
                    fixture.Reset(center, 58f);
                    fixture.GroundCrowd(center, 140);
                    fixture.Advance(12f);
                    Require(game.Horde.Count > 100, "The high-altitude fixture lost its ground crowd.");
                    Require(game.Horde.Boarders == 0 && game.Horde.TowerCount == 0, "Ground monsters boarded or built beyond the altitude limit.");
                    Require(Mathf.Abs(game.Hull - 100f) < .001f, "Ground-only enemies damaged a helicopter at 58 m clearance.");
                    return "A 140-skeleton crowd caused zero hull damage or boarding during 12 s at maximum altitude.";
                });
            }
            finally
            {
                for (int i = game.Supplies.Count - 1; i >= suppliesBefore; i--)
                {
                    if (game.Supplies[i].Visual != null) UnityEngine.Object.Destroy(game.Supplies[i].Visual.gameObject);
                    game.Supplies.RemoveAt(i);
                }
                hordeState.Restore();
                gameState.Restore();
                flightState.Restore();
                game.Flight.transform.SetPositionAndRotation(flightPosition, flightRotation);
                for (int i = 0; i < cryptHealth.Length; i++) game.Crypts[i].Health = cryptHealth[i];
                game.Audio.Muted = muted;
                UnityEngine.Random.state = randomState;
                ClearFixtureEffects(game.Effects);
            }
        }

        static void Check(Action<string, bool, string> result, string name, Func<string> action)
        {
            try { result(name, true, action()); }
            catch (Exception error) { result(name, false, error.GetBaseException().Message); }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static object Get(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Fields);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return field.GetValue(target);
        }

        static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Fields);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        static void ClearFixtureEffects(Effects effects)
        {
            if (effects == null) return;
            var sparks = Get(effects, "sparks") as Array;
            if (sparks != null) Array.Clear(sparks, 0, sparks.Length);
            var beams = Get(effects, "beams") as LineRenderer[];
            if (beams != null) foreach (var beam in beams) if (beam != null) beam.enabled = false;
        }

        sealed class Fixture
        {
            readonly Game game;
            readonly Horde horde;
            readonly Array enemies;
            readonly Type enemyType;

            public Fixture(Game owner)
            {
                game = owner;
                horde = game.Horde;
                enemies = (Array)Get(horde, "_enemies");
                enemyType = enemies.GetType().GetElementType();
            }

            public void Reset(Vector3 ground, float altitude)
            {
                Array.Clear(enemies, 0, enemies.Length);
                foreach (string name in new[] { "_hexes", "_cryptSpawnBudgets" })
                {
                    var array = (Array)Get(horde, name);
                    Array.Clear(array, 0, array.Length);
                }
                Set(horde, "_count", 0);
                Set(horde, "_nextEnemyId", 0);
                foreach (string name in new[]
                {
                    "_targetClock", "_towerClock", "_stillTime", "_towerIntegrity", "_boardClock", "_shakeClock",
                    "_hexCastClock", "_hexHitClock", "_batHitClock", "_hexVisualClock", "_towerFlash"
                }) Set(horde, name, 0f);
                Set(horde, "_nextAssaultTime", float.MaxValue);
                foreach (string name in new[] { "Boarders", "TowerCount", "ChargingHexes", "ActiveHexes", "DivingBats" })
                    Set(horde, "<" + name + ">k__BackingField", 0);
                Set(horde, "<ThreatLevel>k__BackingField", 0f);
                foreach (Crypt crypt in game.Crypts) crypt.Health = 0f;
                game.Started = true;
                game.GameOver = game.Won = game.Paused = false;
                game.Hull = game.Fuel = 100f;
                game.Elapsed = 120f;
                game.Kills = game.Combo = 0;
                game.Flight.Teleport(ground + Vector3.up * altitude);
                ClearFixtureEffects(game.Effects);
            }

            public void Add(EnemyKind kind, Vector3 position, float phase, float attackTime)
            {
                int count = (int)Get(horde, "_count");
                object enemy = Activator.CreateInstance(enemyType);
                Set(enemy, "Position", position);
                Set(enemy, "Ground", World.Height(position.x, position.z));
                Set(enemy, "Kind", kind);
                Set(enemy, "Phase", phase);
                Set(enemy, "Health", kind == EnemyKind.Necromancer ? 150f : 60f);
                Set(enemy, "Speed", kind == EnemyKind.Bat ? 13f : 3.1f);
                Set(enemy, "AttackTime", attackTime);
                Set(enemy, "Army", (byte)0);
                int id = (int)Get(horde, "_nextEnemyId") + 1;
                Set(horde, "_nextEnemyId", id);
                Set(enemy, "Id", id);
                enemies.SetValue(enemy, count);
                Set(horde, "_count", count + 1);
            }

            public void GroundCrowd(Vector3 center, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = i * 2.399963f;
                    float radius = 3f + i % 8;
                    Vector3 point = World.Ground(center.x + Mathf.Cos(angle) * radius, center.z + Mathf.Sin(angle) * radius);
                    Add(EnemyKind.Skeleton, point, .1f + (i % 50) * .01f, 20f);
                }
            }

            public void MovePilot(Vector3 velocity, float dt)
            {
                game.Flight.transform.position += velocity * dt;
                Set(game.Flight, "<Velocity>k__BackingField", velocity);
            }

            public void Advance(float seconds, Action<float> movement = null, Action observe = null)
            {
                int ticks = Mathf.CeilToInt(seconds / Step);
                for (int i = 0; i < ticks; i++)
                {
                    movement?.Invoke(Step);
                    game.Elapsed += Step;
                    horde.Tick(Step);
                    observe?.Invoke();
                }
            }
        }

        sealed class StateSnapshot
        {
            readonly object target;
            readonly List<FieldInfo> fields = new List<FieldInfo>();
            readonly List<object> values = new List<object>();

            public StateSnapshot(object instance)
            {
                target = instance;
                foreach (FieldInfo field in instance.GetType().GetFields(Fields | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic) continue;
                    object value = field.GetValue(instance);
                    if (field.IsInitOnly && !(value is Array)) continue;
                    fields.Add(field);
                    values.Add(value is Array array ? array.Clone() : value);
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
