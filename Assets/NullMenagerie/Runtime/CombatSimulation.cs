using System;
using System.Collections.Generic;

namespace NullMenagerie
{
    /// <summary>Mutable presentation state. A new simulation always creates fresh fighters.</summary>
    public sealed class Combatant
    {
        public Specimen Specimen;
        public float X, Y, Health, Shield, AttackFlash, HurtFlash, Windup, SpecialCharge, Stun, DeathTime;
        public bool Alive = true;
        public int Facing;
        public string Action = "STALKING";
        public float DamageDealt, Evasion;
        public Stance Stance;
        internal float Cooldown, SpecialTimer, KnockX, KnockY, Phase;
        internal bool PreparingSpecial, MutationUsed;
        internal int Attacks;
    }

    /// <summary>
    /// X/Y is the effect destination; FromX/FromY is its origin. Duration is projectile flight
    /// or suggested visual lifetime. IDs increase even after the 96-entry history rolls over.
    /// Kinds: start, windup, projectile, hit, special, heal, mutation, death, result.
    /// </summary>
    public sealed class CombatEvent
    {
        public int Id, Actor, Target;
        public string Kind, Text;
        public float X, Y, FromX, FromY, Time, Value, Duration;
    }

    /// <summary>Engine-independent fixed-step duel. No Unity random state or global time is touched.</summary>
    public sealed class CombatSimulation
    {
        public readonly Combatant[] Fighters;
        public readonly List<CombatEvent> Events = new List<CombatEvent>(96);
        public float Time { get; private set; }
        public bool Finished { get; private set; }
        public int Winner { get; private set; } = -1;
        public string Result { get; private set; } = "";

        const double TickDuration = 1.0 / 60.0;
        const float Tick = 1f / 60f;
        readonly List<Impact> scheduled = new List<Impact>();
        readonly List<Impact> ready = new List<Impact>();
        readonly float[] observedX = new float[2], observedY = new float[2];
        double accumulator;
        int ticks, nextEventId;

        sealed class Impact
        {
            internal int Actor, Target, Due;
            internal float Damage, Knock, Stun, Drain;
            internal bool TrueDamage;
            internal string Label;
        }

        public CombatSimulation(Specimen left, Specimen right, Stance leftStance = Stance.Balanced,
            Stance rightStance = Stance.Balanced, uint battleSeed = 1)
        {
            if (left == null || right == null) throw new ArgumentNullException("A duel needs two specimens.");
            CheckSpecimen(left); CheckSpecimen(right);
            var rng = new StableRandom(battleSeed);
            float phase = rng.Value() * 6.2831853f;
            Fighters = new[] { Create(left, leftStance, 24, 1, phase), Create(right, rightStance, 76, -1, phase) };
            Emit("start", -1, -1, "THE CAGE IS SEALED", 0, .7f);
            for (int i = 0; i < 2; ++i)
                if (Fighters[i].Specimen.Mutation == MutationKind.Crown)
                {
                    Fighters[i].Shield = 18;
                    Emit("mutation", i, i, "CROWN // 18 SHIELD", 18, 1.1f);
                }
        }

        static void CheckSpecimen(Specimen s)
        {
            if (!Finite(s.Health) || s.Health <= 0 || !Finite(s.Power) || s.Power <= 0 ||
                !Finite(s.Speed) || s.Speed <= 0 || !Finite(s.Armor) || s.Armor < 0 || s.Armor >= 1 ||
                !Finite(s.Cadence) || s.Cadence <= 0 || !Finite(s.SpecialCooldown) || s.SpecialCooldown <= 0 ||
                !Finite(s.Reach) || s.Reach <= 0)
                throw new ArgumentException("Combat statistics must be finite and positive, with armor in [0,1).");
        }

        static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }

        static Combatant Create(Specimen s, Stance stance, float x, int facing, float phase)
        {
            return new Combatant { Specimen = s, Stance = stance, X = x, Health = s.Health,
                Facing = facing, SpecialTimer = s.SpecialCooldown * .45f, Cooldown = .35f,
                Phase = phase + (s.Seed % 101) * .061f };
        }

        public void Step(float dt)
        {
            if (!Finite(dt) || dt <= 0 || Finished) return;
            // Bound a single call. The application can fast-forward with repeated normal-size steps.
            accumulator += Math.Min(dt, 1f);
            while (accumulator + 1e-9 >= TickDuration && !Finished)
            {
                accumulator -= TickDuration;
                TickSimulation();
            }
        }

        void TickSimulation()
        {
            ++ticks;
            Time = ticks / 60f;
            ready.Clear();
            for (int i = 0; i < 2; ++i) { observedX[i] = Fighters[i].X; observedY[i] = Fighters[i].Y; }
            // Both fighters get their turn before damage resolves, allowing honest double KOs.
            Advance(0); Advance(1);
            for (int i = scheduled.Count - 1; i >= 0; --i)
                if (scheduled[i].Due <= ticks)
                {
                    ready.Add(scheduled[i]); scheduled.RemoveAt(i);
                }
            foreach (var impact in ready) Apply(impact);
            if (Time >= 38)
            {
                // The terminal's garbage collector prevents defensive builds from stalling forever.
                float damage = (1.4f + (Time - 38) * .24f) * Tick;
                for (int i = 0; i < 2; ++i)
                {
                    Fighters[i].Health -= damage;
                    if (ticks % 120 == 0) Emit("hit", 1 - i, i, "GARBAGE COLLECTION", damage * 120, .5f);
                }
            }
            for (int i = 0; i < 2; ++i) CheckMutationAndDeath(i);
            if (!Fighters[0].Alive || !Fighters[1].Alive)
            {
                Finish(Fighters[0].Alive ? 0 : Fighters[1].Alive ? 1 : -1);
            }
            else if (ticks >= 3600)
            {
                float difference = Fighters[0].Health / Fighters[0].Specimen.Health - Fighters[1].Health / Fighters[1].Specimen.Health;
                Finish(Math.Abs(difference) < .001f ? -1 : difference > 0 ? 0 : 1);
            }
        }

        void Advance(int i)
        {
            Combatant f = Fighters[i], foe = Fighters[1 - i];
            f.AttackFlash = Math.Max(0, f.AttackFlash - Tick);
            f.HurtFlash = Math.Max(0, f.HurtFlash - Tick);
            f.Stun = Math.Max(0, f.Stun - Tick);
            f.Evasion = Math.Max(0, f.Evasion - Tick);
            f.Cooldown = Math.Max(0, f.Cooldown - Tick);
            f.SpecialTimer = Math.Max(0, f.SpecialTimer - Tick);
            f.SpecialCharge = 1 - Clamp(f.SpecialTimer / f.Specimen.SpecialCooldown, 0, 1);
            f.Facing = observedX[1 - i] < observedX[i] ? -1 : 1;
            f.X = Clamp(f.X + f.KnockX * Tick, 8, 92);
            f.Y = Clamp(f.Y + f.KnockY * Tick, -12, 12);
            f.KnockX *= .87f; f.KnockY *= .87f;
            if (!f.Alive) { f.DeathTime += Tick; return; }
            if (f.Stun > 0) { f.Action = "STAGGERED"; return; }
            if (f.Windup > 0)
            {
                f.Windup = Math.Max(0, f.Windup - Tick);
                if (f.Windup <= 0)
                {
                    if (f.PreparingSpecial) ReleaseSpecial(i); else ReleaseAttack(i);
                }
                return;
            }

            float dx = observedX[1 - i] - observedX[i], dy = observedY[1 - i] - observedY[i];
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float desired = f.Specimen.Reach * (f.Stance == Stance.Cautious ? .98f : f.Specimen.Reach >= 17 ? .91f : .72f);
            bool canSpecial = f.SpecialTimer <= 0 && distance < 37;
            if (f.Specimen.Ability == AbilityKind.Patch && f.Health > f.Specimen.Health * .9f && Time < 8) canSpecial = false;
            if (canSpecial)
            {
                f.PreparingSpecial = true;
                f.Windup = .58f;
                f.Action = f.Specimen.AbilityName ?? f.Specimen.Ability.ToString().ToUpperInvariant();
                Emit("windup", i, 1 - i, f.Action, 1, .58f);
                return;
            }
            if (f.Cooldown <= 0 && distance <= f.Specimen.Reach + 1)
            {
                f.PreparingSpecial = false;
                f.Windup = f.Stance == Stance.Aggressive ? .21f : .29f;
                f.Action = "STRIKING";
                Emit("windup", i, 1 - i, "STRIKE", 0, f.Windup);
                return;
            }

            // Orbit while waiting for a strike. Ranged creatures backpedal; bruisers crowd their prey.
            float direction = distance > desired + 1.2f ? 1 : distance < desired - 2 && f.Specimen.Reach > 15 ? -.72f : 0;
            float speed = f.Specimen.Speed * (f.Stance == Stance.Aggressive ? 1.1f : 1);
            float inverse = distance > .01f ? 1 / distance : 0;
            float tangent = (float)Math.Sin(Time * 1.3f + f.Phase) * .45f;
            f.X = Clamp(f.X + (dx * inverse * direction - dy * inverse * tangent) * speed * Tick, 8, 92);
            f.Y = Clamp(f.Y + (dy * inverse * direction + dx * inverse * tangent) * speed * Tick, -12, 12);
            f.Action = direction < 0 ? "KITING" : direction > 0 ? "HUNTING" : "CIRCLING";
        }

        void ReleaseAttack(int i)
        {
            Combatant f = Fighters[i], target = Fighters[1 - i];
            f.AttackFlash = .22f;
            f.Cooldown = f.Specimen.Cadence * (f.Stance == Stance.Aggressive ? .85f : f.Stance == Stance.Cautious ? 1.12f : 1);
            f.Attacks++;
            float dx = observedX[1 - i] - observedX[i], dy = observedY[1 - i] - observedY[i];
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance > f.Specimen.Reach + 5)
            {
                Emit("special", i, 1 - i, "EVADED", 0, .4f);
                return;
            }
            bool ranged = f.Specimen.Reach >= 17;
            Schedule(i, f.Specimen.Power, ranged ? .26f : 0, ranged ? 1.6f : 3, 0, 0, ranged ? "PACKET" : "CLAW");
            if (f.Specimen.Mutation == MutationKind.Twin && f.Attacks % 3 == 0)
            {
                Schedule(i, f.Specimen.Power * .35f, .43f, 1, 0, 0, "TWIN ECHO");
                Emit("mutation", i, i, "THE TWIN ANSWERS", 0, .5f);
            }
        }

        void ReleaseSpecial(int i)
        {
            Combatant f = Fighters[i], target = Fighters[1 - i];
            f.PreparingSpecial = false;
            f.SpecialTimer = f.Specimen.SpecialCooldown;
            f.SpecialCharge = 0;
            f.Cooldown = Math.Max(f.Cooldown, .4f);
            f.AttackFlash = .5f;
            string label = f.Specimen.AbilityName ?? f.Specimen.Ability.ToString().ToUpperInvariant();
            Emit("special", i, 1 - i, label, 1, .85f);
            switch (f.Specimen.Ability)
            {
                case AbilityKind.Ram:
                    f.X = Clamp(observedX[1 - i] - f.Facing * 8, 8, 92);
                    f.Y = observedY[1 - i];
                    Schedule(i, f.Specimen.Power * 1.15f, .08f, 11, .4f, 0, "RAM");
                    break;
                case AbilityKind.Echo:
                    for (int n = 0; n < 3; n++) Schedule(i, f.Specimen.Power * .62f, .16f + n * .24f, 2.6f, .1f, 0, "ECHO " + (n + 1));
                    break;
                case AbilityKind.Swarm:
                    for (int n = 0; n < 6; n++) Schedule(i, f.Specimen.Power * .34f, .23f + n * .12f, .8f, 0, 0, "DECOMPRESS");
                    break;
                case AbilityKind.Patch:
                    Heal(i, f.Specimen.Health * .11f, "SELF PATCH");
                    f.Shield = Math.Min(32, f.Shield + 15);
                    Emit("special", i, i, "+15 FIREWALL", 15, .65f);
                    break;
                case AbilityKind.Siphon:
                    Schedule(i, f.Specimen.Power * 1.65f, .25f, 1, 0, .85f, "LIFE LEAK");
                    break;
                case AbilityKind.Glitch:
                    f.X = Clamp(observedX[1 - i] + f.Facing * 10, 8, 92);
                    f.Y = Clamp(-observedY[1 - i] + 3 * f.Facing, -12, 12);
                    f.Facing = target.X < f.X ? -1 : 1;
                    target.SpecialTimer = Math.Min(target.Specimen.SpecialCooldown, target.SpecialTimer + 1.3f);
                    Schedule(i, f.Specimen.Power * 1.25f, .1f, 4, .45f, 0, "ADDRESS FAULT", true);
                    break;
            }
        }

        void Schedule(int actor, float damage, float delay, float knock, float stun, float drain, string label, bool trueDamage = false)
        {
            Combatant f = Fighters[actor];
            float pressure = 1 + Math.Max(0, Time - 26) * .027f;
            float stance = f.Stance == Stance.Aggressive ? 1.08f : f.Stance == Stance.Cautious ? .94f : 1;
            var impact = new Impact { Actor = actor, Target = 1 - actor, Due = ticks + (int)Math.Ceiling(delay * 60),
                Damage = damage * pressure * stance * .86f, Knock = knock, Stun = stun, Drain = drain, Label = label, TrueDamage = trueDamage };
            if (delay <= 0) ready.Add(impact); else
            {
                scheduled.Add(impact);
                Emit("projectile", actor, 1 - actor, label, damage, delay);
            }
        }

        void Apply(Impact p)
        {
            Combatant target = Fighters[p.Target], attacker = Fighters[p.Actor];
            if (!target.Alive) return;
            float armor = p.TrueDamage ? 0 : target.Specimen.Armor;
            if (!p.TrueDamage && target.Stance == Stance.Cautious) armor += .09f;
            if (!p.TrueDamage && target.Stance == Stance.Aggressive) armor -= .06f;
            float damage = p.Damage * (1 - Clamp(armor, 0, .6f));
            if (target.Evasion > 0) damage *= .5f;
            float absorbed = Math.Min(target.Shield, damage);
            target.Shield -= absorbed;
            damage -= absorbed;
            float dealt = Math.Min(Math.Max(0, target.Health), damage);
            target.Health -= damage;
            attacker.DamageDealt += dealt;
            target.HurtFlash = .2f;
            target.Stun = Math.Max(target.Stun, p.Stun);
            target.KnockX += (target.X < attacker.X ? -1 : 1) * p.Knock * 6;
            target.KnockY += (target.Y < attacker.Y ? -1 : 1) * p.Knock;
            Emit("hit", p.Actor, p.Target, absorbed > damage ? "BLOCK " + (int)Math.Round(absorbed) : "-" + (int)Math.Round(damage), damage, .5f);
            if (p.Drain > 0 && attacker.Health > 0) Heal(p.Actor, dealt * p.Drain, "LIFE RETURN");
        }

        void Heal(int i, float amount, string label)
        {
            Combatant f = Fighters[i];
            if (Time > 35) amount *= .5f;
            float actual = Math.Min(amount, Math.Max(0, f.Specimen.Health - f.Health));
            f.Health += actual;
            Emit("heal", i, i, label + " +" + (int)Math.Round(actual), actual, .7f);
        }

        void CheckMutationAndDeath(int i)
        {
            Combatant f = Fighters[i];
            if (!f.MutationUsed && f.Specimen.Mutation == MutationKind.Hollow && f.Health <= f.Specimen.Health * .5f && f.Health > 0)
            {
                f.MutationUsed = true;
                f.Evasion = 2.4f;
                f.X = Clamp(f.X - f.Facing * 15, 8, 92);
                f.Y = -f.Y;
                Emit("mutation", i, i, "HOLLOW // PHASE SHED", 0, 1.2f);
            }
            if (f.Health <= 0 && !f.MutationUsed && f.Specimen.Mutation == MutationKind.Ouroboros)
            {
                f.MutationUsed = true;
                f.Health = f.Specimen.Health * .22f;
                f.Stun = 0;
                f.Shield = 0;
                f.X = Clamp(f.X - f.Facing * 10, 8, 92);
                Emit("mutation", i, i, "OUROBOROS // RECOMPILED", f.Health, 1.5f);
            }
            if (f.Health <= 0 && f.Alive)
            {
                f.Health = 0;
                f.Alive = false;
                f.Windup = 0;
                f.Action = "DELETED";
                Emit("death", 1 - i, i, "PROCESS TERMINATED", 0, 2f);
            }
        }

        void Finish(int winner)
        {
            Winner = winner;
            Finished = true;
            Result = winner < 0 ? "MUTUAL DESTRUCTION" : Fighters[winner].Specimen.Name + " SURVIVES";
            Emit("result", winner, winner, Result, 0, 3);
        }

        void Emit(string kind, int actor, int target, string text, float value, float duration)
        {
            Combatant a = actor >= 0 ? Fighters[actor] : null;
            Combatant t = target >= 0 ? Fighters[target] : null;
            if (Events.Count >= 96) Events.RemoveAt(0);
            Events.Add(new CombatEvent { Id = ++nextEventId, Actor = actor, Target = target, Kind = kind, Text = text,
                X = t == null ? 50 : t.X, Y = t == null ? 0 : t.Y, FromX = a == null ? 50 : a.X,
                FromY = a == null ? 0 : a.Y, Time = Time, Value = value, Duration = duration });
        }

        static float Distance(Combatant a, Combatant b)
        {
            float x = b.X - a.X, y = b.Y - a.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        static float Clamp(float v, float min, float max) { return Math.Max(min, Math.Min(max, v)); }
    }
}
