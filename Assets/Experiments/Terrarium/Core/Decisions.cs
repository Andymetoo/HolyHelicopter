using System;
using System.Collections.Generic;

namespace Terrarium.Core
{
    public sealed partial class Simulation
    {
        private void Act(Agent agent)
        {
            bool reconsider = agent.Intention == null || agent.LastDecision == null || Tick - agent.LastDecision.Tick >= 9;
            if (agent.Intention != null && agent.Needs.PhysicalUrgency > 0.9 && Tick - agent.LastDecision.Tick >= 2) reconsider = true;
            if (reconsider)
            {
                if (agent.LastDecision != null && !agent.LastDecision.Completed)
                {
                    agent.LastDecision.Outcome = "Reconsidered en route as time passed; no outcome yet.";
                    agent.LastDecision.Completed = true;
                }
                Choose(agent);
            }
            ActionCandidate action = agent.Intention;
            if (action == null) return;
            int distance = Distance(agent.X, agent.Y, action.TargetX, action.TargetY);
            int reach = action.Kind == ActionKind.Converse || action.Kind == ActionKind.Give ? 1 : 0;
            if (distance > reach)
            {
                if (!MoveToward(agent, action.TargetX, action.TargetY, reach))
                    Finish(agent, false, 0, null, "No route through remembered terrain; the plan was abandoned.");
                else agent.Activity = "Travelling: " + action.Label;
                return;
            }
            Execute(agent, action);
        }

        private void Choose(Agent agent)
        {
            List<ActionCandidate> candidates = new List<ActionCandidate>();
            Needs n = agent.Needs;
            Dispositions t = agent.Traits;
            ObjectBelief rememberedHome;
            double stored = agent.Beliefs.TryGetValue(agent.HomeObjectId, out rememberedHome) ? rememberedHome.EstimatedStock : 0;
            double provisioning = 0.8 * Clamp((t.ReserveTarget * 1.7 - stored) / Math.Max(0.1, t.ReserveTarget * 1.7));
            double carryTarget = t.ReserveTarget + provisioning + Math.Max(0, n.Hunger - 0.45) * 2;
            double protectionHere = BelievedProtection(agent, agent.X, agent.Y);
            bool localShelter = false;
            foreach (ObjectBelief belief in agent.Beliefs.Values)
                if (belief.Has(Affordance.Shelter) && Distance(agent.X, agent.Y, belief.X, belief.Y) < 5) { localShelter = true; break; }

            if (agent.Inventory.Food >= 0.04 && n.Hunger > 0.02)
            {
                double food = Math.Min(n.Hunger / 0.36, Math.Min(1, agent.Inventory.Food)), relief = Math.Min(n.Hunger, food * 0.36);
                AddCandidate(agent, candidates, ActionKind.Eat, -1, -1, agent.X, agent.Y,
                    t.FoodWeight * Reduction(n.Hunger, relief) * 3.2, 0, 0, 1, -1,
                    "eat carried food", "Hunger " + F(n.Hunger) + "; carried food " + F(agent.Inventory.Food) + "; expected relief " + F(relief) + ".");
            }

            AddRestCandidate(agent, candidates, agent.X, agent.Y, -1, protectionHere, -1);
            // Iterate IDs, not hash-table order: all candidate ties are deterministic and inspectable.
            List<int> knownIds = new List<int>(agent.Beliefs.Keys); knownIds.Sort();
            for (int b = 0; b < knownIds.Count; b++)
            {
                ObjectBelief belief = agent.Beliefs[knownIds[b]];
                int age = Tick - belief.ObservedTick;
                double confidence = BeliefConfidence(belief);
                double estimated = belief.EstimatedStock;
                string evidence = " Belief: " + F(estimated) + " units, age " + age + ", confidence " + F(confidence) + (belief.Hearsay ? ", reported" : ", seen") + ".";
                if (belief.Has(Affordance.Hydration) && estimated > 0.02)
                {
                    double relief = Math.Min(n.Thirst, belief.Hydration);
                    AddCandidate(agent, candidates, ActionKind.Drink, belief.ObjectId, -1, belief.X, belief.Y,
                        t.WaterWeight * Reduction(n.Thirst, relief) * 3.5, 0, 0, confidence, belief.SourceEventId,
                        "drink at " + belief.Name, "Thirst " + F(n.Thirst) + "; hydration affordance " + F(belief.Hydration) + "." + evidence);
                }
                if (belief.Has(Affordance.Portable) && estimated >= 0.1 && agent.Inventory.Total < agent.Inventory.Capacity - 0.1)
                {
                    double value = 0, socialCost = 0;
                    if (belief.Has(Affordance.Nourishment))
                    {
                        double reserveGap = Clamp((carryTarget - agent.Inventory.Food) / Math.Max(1, carryTarget));
                        value = t.FoodWeight * (0.17 + 0.72 * n.Hunger * n.Hunger) * reserveGap;
                        // A personal cache is a reserve, not a perpetual gather/deposit loop.
                        if (belief.OwnerId == agent.Id && agent.Inventory.Food > 0.8 && n.Hunger < 0.65) value = 0;
                        if (belief.OwnerId >= 0 && belief.OwnerId != agent.Id)
                        {
                            Relationship rel = Relation(agent, belief.OwnerId);
                            double anticipatedWitnesses = 0.2;
                            foreach (PersonBelief person in agent.People.Values)
                                if (person.Alive && Tick - person.ObservedTick < 12 && Distance(person.X, person.Y, belief.X, belief.Y) <= Config.VisionRadius) anticipatedWitnesses += 0.08;
                            socialCost = t.OwnershipRespect * (0.10 + Math.Min(0.8, anticipatedWitnesses) * 0.16)
                                + Math.Max(0, rel.Attachment) * 0.14 + Math.Max(0, rel.Trust) * 0.06;
                            // Desperation changes relative utility through need weighting, not through a separate action.
                        }
                    }
                    if (belief.Has(Affordance.Material) && agent.Inventory.Materials < 2.05 && !localShelter)
                        value = Math.Max(value, t.ComfortWeight * (0.11 + 0.26 * n.Exposure + 0.05 * n.Fatigue) * (1 - agent.Inventory.Materials / 3));
                    if (value > 0.0001)
                        AddCandidate(agent, candidates, ActionKind.Acquire, belief.ObjectId, -1, belief.X, belief.Y,
                            value * Math.Min(1, estimated), socialCost, 0, confidence, belief.SourceEventId,
                            "acquire from " + belief.Name,
                            "Reserve target " + F(carryTarget) + "; carried food " + F(agent.Inventory.Food) + "; hunger " + F(n.Hunger) +
                            "; believed owner " + OwnerName(belief.OwnerId) + "; expected social cost " + F(socialCost) + "." + evidence);
                }
                if (belief.Has(Affordance.Storage) && belief.OwnerId == agent.Id && agent.Inventory.Food > t.ReserveTarget + 0.5 && estimated < t.ReserveTarget * 1.7)
                {
                    double value = (0.07 + 0.10 * Clamp((agent.Inventory.Food - t.ReserveTarget) / 2)) * (1 - n.Hunger * 0.7);
                    AddCandidate(agent, candidates, ActionKind.Deposit, belief.ObjectId, -1, belief.X, belief.Y,
                        value, 0, 0, confidence, belief.SourceEventId, "store food in " + belief.Name,
                        "Carry surplus " + F(agent.Inventory.Food - t.ReserveTarget) + " above retained carry reserve " + F(t.ReserveTarget) + "; stored reserve " + F(estimated) + " below desired " + F(t.ReserveTarget * 1.7) + "." + evidence);
                }
                if (belief.Has(Affordance.Shelter))
                    AddRestCandidate(agent, candidates, belief.X, belief.Y, belief.ObjectId, belief.Protection, belief.SourceEventId);
                if (belief.Has(Affordance.Cultivable) && belief.Condition < 0.92 && estimated < belief.Capacity * 0.8 && n.PhysicalUrgency < 0.8)
                {
                    double investment = (0.08 + 0.13 * n.Hunger + 0.07 * t.Consideration) * (1 - belief.Condition);
                    AddCandidate(agent, candidates, ActionKind.Tend, belief.ObjectId, -1, belief.X, belief.Y,
                        investment, 0, 0.01, confidence, belief.SourceEventId,
                        "tend " + belief.Name, "Remembered condition " + F(belief.Condition) + "; restoring growth has expected future nourishment value " + F(investment) + "." + evidence);
                }
            }

            List<int> knownPeople = new List<int>(agent.People.Keys); knownPeople.Sort();
            for (int p = 0; p < knownPeople.Count; p++)
            {
                PersonBelief person = agent.People[knownPeople[p]];
                if (!person.Alive || Tick - person.ObservedTick > 24) continue;
                Relationship rel = Relation(agent, person.AgentId);
                double confidence = person.Confidence * Math.Max(0.15, 1 - (Tick - person.ObservedTick) / 30.0);
                int sinceContact = Tick - rel.LastInteractionTick;
                if (sinceContact > 8 && Tick - agent.LastSocialTick > 3)
                {
                    double relief = Math.Min(n.Loneliness, 0.23 + rel.Familiarity * 0.12);
                    double socialValue = t.SocialWeight * Reduction(n.Loneliness, relief) * 2.1
                        + t.Curiosity * n.Uncertainty * 0.13 + rel.Attachment * n.Loneliness * 0.13;
                    double anticipatedCost = Math.Max(0, -rel.Trust) * (0.12 + (1 - t.RiskTolerance) * 0.1);
                    AddCandidate(agent, candidates, ActionKind.Converse, -1, person.AgentId, person.X, person.Y,
                        socialValue, anticipatedCost, 0, confidence, rel.LastEventId,
                        "approach " + OwnerName(person.AgentId), "Loneliness " + F(n.Loneliness) + "; uncertainty " + F(n.Uncertainty) +
                        "; directed trust " + F(rel.Trust) + "; attachment " + F(rel.Attachment) + "; expected contact relief " + F(relief) + "; person last seen " + (Tick - person.ObservedTick) + " ticks ago.");
                }
                if (agent.Inventory.Food > 1.05 && n.Hunger < 0.65 && person.ApparentHunger > 0.25 && person.ApparentFood < 2 && sinceContact > 12)
                {
                    double expectedHelp = person.ApparentHunger * 0.36;
                    double value = t.Consideration * expectedHelp * (0.5 + Math.Max(0, rel.Affinity) * 0.5)
                        + t.SocialWeight * n.Loneliness * 0.10 + Math.Max(0, rel.ReciprocityBalance) * 0.06 + rel.Attachment * 0.06;
                    double opportunityCost = t.FoodWeight * (0.025 + n.Hunger * 0.13) * (1.5 / agent.Inventory.Food);
                    AddCandidate(agent, candidates, ActionKind.Give, -1, person.AgentId, person.X, person.Y,
                        value, 0, opportunityCost, confidence, rel.LastEventId,
                        "offer food to " + OwnerName(person.AgentId), "Visible hunger cue " + F(person.ApparentHunger) + "; visible carried food " + F(person.ApparentFood) +
                        "; consideration " + F(t.Consideration) + "; own food cost " + F(opportunityCost) + "; expected helpfulness " + F(expectedHelp) + ".");
                }
            }

            if (agent.Inventory.Materials >= 2 && !localShelter)
            {
                double value = t.ComfortWeight * (0.16 + n.Exposure * 0.45 + n.Fatigue * 0.12) + t.Consideration * 0.03;
                AddCandidate(agent, candidates, ActionKind.Build, -1, -1, agent.X, agent.Y,
                    value, 0, 0.035, 1, -1, "assemble a shared windbreak", "Two carried material units can provide shared protection; exposure " + F(n.Exposure) + "; no remembered shelter within four steps.");
            }

            AddExploration(agent, candidates);
            AddCandidate(agent, candidates, ActionKind.Wait, -1, -1, agent.X, agent.Y, 0.001, 0, 0, 1, -1, "remain here", "No urgent opportunity is required; quiet outcomes are allowed.");
            candidates.Sort(delegate(ActionCandidate a, ActionCandidate b) {
                int score = b.Score.CompareTo(a.Score); if (score != 0) return score;
                int kind = a.Kind.CompareTo(b.Kind); if (kind != 0) return kind;
                int obj = a.TargetObjectId.CompareTo(b.TargetObjectId); if (obj != 0) return obj;
                int person = a.TargetAgentId.CompareTo(b.TargetAgentId); if (person != 0) return person;
                int y = a.TargetY.CompareTo(b.TargetY); return y != 0 ? y : a.TargetX.CompareTo(b.TargetX);
            });
            DecisionRecord decision = new DecisionRecord { Tick = Tick, AgentId = agent.Id, NeedsAtDecision = n.Copy(), Chosen = candidates[0].Copy(),
                ExpectedUtility = candidates[0].ExpectedValue, PerceivedContext = "Compared " + candidates.Count + " opportunities using " + agent.Beliefs.Count + " object beliefs and " + agent.People.Count + " person sightings. Selection score = expected benefit × confidence × learned reliability / travel delay − effort − anticipated costs. Expected/actual outcome fields compare raw perceived benefit; top alternatives are recorded." };
            for (int i = 1; i < Math.Min(13, candidates.Count); i++) decision.Alternatives.Add(candidates[i].Copy());
            agent.LastDecision = decision; agent.Intention = decision.Chosen.Copy();
            agent.Decisions.Add(decision);
            Trim(agent.Decisions, Config.DecisionCapacity);
            Statistics.Decisions++;
        }

        private void AddRestCandidate(Agent agent, List<ActionCandidate> candidates, int x, int y, int objectId, double protection, int cause)
        {
            double fatigueRelief = 0.16 + protection * 0.14;
            double exposureRelief = 0.025 + protection * 0.14;
            double value = agent.Traits.RestWeight * Reduction(agent.Needs.Fatigue, fatigueRelief) * 2.6
                + agent.Traits.ComfortWeight * Reduction(agent.Needs.Exposure, exposureRelief) * 2.5;
            AddCandidate(agent, candidates, ActionKind.Rest, objectId, -1, x, y, value, 0, 0, 1, cause,
                objectId < 0 ? "rest here" : "rest at remembered shelter", "Fatigue " + F(agent.Needs.Fatigue) + "; exposure " + F(agent.Needs.Exposure) + "; believed protection " + F(protection) + ".");
        }

        private void AddCandidate(Agent agent, List<ActionCandidate> candidates, ActionKind kind, int objectId, int personId, int x, int y,
            double benefit, double socialCost, double risk, double confidence, int cause, string label, string reason)
        {
            int distance = Distance(agent.X, agent.Y, x, y);
            if (kind == ActionKind.Converse || kind == ActionKind.Give) distance = Math.Max(0, distance - 1);
            double effort = distance * (0.003 + agent.Needs.Fatigue * 0.004);
            double reliability = 1;
            ActionLearning learning;
            if (agent.Learning.TryGetValue(kind, out learning) && learning.Attempts > 2) reliability = Math.Max(0.35, Math.Min(1.3, learning.ExpectedReturn));
            double score = benefit * confidence * reliability / (1 + distance * 0.12) - effort - socialCost - risk;
            candidates.Add(new ActionCandidate { Kind = kind, TargetObjectId = objectId, TargetAgentId = personId, TargetX = x, TargetY = y,
                ExpectedValue = benefit, Score = score, Confidence = confidence, TravelCost = effort, RiskCost = risk, SocialCost = socialCost,
                CauseEventId = cause, Label = label, Reason = reason + " Benefit " + F(benefit) + "; travel " + distance + "; learned reliability " + F(reliability) + "; score " + F(score) + "." });
        }

        private void AddExploration(Agent agent, List<ActionCandidate> candidates)
        {
            double best = double.NegativeInfinity; int bestX = agent.X, bestY = agent.Y; int bestUnknown = 0;
            int range = Math.Min(9, Config.VisionRadius + 4);
            for (int y = Math.Max(1, agent.Y - range); y < Math.Min(Config.Height - 1, agent.Y + range + 1); y++)
            for (int x = Math.Max(1, agent.X - range); x < Math.Min(Config.Width - 1, agent.X + range + 1); x++)
            {
                int distance = Distance(agent.X, agent.Y, x, y);
                if (distance == 0 || distance > range) continue;
                int at = Index(x, y);
                if (agent.KnownTerrain[at] && !RememberedWalkable(agent, x, y)) continue;
                int unknown = 0;
                for (int sy = Math.Max(0, y - 2); sy <= Math.Min(Config.Height - 1, y + 2); sy++)
                for (int sx = Math.Max(0, x - 2); sx <= Math.Min(Config.Width - 1, x + 2); sx++)
                    if (!agent.KnownTerrain[Index(sx, sy)]) unknown++;
                double novelty = unknown * 0.06 + (!agent.Visited[at] ? 0.35 : 0);
                double value = novelty / (1 + distance * 0.35);
                // A stable seed-dependent, agent-specific directional preference only breaks equal information values.
                value += ((x * 17 + y * 31 + agent.Id * 13) % 23) * 0.00001;
                if (value > best) { best = value; bestX = x; bestY = y; bestUnknown = unknown; }
            }
            if (bestX == agent.X && bestY == agent.Y) return;
            double supplyUncertainty = agent.Inventory.Food < 0.8 ? 0.08 : 0;
            double benefit = (0.035 + agent.Traits.Curiosity * (0.07 + agent.Needs.Uncertainty * 0.13) + supplyUncertainty) * Math.Max(0.2, Math.Min(1, best * 2));
            AddCandidate(agent, candidates, ActionKind.Explore, -1, -1, bestX, bestY,
                benefit, 0, (1 - agent.Traits.RiskTolerance) * 0.005, 1, -1,
                "inspect ground at " + bestX + "," + bestY, "Uncertainty " + F(agent.Needs.Uncertainty) + "; " + bestUnknown + " unknown nearby cells; curiosity " + F(agent.Traits.Curiosity) + ". No resource contents were consulted.");
        }

        private bool MoveToward(Agent agent, int targetX, int targetY, int reach)
        {
            if (!Inside(targetX, targetY)) return false;
            int length = Config.Width * Config.Height;
            int[] parent = new int[length];
            for (int i = 0; i < length; i++) parent[i] = -1;
            int start = Index(agent.X, agent.Y), found = -1;
            Queue<int> queue = new Queue<int>(); queue.Enqueue(start); parent[start] = start;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(), x = at % Config.Width, y = at / Config.Width;
                if (Distance(x, y, targetX, targetY) <= reach) { found = at; break; }
                for (int d = 0; d < 4; d++)
                {
                    int direction = (d + agent.Id) % 4;
                    int nx = x + DirectionX[direction], ny = y + DirectionY[direction];
                    if (!Inside(nx, ny) || !RememberedWalkable(agent, nx, ny)) continue;
                    int next = Index(nx, ny); if (parent[next] >= 0) continue;
                    parent[next] = at; queue.Enqueue(next);
                }
            }
            if (found < 0 || found == start) return false;
            int step = found;
            while (parent[step] != start) step = parent[step];
            int stepX = step % Config.Width, stepY = step / Config.Width;
            // Execution checks physical truth; planning above only uses the resident's map.
            if (!Tiles[stepX, stepY].Walkable)
            {
                agent.KnownTerrain[step] = true; agent.TerrainMemory[step] = Tiles[stepX, stepY].Terrain;
                return false;
            }
            agent.X = stepX; agent.Y = stepY; agent.Visited[step] = true;
            agent.Needs.Fatigue = Clamp(agent.Needs.Fatigue + 0.0015);
            Statistics.Moves++;
            return true;
        }

        private bool RememberedWalkable(Agent agent, int x, int y)
        {
            int at = Index(x, y);
            return !agent.KnownTerrain[at] || (agent.TerrainMemory[at] != Terrain.Rock && agent.TerrainMemory[at] != Terrain.Water);
        }

        private double BelievedProtection(Agent agent, int x, int y)
        {
            double protection = 0;
            foreach (ObjectBelief belief in agent.Beliefs.Values)
                if (belief.Has(Affordance.Shelter) && Distance(x, y, belief.X, belief.Y) <= 1)
                    protection = Math.Max(protection, belief.Protection);
            return protection;
        }
        private double BeliefConfidence(ObjectBelief belief) { return Math.Max(0.12, belief.Confidence / (1 + Math.Max(0, Tick - belief.ObservedTick) * 0.006)); }
        private static double Reduction(double deficit, double relief) { double after = Math.Max(0, deficit - relief); return deficit * deficit - after * after; }
        private string OwnerName(int id) { Agent a = GetAgent(id); return a == null ? "unclaimed" : a.Name; }
        private int ShelterCount() { int count = 0; for (int i = 0; i < Objects.Count; i++) if (Objects[i].Has(Affordance.Shelter)) count++; return count; }
        private static void Trim<T>(List<T> list, int capacity) { if (list.Count > capacity) list.RemoveRange(0, list.Count - capacity); }
    }
}
