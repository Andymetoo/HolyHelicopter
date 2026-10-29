# Mechanisms and boundaries

These are the implemented rules, not claims of human psychology. The prototype asks what patterns these particular coupled rules produce.

## Decision and learning loop

Each fixed tick updates weather, objects, then inhabitants. The first inhabitant processed rotates each tick. An inhabitant's turn applies physiology, local perception, then one move or interaction. A decision can retain its destination for up to nine ticks; extreme physical need prompts earlier reconsideration. Movement uses breadth-first search on remembered terrain, treating unknown terrain as potentially traversable. Physical execution can discover a blocked route.

The decision system generates candidates from the inhabitant's beliefs, carried resources, deficits, and directional relationships. It is utility selection with short-lived intentions, not a general multi-step planner. Squared deficit reduction makes relief more valuable when a deficit is large. Resource acquisition, reserves, cultivation, and construction also use hand-chosen estimates of future benefit.

The implemented score is:

```text
benefit * confidence * learned reliability / (1 + 0.12 * travel distance)
    - travel effort - anticipated social cost - other cost
```

Travel effort increases with distance and fatigue. Expected benefit and selection score are separate: the Why panel compares raw expected and realized benefit, while displaying the score and its costs for the selected action. Each record retains the twelve best rejected candidates and states the total number considered. A recorded causal link is supporting provenance, not a counterfactual proof that one prior event alone caused the action.

For each primitive, an exponentially updated ratio of observed to expected benefit changes later reliability. A failed route or missing resource can therefore reduce willingness to attempt that primitive. Learning is shared across targets of the same primitive: failure at one cache can unfairly discourage gathering elsewhere. Future-value actions such as building and tending currently evaluate their immediate success using estimated future benefit; they do not wait to credit a later harvest or sheltered rest.

## Information

Perception is a Manhattan-radius sensor, not line of sight: rocks block walking, not sensing. Locally seen objects reveal accurate quantities, affordances, and owner marks. Nearby people reveal coarse hunger/fatigue and visible inventory cues. Names are stable identities. Other minds' needs, memories, and intentions are not exposed to the chooser.

Each remembered object retains its observation time, stock estimate, perceived owner, properties, confidence, and source. Confidence decays with age when used in decisions; the UI also shows age. Direct perception supersedes a report. Communication describes one attractive remembered food opportunity, with possible quantitative noise. It preserves the original observation time, so repeated communication cannot make old evidence fresh. A listener weighs its existing evidence against the speaker's directional trust.

A material discrepancy between a report and later local observation reduces trust in that reporter. This can penalize an honest but outdated report. The model has no explicit distinction between error, environmental change, and intentional deception. It has no intentional lying or language generation.

## Social evaluation

Directed relationships contain trust, affinity, familiarity, attachment, expected care, and an exchange balance. A contact's utility depends on the observer's own need relief and weights. Expectations update with prediction error. Positive experiences can accumulate attachment; familiarity and attachment slowly decay. The reverse direction is updated separately.

The recipient can decline contact when its own needs and evaluations make contact unattractive. Food offers physically move inventory. Taking portable stock uses the same action for public and owned resources. Anticipated social costs come from perceived ownership, local witness estimates, dispositions, and relationships. A witness interprets an acquisition through its own ownership belief. An absent owner receives no remote blame attribution.

Third-party social evaluation is narrow: observed access to another person's attributed stock matters, but there is no general reputation gossip, negotiated permission, explicit promise, institution, or group identity. A relationship variable called attachment is not evidence of romance, exclusivity, jealousy, or a human-like bond.

## Physical accounting

All portable nourishment becomes homogeneous food with 0.36 hunger relief per unit. All construction resources become homogeneous material. This is an affordance-driven catalogue with two carried resource types, not arbitrary compositional chemistry. Acquisition removes actual stock, eating consumes only the needed portion up to one unit, storage transfers only surplus above the desired carried reserve, and gifts transfer to available carrying capacity.

Rain, fertility, and plant condition affect food regrowth. Harvesting lowers condition; tending improves it. Weather affects exposure. Shared windbreaks consume two material units, improve protection and rest for anyone nearby, and reduce storage spoilage nearby. Their condition slowly declines to a floor. They are not exclusive residences. Springs have large stocks and rapid renewal; fresh water is usually plentiful in the default world.

Food spoils in inventory and storage. Sustained severe hunger, thirst, or exposure damages health; lower deficits permit recovery. Death drops carried food and material at the death cell, at most two objects per person. A simplified estate rule makes the deceased person's home stock unclaimed without moving it. There are no births, ageing, injury interactions, disease, reproduction, or recolonization; extinction is absorbing.

## Computational bounds

Population is fixed, including retained deceased records. Terrain and identity-indexed beliefs are bounded by world size and object count. Construction is capped at one shelter per 24 world cells; death adds at most two objects per initial inhabitant. Event, personal memory, and decision retention are configured explicitly. Old causes may fall outside retained history. Standalone trace collection can preserve events beyond the simulation's own retention window.

The exposed public model is intended for inspection and experimental intervention. Bounds and invariant guarantees apply to normal engine transitions from validated configuration, not arbitrary external mutation. Long-run performance still depends on population, map size, and candidate count. The observer limits work per rendered frame and shows its achieved rate.
