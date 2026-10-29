using System;
using System.Collections.Generic;

namespace Terrarium.Core
{
    // All state in this assembly is independent of Unity and advances only in Simulation.Step.
    [Serializable]
    public sealed class SimulationConfig
    {
        public int Width = 36;
        public int Height = 24;
        public int Population = 16;
        public int VisionRadius = 5;
        public int DayLength = 240;
        public int EventCapacity = 1800;
        public int MemoryCapacity = 40;
        public int DecisionCapacity = 24;
        public double ResourceAbundance = 1.0;
        public double RegrowthMultiplier = 1.0;
        public double NeedRateMultiplier = 1.0;
        public double SpoilageRate = 0.0015;
        public double LearningRate = 0.12;
        public double CommunicationAccuracy = 0.82;

        public SimulationConfig Copy() { return (SimulationConfig)MemberwiseClone(); }
        public void Validate()
        {
            if (Width < 16 || Width > 80 || Height < 12 || Height > 60) throw new ArgumentOutOfRangeException("World dimensions must be 16..80 by 12..60.");
            if (Population < 2 || Population > 40) throw new ArgumentOutOfRangeException("Population must be 2..40.");
            if (VisionRadius < 1 || VisionRadius > 12 || DayLength < 40) throw new ArgumentOutOfRangeException("Vision or day length is outside supported bounds.");
            if (EventCapacity < 50 || MemoryCapacity < 4 || DecisionCapacity < 2) throw new ArgumentOutOfRangeException("History capacities are too small.");
            CheckRange(ResourceAbundance, 0.15, 4, "ResourceAbundance");
            CheckRange(RegrowthMultiplier, 0, 5, "RegrowthMultiplier");
            CheckRange(NeedRateMultiplier, 0.1, 5, "NeedRateMultiplier");
            CheckRange(SpoilageRate, 0, 0.1, "SpoilageRate");
            CheckRange(LearningRate, 0, 1, "LearningRate");
            CheckRange(CommunicationAccuracy, 0, 1, "CommunicationAccuracy");
        }
        private static void CheckRange(double value, double min, double max, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name);
        }
    }

    public enum Terrain { Meadow, Soil, Rock, Water }
    [Flags]
    public enum Affordance { None = 0, Nourishment = 1, Hydration = 2, Portable = 4, Material = 8, Shelter = 16, Renewable = 32, Cultivable = 64, Storage = 128 }
    public enum ObjectKind { Spring, BerryPatch, Grove, Cache, Shelter }
    public enum ActionKind { Eat, Drink, Acquire, Deposit, Give, Converse, Rest, Build, Tend, Explore, Wait }
    public enum EventKind { Environment, Acquire, Consume, Transfer, Conversation, Construction, Cultivation, Observation, Evaluation, Death }

    public sealed class Tile
    {
        public int X, Y;
        public Terrain Terrain;
        public double Fertility;
        public bool Walkable { get { return Terrain != Terrain.Rock && Terrain != Terrain.Water; } }
    }

    public sealed class WorldObject
    {
        public int Id, X, Y;
        public string Name;
        public ObjectKind Kind;
        public Affordance Properties;
        public int OwnerId = -1;
        public double Stock, Capacity, Regrowth, Nutrition, Hydration, Material, Protection, Condition = 1;
        public int LastChangedEventId = -1;
        public bool Has(Affordance property) { return (Properties & property) != 0; }
    }

    public sealed class Needs
    {
        // All values are deficits: zero is satisfied, one is urgent.
        public double Hunger, Thirst, Fatigue, Loneliness, Exposure, Uncertainty;
        public Needs Copy() { return (Needs)MemberwiseClone(); }
        public double PhysicalUrgency { get { return Math.Max(Math.Max(Hunger, Thirst), Math.Max(Fatigue, Exposure)); } }
    }

    public sealed class Dispositions
    {
        // Continuous motivational weights, not named character classes.
        public double FoodWeight, WaterWeight, RestWeight, SocialWeight, ComfortWeight, Curiosity;
        public double Consideration, OwnershipRespect, RiskTolerance, AttachmentRate, ReserveTarget;
    }

    public sealed class Inventory
    {
        public double Food, Materials;
        public double Capacity = 6;
        public double Total { get { return Food + Materials; } }
    }

    public sealed class ObjectBelief
    {
        public int ObjectId, X, Y, ObservedTick;
        public string Name;
        public ObjectKind Kind;
        public Affordance Properties;
        public int OwnerId = -1;
        public double EstimatedStock, Confidence, Nutrition, Hydration, Material, Protection;
        public double Capacity, Regrowth, Condition;
        public int SourceAgentId = -1;
        public int SourceEventId = -1;
        public bool Hearsay;
        public bool Has(Affordance property) { return (Properties & property) != 0; }
    }

    public sealed class PersonBelief
    {
        public int AgentId, X, Y, ObservedTick;
        public double ApparentHunger, ApparentFatigue, ApparentFood;
        public bool Alive = true;
        public double Confidence;
        public int SourceEventId = -1;
    }

    public sealed class Relationship
    {
        public int OtherId;
        public double Trust, Affinity, Familiarity, Attachment, ExpectedCare, ReciprocityBalance;
        public int Interactions, LastInteractionTick = -1000, LastEventId = -1;
    }

    public sealed class MemoryRecord
    {
        public int Tick, EventId, SubjectId = -1, ObjectId = -1, CauseEventId = -1;
        public string Summary;
        public double Valence, Expected, Actual;
    }

    public sealed class ActionLearning
    {
        public ActionKind Kind;
        public int Attempts;
        public double ExpectedReturn, LastPredictionError;
    }

    public sealed class ActionCandidate
    {
        public ActionKind Kind;
        public int TargetObjectId = -1, TargetAgentId = -1, TargetX, TargetY;
        public double Score, ExpectedValue, TravelCost, RiskCost, SocialCost, Confidence = 1;
        public int CauseEventId = -1;
        public string Reason;
        public string Label;
        public ActionCandidate Copy() { return (ActionCandidate)MemberwiseClone(); }
    }

    public sealed class DecisionRecord
    {
        public int Tick, AgentId, EventId = -1;
        public Needs NeedsAtDecision;
        public ActionCandidate Chosen;
        public readonly List<ActionCandidate> Alternatives = new List<ActionCandidate>();
        public string PerceivedContext, Outcome;
        public double ExpectedUtility, ActualUtility;
        public bool Completed;
    }

    public sealed class Agent
    {
        public int Id, X, Y;
        public string Name;
        public bool Alive = true;
        public double Health = 1;
        public Needs Needs = new Needs();
        public Dispositions Traits = new Dispositions();
        public Inventory Inventory = new Inventory();
        public readonly Dictionary<int, ObjectBelief> Beliefs = new Dictionary<int, ObjectBelief>();
        public readonly Dictionary<int, PersonBelief> People = new Dictionary<int, PersonBelief>();
        public readonly Dictionary<int, Relationship> Relationships = new Dictionary<int, Relationship>();
        public readonly Dictionary<ActionKind, ActionLearning> Learning = new Dictionary<ActionKind, ActionLearning>();
        public readonly List<MemoryRecord> Memories = new List<MemoryRecord>();
        public readonly List<DecisionRecord> Decisions = new List<DecisionRecord>();
        public bool[] KnownTerrain, Visited;
        public Terrain[] TerrainMemory;
        public ActionCandidate Intention;
        public DecisionRecord LastDecision;
        public int LastActedTick, LastSocialTick = -1000, HomeObjectId = -1;
        public double LastOutcome;
        public string Activity = "Observing";
    }

    public sealed class WorldEvent
    {
        public int Id, Tick;
        public EventKind Kind;
        public int ActorId = -1, TargetAgentId = -1, ObjectId = -1;
        public int X, Y;
        public string Summary;
        public double Amount;
        public int CauseEventId = -1;
        public int SecondaryCauseEventId = -1;
        public readonly List<int> WitnessIds = new List<int>();
    }

    public sealed class SimulationStatistics
    {
        public long Decisions, Moves, SuccessfulActions, FailedActions, Conversations, HearsayBeliefs, BeliefCorrections;
        public long Gifts, OtherOwnedAcquisitions, WitnessedTransfers, SheltersBuilt, TendActions, Deaths;
        public double FoodHarvested, FoodConsumed, FoodSpoiled, MaterialsHarvested;
        public readonly Dictionary<ActionKind, long> ActionCounts = new Dictionary<ActionKind, long>();
    }

    public sealed class WeatherState
    {
        public double Rain, Cold;
        public string Description = "Mild";
    }
}
