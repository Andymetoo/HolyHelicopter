using System;
using System.Collections.Generic;

namespace Terrarium.Core
{
    public sealed partial class Simulation
    {
        private readonly Dictionary<int, int> lastFoodSource = new Dictionary<int, int>();

        private void Execute(Agent agent, ActionCandidate action)
        {
            agent.LastActedTick = Tick;
            WorldObject obj = GetObject(action.TargetObjectId);
            Agent other = GetAgent(action.TargetAgentId);
            switch (action.Kind)
            {
                case ActionKind.Eat:
                {
                    if (agent.Inventory.Food < 0.02) { Finish(agent, false, 0, null, "No edible carried stock remained."); break; }
                    double before = agent.Needs.Hunger, amount = Math.Min(before / 0.36, Math.Min(1, agent.Inventory.Food));
                    agent.Inventory.Food -= amount; agent.Needs.Hunger = Clamp(before - amount * 0.36);
                    Statistics.FoodConsumed += amount;
                    int source; if (!lastFoodSource.TryGetValue(agent.Id, out source)) source = -1;
                    WorldEvent ev = AddEvent(EventKind.Consume, agent, null, null,
                        agent.Name + " ate " + F(amount) + " carried food; hunger " + F(before) + " → " + F(agent.Needs.Hunger) + ".", amount, source);
                    Finish(agent, true, agent.Traits.FoodWeight * Reduction(before, amount * 0.36) * 3.2, ev, "Nourishment reduced hunger.");
                    break;
                }
                case ActionKind.Drink:
                {
                    if (!AtObject(agent, obj) || !obj.Has(Affordance.Hydration) || obj.Stock <= 0) { FailObject(agent, obj, "The remembered hydration affordance was unavailable."); break; }
                    double amount = Math.Min(1, obj.Stock), relief = amount * obj.Hydration;
                    double before = agent.Needs.Thirst; obj.Stock -= amount; agent.Needs.Thirst = Clamp(before - relief);
                    WorldEvent ev = AddEvent(EventKind.Consume, agent, null, obj, agent.Name + " drank at " + obj.Name + "; thirst " + F(before) + " → " + F(agent.Needs.Thirst) + ".", amount, action.CauseEventId);
                    ev.SecondaryCauseEventId = obj.LastChangedEventId; obj.LastChangedEventId = ev.Id;
                    Finish(agent, true, agent.Traits.WaterWeight * Reduction(before, relief) * 3.5, ev, "Hydration stock was consumed and reduced thirst; renewable sources replenish it.");
                    break;
                }
                case ActionKind.Acquire: Acquire(agent, action, obj); break;
                case ActionKind.Deposit:
                {
                    if (!AtObject(agent, obj) || !obj.Has(Affordance.Storage) || obj.OwnerId != agent.Id || agent.Inventory.Food <= agent.Traits.ReserveTarget || obj.Stock >= obj.Capacity - 0.02)
                    { FailObject(agent, obj, "The planned storage opportunity was unavailable."); break; }
                    double amount = Math.Min(1, Math.Min(agent.Inventory.Food - agent.Traits.ReserveTarget, obj.Capacity - obj.Stock));
                    agent.Inventory.Food -= amount; obj.Stock += amount;
                    int source; if (!lastFoodSource.TryGetValue(agent.Id, out source)) source = -1;
                    WorldEvent ev = AddEvent(EventKind.Transfer, agent, null, obj, agent.Name + " stored " + F(amount) + " food in " + obj.Name + "; reserve now " + F(obj.Stock) + ".", amount, source);
                    ev.SecondaryCauseEventId = obj.LastChangedEventId; obj.LastChangedEventId = ev.Id;
                    Finish(agent, true, action.ExpectedValue, ev, "A reserve was established at a remembered location; it remains accessible to others and can spoil.");
                    break;
                }
                case ActionKind.Give: Give(agent, action, other); break;
                case ActionKind.Converse: Converse(agent, action, other); break;
                case ActionKind.Rest:
                {
                    double protection = ActualProtection(agent.X, agent.Y), fatigue = agent.Needs.Fatigue, exposure = agent.Needs.Exposure;
                    double relief = 0.16 + protection * 0.14, comfort = 0.025 + protection * 0.14;
                    agent.Needs.Fatigue = Clamp(fatigue - relief); agent.Needs.Exposure = Clamp(exposure - comfort);
                    double outcome = agent.Traits.RestWeight * Reduction(fatigue, relief) * 2.6 + agent.Traits.ComfortWeight * Reduction(exposure, comfort) * 2.5;
                    Finish(agent, true, outcome, null, "Rest reduced fatigue by " + F(fatigue - agent.Needs.Fatigue) + " and exposure by " + F(exposure - agent.Needs.Exposure) + "; actual protection " + F(protection) + ".");
                    break;
                }
                case ActionKind.Build:
                {
                    if (agent.Inventory.Materials < 2 || ActualProtection(agent.X, agent.Y) > 0 || ShelterCount() >= Config.Width * Config.Height / 24)
                    { Finish(agent, false, 0, null, "Construction could not proceed: insufficient material, an occupied site, or the world's finite shelter capacity."); break; }
                    agent.Inventory.Materials -= 2; agent.Needs.Fatigue = Clamp(agent.Needs.Fatigue + 0.035);
                    WorldObject shelter = AddObject(ObjectKind.Shelter, "windbreak " + (ShelterCount() + 1), agent.X, agent.Y, Affordance.Shelter,
                        0, 0, 0, 0, 0, 0, 0.82);
                    WorldEvent ev = AddEvent(EventKind.Construction, agent, null, shelter, agent.Name + " assembled a shared windbreak from two material units; anyone nearby gains protection and more effective rest.", 2, action.CauseEventId);
                    shelter.LastChangedEventId = ev.Id; Statistics.SheltersBuilt++;
                    Finish(agent, true, action.ExpectedValue, ev, "Materials became a persistent shared protection affordance.");
                    break;
                }
                case ActionKind.Tend:
                {
                    if (!AtObject(agent, obj) || !obj.Has(Affordance.Cultivable)) { FailObject(agent, obj, "The remembered cultivable opportunity was unavailable."); break; }
                    double before = obj.Condition; obj.Condition = Clamp(obj.Condition + 0.42); agent.Needs.Fatigue = Clamp(agent.Needs.Fatigue + 0.015);
                    WorldEvent ev = AddEvent(EventKind.Cultivation, agent, null, obj, agent.Name + " tended " + obj.Name + "; condition " + F(before) + " → " + F(obj.Condition) + ", improving subsequent regrowth for any gatherer.", obj.Condition - before, obj.LastChangedEventId);
                    obj.LastChangedEventId = ev.Id; Statistics.TendActions++;
                    Finish(agent, true, action.ExpectedValue * Math.Min(1, (obj.Condition - before) / 0.3), ev, "An investment changed future growth; the resident estimates its future value, not a guaranteed personal harvest.");
                    break;
                }
                case ActionKind.Explore:
                {
                    double before = agent.Needs.Uncertainty; agent.Needs.Uncertainty = Clamp(before - 0.13);
                    Perceive(agent);
                    Finish(agent, true, action.ExpectedValue, null, "Reached the chosen information frontier and refreshed local beliefs.");
                    break;
                }
                default:
                    Finish(agent, true, 0.001, null, "Remained in place.");
                    break;
            }
        }

        private void Acquire(Agent agent, ActionCandidate action, WorldObject obj)
        {
            if (!AtObject(agent, obj) || !obj.Has(Affordance.Portable) || obj.Stock < 0.04 || agent.Inventory.Total >= agent.Inventory.Capacity - 0.02)
            { FailObject(agent, obj, "The expected portable stock was absent or carrying space was exhausted."); return; }
            double amount = Math.Min(1.2, Math.Min(obj.Stock, agent.Inventory.Capacity - agent.Inventory.Total));
            if (!obj.Has(Affordance.Nourishment) && !obj.Has(Affordance.Material))
            { FailObject(agent, obj, "This object's properties supply no usable carried resource."); return; }
            obj.Stock -= amount;
            if (obj.Has(Affordance.Nourishment)) { agent.Inventory.Food += amount; Statistics.FoodHarvested += obj.Has(Affordance.Renewable) ? amount : 0; }
            else { agent.Inventory.Materials += amount; Statistics.MaterialsHarvested += amount; }
            if (obj.Has(Affordance.Cultivable)) obj.Condition = Clamp(obj.Condition - amount * 0.045);
            int previousChange = obj.LastChangedEventId;
            WorldEvent ev = AddEvent(EventKind.Acquire, agent, null, obj,
                agent.Name + " acquired " + F(amount) + (obj.Has(Affordance.Nourishment) ? " food" : " material") + " from " + obj.Name +
                (obj.OwnerId >= 0 ? " (owned by " + OwnerName(obj.OwnerId) + ")" : " (unclaimed)") + "; stock now " + F(obj.Stock) + ".", amount, action.CauseEventId);
            ev.SecondaryCauseEventId = previousChange;
            obj.LastChangedEventId = ev.Id;
            if (obj.Has(Affordance.Nourishment)) lastFoodSource[agent.Id] = ev.Id;
            double socialOutcomeCost = 0;
            if (obj.OwnerId >= 0 && obj.OwnerId != agent.Id) Statistics.OtherOwnedAcquisitions++;
            // The observer's truth-based metric above is separate from witnesses' fallible attributions.
            for (int w = 0; w < ev.WitnessIds.Count; w++)
            {
                Agent witness = GetAgent(ev.WitnessIds[w]);
                if (witness == null || witness.Id == agent.Id || !witness.Alive) continue;
                ObjectBelief belief;
                if (!witness.Beliefs.TryGetValue(obj.Id, out belief) || belief.OwnerId < 0 || belief.OwnerId == agent.Id) continue;
                double impact = belief.OwnerId == witness.Id ? -(0.15 + witness.Needs.Hunger * 0.28 + Relation(witness, agent.Id).ExpectedCare * 0.1) : -0.045 * witness.Traits.OwnershipRespect;
                EvaluateSocial(witness, agent, impact, ev, "Observed access to a stock believed to belong to " + OwnerName(belief.OwnerId) + ".", false);
                if (belief.OwnerId == witness.Id) socialOutcomeCost += -impact * (0.2 + agent.Traits.OwnershipRespect * 0.3);
                Statistics.WitnessedTransfers++;
            }
            Finish(agent, true, Math.Max(0, action.ExpectedValue * Math.Min(1, amount) - socialOutcomeCost), ev,
                "Portable properties supplied inventory. Observed ownership consequences cost " + F(socialOutcomeCost) + "; remote owners were not notified.");
        }

        private void Give(Agent agent, ActionCandidate action, Agent other)
        {
            if (other == null || !other.Alive || Distance(agent.X, agent.Y, other.X, other.Y) > 1 || agent.Inventory.Food < 0.1 || other.Inventory.Total > other.Inventory.Capacity - 0.05)
            { Finish(agent, false, 0, null, "The intended recipient was absent or could not carry the offer."); return; }
            double amount = Math.Min(0.8, Math.Min(agent.Inventory.Food, other.Inventory.Capacity - other.Inventory.Total));
            agent.Inventory.Food -= amount; other.Inventory.Food += amount;
            double help = (0.12 + other.Needs.Hunger * 0.5) * amount;
            WorldEvent ev = AddEvent(EventKind.Transfer, agent, other, null,
                agent.Name + " passed " + F(amount) + " food to " + other.Name + "; recipient hunger cue at choice was " +
                F(agent.People.ContainsKey(other.Id) ? agent.People[other.Id].ApparentHunger : 0) + ".", amount, action.CauseEventId);
            int source; if (lastFoodSource.TryGetValue(agent.Id, out source)) ev.SecondaryCauseEventId = source;
            lastFoodSource[other.Id] = ev.Id;
            EvaluateSocial(other, agent, help, ev, "Received usable nourishment while hunger was " + F(other.Needs.Hunger) + ".", true);
            EvaluateSocial(agent, other, help * agent.Traits.Consideration * 0.4, ev, "An offer was accepted; its subjective value depended on consideration.", false);
            Relation(other, agent.Id).ReciprocityBalance = Math.Min(2, Relation(other, agent.Id).ReciprocityBalance + help);
            Relation(agent, other.Id).ReciprocityBalance = Math.Max(-2, Relation(agent, other.Id).ReciprocityBalance - help);
            agent.Needs.Loneliness = Clamp(agent.Needs.Loneliness - 0.08); other.Needs.Loneliness = Clamp(other.Needs.Loneliness - 0.07);
            agent.LastSocialTick = Tick; other.LastSocialTick = Tick;
            Statistics.Gifts++;
            Finish(agent, true, action.ExpectedValue, ev, "Food physically moved to another inventory; both residents independently evaluated the exchange.");
        }

        private void Converse(Agent agent, ActionCandidate action, Agent other)
        {
            if (other == null || !other.Alive || Distance(agent.X, agent.Y, other.X, other.Y) > 1)
            { Finish(agent, false, 0, null, "The resident was no longer at the remembered location."); return; }
            Relationship recipientRelation = Relation(other, agent.Id);
            double willingness = other.Traits.SocialWeight * (0.12 + other.Needs.Loneliness * 0.5)
                + recipientRelation.Affinity * 0.1 - Math.Max(0, other.Needs.PhysicalUrgency - 0.75) * 2.2 - Math.Max(0, -recipientRelation.Trust) * 0.25;
            if (willingness <= 0)
            {
                WorldEvent refused = AddEvent(EventKind.Conversation, agent, other, null,
                    agent.Name + " sought contact with " + other.Name + ", who continued attending to other needs (contact value " + F(willingness) + ").", 0, action.CauseEventId);
                EvaluateSocial(agent, other, -0.025 - Relation(agent, other.Id).ExpectedCare * 0.05, refused, "Expected contact was unavailable.", false);
                agent.LastSocialTick = Tick;
                Finish(agent, false, 0, refused, "The other resident's own needs and evaluation made contact unattractive.");
                return;
            }
            double actorBefore = agent.Needs.Loneliness, otherBefore = other.Needs.Loneliness;
            double actorRelief = Math.Min(actorBefore, 0.23 + Relation(agent, other.Id).Familiarity * 0.12);
            double otherRelief = Math.Min(otherBefore, 0.18 + recipientRelation.Familiarity * 0.1);
            agent.Needs.Loneliness = Clamp(actorBefore - actorRelief); other.Needs.Loneliness = Clamp(otherBefore - otherRelief);
            double uncertaintyRelief = Math.Min(agent.Needs.Uncertainty, 0.14);
            agent.Needs.Uncertainty = Clamp(agent.Needs.Uncertainty - 0.14); other.Needs.Uncertainty = Clamp(other.Needs.Uncertainty - 0.1);
            WorldEvent ev = AddEvent(EventKind.Conversation, agent, other, null,
                agent.Name + " and " + other.Name + " exchanged observations", actorRelief, action.CauseEventId);
            string reportA = Communicate(agent, other, ev), reportB = Communicate(other, agent, ev);
            ev.Summary += ". " + reportA + (reportB.Length > 0 ? " " + reportB : "");
            EvaluateSocial(agent, other, actorRelief * agent.Traits.SocialWeight + uncertaintyRelief * agent.Traits.Curiosity * 0.3, ev,
                "Contact relieved loneliness by " + F(actorRelief) + " and provided an opportunity to compare observations.", false);
            EvaluateSocial(other, agent, otherRelief * other.Traits.SocialWeight + 0.015, ev,
                "Contact relieved loneliness by " + F(otherRelief) + "; the same contact had a separately weighted value.", false);
            agent.LastSocialTick = Tick; other.LastSocialTick = Tick;
            Statistics.Conversations++;
            Finish(agent, true, agent.Traits.SocialWeight * Reduction(actorBefore, actorRelief) * 2.1 + agent.Traits.Curiosity * uncertaintyRelief * 0.13, ev,
                "Both residents evaluated contact; accepted reports retain their original observation age and are not world truth.");
        }

        private string Communicate(Agent speaker, Agent listener, WorldEvent ev)
        {
            ObjectBelief chosen = null; double best = double.NegativeInfinity;
            List<int> ids = new List<int>(speaker.Beliefs.Keys); ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                ObjectBelief belief = speaker.Beliefs[ids[i]];
                if (!belief.Has(Affordance.Nourishment) || belief.EstimatedStock <= 0.05) continue;
                double value = belief.EstimatedStock * BeliefConfidence(belief) / (1 + Distance(speaker.X, speaker.Y, belief.X, belief.Y) * 0.04);
                if (value > best) { best = value; chosen = belief; }
            }
            if (chosen == null) return speaker.Name + " had no remembered food opportunity to describe.";
            ObjectBelief existing;
            if (listener.Beliefs.TryGetValue(chosen.ObjectId, out existing) && (existing.ObservedTick > chosen.ObservedTick || (!existing.Hearsay && existing.ObservedTick == chosen.ObservedTick)))
                return speaker.Name + " described " + chosen.Name + ", but " + listener.Name + " retained equally fresh or newer evidence.";
            double trust = Relation(listener, speaker.Id).Trust;
            double confidence = Math.Min(0.92, BeliefConfidence(chosen) * (0.6 + trust * 0.3));
            if (existing != null && BeliefConfidence(existing) > confidence && existing.ObservedTick >= chosen.ObservedTick - 12)
                return listener.Name + " retained stronger evidence about " + chosen.Name + ".";
            double estimate = chosen.EstimatedStock;
            if (!random.Chance(Config.CommunicationAccuracy)) estimate = Math.Max(0, estimate + random.Range(-1.8, 1.8));
            ObjectBelief incoming = new ObjectBelief { ObjectId = chosen.ObjectId, X = chosen.X, Y = chosen.Y, ObservedTick = chosen.ObservedTick,
                Name = chosen.Name, Kind = chosen.Kind, Properties = chosen.Properties, OwnerId = chosen.OwnerId,
                EstimatedStock = estimate, Confidence = confidence, Nutrition = chosen.Nutrition, Hydration = chosen.Hydration,
                Material = chosen.Material, Protection = chosen.Protection, Capacity = chosen.Capacity, Regrowth = chosen.Regrowth, Condition = chosen.Condition,
                Hearsay = true, SourceAgentId = speaker.Id, SourceEventId = ev.Id };
            listener.Beliefs[incoming.ObjectId] = incoming; Statistics.HearsayBeliefs++;
            if (ev.SecondaryCauseEventId < 0 && chosen.SourceEventId >= 0 && chosen.SourceEventId < ev.Id) ev.SecondaryCauseEventId = chosen.SourceEventId;
            return speaker.Name + " reported " + chosen.Name + " at " + F(estimate) + " units (observation age " + (Tick - chosen.ObservedTick) + "); " + listener.Name + " accepted it at confidence " + F(confidence) + ".";
        }

        private void EvaluateSocial(Agent observer, Agent subject, double value, WorldEvent ev, string explanation, bool receivedFood)
        {
            Relationship rel = Relation(observer, subject.Id);
            double expected = rel.ExpectedCare;
            rel.Trust = SignedClamp(rel.Trust + Config.LearningRate * (value - expected) * 0.65);
            rel.Affinity = SignedClamp(rel.Affinity + value * 0.19);
            rel.ExpectedCare = Math.Max(-0.5, Math.Min(0.7, expected + Config.LearningRate * (value - expected)));
            rel.Familiarity = Clamp(rel.Familiarity + 0.035);
            if (value > 0) rel.Attachment = Clamp(rel.Attachment + observer.Traits.AttachmentRate * value * (0.5 + Math.Max(0, rel.Affinity)));
            else rel.Attachment = Clamp(rel.Attachment + value * 0.015);
            rel.Interactions++; rel.LastInteractionTick = Tick;
            int previous = rel.LastEventId; rel.LastEventId = ev.Id;
            Remember(observer, ev, explanation + " Expected care " + F(expected) + ", evaluated value " + F(value) + "; directed trust now " + F(rel.Trust) + ".", value, expected, value, subject.Id, previous);
        }

        private void Finish(Agent agent, bool success, double actual, WorldEvent ev, string outcome)
        {
            ActionCandidate action = agent.Intention;
            if (action == null) return;
            ActionLearning learning;
            if (!agent.Learning.TryGetValue(action.Kind, out learning))
            { learning = new ActionLearning { Kind = action.Kind, ExpectedReturn = 1 }; agent.Learning.Add(action.Kind, learning); }
            double ratio = action.ExpectedValue > 0.001 ? Math.Max(0, Math.Min(1.5, actual / action.ExpectedValue)) : (success ? 1 : 0);
            learning.Attempts++; learning.LastPredictionError = ratio - learning.ExpectedReturn;
            learning.ExpectedReturn = Math.Max(0, learning.ExpectedReturn + Config.LearningRate * learning.LastPredictionError);
            agent.LastOutcome = actual;
            agent.Activity = (success ? "Completed: " : "Reconsidering: ") + action.Label;
            if (agent.LastDecision != null)
            {
                agent.LastDecision.Completed = true; agent.LastDecision.ActualUtility = actual;
                agent.LastDecision.Outcome = outcome + " Learned reliability now " + F(learning.ExpectedReturn) + ".";
                agent.LastDecision.EventId = ev == null ? -1 : ev.Id;
            }
            if (success) Statistics.SuccessfulActions++; else Statistics.FailedActions++;
            long count; Statistics.ActionCounts.TryGetValue(action.Kind, out count); Statistics.ActionCounts[action.Kind] = count + 1;
            if (ev != null) Remember(agent, ev, outcome, success ? actual : -0.08, action.ExpectedValue, actual, action.TargetAgentId);
            agent.Intention = null;
        }

        private bool AtObject(Agent agent, WorldObject obj) { return obj != null && agent.X == obj.X && agent.Y == obj.Y; }
        private void FailObject(Agent agent, WorldObject obj, string explanation)
        {
            WorldEvent ev = AddEvent(EventKind.Evaluation, agent, null, obj, agent.Name + ": " + explanation, 0, agent.Intention == null ? -1 : agent.Intention.CauseEventId);
            Statistics.BeliefCorrections++;
            if (obj != null && Visible(agent, obj.X, obj.Y)) agent.Beliefs[obj.Id] = BeliefFromObject(obj, agent);
            Finish(agent, false, 0, ev, explanation);
        }

        private WorldEvent AddEvent(EventKind kind, Agent actor, Agent other, WorldObject obj, string summary, double amount, int cause)
        {
            WorldEvent ev = new WorldEvent { Id = nextEventId++, Tick = Tick, Kind = kind, ActorId = actor == null ? -1 : actor.Id,
                TargetAgentId = other == null ? -1 : other.Id, ObjectId = obj == null ? -1 : obj.Id,
                X = obj != null ? obj.X : (actor == null ? -1 : actor.X), Y = obj != null ? obj.Y : (actor == null ? -1 : actor.Y),
                Summary = summary, Amount = amount, CauseEventId = cause >= 0 && cause < nextEventId - 1 ? cause : -1 };
            if (ev.X >= 0)
                for (int i = 0; i < Agents.Count; i++) if (Agents[i].Alive && Visible(Agents[i], ev.X, ev.Y)) ev.WitnessIds.Add(Agents[i].Id);
            Events.Add(ev); Trim(Events, Config.EventCapacity); return ev;
        }

        private void Remember(Agent agent, WorldEvent ev, string summary, double valence, double expected, double actual, int subject = -1, int previous = -1)
        {
            agent.Memories.Add(new MemoryRecord { Tick = Tick, EventId = ev.Id, SubjectId = subject, ObjectId = ev.ObjectId,
                Summary = summary, Valence = valence, Expected = expected, Actual = actual, CauseEventId = previous >= 0 ? previous : ev.CauseEventId });
            Trim(agent.Memories, Config.MemoryCapacity);
        }
    }
}
