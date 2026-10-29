#if TERRARIUM_HEADLESS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Terrarium.Core;

namespace Terrarium.Validation
{
    internal static class HeadlessHarness
    {
        private static int assertions;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private sealed class Options
        {
            public int Ticks = 12000, TraceSeed = 7, TraceTicks = 2000;
            public int[] Seeds = { 7, 19, 42, 103, 211, 509, 997, 2026 };
            public string Output = Path.Combine(Path.GetTempPath(), "Terrarium-results");
            public bool SkipStress;
        }

        private sealed class RunResult
        {
            public string Scenario, Digest;
            public int Seed, Ticks, Alive, PositiveDirections, NegativeDirections, AttachedDirections, AsymmetricPairs, ExploredTiles, DistinctActions;
            public long EarlyActivity, LateActivity;
            public long OwnCacheAcquisitions, CacheDeposits, ImmediateCacheReversals;
            public double MeanHealth, MeanHunger, MeanTrust, MeanAffinity, Seconds;
            public SimulationStatistics Stats;
        }

        public static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = Invariant;
            CultureInfo.CurrentUICulture = Invariant;
            string outputDirectory = null;
            try
            {
                Options options = Parse(args);
                Directory.CreateDirectory(options.Output);
                outputDirectory = options.Output;
                File.WriteAllText(Path.Combine(options.Output, "validation-status.txt"), "RUNNING: no completed result yet.\n", Utf8);
                File.WriteAllText(Path.Combine(options.Output, "run-report.md"), "# Terrarium validation in progress\n\nThis invocation has not passed. Older metrics in this directory, if any, belong to an earlier invocation.\n", Utf8);
                var stopwatch = Stopwatch.StartNew();
                Console.WriteLine("Terrarium headless validation: no rendering or Unity runtime.");
                ValidateConfiguration();
                ValidateDeterminism();
                MechanismChecks.Run(Check);
                var results = new List<RunResult>();
                foreach (int seed in options.Seeds)
                {
                    results.Add(Run("baseline", new SimulationConfig(), seed, options.Ticks, options));
                }
                if (!options.SkipStress)
                {
                    int stressTicks = Math.Min(options.Ticks, 12000);
                    var scarce = new SimulationConfig { ResourceAbundance = 0.35, RegrowthMultiplier = 0.45, NeedRateMultiplier = 1.25 };
                    var plentiful = new SimulationConfig { ResourceAbundance = 1.8, RegrowthMultiplier = 1.5 };
                    var noisy = new SimulationConfig { CommunicationAccuracy = 0.2 };
                    var small = new SimulationConfig { Width = 16, Height = 12, Population = 12, EventCapacity = 64, MemoryCapacity = 4, DecisionCapacity = 2 };
                    results.Add(Run("scarcity", scarce, options.Seeds[0], stressTicks, options));
                    results.Add(Run("abundance", plentiful, options.Seeds[0], stressTicks, options));
                    results.Add(Run("noisy_information", noisy, options.Seeds[0], stressTicks, options));
                    results.Add(Run("small_world_bounded_history", small, options.Seeds[0], stressTicks, options));
                }
                var behavioralSignatures = new HashSet<string>();
                foreach (RunResult result in results) if (result.Scenario == "baseline")
                    behavioralSignatures.Add(result.Alive + ":" + result.Stats.Moves + ":" + result.Stats.Conversations + ":" + result.Stats.Gifts + ":" + result.Stats.OtherOwnedAcquisitions + ":" + result.Stats.BeliefCorrections + ":" + result.AsymmetricPairs);
                if (options.Seeds.Length > 1) Check(behavioralSignatures.Count > 1, "Multiple tested baseline seeds must produce differing behavioral measurements, not merely different seed-containing state digests.");
                WriteMetrics(Path.Combine(options.Output, "metrics.csv"), results);
                WriteActionCounts(Path.Combine(options.Output, "action-counts.csv"), results);
                WriteReport(Path.Combine(options.Output, "run-report.md"), results, options, stopwatch.Elapsed.TotalSeconds);
                File.WriteAllText(Path.Combine(options.Output, "validation-status.txt"), "PASS: " + assertions + " assertions; " + results.Count + " measured simulation runs.\n", Utf8);
                Console.WriteLine("PASS: " + assertions + " assertions. Results: " + options.Output);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error);
                if (outputDirectory != null)
                {
                    string failure = "FAIL after " + assertions + " assertions.\n" + error + "\n";
                    try
                    {
                        File.WriteAllText(Path.Combine(outputDirectory, "validation-status.txt"), failure, Utf8);
                        File.WriteAllText(Path.Combine(outputDirectory, "run-report.md"), "# Terrarium validation failed\n\nThis invocation did not pass. Older metrics, if present, are not evidence for this invocation.\n\n```text\n" + failure + "```\n", Utf8);
                    }
                    catch (IOException reportError) { Console.Error.WriteLine("Additionally, the failure report could not be written: " + reportError.Message); }
                }
                return 1;
            }
        }

        private static Options Parse(string[] args)
        {
            var result = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (key == "--skip-stress") { result.SkipStress = true; continue; }
                if (++i >= args.Length) throw new ArgumentException("Missing value for " + key);
                string value = args[i];
                switch (key)
                {
                    case "--ticks": result.Ticks = int.Parse(value, Invariant); break;
                    case "--seeds":
                        string[] parts = value.Split(',');
                        result.Seeds = new int[parts.Length];
                        for (int j = 0; j < parts.Length; j++) result.Seeds[j] = int.Parse(parts[j], Invariant);
                        break;
                    case "--output": result.Output = Path.GetFullPath(value); break;
                    case "--trace-seed": result.TraceSeed = int.Parse(value, Invariant); break;
                    case "--trace-ticks": result.TraceTicks = int.Parse(value, Invariant); break;
                    default: throw new ArgumentException("Unknown option: " + key);
                }
            }
            if (result.Ticks < 100 || result.Ticks > 1000000) throw new ArgumentException("Ticks must be between 100 and 1000000.");
            if (result.TraceTicks < 0) throw new ArgumentException("TraceTicks must be nonnegative.");
            result.TraceTicks = Math.Min(result.TraceTicks, result.Ticks);
            if (result.Seeds.Length < 1 || new HashSet<int>(result.Seeds).Count != result.Seeds.Length) throw new ArgumentException("Provide one or more distinct seeds.");
            return result;
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Unit(double value) { return Finite(value) && value >= -0.000001 && value <= 1.000001; }

        private static void ValidateConfiguration()
        {
            new SimulationConfig().Validate();
            Action<SimulationConfig> invalid = configuration =>
            {
                bool rejected = false;
                try { configuration.Validate(); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Invalid configuration must be rejected.");
            };
            invalid(new SimulationConfig { Width = 0 });
            invalid(new SimulationConfig { Population = 0 });
            invalid(new SimulationConfig { RegrowthMultiplier = double.NaN });
            invalid(new SimulationConfig { CommunicationAccuracy = double.PositiveInfinity });
            invalid(new SimulationConfig { SpoilageRate = -1 });
            invalid(new SimulationConfig { EventCapacity = 1 });
        }

        private static void ValidateDeterminism()
        {
            var a = new Simulation(new SimulationConfig(), 31337);
            var b = new Simulation(new SimulationConfig(), 31337);
            var distraction = new Simulation(new SimulationConfig(), -912);
            Check(a.StateDigest() == b.StateDigest(), "Generation is reproducible.");
            for (int tick = 1; tick <= 1200; tick++)
            {
                a.Step();
                distraction.Step();
                b.Step();
                if (tick % 100 == 0)
                {
                    Check(a.StateDigest() == b.StateDigest(), "Interleaving independent worlds must not change determinism at tick " + tick);
                    Check(EventSignature(a) == EventSignature(b), "Retained event histories must reproduce at tick " + tick);
                }
            }
            var chunked = new Simulation(new SimulationConfig(), 31337);
            chunked.Step(1200);
            Check(a.StateDigest() == chunked.StateDigest(), "One 1200-tick batch matches 1200 single steps.");
            Check(EventSignature(a) == EventSignature(chunked), "Chunking preserves the chronological event stream.");
            Console.WriteLine("Deterministic generation, interleaving, and batch stepping: passed.");
        }

        private static string EventSignature(Simulation simulation)
        {
            var text = new StringBuilder();
            foreach (WorldEvent entry in simulation.Events)
                text.Append(entry.Id).Append('|').Append(entry.Tick).Append('|').Append(entry.Kind).Append('|').Append(entry.Summary).Append('|').Append(entry.CauseEventId).Append('\n');
            return text.ToString();
        }

        private static RunResult Run(string scenario, SimulationConfig configuration, int seed, int ticks, Options options)
        {
            var watch = Stopwatch.StartNew();
            Simulation simulation;
            try { simulation = new Simulation(configuration, seed); ValidateState(simulation); }
            catch (Exception error) { throw new InvalidOperationException("World generation validation failed: " + scenario + " seed=" + seed + " tick=0", error); }
            TraceWriter trace = scenario == "baseline" && seed == options.TraceSeed && options.TraceTicks > 0
                ? new TraceWriter(options.Output, seed, options.TraceTicks) : null;
            var cacheTransfers = new CacheTransferTracker();
            long early = 0, lateStart = 0;
            try
            {
                if (trace != null) trace.Capture(simulation);
                for (int tick = 1; tick <= ticks; tick++)
                {
                    simulation.Step();
                    cacheTransfers.Observe(simulation);
                    if (trace != null && tick <= options.TraceTicks) trace.Capture(simulation);
                    if (tick % 32 == 0 || tick == ticks) ValidateState(simulation);
                    if (tick == ticks / 4) early = simulation.Statistics.SuccessfulActions;
                    if (tick == ticks * 3 / 4) lateStart = simulation.Statistics.SuccessfulActions;
                }
            }
            catch (Exception error)
            {
                using (var failure = new StreamWriter(Path.Combine(options.Output, "failure-state.txt"), false, Utf8))
                {
                    failure.WriteLine(scenario + " seed=" + seed + " tick=" + simulation.Tick);
                    failure.WriteLine(error);
                    foreach (Agent agent in simulation.Agents)
                        failure.WriteLine("Agent " + agent.Id + " " + agent.Name + " alive=" + agent.Alive + " at=" + agent.X + "," + agent.Y + " health=" + agent.Health
                            + " hunger=" + agent.Needs.Hunger + " thirst=" + agent.Needs.Thirst + " fatigue=" + agent.Needs.Fatigue + " exposure=" + agent.Needs.Exposure
                            + " food=" + agent.Inventory.Food + " materials=" + agent.Inventory.Materials + " activity=" + agent.Activity);
                }
                throw new InvalidOperationException("Simulation validation failed: " + scenario + " seed=" + seed + " tick=" + simulation.Tick, error);
            }
            finally { if (trace != null) trace.Dispose(); }
            SimulationStatistics stats = simulation.Statistics;
            var result = new RunResult { Scenario = scenario, Seed = seed, Ticks = ticks, Digest = simulation.StateDigest(), Stats = stats,
                EarlyActivity = early, LateActivity = stats.SuccessfulActions - lateStart, Seconds = watch.Elapsed.TotalSeconds,
                OwnCacheAcquisitions = cacheTransfers.Acquisitions, CacheDeposits = cacheTransfers.Deposits, ImmediateCacheReversals = cacheTransfers.Reversals };
            double trust = 0, affinity = 0;
            int directions = 0;
            var visited = new HashSet<int>();
            foreach (Agent agent in simulation.Agents)
            {
                if (agent.Alive) result.Alive++;
                result.MeanHealth += agent.Health;
                result.MeanHunger += agent.Needs.Hunger;
                for (int cell = 0; cell < agent.Visited.Length; cell++) if (agent.Visited[cell]) visited.Add(cell);
                foreach (Relationship relation in agent.Relationships.Values)
                {
                    directions++; trust += relation.Trust; affinity += relation.Affinity;
                    if (relation.Affinity > 0.15) result.PositiveDirections++;
                    if (relation.Affinity < -0.15) result.NegativeDirections++;
                    if (relation.Attachment > 0.3) result.AttachedDirections++;
                    Relationship reverse;
                    Agent other = simulation.GetAgent(relation.OtherId);
                    if (agent.Id < relation.OtherId && other.Relationships.TryGetValue(agent.Id, out reverse)
                        && (Math.Abs(relation.Affinity - reverse.Affinity) > 0.1 || Math.Abs(relation.Trust - reverse.Trust) > 0.1)) result.AsymmetricPairs++;
                }
            }
            result.MeanHealth /= simulation.Agents.Count;
            result.MeanHunger /= simulation.Agents.Count;
            result.MeanTrust = directions == 0 ? 0 : trust / directions;
            result.MeanAffinity = directions == 0 ? 0 : affinity / directions;
            result.ExploredTiles = visited.Count;
            foreach (long count in stats.ActionCounts.Values) if (count > 0) result.DistinctActions++;
            if (scenario == "baseline" && ticks >= 1000)
            {
                Check(stats.SuccessfulActions > ticks / 2, "Baseline should sustain successful physical or social actions: seed " + seed);
                Check(stats.Moves > simulation.Agents.Count, "Baseline should involve movement: seed " + seed);
                Check(stats.Conversations > 0, "Baseline should produce local communication: seed " + seed);
                Check(result.DistinctActions >= 4, "Baseline should exercise at least four primitives: seed " + seed);
                if (result.Alive > 0) Check(result.LateActivity > 0, "Surviving baseline must remain active late in the run: seed " + seed);
            }
            Console.WriteLine(scenario + " seed=" + seed + " ticks=" + ticks + " alive=" + result.Alive + "/" + simulation.Agents.Count
                + " actions=" + stats.SuccessfulActions + " conversations=" + stats.Conversations + " gifts=" + stats.Gifts
                + " other-owned=" + stats.OtherOwnedAcquisitions + " corrections=" + stats.BeliefCorrections + " seconds=" + F(result.Seconds));
            return result;
        }

        private static void ValidateState(Simulation simulation)
        {
            SimulationConfig config = simulation.Config;
            Check(simulation.Agents.Count == config.Population, "Population list remains stable (dead inhabitants retain identity).");
            Check(simulation.Events.Count <= config.EventCapacity, "Event retention is bounded.");
            Check(Unit(simulation.Weather.Cold) && Unit(simulation.Weather.Rain), "Weather is finite and bounded.");
            int priorEvent = -1, priorTick = -1;
            foreach (WorldEvent entry in simulation.Events)
            {
                Check(entry.Id > priorEvent && entry.Tick >= priorTick && entry.Tick <= simulation.Tick, "Events have unique increasing IDs and chronological timestamps.");
                Check(entry.CauseEventId < entry.Id && entry.SecondaryCauseEventId < entry.Id, "Causal parents precede their consequences.");
                Check(Finite(entry.Amount), "Event amount remains finite.");
                priorEvent = entry.Id; priorTick = entry.Tick;
            }
            var objectIds = new HashSet<int>();
            foreach (WorldObject obj in simulation.Objects)
            {
                Check(objectIds.Add(obj.Id), "Object IDs are unique.");
                Check(obj.X >= 0 && obj.X < config.Width && obj.Y >= 0 && obj.Y < config.Height, "Objects remain inside the world.");
                Check(Finite(obj.Stock) && Finite(obj.Capacity) && obj.Stock >= -0.000001 && obj.Stock <= obj.Capacity + 0.000001, "Object stock respects finite capacity: " + obj.Id);
                Check(Unit(obj.Condition) && Finite(obj.Regrowth), "Object condition/regrowth are finite.");
                Check(obj.OwnerId < config.Population && obj.OwnerId >= -1, "Ownership refers to a valid inhabitant or nobody.");
            }
            foreach (Agent agent in simulation.Agents)
            {
                Check(agent.X >= 0 && agent.X < config.Width && agent.Y >= 0 && agent.Y < config.Height, "Inhabitants remain inside the world.");
                Check(simulation.Tiles[agent.X, agent.Y].Walkable, "Inhabitants occupy walkable terrain.");
                Check(Unit(agent.Health), "Health remains finite and bounded.");
                Needs needs = agent.Needs;
                Check(Unit(needs.Hunger) && Unit(needs.Thirst) && Unit(needs.Fatigue) && Unit(needs.Loneliness) && Unit(needs.Exposure) && Unit(needs.Uncertainty), "Needs remain finite and bounded.");
                Check(Finite(agent.Inventory.Food) && Finite(agent.Inventory.Materials) && agent.Inventory.Food >= -0.000001 && agent.Inventory.Materials >= -0.000001, "Inventory is finite and nonnegative.");
                Check(agent.Inventory.Total <= agent.Inventory.Capacity + 0.000001, "Inventory respects carrying capacity.");
                Check(agent.Memories.Count <= config.MemoryCapacity && agent.Decisions.Count <= config.DecisionCapacity, "Personal history is bounded.");
                Check(agent.KnownTerrain.Length == config.Width * config.Height && agent.Visited.Length == agent.KnownTerrain.Length, "Knowledge grid size is stable.");
                Check(agent.Beliefs.Count <= simulation.Objects.Count && agent.People.Count <= config.Population, "Knowledge has bounded identities.");
                foreach (ObjectBelief belief in agent.Beliefs.Values)
                {
                    Check(belief.ObservedTick <= simulation.Tick && Unit(belief.Confidence) && Finite(belief.EstimatedStock) && belief.EstimatedStock >= -0.000001, "Object belief is a valid dated estimate.");
                }
                foreach (Relationship relation in agent.Relationships.Values)
                {
                    Check(Finite(relation.Trust) && Finite(relation.Affinity) && Unit(relation.Attachment) && Unit(relation.Familiarity)
                        && Finite(relation.ExpectedCare) && Finite(relation.ReciprocityBalance), "Relationship values remain finite and attachments bounded.");
                }
                foreach (DecisionRecord decision in agent.Decisions)
                {
                    Check(decision.Tick <= simulation.Tick && decision.Chosen != null, "Decisions record a valid chosen primitive.");
                    Check(Finite(decision.ExpectedUtility) && Finite(decision.ActualUtility), "Outcome evaluation remains finite.");
                    foreach (ActionCandidate candidate in decision.Alternatives)
                        Check(Finite(candidate.Score) && Finite(candidate.ExpectedValue) && Finite(candidate.TravelCost) && Finite(candidate.RiskCost) && Finite(candidate.SocialCost), "Candidate utility terms remain finite.");
                }
            }
        }

        private static string F(double value) { return value.ToString("0.000", Invariant); }
        private static string Csv(object value)
        {
            if (value == null) return "\"\"";
            string text = Convert.ToString(value, Invariant);
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        private static void Row(TextWriter writer, params object[] values)
        {
            for (int i = 0; i < values.Length; i++) { if (i > 0) writer.Write(','); writer.Write(Csv(values[i])); }
            writer.WriteLine();
        }

        private static void WriteMetrics(string path, List<RunResult> results)
        {
            using (var writer = new StreamWriter(path, false, Utf8))
            {
                Row(writer, "scenario", "seed", "ticks", "alive", "mean_health", "mean_hunger", "decisions", "moves", "successful_actions", "failed_actions", "conversations", "gifts", "other_owned_acquisitions", "witnessed_transfers", "hearsay_beliefs", "belief_corrections", "shelters_built", "tend_actions", "food_harvested", "food_consumed", "food_spoiled", "materials_harvested", "positive_affinity_directions", "negative_affinity_directions", "attached_directions", "asymmetric_pairs", "mean_trust", "mean_affinity", "distinct_action_primitives", "visited_tiles_union", "first_quarter_actions", "last_quarter_actions", "own_cache_acquisitions", "cache_deposits", "immediate_cache_reversals", "state_digest", "wall_seconds");
                foreach (RunResult r in results)
                {
                    SimulationStatistics s = r.Stats;
                    Row(writer, r.Scenario, r.Seed, r.Ticks, r.Alive, F(r.MeanHealth), F(r.MeanHunger), s.Decisions, s.Moves, s.SuccessfulActions, s.FailedActions, s.Conversations, s.Gifts, s.OtherOwnedAcquisitions, s.WitnessedTransfers, s.HearsayBeliefs, s.BeliefCorrections, s.SheltersBuilt, s.TendActions, F(s.FoodHarvested), F(s.FoodConsumed), F(s.FoodSpoiled), F(s.MaterialsHarvested), r.PositiveDirections, r.NegativeDirections, r.AttachedDirections, r.AsymmetricPairs, F(r.MeanTrust), F(r.MeanAffinity), r.DistinctActions, r.ExploredTiles, r.EarlyActivity, r.LateActivity, r.OwnCacheAcquisitions, r.CacheDeposits, r.ImmediateCacheReversals, r.Digest, F(r.Seconds));
                }
            }
        }

        private static void WriteReport(string path, List<RunResult> results, Options options, double seconds)
        {
            using (var writer = new StreamWriter(path, false, Utf8))
            {
                writer.WriteLine("# Terrarium measured headless runs");
                writer.WriteLine();
                writer.WriteLine("Generated by the actual C# simulation with no Unity process. " + assertions + " assertions passed; wall time " + F(seconds) + " seconds. This report records measurements, not claims that a named social behavior has emerged.");
                writer.WriteLine("Runtime: CLR " + Environment.Version + "; " + Environment.OSVersion + ". Repeatability was checked within this runtime; floating-point equality across different runtimes/platforms was not tested.");
                writer.WriteLine();
                writer.WriteLine("| Scenario | Seed | Ticks | Alive | Completed actions | Conversations | Gifts | Other-owned acquisitions | Belief corrections | Asymmetric pairs |");
                writer.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
                foreach (RunResult r in results)
                    writer.WriteLine("| " + r.Scenario + " | " + r.Seed + " | " + r.Ticks + " | " + r.Alive + " | " + r.Stats.SuccessfulActions + " | " + r.Stats.Conversations + " | " + r.Stats.Gifts + " | " + r.Stats.OtherOwnedAcquisitions + " | " + r.Stats.BeliefCorrections + " | " + r.AsymmetricPairs + " |");
                writer.WriteLine();
                writer.WriteLine("Reproducibility checks compare identical seeds after independent worlds are interleaved, and compare one batch against individual steps. Invariant checks sample every 32 ticks and the final tick, covering bounded histories, finite and bounded state, capacity, geography, causal order, and dated beliefs. Targeted mechanism checks are separate from these observational runs.");
                writer.WriteLine("Seed diversity is checked using measured movement, conversations, gifts, other-owned acquisitions, corrections, survival, and directional asymmetry; a seed-containing state digest alone is not considered evidence of behavioral diversity.");
                writer.WriteLine();
                writer.WriteLine("`metrics.csv` includes first/last quarter activity, resource accounting, final state digests, and continuous relationship summaries. Positive/negative directions mean affinity above +0.15/below -0.15; attached directions mean attachment above 0.3; asymmetric pairs differ by more than 0.1 in trust or affinity. These diagnostic thresholds are not behavior triggers.");
                writer.WriteLine("`action-counts.csv` lists recorded counts for every primitive, including zero-count actions, for each scenario and seed.");
                writer.WriteLine("Cache diagnostics count actual completed events. An immediate cache reversal is an acquisition followed by a deposit (or vice versa) at the same personally owned cache within two ticks; this is a churn diagnostic, not a behavior prohibition.");
                writer.WriteLine();
                writer.WriteLine("Trace seed " + options.TraceSeed + " is archived through tick " + options.TraceTicks + " when that seed is in the baseline list. `trace-events.csv` contains every retained event collected each tick in that interval, `trace-decisions.csv` contains completed decision snapshots and candidate reasons, and `trace-relations.csv` samples directional relationships each 100 ticks. Event cause IDs identify explicit prior experiences or information sources; they are a partial causal record, not a proof that the linked event was necessary.");
                writer.WriteLine("`trace-relation-changes.csv` records before/after relationship snapshots when LastEventId changes. Passive decay alone is omitted. These are end-of-tick deltas: multiple interactions during one tick may contribute, and LastEventId is the most recent recorded cause. Blank before-values mean the relation was first seen during that tick.");
                writer.WriteLine();
                writer.WriteLine("Scarcity changes abundance to 0.35, regrowth to 0.45, and need rate to 1.25. Abundance changes abundance to 1.8 and regrowth to 1.5. Noisy information changes communication accuracy to 0.2. Small world uses 16x12, 12 inhabitants, and history capacities 64/4/2. Other settings are defaults. A single matched stress seed does not establish a general effect.");
                writer.WriteLine();
                writer.WriteLine("Unity scene import, rendering, pointer input, scaling, and live performance remain runtime checks. No Unity launch or Play Mode occurred during this validation.");
            }
        }

        private static void WriteActionCounts(string path, List<RunResult> results)
        {
            using (var writer = new StreamWriter(path, false, Utf8))
            {
                Row(writer, "scenario", "seed", "ticks", "primitive", "count");
                foreach (RunResult result in results)
                foreach (ActionKind kind in Enum.GetValues(typeof(ActionKind)))
                {
                    long count;
                    result.Stats.ActionCounts.TryGetValue(kind, out count);
                    Row(writer, result.Scenario, result.Seed, result.Ticks, kind, count);
                }
            }
        }

        private sealed class CacheTransferTracker
        {
            public long Acquisitions, Deposits, Reversals;
            private int lastEventId = -1;
            private readonly Dictionary<int, CacheTransfer> lastTransfer = new Dictionary<int, CacheTransfer>();

            public void Observe(Simulation simulation)
            {
                int start = simulation.Events.Count;
                while (start > 0 && simulation.Events[start - 1].Id > lastEventId) start--;
                for (int i = start; i < simulation.Events.Count; i++)
                {
                    WorldEvent entry = simulation.Events[i];
                    lastEventId = entry.Id;
                    if (entry.ActorId < 0 || entry.ObjectId < 0 || entry.TargetAgentId >= 0) continue;
                    WorldObject obj = simulation.GetObject(entry.ObjectId);
                    if (obj == null || !obj.Has(Affordance.Storage) || obj.OwnerId != entry.ActorId) continue;
                    bool acquisition = entry.Kind == EventKind.Acquire;
                    if (!acquisition && entry.Kind != EventKind.Transfer) continue;
                    if (acquisition) Acquisitions++; else Deposits++;
                    CacheTransfer previous;
                    if (lastTransfer.TryGetValue(entry.ActorId, out previous)
                        && previous.ObjectId == entry.ObjectId && previous.Acquisition != acquisition && entry.Tick - previous.Tick <= 2) Reversals++;
                    lastTransfer[entry.ActorId] = new CacheTransfer { ObjectId = entry.ObjectId, Tick = entry.Tick, Acquisition = acquisition };
                }
            }

            private sealed class CacheTransfer
            {
                public int ObjectId, Tick;
                public bool Acquisition;
            }
        }

        private sealed class TraceWriter : IDisposable
        {
            private readonly StreamWriter events, decisions, relations, relationChanges;
            private readonly HashSet<long> completed = new HashSet<long>();
            private readonly HashSet<int> archivedEvents = new HashSet<int>();
            private readonly Dictionary<long, RelationSnapshot> priorRelations = new Dictionary<long, RelationSnapshot>();
            private int lastEventId = -1;
            private readonly int seed;

            public TraceWriter(string output, int traceSeed, int ticks)
            {
                seed = traceSeed;
                events = new StreamWriter(Path.Combine(output, "trace-events.csv"), false, Utf8);
                decisions = new StreamWriter(Path.Combine(output, "trace-decisions.csv"), false, Utf8);
                relations = new StreamWriter(Path.Combine(output, "trace-relations.csv"), false, Utf8);
                relationChanges = new StreamWriter(Path.Combine(output, "trace-relation-changes.csv"), false, Utf8);
                Row(events, "seed", "id", "tick", "kind", "actor", "target", "object", "x", "y", "amount", "cause", "secondary_cause", "witnesses", "summary");
                Row(decisions, "seed", "agent", "name", "decision_tick", "captured_tick", "event", "chosen", "target_object", "target_agent", "target_x", "target_y", "score", "expected", "actual", "cause", "hunger", "thirst", "fatigue", "loneliness", "exposure", "uncertainty", "context", "reason", "outcome", "alternatives");
                Row(relations, "seed", "tick", "from", "to", "trust", "affinity", "familiarity", "attachment", "expected_care", "reciprocity", "interactions", "last_event");
                Row(relationChanges, "seed", "tick", "from", "to", "event", "previous_event", "trust_before", "trust_after", "affinity_before", "affinity_after", "attachment_before", "attachment_after", "expected_care_before", "expected_care_after", "reciprocity_before", "reciprocity_after");
            }

            public void Capture(Simulation simulation)
            {
                int start = simulation.Events.Count;
                while (start > 0 && simulation.Events[start - 1].Id > lastEventId) start--;
                for (int i = start; i < simulation.Events.Count; i++)
                {
                    WorldEvent entry = simulation.Events[i];
                    if (lastEventId >= 0) Check(entry.Id == lastEventId + 1, "Full trace collection must not skip event IDs.");
                    Check(entry.CauseEventId < 0 || archivedEvents.Contains(entry.CauseEventId), "Trace causal parent must be archived before consequence #" + entry.Id);
                    Check(entry.SecondaryCauseEventId < 0 || archivedEvents.Contains(entry.SecondaryCauseEventId), "Trace secondary parent must be archived before consequence #" + entry.Id);
                    Row(events, seed, entry.Id, entry.Tick, entry.Kind, entry.ActorId, entry.TargetAgentId, entry.ObjectId, entry.X, entry.Y, F(entry.Amount), entry.CauseEventId, entry.SecondaryCauseEventId, string.Join(";", entry.WitnessIds), entry.Summary);
                    archivedEvents.Add(entry.Id);
                    lastEventId = entry.Id;
                }
                foreach (Agent agent in simulation.Agents)
                {
                    foreach (DecisionRecord decision in agent.Decisions)
                    {
                        long key = ((long)agent.Id << 32) | (uint)decision.Tick;
                        if (!decision.Completed || !completed.Add(key)) continue;
                        ActionCandidate choice = decision.Chosen;
                        Needs needs = decision.NeedsAtDecision ?? new Needs();
                        var alternatives = new StringBuilder();
                        foreach (ActionCandidate candidate in decision.Alternatives)
                        {
                            if (alternatives.Length > 0) alternatives.Append(" || ");
                            alternatives.Append(candidate.Kind).Append(" object=").Append(candidate.TargetObjectId).Append(" agent=").Append(candidate.TargetAgentId)
                                .Append(" score=").Append(F(candidate.Score)).Append(" travel=").Append(F(candidate.TravelCost)).Append(" risk=").Append(F(candidate.RiskCost))
                                .Append(" social=").Append(F(candidate.SocialCost)).Append(" : ").Append(candidate.Reason);
                        }
                        Row(decisions, seed, agent.Id, agent.Name, decision.Tick, simulation.Tick, decision.EventId, choice.Kind, choice.TargetObjectId, choice.TargetAgentId,
                            choice.TargetX, choice.TargetY, F(choice.Score), F(decision.ExpectedUtility), F(decision.ActualUtility), choice.CauseEventId,
                            F(needs.Hunger), F(needs.Thirst), F(needs.Fatigue), F(needs.Loneliness), F(needs.Exposure), F(needs.Uncertainty), decision.PerceivedContext, choice.Reason, decision.Outcome, alternatives.ToString());
                    }
                    var keys = new List<int>(agent.Relationships.Keys); keys.Sort();
                    foreach (int key in keys)
                    {
                        Relationship relation = agent.Relationships[key];
                        long relationKey = ((long)agent.Id << 32) | (uint)relation.OtherId;
                        RelationSnapshot before;
                        bool known = priorRelations.TryGetValue(relationKey, out before);
                        if (relation.LastEventId >= 0 && (!known || before.EventId != relation.LastEventId))
                        {
                            Row(relationChanges, seed, simulation.Tick, agent.Id, relation.OtherId, relation.LastEventId, known ? before.EventId : -1,
                                known ? F(before.Trust) : "", F(relation.Trust), known ? F(before.Affinity) : "", F(relation.Affinity),
                                known ? F(before.Attachment) : "", F(relation.Attachment), known ? F(before.ExpectedCare) : "", F(relation.ExpectedCare),
                                known ? F(before.Reciprocity) : "", F(relation.ReciprocityBalance));
                        }
                        if (!known) { before = new RelationSnapshot(); priorRelations.Add(relationKey, before); }
                        before.EventId = relation.LastEventId; before.Trust = relation.Trust; before.Affinity = relation.Affinity;
                        before.Attachment = relation.Attachment; before.ExpectedCare = relation.ExpectedCare; before.Reciprocity = relation.ReciprocityBalance;
                        if (simulation.Tick % 100 == 0)
                            Row(relations, seed, simulation.Tick, agent.Id, relation.OtherId, F(relation.Trust), F(relation.Affinity), F(relation.Familiarity), F(relation.Attachment), F(relation.ExpectedCare), F(relation.ReciprocityBalance), relation.Interactions, relation.LastEventId);
                    }
                }
            }

            private sealed class RelationSnapshot
            {
                public int EventId;
                public double Trust, Affinity, Attachment, ExpectedCare, Reciprocity;
            }

            public void Dispose() { events.Dispose(); decisions.Dispose(); relations.Dispose(); relationChanges.Dispose(); }
        }
    }
}
#endif
