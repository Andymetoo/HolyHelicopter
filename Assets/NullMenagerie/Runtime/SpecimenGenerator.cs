using System;
using System.Globalization;
using System.Text;

namespace NullMenagerie
{
    /// <summary>Version-one genetics: measurements set tradeoffs; the stable fingerprint sets identity and rare mutations.</summary>
    public static class SpecimenGenerator
    {
        public static Specimen Generate(FileProfile p)
        {
            if (p == null) throw new ArgumentNullException("p");
            var random = new StableRandom(p.Seed);
            float mass = Clamp((float)(Math.Log(Math.Max(1, p.Size), 2) / 32.0));
            float order = Clamp(.55f * p.Compressibility + .45f * p.Repetition);
            float chaos = Clamp(.7f * p.Entropy + .3f * p.Transitions);
            float bulk = Clamp(.17f + .57f * mass + .23f * order - .2f * p.Printable);
            float shell = Clamp(.16f + .44f * order + .13f * mass);
            float reach = .2f + .35f * p.Printable + .3f * chaos;
            float aggression = .35f + .35f * p.Transitions;
            BodyKind body;
            AbilityKind ability;
            float hue;
            switch (p.Family)
            {
                case FileFamily.Text:
                    body = p.LineDensity > .42f ? BodyKind.Mantis : BodyKind.Wraith;
                    ability = AbilityKind.Siphon; hue = .45f; bulk -= .12f; reach += .2f; shell -= .1f;
                    break;
                case FileFamily.Image:
                    body = p.Compressibility > .45f ? BodyKind.Crab : BodyKind.Moth;
                    ability = AbilityKind.Patch; hue = .87f; shell += .15f; aggression -= .1f;
                    break;
                case FileFamily.Audio:
                    body = BodyKind.Serpent; ability = AbilityKind.Echo; hue = .13f; reach += .2f; bulk -= .06f;
                    break;
                case FileFamily.Video:
                    body = BodyKind.Beetle; ability = AbilityKind.Ram; hue = .62f; bulk += .18f; shell += .1f; reach -= .12f;
                    break;
                case FileFamily.Executable:
                    body = p.Compressibility > .4f ? BodyKind.Beetle : BodyKind.Mantis;
                    ability = AbilityKind.Ram; hue = .04f; aggression += .25f; reach -= .14f;
                    break;
                case FileFamily.Archive:
                    body = BodyKind.Crab; ability = AbilityKind.Swarm; hue = .74f; bulk += .08f; shell += .1f;
                    break;
                case FileFamily.Document:
                    body = BodyKind.Moth; ability = AbilityKind.Siphon; hue = .53f; reach += .1f; shell -= .05f;
                    break;
                default:
                    body = p.ZeroFraction > .6f || p.Size == 0 ? BodyKind.Wraith : p.Repetition > .6f ? BodyKind.Serpent : BodyKind.Mantis;
                    ability = p.Repetition > .6f ? AbilityKind.Echo : AbilityKind.Glitch; hue = .3f; aggression += .1f;
                    break;
            }
            bulk = Clamp(bulk); shell = Clamp(shell); reach = Clamp(reach); aggression = Clamp(aggression);
            MutationKind mutation = MutationKind.None;
            if (p.Size == 0 || p.ZeroFraction > .92f) mutation = MutationKind.Hollow;
            else if (p.Repetition > .88f && p.Seed % 11 == 0) mutation = MutationKind.Ouroboros;
            else if (p.Seed % 43 == 0) mutation = MutationKind.Crown;
            else if (p.Seed % 31 == 0) mutation = MutationKind.Twin;

            var s = new Specimen
            {
                Source = p, Seed = p.Seed, Body = body, Ability = ability, Mutation = mutation,
                Bulk = bulk, Volatility = chaos,
                // Logarithmic mass gains durability at the cost of movement and attack cadence.
                Health = Round(112 + 76 * bulk - 12 * reach),
                Power = Round(14 + 5 * aggression + 2 * bulk - reach),
                Armor = .04f + .29f * shell,
                Speed = 14.4f - 6.5f * bulk - 1.6f * shell + 1.3f * p.Printable,
                Reach = 9 + 15 * reach,
                // Light readable bodies trade away durability for a genuinely faster strike rhythm.
                Cadence = .62f + .72f * bulk + .08f * reach - .08f * aggression - .1f * p.Printable,
                SpecialCooldown = 7.8f - 1.4f * chaos + .8f * bulk - .9f * p.LineDensity,
                Hue = (hue + (random.Value() - .5f) * .12f + 1) % 1,
                Eyes = 2 + (p.DistinctBytes > 180 ? 2 : 0) + (mutation == MutationKind.Crown ? 3 : 0),
                Horns = aggression > .65f ? 3 : shell > .5f ? 2 : 1,
                Limbs = body == BodyKind.Serpent ? 0 : body == BodyKind.Moth ? 4 : body == BodyKind.Wraith ? 6 : 6 + (p.Repetition > .65f ? 2 : 0)
            };
            if (mutation == MutationKind.Hollow)
            {
                s.Health = Round(s.Health * .94f); s.Speed += 1.1f; s.Armor *= .7f; s.Body = BodyKind.Wraith;
            }
            // Rare forms gain mechanics, not an across-the-board stat upgrade.
            if (mutation == MutationKind.Ouroboros) { s.Health = Round(s.Health * .87f); s.Body = BodyKind.Serpent; s.Limbs = 0; }
            if (mutation == MutationKind.Twin) { s.Power = Round(s.Power * .94f); s.Eyes += 2; }
            if (mutation == MutationKind.Crown) s.Speed *= .94f;
            s.Name = MakeName(ref random, s);
            s.Epithet = Epithet(s);
            SetAbility(s);
            s.MutationDescription = MutationText(mutation);
            s.OriginDescription = Origin(p, s, mass, order);
            return s;
        }

        static string MakeName(ref StableRandom random, Specimen s)
        {
            string[] first = { "VELVET", "GUTTER", "STATIC", "LACUNA", "CINDER", "GHOST", "NACRE", "HEX", "MORROW", "SILT", "FABLE", "PALE", "LITTLE", "CATHODE", "FERAL", "LUMEN" };
            string[][] last = {
                new[] { "CARAPACE", "BASTION", "HUSK", "RELIQUARY" },
                new[] { "RAZOR", "NEEDLE", "SCISSOR", "STILETTO" },
                new[] { "ORACLE", "WITNESS", "GHOST", "LANTERN" },
                new[] { "CLAMP", "MAW", "CITADEL", "SCUTTLE" },
                new[] { "RIBBON", "COIL", "VOW", "OURO" },
                new[] { "MOTH", "CHOIR", "VEIL", "IRIS" }
            };
            return first[random.Range(0, first.Length)] + " " + last[(int)s.Body][random.Range(0, 4)];
        }

        static string Epithet(Specimen s)
        {
            if (s.Mutation == MutationKind.Hollow) return "THE ABSENCE THAT BITES";
            if (s.Mutation == MutationKind.Ouroboros) return "AN END THAT BEGINS AGAIN";
            if (s.Mutation == MutationKind.Twin) return "TWO HEARTS, ONE FILE";
            if (s.Mutation == MutationKind.Crown) return "A CORRUPTED LITTLE MONARCH";
            switch (s.Body)
            {
                case BodyKind.Beetle: return "A FORTRESS WITH AN APPETITE";
                case BodyKind.Mantis: return "ALL ELBOWS AND BAD INTENTIONS";
                case BodyKind.Wraith: return "IT READS BETWEEN YOUR LINES";
                case BodyKind.Crab: return "SOMETHING HIDES INSIDE THE SHELL";
                case BodyKind.Serpent: return "A SONG WITH TOO MANY TEETH";
                default: return "THE LIGHT AT THE END OF THE FILE";
            }
        }

        static void SetAbility(Specimen s)
        {
            switch (s.Ability)
            {
                case AbilityKind.Ram: s.AbilityName = "EXECUTE / RAM"; s.AbilityDescription = "Winds up, then charges through its rival. Heavy impact, knockback and a brief stun."; break;
                case AbilityKind.Echo: s.AbilityName = "RECURSIVE ECHO"; s.AbilityDescription = "Three delayed sonic pulses chase the rival. Each pulse delivers a separate impact."; break;
                case AbilityKind.Swarm: s.AbilityName = "UNPACK SWARM"; s.AbilityDescription = "The shell opens and releases a fan of hungry byte projectiles. A sequence of small hits."; break;
                case AbilityKind.Patch: s.AbilityName = "RESTORE POINT"; s.AbilityDescription = "Repairs its own body and raises a temporary protective shell. A defensive recovery window."; break;
                case AbilityKind.Siphon: s.AbilityName = "READ / CONSUME"; s.AbilityDescription = "Links to its rival and steals vitality. Damage becomes self-repair."; break;
                default: s.AbilityName = "BAD ADDRESS"; s.AbilityDescription = "Blinks behind its rival, strikes and disrupts its rhythm. Chaotic data refuses to stay put."; break;
            }
        }

        static string MutationText(MutationKind mutation)
        {
            switch (mutation)
            {
                case MutationKind.Crown: return "CROWN // A rare fingerprint crowns this specimen. Starts with a protective shield; slightly slower.";
                case MutationKind.Twin: return "TWIN // A rare fingerprint grew a second heart. Every third basic strike echoes; lower base power.";
                case MutationKind.Hollow: return "HOLLOW // Empty or almost entirely zero-filled data. Once below half health, slips away from danger. Frail but fast.";
                case MutationKind.Ouroboros: return "OUROBOROS // An exceptional repeating pattern. Sheds its body at the first lethal hit and returns with a little life. Lower base health.";
                default: return "No anomalous anatomy detected. Ordinary files still have extraordinary teeth.";
            }
        }

        static string Origin(FileProfile p, Specimen s, float mass, float order)
        {
            string size = mass > .7f ? "A deep file gives it bulk, but slows its stride." : mass < .35f ? "A small footprint leaves it light and quick." : "Moderate file mass balances stamina and movement.";
            string structure = order > .65f ? "Repeating, compressible bytes weave a stronger shell." : p.Entropy > .85f ? "High byte entropy feeds volatile attacks and faster specials." : p.Printable > .7f ? "Readable symbols stretch its reach into a siphoning script." : "Its mixed byte structure builds a versatile body.";
            string family = p.Family == FileFamily.Unknown ? "Unrecognized data becomes a wild, format-agnostic organism." : p.Family.ToString() + " ancestry shapes its " + s.AbilityName.ToLowerInvariant() + " instinct.";
            return size + " " + structure + " " + family;
        }

        static float Clamp(float value) { return Math.Max(0, Math.Min(1, value)); }
        static float Round(float value) { return (float)Math.Round(value, 0, MidpointRounding.AwayFromZero); }

        /// <summary>Six reproducible artificial source files, generated in memory; no user's files are bundled or read.</summary>
        public static Specimen[] Samples()
        {
            var output = new Specimen[6];
            var text = new StringBuilder();
            for (int i = 0; i < 180; i++) text.Append("dear future me,\nremember to feed the strange thing in the terminal.\n");
            output[0] = Generate(FileAnalyzer.AnalyzeBytes("letters-to-the-void.txt", Encoding.UTF8.GetBytes(text.ToString())));
            byte[] image = new byte[49152];
            var imageNoise = new StableRandom(0xfacade);
            for (int i = 0; i < image.Length; i++) image[i] = (byte)((i / 128 + (i % 128) / 9 + imageNoise.Next()) & 255);
            output[1] = Generate(FileAnalyzer.AnalyzeBytes("velvet-garden.png", image));
            byte[] audio = new byte[131072];
            for (int i = 0; i < audio.Length; i++) audio[i] = (byte)(128 + 108 * Math.Sin(i * Math.PI / 32));
            output[2] = Generate(FileAnalyzer.AnalyzeBytes("last-lullaby.wav", audio));
            var random = new StableRandom(0x1feead);
            byte[] machine = new byte[98304];
            byte[] opcodes = { 0x55, 0x48, 0x89, 0xe5, 0x48, 0x83, 0xec, 0x20, 0x31, 0xc0, 0x5d, 0xc3 };
            for (int i = 0; i < machine.Length; i++) machine[i] = i % 16 < opcodes.Length ? opcodes[i % 16] : (byte)random.Next();
            machine[0] = (byte)'M'; machine[1] = (byte)'Z';
            output[3] = Generate(FileAnalyzer.AnalyzeBytes("do-not-feed.exe", machine));
            byte[] archive = new byte[196608];
            for (int i = 0; i < archive.Length; i++) archive[i] = (byte)random.Next();
            output[4] = Generate(FileAnalyzer.AnalyzeBytes("attic-of-teeth.zip", archive));
            output[5] = Generate(FileAnalyzer.AnalyzeBytes("nothing-at-all.bin", new byte[0]));
            return output;
        }
    }
}
