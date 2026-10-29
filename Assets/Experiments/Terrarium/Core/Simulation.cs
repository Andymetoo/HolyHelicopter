using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Terrarium.Core
{
    /// <summary>The complete world. No clocks, rendering calls, global random stream or Unity objects.</summary>
    public sealed partial class Simulation
    {
        public SimulationConfig Config { get; private set; }
        public int Seed { get; private set; }
        public int Tick { get; private set; }
        public Tile[,] Tiles { get; private set; }
        public List<WorldObject> Objects { get; private set; }
        public List<Agent> Agents { get; private set; }
        public List<WorldEvent> Events { get; private set; }
        public SimulationStatistics Statistics { get; private set; }
        public WeatherState Weather { get; private set; }
        private readonly DeterministicRandom random;
        private readonly double weatherPhase;
        private int nextEventId;
        private static readonly int[] DirectionX = { 0, 1, 0, -1 };
        private static readonly int[] DirectionY = { -1, 0, 1, 0 };

        public Simulation(SimulationConfig config, int seed)
        {
            if (config == null) throw new ArgumentNullException("config");
            config.Validate();
            Config = config.Copy();
            Seed = seed;
            random = new DeterministicRandom(seed);
            weatherPhase = random.Range(0, Math.PI * 2);
            Tiles = new Tile[Config.Width, Config.Height];
            Objects = new List<WorldObject>();
            Agents = new List<Agent>();
            Events = new List<WorldEvent>();
            Statistics = new SimulationStatistics();
            Weather = new WeatherState();
            GenerateWorld();
            UpdateWeather();
            for (int i = 0; i < Agents.Count; i++) Perceive(Agents[i]);
            AddEvent(EventKind.Environment, null, null, null, "The world begins. Resources, geography and individual weights are seeded; subsequent social events are consequences of choices.", 0, -1);
        }

        public WorldObject GetObject(int id) { return id >= 0 && id < Objects.Count ? Objects[id] : null; }
        public Agent GetAgent(int id) { return id >= 0 && id < Agents.Count ? Agents[id] : null; }
        public void Step(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException("ticks");
            for (int i = 0; i < ticks; i++) Step();
        }

        public void Step()
        {
            Tick++;
            UpdateWeather();
            UpdateEnvironment();
            // Rotating first mover makes the update order reproducible without granting one resident permanent priority.
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent agent = Agents[(Tick + i) % Agents.Count];
                if (!agent.Alive) continue;
                UpdateBody(agent);
                if (!agent.Alive) continue;
                Perceive(agent);
                Act(agent);
            }
        }

        private void GenerateWorld()
        {
            int centerX = Config.Width / 2, centerY = Config.Height / 2;
            for (int y = 0; y < Config.Height; y++)
            for (int x = 0; x < Config.Width; x++)
            {
                bool border = x == 0 || y == 0 || x == Config.Width - 1 || y == Config.Height - 1;
                double fertility = random.Range(0.45, 1);
                Terrain terrain = fertility > 0.7 ? Terrain.Soil : Terrain.Meadow;
                if (border || (Distance(x, y, centerX, centerY) > 4 && random.Chance(0.085))) terrain = Terrain.Rock;
                Tiles[x, y] = new Tile { X = x, Y = y, Terrain = terrain, Fertility = fertility };
            }
            // Small ponds, with natural drinking points on their walkable banks.
            int pondCount = Math.Max(2, Config.Width * Config.Height / 260);
            for (int p = 0; p < pondCount; p++)
            {
                int x = 3 + random.Next(Config.Width - 6), y = 3 + random.Next(Config.Height - 6);
                if (Distance(x, y, centerX, centerY) < 5) x = 2;
                Tiles[x, y].Terrain = Terrain.Water;
                Tiles[x + 1, y].Terrain = Terrain.Water;
                Tiles[x, y + 1].Terrain = Terrain.Water;
                Tiles[x - 1, y].Terrain = Terrain.Meadow;
                AddObject(ObjectKind.Spring, "water bank " + (p + 1), x - 1, y,
                    Affordance.Hydration | Affordance.Renewable, 999, 999, 1, 0, 0.72, 0, 0);
            }
            // A central spring lets the initial settlement form around access rather than forced social pairings.
            Tiles[centerX, centerY].Terrain = Terrain.Meadow;
            AddObject(ObjectKind.Spring, "central seep", centerX, centerY,
                Affordance.Hydration | Affordance.Renewable, 999, 999, 1, 0, 0.72, 0, 0);
            EnsureConnectedFloor(centerX, centerY);
            int patches = Math.Max(8, (int)Math.Round(Config.Population * 1.75 * Config.ResourceAbundance));
            for (int i = 0; i < patches; i++)
            {
                int x, y;
                FindFreePosition(out x, out y, i < patches / 2);
                double capacity = random.Range(2.8, 5.5);
                WorldObject patch = AddObject(ObjectKind.BerryPatch, "fruit patch " + (i + 1), x, y,
                    Affordance.Nourishment | Affordance.Portable | Affordance.Renewable | Affordance.Cultivable,
                    capacity * random.Range(0.55, 1), capacity, random.Range(0.019, 0.033), 0.36, 0, 0, 0);
                patch.Condition = random.Range(0.65, 1);
            }
            int groves = Math.Max(6, Config.Population / 2);
            for (int i = 0; i < groves; i++)
            {
                int x, y; FindFreePosition(out x, out y, false);
                AddObject(ObjectKind.Grove, "fallen branches " + (i + 1), x, y,
                    Affordance.Material | Affordance.Portable | Affordance.Renewable,
                    random.Range(2, 4), 5, 0.009, 0, 0, 1, 0);
            }
            string[] names = { "Ari", "Bela", "Cato", "Dara", "Eli", "Fenn", "Gala", "Hale", "Ira", "Jori", "Kest", "Luma", "Miri", "Neri", "Oren", "Pax", "Quill", "Rami", "Sola", "Tavi", "Una", "Vale", "Wren", "Xari", "Yara", "Zev", "Asta", "Bryn", "Cyra", "Davi", "Enna", "Faro", "Giri", "Hana", "Iven", "Juno", "Kira", "Lio", "Mora", "Nilo" };
            for (int i = 0; i < Config.Population; i++)
            {
                int x, y; FindFreePosition(out x, out y, true);
                Agent agent = new Agent {
                    Id = i, Name = names[i], X = x, Y = y,
                    KnownTerrain = new bool[Config.Width * Config.Height],
                    Visited = new bool[Config.Width * Config.Height],
                    TerrainMemory = new Terrain[Config.Width * Config.Height],
                    Needs = new Needs { Hunger = random.Range(0.12, 0.45), Thirst = random.Range(0.08, 0.4), Fatigue = random.Range(0.08, 0.35), Loneliness = random.Range(0.1, 0.5), Exposure = 0.1, Uncertainty = random.Range(0.15, 0.45) },
                    Traits = new Dispositions {
                        FoodWeight = random.Range(0.85, 1.25), WaterWeight = random.Range(0.95, 1.3), RestWeight = random.Range(0.7, 1.2),
                        SocialWeight = random.Range(0.45, 1.5), ComfortWeight = random.Range(0.6, 1.25), Curiosity = random.Range(0.2, 1.1),
                        Consideration = random.Range(0.1, 1), OwnershipRespect = random.Range(0.15, 1), RiskTolerance = random.Range(0.1, 0.9),
                        AttachmentRate = random.Range(0.015, 0.07), ReserveTarget = random.Range(1.2, 3.5)
                    }
                };
                agent.Inventory.Food = random.Range(0.65, 1.4);
                Agents.Add(agent);
                WorldObject cache = AddObject(ObjectKind.Cache, agent.Name + "'s stock", x, y,
                    Affordance.Nourishment | Affordance.Portable | Affordance.Storage, 0, 7, 0, 0.36, 0, 0, 0);
                cache.OwnerId = agent.Id;
                agent.HomeObjectId = cache.Id;
            }
        }

        private void FindFreePosition(out int x, out int y, bool central)
        {
            for (int attempt = 0; attempt < Config.Width * Config.Height * 2; attempt++)
            {
                x = central ? Config.Width / 2 + random.Next(9) - 4 : 1 + random.Next(Config.Width - 2);
                y = central ? Config.Height / 2 + random.Next(9) - 4 : 1 + random.Next(Config.Height - 2);
                x = Math.Max(1, Math.Min(Config.Width - 2, x)); y = Math.Max(1, Math.Min(Config.Height - 2, y));
                if (!Tiles[x, y].Walkable) continue;
                bool occupied = false;
                for (int i = 0; i < Objects.Count; i++) if (Objects[i].X == x && Objects[i].Y == y) { occupied = true; break; }
                if (!occupied) return;
            }
            // Very dense valid custom configurations may share a cell; objects themselves are not obstacles.
            x = Config.Width / 2; y = Config.Height / 2;
        }

        private void EnsureConnectedFloor(int startX, int startY)
        {
            bool[] reached = new bool[Config.Width * Config.Height];
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(Index(startX, startY)); reached[Index(startX, startY)] = true;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(), x = at % Config.Width, y = at / Config.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DirectionX[d], ny = y + DirectionY[d];
                    if (!Inside(nx, ny) || !Tiles[nx, ny].Walkable || reached[Index(nx, ny)]) continue;
                    reached[Index(nx, ny)] = true; queue.Enqueue(Index(nx, ny));
                }
            }
            for (int y = 1; y < Config.Height - 1; y++)
            for (int x = 1; x < Config.Width - 1; x++)
                if (Tiles[x, y].Walkable && !reached[Index(x, y)]) Tiles[x, y].Terrain = Terrain.Rock;
            // Relocate a disconnected generated drinking bank to the connected settlement.
            for (int i = 0; i < Objects.Count; i++)
                if (!Tiles[Objects[i].X, Objects[i].Y].Walkable) { Objects[i].X = startX; Objects[i].Y = startY; }
        }

        private WorldObject AddObject(ObjectKind kind, string name, int x, int y, Affordance properties,
            double stock, double capacity, double regrowth, double nutrition, double hydration, double material, double protection)
        {
            WorldObject obj = new WorldObject { Id = Objects.Count, Kind = kind, Name = name, X = x, Y = y, Properties = properties,
                Stock = stock, Capacity = capacity, Regrowth = regrowth, Nutrition = nutrition, Hydration = hydration, Material = material, Protection = protection };
            Objects.Add(obj); return obj;
        }

        private void UpdateWeather()
        {
            double day = Tick / (double)Config.DayLength;
            Weather.Cold = Clamp(0.38 + 0.3 * Math.Cos(day * Math.PI * 2) + 0.14 * Math.Sin(day * 0.83 + weatherPhase));
            Weather.Rain = Clamp(0.36 + 0.42 * Math.Sin(day * 1.21 + weatherPhase));
            Weather.Description = Weather.Cold > 0.6 ? (Weather.Rain > 0.5 ? "Cold rain" : "Cold") : (Weather.Rain > 0.5 ? "Rain" : "Mild");
            if (Tick > 0 && Tick % Config.DayLength == 0)
                AddEvent(EventKind.Environment, null, null, null, "Day " + (Tick / Config.DayLength + 1) + ": " + Weather.Description + "; rain influences regrowth, cold increases exposure.", 0, -1);
        }

        private void UpdateEnvironment()
        {
            for (int i = 0; i < Objects.Count; i++)
            {
                WorldObject obj = Objects[i];
                if (obj.Has(Affordance.Renewable))
                {
                    double growth = obj.Regrowth * Config.RegrowthMultiplier;
                    if (obj.Has(Affordance.Cultivable))
                    {
                        growth *= (0.42 + Weather.Rain * 0.85) * (0.35 + obj.Condition * 0.65) * (0.65 + Tiles[obj.X, obj.Y].Fertility * 0.35);
                        obj.Condition = Clamp(obj.Condition + 0.0005 * Weather.Rain);
                    }
                    obj.Stock = Math.Min(obj.Capacity, obj.Stock + growth);
                }
                if (obj.Has(Affordance.Storage) && obj.Stock > 0)
                {
                    double spoiled = Math.Min(obj.Stock, obj.Stock * Config.SpoilageRate * (1 - ActualProtection(obj.X, obj.Y) * 0.5));
                    obj.Stock -= spoiled; Statistics.FoodSpoiled += spoiled;
                }
                if (obj.Has(Affordance.Shelter)) obj.Condition = Math.Max(0.35, obj.Condition - 0.000025);
            }
        }

        private void UpdateBody(Agent agent)
        {
            Needs n = agent.Needs;
            double rate = Config.NeedRateMultiplier;
            n.Hunger = Clamp(n.Hunger + 0.0048 * rate);
            n.Thirst = Clamp(n.Thirst + (0.0056 + (1 - Weather.Cold) * 0.001) * rate);
            n.Fatigue = Clamp(n.Fatigue + 0.0025 * rate);
            n.Loneliness = Clamp(n.Loneliness + 0.0032 * rate);
            n.Uncertainty = Clamp(n.Uncertainty + 0.0015 * rate);
            double protection = ActualProtection(agent.X, agent.Y);
            n.Exposure = Clamp(n.Exposure + ((Weather.Cold * 0.0045 + Weather.Rain * 0.0015) * (1 - protection) - 0.0015) * rate);
            double spoiled = agent.Inventory.Food * Config.SpoilageRate;
            agent.Inventory.Food -= spoiled; Statistics.FoodSpoiled += spoiled;
            double harm = Math.Max(0, n.Hunger - 0.93) * 0.035 + Math.Max(0, n.Thirst - 0.93) * 0.055 + Math.Max(0, n.Exposure - 0.95) * 0.015;
            agent.Health = Clamp(agent.Health - harm + (n.Hunger < 0.65 && n.Thirst < 0.65 ? 0.0008 : 0));
            foreach (Relationship relation in agent.Relationships.Values)
            {
                relation.Familiarity *= 0.9998;
                relation.Attachment *= 0.99994;
            }
            if (agent.Health > 0) return;
            agent.Alive = false; agent.Intention = null; agent.Activity = "Died from unmet physical needs";
            Statistics.Deaths++;
            WorldObject cache = GetObject(agent.HomeObjectId);
            // A simplified estate rule releases ownership, but never transports possessions.
            if (cache != null) cache.OwnerId = -1;
            double droppedFood = agent.Inventory.Food, droppedMaterials = agent.Inventory.Materials;
            WorldObject foodDrop = null, materialDrop = null;
            if (droppedFood > 0)
                foodDrop = AddObject(ObjectKind.Cache, agent.Name + "'s dropped food", agent.X, agent.Y,
                    Affordance.Nourishment | Affordance.Portable | Affordance.Storage,
                    droppedFood, Math.Max(1, droppedFood), 0, 0.36, 0, 0, 0);
            if (droppedMaterials > 0)
                materialDrop = AddObject(ObjectKind.Grove, agent.Name + "'s dropped materials", agent.X, agent.Y,
                    Affordance.Material | Affordance.Portable,
                    droppedMaterials, Math.Max(1, droppedMaterials), 0, 0, 0, 1, 0);
            // Residents are retained with stable IDs after death; at most two drop objects can be added per resident.
            agent.Inventory.Food = 0; agent.Inventory.Materials = 0;
            WorldEvent death = AddEvent(EventKind.Death, agent, null, null,
                agent.Name + " died after sustained deprivation (hunger " + F(n.Hunger) + ", thirst " + F(n.Thirst) + ", exposure " + F(n.Exposure) +
                "), leaving " + F(droppedFood) + " food and " + F(droppedMaterials) + " materials at this location. The home stock is now unclaimed.",
                0, agent.LastDecision == null ? -1 : agent.LastDecision.EventId);
            int foodSource;
            if (lastFoodSource.TryGetValue(agent.Id, out foodSource)) death.SecondaryCauseEventId = foodSource;
            if (foodDrop != null) foodDrop.LastChangedEventId = death.Id;
            if (materialDrop != null) materialDrop.LastChangedEventId = death.Id;
            if (cache != null) cache.LastChangedEventId = death.Id;
        }

        private void Perceive(Agent agent)
        {
            int radius = Config.VisionRadius;
            for (int y = Math.Max(0, agent.Y - radius); y <= Math.Min(Config.Height - 1, agent.Y + radius); y++)
            for (int x = Math.Max(0, agent.X - radius); x <= Math.Min(Config.Width - 1, agent.X + radius); x++)
            {
                if (Distance(agent.X, agent.Y, x, y) > radius) continue;
                int index = Index(x, y); agent.KnownTerrain[index] = true; agent.TerrainMemory[index] = Tiles[x, y].Terrain;
            }
            agent.Visited[Index(agent.X, agent.Y)] = true;
            for (int i = 0; i < Objects.Count; i++)
            {
                WorldObject obj = Objects[i];
                if (!Visible(agent, obj.X, obj.Y)) continue;
                ObjectBelief previous;
                if (agent.Beliefs.TryGetValue(obj.Id, out previous) && previous.Hearsay && Math.Abs(previous.EstimatedStock - obj.Stock) > 1.1)
                {
                    Statistics.BeliefCorrections++;
                    WorldEvent correction = AddEvent(EventKind.Observation, agent, null, obj,
                        agent.Name + " found " + obj.Name + " at " + F(obj.Stock) + " units; a report suggested " + F(previous.EstimatedStock) + ".", obj.Stock - previous.EstimatedStock, previous.SourceEventId);
                    if (previous.SourceAgentId >= 0)
                    {
                        Relationship rel = Relation(agent, previous.SourceAgentId);
                        rel.Trust = SignedClamp(rel.Trust - 0.04 * previous.Confidence);
                        rel.LastEventId = correction.Id;
                    }
                    Remember(agent, correction, "A resource report did not match direct observation; the report may have aged.", -0.08, previous.EstimatedStock, obj.Stock, previous.SourceAgentId);
                }
                agent.Beliefs[obj.Id] = BeliefFromObject(obj, agent);
            }
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent other = Agents[i];
                if (other.Id == agent.Id || !Visible(agent, other.X, other.Y)) continue;
                // Nearby bodily cues are deliberately coarse; remote residents are never read by the chooser.
                agent.People[other.Id] = new PersonBelief { AgentId = other.Id, X = other.X, Y = other.Y, ObservedTick = Tick,
                    ApparentHunger = Quantize(other.Needs.Hunger), ApparentFatigue = Quantize(other.Needs.Fatigue), ApparentFood = Math.Floor(other.Inventory.Food), Alive = other.Alive, Confidence = 1 };
                Relation(agent, other.Id);
            }
        }

        private ObjectBelief BeliefFromObject(WorldObject obj, Agent observer)
        {
            int knownCause = -1;
            if (Events.Count > 0 && obj.LastChangedEventId >= Events[0].Id)
            {
                int index = obj.LastChangedEventId - Events[0].Id;
                if (index < Events.Count && Events[index].WitnessIds.Contains(observer.Id)) knownCause = obj.LastChangedEventId;
            }
            return new ObjectBelief { ObjectId = obj.Id, X = obj.X, Y = obj.Y, ObservedTick = Tick, Name = obj.Name, Kind = obj.Kind,
                Properties = obj.Properties, OwnerId = obj.OwnerId, EstimatedStock = obj.Stock, Confidence = 1,
                Nutrition = obj.Nutrition, Hydration = obj.Hydration, Material = obj.Material, Protection = obj.Protection * obj.Condition,
                Capacity = obj.Capacity, Regrowth = obj.Regrowth, Condition = obj.Condition, SourceEventId = knownCause };
        }

        private Relationship Relation(Agent agent, int otherId)
        {
            Relationship relation;
            if (!agent.Relationships.TryGetValue(otherId, out relation))
            {
                relation = new Relationship { OtherId = otherId, ExpectedCare = 0.1 };
                agent.Relationships.Add(otherId, relation);
            }
            return relation;
        }

        private double ActualProtection(int x, int y)
        {
            double protection = 0;
            for (int i = 0; i < Objects.Count; i++)
            {
                WorldObject obj = Objects[i];
                if (obj.Has(Affordance.Shelter) && Distance(x, y, obj.X, obj.Y) <= 1)
                    protection = Math.Max(protection, obj.Protection * obj.Condition);
            }
            return protection;
        }

        private bool Inside(int x, int y) { return x >= 0 && y >= 0 && x < Config.Width && y < Config.Height; }
        private int Index(int x, int y) { return y * Config.Width + x; }
        private bool Visible(Agent a, int x, int y) { return Distance(a.X, a.Y, x, y) <= Config.VisionRadius; }
        private static int Distance(int x1, int y1, int x2, int y2) { return Math.Abs(x1 - x2) + Math.Abs(y1 - y2); }
        private static double Clamp(double n) { return Math.Max(0, Math.Min(1, n)); }
        private static double SignedClamp(double n) { return Math.Max(-1, Math.Min(1, n)); }
        private static double Quantize(double n) { return Math.Round(n * 4) / 4; }
        private static string F(double n) { return n.ToString("0.00", CultureInfo.InvariantCulture); }
    }
}
