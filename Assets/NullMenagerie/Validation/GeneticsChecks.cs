using System;
using System.IO;
using System.Text;
using System.Threading;

namespace NullMenagerie.Validation
{
    /// <summary>Dependency-free checks shared by the editor validator and the standalone validation harness.</summary>
    public static class GeneticsChecks
    {
        public static string Run(string scratchDirectory)
        {
            Directory.CreateDirectory(scratchDirectory);
            int checks = 0;
            Action<bool, string> check = (condition, message) =>
            {
                if (!condition) throw new Exception("Genetics check failed: " + message);
                checks++;
            };
            byte[] poem = Encoding.UTF8.GetBytes("Every file contains a monster.\nEvery monster contains a secret.\n");
            FileProfile first = FileAnalyzer.AnalyzeBytes("first.txt", poem);
            FileProfile renamed = FileAnalyzer.AnalyzeBytes("renamed.TXT", poem);
            check(first.Fingerprint == renamed.Fingerprint && first.Seed == renamed.Seed, "identity ignores base name and extension case");
            check(first.Fingerprint.Length == 64, "SHA-256 digest width");
            check(first.Printable == 1 && first.Family == FileFamily.Text, "readable text metrics");
            Specimen a = SpecimenGenerator.Generate(first), b = SpecimenGenerator.Generate(renamed);
            check(a.Name == b.Name && a.Health == b.Health && a.Hue == b.Hue && a.Ability == b.Ability && a.Body == b.Body, "stable specimen generation");
            check(FileAnalyzer.AnalyzeBytes("first.exe", poem).Fingerprint != first.Fingerprint, "extension intentionally participates in genetics");
            byte[] changed = (byte[])poem.Clone(); changed[0] ^= 31;
            check(FileAnalyzer.AnalyzeBytes("first.txt", changed).Fingerprint != first.Fingerprint, "content changes identity");
            FileProfile empty = FileAnalyzer.AnalyzeBytes("empty.mystery", new byte[0]);
            check(empty.SampleBytes == 0 && empty.Size == 0 && empty.Entropy == 0, "empty data is valid");
            check(SpecimenGenerator.Generate(empty).Mutation == MutationKind.Hollow, "empty data creates Hollow anomaly");
            byte[] zeros = new byte[32768];
            FileProfile zero = FileAnalyzer.AnalyzeBytes("zero.bin", zeros);
            check(zero.ZeroFraction == 1 && zero.Repetition == 1 && zero.Compressibility > .95f, "zero structure measured");
            byte[] noise = new byte[32768]; var random = new StableRandom(0x12345);
            for (int i = 0; i < noise.Length; i++) noise[i] = (byte)random.Next();
            FileProfile chaos = FileAnalyzer.AnalyzeBytes("unknown.oddtype", noise);
            check(chaos.Family == FileFamily.Unknown && chaos.Entropy > .98f && chaos.Compressibility < .03f, "unknown random data is a valid chaotic organism");
            check(SpecimenGenerator.Generate(chaos).Ability == AbilityKind.Glitch, "chaotic unknown data has glitch behavior");
            byte[] repetitive = new byte[32768];
            for (int i = 0; i < repetitive.Length; i++) repetitive[i] = (byte)(i % 16);
            FileProfile loop = FileAnalyzer.AnalyzeBytes("loop.bin", repetitive);
            check(loop.Repetition == 1 && loop.Compressibility > .95f, "periodic data detected beyond adjacent runs");
            Specimen[] samples = SpecimenGenerator.Samples();
            check(samples.Length == 6, "six immediately playable starters");
            var bodies = new System.Collections.Generic.HashSet<BodyKind>();
            var abilities = new System.Collections.Generic.HashSet<AbilityKind>();
            foreach (Specimen s in samples)
            {
                bodies.Add(s.Body); abilities.Add(s.Ability);
                check(s.Health > 60 && s.Health < 220 && s.Power >= 9 && s.Power <= 23 && s.Armor >= 0 && s.Armor < .5f && s.Speed > 5 && s.Speed < 18 && s.Cadence > .5f && s.Cadence < 2, "bounded starter stats: " + s.Name);
                check(!string.IsNullOrEmpty(s.OriginDescription) && !string.IsNullOrEmpty(s.AbilityDescription), "explainable genetics: " + s.Name);
            }
            check(bodies.Count == 6 && abilities.Count == 6, "starter silhouette and behavior diversity");

            string file = Path.Combine(scratchDirectory, "genetics-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(file, poem);
                FileProfile disk = FileAnalyzer.Analyze(file);
                check(disk.Fingerprint == FileAnalyzer.AnalyzeBytes("same.bin", poem).Fingerprint, "memory and disk analyzers agree");
                check(Convert.ToBase64String(File.ReadAllBytes(file)) == Convert.ToBase64String(poem), "source file bytes remain unchanged");
                File.SetLastWriteTimeUtc(file, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
                check(FileAnalyzer.Analyze(file).Fingerprint == disk.Fingerprint, "timestamps do not change genetics");
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel(); bool cancelled = false;
                    try { FileAnalyzer.Analyze(file, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                    check(cancelled, "cancellation is respected");
                }
                bool locked = false;
                using (var handle = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try { FileAnalyzer.Analyze(file); } catch (IOException) { locked = true; }
                }
                check(locked, "locked file produces recoverable IO failure");
                using (var handle = new FileStream(file, FileMode.Create, FileAccess.Write))
                {
                    byte[] block = new byte[4096];
                    for (int i = 0; i < block.Length; i++) block[i] = (byte)(i % 251);
                    for (int i = 0; i < 2048; i++) handle.Write(block, 0, block.Length);
                }
                FileProfile large = FileAnalyzer.Analyze(file);
                check(large.Size == 8 * 1024 * 1024 && large.Sampled && large.SampleBytes == FileAnalyzer.SampleBudget, "large input has bounded sample budget");
                using (var handle = new FileStream(file, FileMode.Open, FileAccess.Write))
                { handle.Seek(-1, SeekOrigin.End); handle.WriteByte(255); }
                check(FileAnalyzer.Analyze(file).Fingerprint != large.Fingerprint, "distributed sampling includes final byte");
                bool directory = false, missing = false, network = false, device = false;
                try { FileAnalyzer.Analyze(scratchDirectory); } catch (IOException) { directory = true; }
                try { FileAnalyzer.Analyze(file + ".missing"); } catch (IOException) { missing = true; }
                try { FileAnalyzer.Analyze("\\\\server\\share\\file.bin"); } catch (IOException) { network = true; }
                try { FileAnalyzer.Analyze("\\\\.\\PhysicalDrive0"); } catch (IOException) { device = true; }
                check(directory && missing && network && device, "directories, missing files, network paths, devices rejected");
            }
            finally { if (File.Exists(file)) File.Delete(file); }
            return "PASS genetics: " + checks + " checks (identity, metrics, diversity, empty/unknown/locked/missing inputs, read-only import, cancellation, bounded distributed sampling).";
        }
    }
}
