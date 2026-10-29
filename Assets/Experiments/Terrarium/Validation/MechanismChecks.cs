#if TERRARIUM_HEADLESS
using System;
using System.Globalization;
using System.Text;
using Terrarium.Core;

namespace Terrarium.Validation
{
    // Controlled interventions test mechanisms. They are not evidence of spontaneous stories.
    internal static class MechanismChecks
    {
        public static void Run(Action<bool, string> check)
        {
            CallerConfigurationIsCopied(check);
            KnowledgeIsLocal(check);
            HiddenTruthCannotChangeAChoice(check);
            OwnershipUsesTheSamePhysicalPrimitive(check);
            SocialAttributionNeedsAWitness(check);
            StoragePreservesTheCarriedReserve(check);
            EatingDoesNotDiscardAnUnneededPortion(check);
            DeathLeavesResourcesAtTheDeathSite(check);
            Console.WriteLine("Mechanism checks: local knowledge, stale beliefs, ownership, witness boundaries, conservation and isolated configuration passed.");
        }

        private static void CallerConfigurationIsCopied(Action<bool, string> check)
        {
            var config = new SimulationConfig();
            var a = new Simulation(config, 81);
            var b = new Simulation(new SimulationConfig(), 81);
            config.NeedRateMultiplier = 4;
            config.RegrowthMultiplier = 0;
            a.Step(30); b.Step(30);
            check(a.StateDigest() == b.StateDigest(), "Mutating the caller's configuration must not mutate an existing world.");
        }

        private static void KnowledgeIsLocal(Action<bool, string> check)
        {
            var world = new Simulation(new SimulationConfig(), 81);
            Agent observer = world.Agents[0];
            int unknown = 0;
            foreach (WorldObject obj in world.Objects)
            {
                int distance = Math.Abs(obj.X - observer.X) + Math.Abs(obj.Y - observer.Y);
                if (distance > world.Config.VisionRadius)
                {
                    unknown++;
                    check(!observer.Beliefs.ContainsKey(obj.Id), "Generation must not seed knowledge of an unseen object.");
                }
            }
            check(unknown > 0, "Local-knowledge fixture must include unseen objects.");
            int known = 0;
            foreach (bool cell in observer.KnownTerrain) if (cell) known++;
            check(known < world.Config.Width * world.Config.Height, "An inhabitant must not begin with an omniscient terrain map.");
        }

        private static void HiddenTruthCannotChangeAChoice(Action<bool, string> check)
        {
            var a = HiddenWorld();
            var b = HiddenWorld();
            // Only inaccessible truth changes. The observer's resource and person beliefs stay identical.
            WorldObject target = b.Objects[0];
            target.Stock = 0;
            target.OwnerId = -1;
            target.Nutrition = 0.01;
            b.Agents[1].Inventory.Food = 5;
            b.Agents[1].Needs.Hunger = 1;
            b.Agents[1].X = b.Config.Width - 5;
            a.Step(); b.Step();
            check(a.Agents[0].LastDecision != null && b.Agents[0].LastDecision != null, "Hidden-truth fixture must produce real decisions.");
            check(ChoiceSignature(a.Agents[0]) == ChoiceSignature(b.Agents[0]), "Invisible stock, owner, nutritional properties and remote bodily cues must not alter a decision from identical beliefs.");
            check(a.Agents[0].Beliefs[0].OwnerId == 1 && b.Agents[0].Beliefs[0].OwnerId == 1,
                "An unseen ownership change must not overwrite remembered ownership.");
        }

        private static Simulation HiddenWorld()
        {
            var world = new Simulation(new SimulationConfig { Population = 2, VisionRadius = 2, RegrowthMultiplier = 0, SpoilageRate = 0 }, 67);
            foreach (WorldObject obj in world.Objects)
            {
                obj.X = world.Config.Width - 4;
                obj.Y = world.Config.Height - 4;
                obj.Stock = 0;
            }
            Agent observer = world.Agents[0];
            observer.X = 3; observer.Y = 3;
            world.Tiles[3, 3].Terrain = Terrain.Meadow;
            observer.Beliefs.Clear(); observer.People.Clear(); observer.Relationships.Clear();
            Array.Clear(observer.KnownTerrain, 0, observer.KnownTerrain.Length);
            Array.Clear(observer.Visited, 0, observer.Visited.Length);
            observer.Needs.Hunger = 0.8; observer.Inventory.Food = 0;
            observer.Beliefs[0] = new ObjectBelief { ObjectId = 0, X = world.Config.Width - 4, Y = world.Config.Height - 4,
                Name = "remembered stock", Kind = ObjectKind.Cache, OwnerId = 1, EstimatedStock = 3, Capacity = 4,
                Nutrition = 0.36, Confidence = 0.8, ObservedTick = 0, Properties = Affordance.Nourishment | Affordance.Portable | Affordance.Storage };
            WorldObject resource = world.Objects[0];
            resource.Kind = ObjectKind.Cache; resource.Properties = observer.Beliefs[0].Properties;
            resource.Stock = 3; resource.Capacity = 4; resource.Nutrition = 0.36; resource.OwnerId = 1;
            Agent other = world.Agents[1];
            other.X = world.Config.Width - 4; other.Y = world.Config.Height - 4;
            // A frozen remote resident isolates information availability from other agents' choices.
            other.Alive = false;
            observer.People[1] = new PersonBelief { AgentId = 1, X = other.X, Y = other.Y, ObservedTick = 0,
                ApparentHunger = 0.5, ApparentFood = 1, Confidence = 0.5, Alive = true };
            return world;
        }

        private static string ChoiceSignature(Agent agent)
        {
            var text = new StringBuilder();
            ActionCandidate selected = agent.LastDecision.Chosen;
            text.Append(selected.Kind).Append(':').Append(selected.TargetObjectId).Append(':').Append(selected.TargetAgentId).Append(':')
                .Append(selected.TargetX).Append(':').Append(selected.TargetY);
            foreach (ActionCandidate alternative in agent.LastDecision.Alternatives)
                text.Append('|').Append(alternative.Kind).Append(':').Append(alternative.TargetObjectId).Append(':')
                    .Append(alternative.TargetAgentId).Append(':').Append(alternative.Score.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(alternative.Reason);
            return text.ToString();
        }

        private static void OwnershipUsesTheSamePhysicalPrimitive(Action<bool, string> check)
        {
            var owned = AcquisitionWorld(true, true);
            var unowned = AcquisitionWorld(false, true);
            double before = owned.Objects[0].Stock;
            owned.Step(); unowned.Step();
            Agent actor = owned.Agents[0];
            check(actor.LastDecision.Chosen.Kind == ActionKind.Acquire && unowned.Agents[0].LastDecision.Chosen.Kind == ActionKind.Acquire,
                "Owned and unowned resources must be acquired by the same physical primitive.");
            check(actor.Inventory.Food > 0 && owned.Objects[0].Stock < before,
                "Acquiring another person's stock must execute a physical transfer, not a descriptive event only.");
            check(Math.Abs(actor.Inventory.Food + owned.Objects[0].Stock - before) < 0.000001,
                "Acquisition conserves food when growth and spoilage are disabled.");
            check(owned.Statistics.OtherOwnedAcquisitions == 1 && unowned.Statistics.OtherOwnedAcquisitions == 0,
                "Ownership affects the social interpretation of the same resource transfer.");
        }

        private static void SocialAttributionNeedsAWitness(Action<bool, string> check)
        {
            var seen = AcquisitionWorld(true, true);
            var unseen = AcquisitionWorld(true, false);
            seen.Step(); unseen.Step();
            Relationship seenView, unseenView, reverseView;
            seen.Agents[1].Relationships.TryGetValue(0, out seenView);
            unseen.Agents[1].Relationships.TryGetValue(0, out unseenView);
            seen.Agents[0].Relationships.TryGetValue(1, out reverseView);
            check(seenView != null && (seenView.Trust < 0 || seenView.Affinity < 0),
                "A witnessed loss of one's stock must affect the owner's evaluation of the acquirer.");
            check(unseenView == null || (Math.Abs(unseenView.Trust) < 0.000001 && Math.Abs(unseenView.Affinity) < 0.000001),
                "An absent owner must not know whom to blame for an unseen transfer.");
            check(reverseView == null || Math.Abs(reverseView.Trust - seenView.Trust) > 0.000001 || Math.Abs(reverseView.Affinity - seenView.Affinity) > 0.000001,
                "Evaluations of a witnessed transfer must not force both relationship directions to match.");
        }

        private static Simulation AcquisitionWorld(bool owned, bool witnessed)
        {
            var world = new Simulation(new SimulationConfig { Population = 2, VisionRadius = 2, RegrowthMultiplier = 0,
                SpoilageRate = 0, NeedRateMultiplier = 0.1 }, 120);
            for (int x = 1; x < world.Config.Width - 1; x++)
            for (int y = 1; y < world.Config.Height - 1; y++) world.Tiles[x, y].Terrain = Terrain.Meadow;
            foreach (WorldObject obj in world.Objects)
            {
                obj.X = world.Config.Width - 3; obj.Y = world.Config.Height - 3;
                obj.Stock = 0; obj.Properties = Affordance.None;
            }
            foreach (Agent agent in world.Agents)
            {
                agent.Beliefs.Clear(); agent.People.Clear(); agent.Relationships.Clear(); agent.Memories.Clear();
                agent.Needs = new Needs(); agent.Inventory = new Inventory(); agent.HomeObjectId = -1;
                agent.Traits = new Dispositions { FoodWeight = 4, RestWeight = 2, RiskTolerance = 1, ReserveTarget = 3 };
                Array.Clear(agent.KnownTerrain, 0, agent.KnownTerrain.Length);
                Array.Clear(agent.Visited, 0, agent.Visited.Length);
            }
            Agent actor = world.Agents[0], owner = world.Agents[1];
            actor.X = 8; actor.Y = 8; actor.Needs.Hunger = 0.9;
            owner.X = witnessed ? 9 : 28; owner.Y = witnessed ? 8 : 18;
            owner.Needs.Fatigue = 0.9; owner.Traits.OwnershipRespect = 0.8;
            WorldObject resource = world.Objects[0];
            resource.X = 8; resource.Y = 8; resource.Kind = ObjectKind.Cache;
            resource.Properties = Affordance.Nourishment | Affordance.Portable | Affordance.Storage;
            resource.Stock = 4; resource.Capacity = 4; resource.Nutrition = 0.36; resource.OwnerId = owned ? 1 : -1;
            return world;
        }

        private static void StoragePreservesTheCarriedReserve(Action<bool, string> check)
        {
            var world = AcquisitionWorld(true, false);
            Agent actor = world.Agents[0];
            world.Agents[1].Alive = false;
            actor.Needs = new Needs(); actor.Inventory.Food = 4; actor.Traits.ReserveTarget = 3;
            world.Objects[0].OwnerId = actor.Id; world.Objects[0].Stock = 0; world.Objects[0].Capacity = 7;
            world.Step();
            check(actor.LastDecision.Chosen.Kind == ActionKind.Deposit && actor.LastDecision.Completed,
                "The controlled surplus fixture must perform a real deposit.");
            check(actor.Inventory.Food >= actor.Traits.ReserveTarget && world.Objects[0].Stock > 0,
                "Storing a surplus must preserve the carried reserve that motivated the choice.");
            check(Math.Abs(actor.Inventory.Food + world.Objects[0].Stock - 4) < 0.000001,
                "Depositing conserves food when spoilage is disabled.");
            world.Step();
            check(actor.LastDecision.Chosen.Kind != ActionKind.Acquire || actor.LastDecision.Chosen.TargetObjectId != 0,
                "A surplus deposit must not immediately create a need to reclaim that same stock.");
        }

        private static void EatingDoesNotDiscardAnUnneededPortion(Action<bool, string> check)
        {
            var world = AcquisitionWorld(false, false);
            Agent actor = world.Agents[0];
            world.Agents[1].Alive = false;
            actor.Needs = new Needs { Hunger = 0.2 }; actor.Inventory.Food = 3;
            world.Objects[0].Properties = Affordance.None;
            world.Step();
            check(actor.LastDecision.Chosen.Kind == ActionKind.Eat && actor.Needs.Hunger < 0.000001,
                "The partial-meal fixture must satisfy hunger through the normal eating primitive.");
            check(world.Statistics.FoodConsumed > 0 && world.Statistics.FoodConsumed < 0.75,
                "A deficit smaller than a full meal must not silently destroy a whole food unit.");
            check(Math.Abs(actor.Inventory.Food + world.Statistics.FoodConsumed - 3) < 0.000001,
                "Only the food actually consumed leaves inventory.");
        }

        private static void DeathLeavesResourcesAtTheDeathSite(Action<bool, string> check)
        {
            var world = AcquisitionWorld(false, false);
            Agent resident = world.Agents[0];
            world.Agents[1].Alive = false;
            foreach (WorldObject obj in world.Objects) obj.Stock = 0;
            resident.Health = 0.000001;
            resident.Needs.Hunger = resident.Needs.Thirst = 1;
            resident.Inventory.Food = 1.25; resident.Inventory.Materials = 0.75;
            WorldObject home = world.Objects[1];
            home.OwnerId = resident.Id; home.Stock = 0.2; home.Capacity = 7;
            home.Properties = Affordance.Nourishment | Affordance.Portable | Affordance.Storage;
            resident.HomeObjectId = home.Id;
            int priorCount = world.Objects.Count;
            world.Step();
            check(!resident.Alive && world.Statistics.Deaths == 1, "Sustained-deprivation fixture must actually die through physiology.");
            check(Math.Abs(home.Stock - 0.2) < 0.000001, "Death must not teleport carried resources to a remote home.");
            double droppedFood = 0, droppedMaterials = 0;
            for (int i = priorCount; i < world.Objects.Count; i++)
            {
                WorldObject drop = world.Objects[i];
                check(drop.X == resident.X && drop.Y == resident.Y, "Death drops remain at the physical death location.");
                if (drop.Has(Affordance.Nourishment)) droppedFood += drop.Stock;
                if (drop.Has(Affordance.Material)) droppedMaterials += drop.Stock;
            }
            check(Math.Abs(droppedFood - 1.25) < 0.000001 && Math.Abs(droppedMaterials - 0.75) < 0.000001 && resident.Inventory.Total == 0,
                "Death conserves carried food and material as accessible local resources.");
            int afterCount = world.Objects.Count;
            world.Step(3);
            check(afterCount <= priorCount + 2 && world.Objects.Count == afterCount, "Each death creates at most two drops, once.");
        }
    }
}
#endif
