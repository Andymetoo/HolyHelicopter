using System;
using System.Collections.Generic;
using System.Text;

namespace NullMenagerie.Validation
{
    /// <summary>Runs against the actual simulation, both from the editor and an ordinary C# host.</summary>
    public static class CombatChecks
    {
        public static string Run()
        {
            var report = new StringBuilder();
            Specimen[] specimens = SpecimenGenerator.Samples();
            int bouts = 0;
            float shortest = 60, longest = 0;
            for (int a = 0; a < specimens.Length; ++a)
            for (int b = 0; b < specimens.Length; ++b)
            {
                var fight = new CombatSimulation(specimens[a], specimens[b], Stance.Balanced, Stance.Balanced, 704);
                bool moved = false, attacked = false, damaged = false;
                int seen = 0;
                for (int frame = 0; frame < 3700 && !fight.Finished; frame++)
                {
                    fight.Step(1f / 60);
                    foreach (Combatant f in fight.Fighters)
                    {
                        Require(Finite(f.X) && Finite(f.Y) && Finite(f.Health), "Nonfinite combat state");
                        Require(f.X >= 8 && f.X <= 92 && f.Y >= -12 && f.Y <= 12, "Fighter escaped cage");
                        Require(f.Health >= 0 && f.Health <= f.Specimen.Health + .01f, "Invalid health");
                        Require(f.Shield >= 0 && f.SpecialCharge >= 0 && f.SpecialCharge <= 1, "Invalid shield or charge");
                    }
                    moved |= Math.Abs(fight.Fighters[0].X - 24) > 2 || Math.Abs(fight.Fighters[0].Y) > 1;
                    foreach (CombatEvent e in fight.Events)
                        if (e.Id > seen) { attacked |= e.Kind == "windup"; damaged |= e.Kind == "hit" && e.Value > 0; }
                    if (fight.Events.Count > 0) seen = fight.Events[fight.Events.Count - 1].Id;
                }
                Require(fight.Finished && fight.Time <= 60.01f, "Bout failed to terminate");
                Require(moved && attacked && damaged, "Bout did not move, attack and damage");
                Require(fight.Events.Count <= 96, "Event history grew without bound");
                Require(fight.Result.Length > 0, "Missing result");
                if (fight.Winner >= 0) Require(fight.Fighters[fight.Winner].Alive, "Dead winner");
                shortest = Math.Min(shortest, fight.Time); longest = Math.Max(longest, fight.Time); bouts++;
            }
            report.AppendLine("PASS " + bouts + " round-robin bouts: movement, windups, damage, bounds, finite state and results (" + shortest.ToString("F1") + "-" + longest.ToString("F1") + " seconds)");

            var first = Finish(new CombatSimulation(specimens[0], specimens[3], Stance.Cautious, Stance.Aggressive, 99), 1f / 60);
            var replay = Finish(new CombatSimulation(specimens[0], specimens[3], Stance.Cautious, Stance.Aggressive, 99), 1f / 30);
            Require(first.Winner == replay.Winner && first.Time == replay.Time, "Frame-rate-dependent result");
            for (int i = 0; i < 2; ++i)
                Require(first.Fighters[i].Health == replay.Fighters[i].Health && first.Fighters[i].DamageDealt == replay.Fighters[i].DamageDealt && first.Fighters[i].X == replay.Fighters[i].X, "Replay diverged");
            var fresh = new CombatSimulation(specimens[0], specimens[3]);
            Require(fresh.Time == 0 && !fresh.Finished && fresh.Fighters[0].Health == specimens[0].Health, "Rematch reused dead state");
            report.AppendLine("PASS deterministic replay at 30/60 Hz and fresh rematch state");

            var checkedKinds = new HashSet<AbilityKind>();
            foreach (AbilityKind ability in Enum.GetValues(typeof(AbilityKind)))
            {
                Specimen a = Copy(specimens[0]);
                Specimen b = Copy(specimens[1]);
                a.Ability = ability; a.AbilityName = ability.ToString(); a.SpecialCooldown = 2;
                a.Health = b.Health = 500;
                var fight = new CombatSimulation(a, b);
                int seen = 0;
                bool special = false, specific = false;
                for (int frame = 0; frame < 1200 && !fight.Finished; ++frame)
                {
                    fight.Step(1f / 60);
                    foreach (CombatEvent e in fight.Events)
                        if (e.Id > seen && e.Actor == 0)
                        {
                            special |= e.Kind == "special" && e.Text == ability.ToString();
                            specific |= ability == AbilityKind.Patch ? e.Kind == "heal" : e.Kind == "projectile";
                        }
                    seen = fight.Events[fight.Events.Count - 1].Id;
                }
                Require(special && specific, "Ability never resolved: " + ability);
                checkedKinds.Add(ability);
            }
            report.AppendLine("PASS all " + checkedKinds.Count + " abilities wind up and resolve their effect");

            foreach (MutationKind mutation in new[] { MutationKind.Crown, MutationKind.Twin, MutationKind.Hollow, MutationKind.Ouroboros })
            {
                Specimen a = Copy(specimens[0]), b = Copy(specimens[3]);
                a.Mutation = mutation; a.Health = 75; a.Power = 8;
                b.Health = 350; b.Power = 19;
                var fight = new CombatSimulation(a, b);
                bool activated = false;
                int seen = 0;
                for (int frame = 0; frame < 3700 && !fight.Finished; ++frame)
                {
                    fight.Step(1f / 60);
                    foreach (CombatEvent e in fight.Events)
                        if (e.Id > seen && e.Actor == 0 && e.Kind == "mutation") activated = true;
                    seen = fight.Events[fight.Events.Count - 1].Id;
                }
                Require(activated, "Mutation never activated: " + mutation);
                Require(fight.Finished, "Mutant bout stalled");
            }
            report.AppendLine("PASS Crown shield, Twin echo, Hollow phase and Ouroboros resurrection activate");

            // Equal mirrored creatures must not give the left slot a guaranteed first-hit win.
            var mirror = Finish(new CombatSimulation(specimens[3], specimens[3], battleSeed: 7), 1f / 60);
            Require(mirror.Winner == -1 || Math.Abs(mirror.Fighters[0].DamageDealt - mirror.Fighters[1].DamageDealt) <= specimens[3].Power * 1.5f,
                "Large first-slot bias in identical matchup");
            var invalid = new CombatSimulation(specimens[0], specimens[1]);
            invalid.Step(float.NaN); invalid.Step(float.PositiveInfinity); invalid.Step(-1);
            Require(invalid.Time == 0, "Invalid timestep advanced combat");
            report.AppendLine("PASS mirrored matchup and invalid timestep guards");
            return report.ToString();
        }

        static CombatSimulation Finish(CombatSimulation fight, float step)
        {
            for (int i = 0; i < 7200 && !fight.Finished; ++i) fight.Step(step);
            Require(fight.Finished, "Simulation did not finish");
            return fight;
        }

        static Specimen Copy(Specimen source)
        {
            return new Specimen { Source = source.Source, Name = source.Name, Seed = source.Seed, Health = source.Health,
                Power = source.Power, Armor = source.Armor, Speed = source.Speed, Reach = source.Reach,
                Cadence = source.Cadence, SpecialCooldown = source.SpecialCooldown, Ability = source.Ability,
                AbilityName = source.AbilityName, Mutation = MutationKind.None };
        }

        static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    }
}
