# Terrarium: measured prototype assessment

Recorded 2026-10-28. This is an executable artificial-life prototype, with evidence from its actual C# transitions. It demonstrates resource-mediated social consequences, uncertain information, directional evaluations, and shared environmental effects. It does **not** demonstrate jealousy, betrayal, romance, revenge, or a general model of human society.

## What was run

The primary experiment runs seeds **7, 19, 42, 103, 211, 509, 997, 2026** for **12,000 ticks each**, with 16 initial inhabitants and default parameters. A day is 240 ticks, so each run covers 50 days. Four additional runs use seed 7 with scarcity, abundance, noisier communication, or a smaller world with very short history buffers. Configuration changes are recorded in the generated [run report](Evidence/run-report.md) and [metrics](Evidence/metrics.csv).

Reproduce from the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'Assets/Experiments/Terrarium/Validation/Run-Headless.ps1' -Ticks 12000 -TraceTicks 2000 -OutputDirectory 'Temp/Terrarium/reproduction'
```

The main run preserves the first 2,000 ticks of seed 7. A supplementary run of the **same compiled executable** preserves seed 19 through tick 2,000. The two [source manifests](Evidence/source-manifest.txt) and [supplementary manifest](Evidence/seed19-source-manifest.txt) record hashes and exact arguments. The hashes identify the tested source, without requiring a branch or commit.

## Results and interpretation

Across the eight baseline runs, **13–16 of the initial 16 inhabitants survived**. Accepted conversations ranged from **5,959 to 6,977**, and food gifts from **2 to 135**. Other-owned acquisitions were uncommon: **1–3 per baseline run**. The scarcity run ended with **one survivor**. These are observed differences, not a promise that every seed will be stable or interesting.

All twelve measured runs passed, with **52,397,667 sampled assertions** in about **12.4 minutes**. This large count comes mostly from repeated invariant checks, not millions of independent experiments. The supplementary seed 19 trace passed another 771,534 assertions. Every baseline retained activity in its last quarter. Final asymmetric relationship-pair counts ranged from **38 to 64**, but **no final directed affinity was below −0.15** in any measured condition. Persistent rivalry was not observed.

Abundant seed 7 kept all 16 inhabitants alive, with 7,988 accepted conversations, **zero gifts and zero other-owned acquisitions**. This is a useful peaceful outcome under altered resources. Its lack of gifts can reflect a lack of visible need, rather than hostility. That interpretation is a hypothesis; this single paired run is descriptive evidence, not a general parameter-effect estimate.

See [the measured table](Evidence/run-report.md), [all metrics](Evidence/metrics.csv), and [primitive counts](Evidence/action-counts.csv). Successful action counts include rest and waiting; primitive counts include failed attempts. They should not be read as a count of meaningful narrative moments. First- and last-quarter activity, accepted conversations, gifts, resource transfers, deaths, and directional relationships provide separate evidence about what persisted.

The prototype allows substantial mundane activity. For example, seed 19's first 2,000 ticks include 3,020 waits and 1,953 rests alongside 964 accepted conversations. Sharing is possible but sparse in some seeds. More frequent sharing in another seed is not, by itself, evidence of a friendship or altruistic culture.

The correction counter includes both contradicted reports and failed resource-opportunity checks, so it is not a pure misinformation measure. The immediate cache-reversal diagnostic found **0–78** cases per baseline over 12,000 ticks, and 93 in the noisy-information condition. The old self-draining deposit loop was fixed; fixed-size acquisitions can still overshoot a reserve and lead to a single surplus return. Some rebalancing inefficiency remains, and these aggregate counts alone cannot exclude every repetitive pattern.

## Three recorded causal chains

### A remembered report, a correction, and a later decision — seed 7

At tick **187**, event **#619**, Jori and Ira converse. Ira describes fruit patch 7 as holding **4.75 units**, based on an observation already **27 ticks old**. Jori accepts the report at confidence **0.52**.

At tick **324**, event **#1241**, Jori observes **3.18 units** there. The event links back to #619. Jori's trust toward Ira changes from **+0.011 to −0.009**, while affinity stays **+0.068**. The mismatch penalizes the reporter even though an old observation can be honestly wrong. There is no scripted deception.

At tick **706**, Jori again chooses to approach Ira. The decision cites #1241 and explicitly includes trust **−0.01**, attachment **0.01**, a current person sighting, and three travel steps. The approach scores **0.005**, ahead of drinking at **0.002** and waiting at **0.001**. By tick **709**, Ira has moved; the attempted contact returns zero benefit against **0.024** expected, and learned conversation reliability falls from **0.87 to 0.77**.

This is evidence that a report changed a relationship estimate, that the estimate appeared in later utility evaluation, and that a failed encounter changed action learning. It does not establish that the trust change was necessary for the later choice. Indeed, Jori still selected contact. The current model does not identify whether report error came from noise, depletion, regrowth, or intent.

Sources: [events and their recorded ancestors](Evidence/seed7-events.csv), [decision snapshots with alternatives](Evidence/seed7-decisions.csv), [directional evaluations](Evidence/seed7-social-evaluations.csv).

### Stored food becomes a social cost — seed 19

At tick **270**, Ari gathers **1.20 food** in event **#839**. At tick **273**, event **#850**, Ari deposits **0.983 food**, leaving a reserve of roughly **4.26**. That transfer explicitly links to the gathering event.

At tick **332**, event **#998**, Neri acquires **1.20 food** from Ari's stock. Neri knows the attributed owner is Ari, has **0.26 food** carried, and wants a reserve of **1.80**. Hunger is only **0.168**. The local cache scores **0.090**, narrowly above unclaimed sources at **0.089** and **0.087**, and exploration at **0.082**. The choice results from proximity and reserve utility outweighing anticipated social cost, rather than a desperation rule or a named theft action.

Ari and Cato witness the transfer. Ari's estimate of Neri changes from neutral trust to **−0.023**, and affinity to **−0.037**. Cato's changes are smaller: trust **−0.009**, affinity **−0.003**. Neri's realized benefit is **0.100**, below the expected **0.154**, after an observed ownership cost. Acquisition reliability falls from **0.87 to 0.84**. The physical event's secondary parent is Ari's deposit #850; Neri's own decision does not falsely claim to have witnessed that earlier deposit.

This demonstrates a physical transfer producing distinct social evaluations and an updated expectation. It does not demonstrate a lasting rivalry. The mechanism is the ordinary acquire action plus each witness's ownership belief and valuation.

Sources: [events and ancestors](Evidence/seed19-events.csv), [decision snapshots](Evidence/seed19-decisions.csv), [witness evaluations](Evidence/seed19-social-evaluations.csv).

### A private comfort goal creates a shared benefit — seed 7

Ira gathers **1.20 material** at tick **14**, event **#80**, and another **1.20** at tick **15**, event **#89**. At tick **16**, event **#95**, Ira spends two units on windbreak **#56**. The build choice scores **0.156**, compared with eating at **0.050**, acquisition at **0.041**, and exploration at **0.039**. Its explanation cites exposure and the lack of a remembered nearby shelter.

Pax later rests there, choosing at tick **29** and completing at **31**. At tick **449**, Jori also chooses that windbreak, scoring **0.025**, compared with exposed local rest at **0.018** and exploration at **0.011**. The decision cites construction event #95. At tick **451**, actual protection is **0.81**; rest reduces fatigue by about **0.06** and exposure by about **0.12**.

One inhabitant's investment changed another inhabitant's later options and physical outcome. There is no gratitude mechanism, ownership of shelters, or assigned cooperative project. Build's immediate reported benefit is a future-value estimate, while the later rest reduction is an actual measured physical effect. The material acquisitions and construction share actor/time/inventory evidence; construction does not currently retain an explicit material provenance event link.

Sources: [events](Evidence/seed7-events.csv) and [the builder's and users' decisions](Evidence/seed7-decisions.csv).

## Validation and practical limits

The harness checks repeatable generation, interleaved independent worlds, and single-step versus batched advancement. Controlled interventions check local knowledge, hidden truth not changing a choice from identical beliefs, owned and unowned acquisition using the same primitive, food conservation, directional witnessed evaluation, absent-owner ignorance, preservation of carried reserves during storage, partial meal accounting, and local resource drops on death. The multi-seed runs sample bounded and finite state, inventories, positions, dated beliefs, histories, and causal ordering every 32 ticks and at the end. Full traces additionally check event continuity and parent availability.

These checks validate specific mechanisms and sampled invariants; they do not constitute exhaustive correctness or a scientific calibration of the utility model. Trace social deltas are end-of-tick snapshots, so multiple interactions in one tick can contribute to a row. Evidence CSV values are rounded for readability. Raw traces remain reproducible with the harness; the compact checked-in excerpts include recorded event ancestors for the chosen examples.

Static compilation of both assemblies passed with warnings treated as errors against installed **Unity 6000.3.5f1** reference DLLs. The project specifies **6000.4.1f1**. Unity was **not launched**, and scene import, actual rendering, interaction, and live speed remain manual checks. There is no claim of cross-runtime bit-identical floating-point replay.

## What this foundation can and cannot produce

It can currently produce sustained need-driven activity, uncertain resource search, communication and corrected reports, differing individual evaluations, occasional food transfers, access to another person's attributed reserves, shared shelter benefits, resource scarcity, and population loss. Those outcomes are supported by measured runs and the examples above. Continuous affinity and attachment can accumulate; that alone is not evidence of human-like friendship or exclusive attachment.

Important limits are small and explicit:

* Utilities are engineered one-step estimates with short destination commitments. Agents cannot invent tools, social conventions, plans, or new action types.
* Food and material are homogeneous. Geography is coarse, agents can share a cell, and radius sensing has no occlusion.
* Reports describe resources, not intentions or general reputation. Trust conflates information reliability and interpersonal evaluations. Old honest reports can therefore damage trust unfairly.
* Learning is per primitive, not per resource, person, context, or multi-step goal. Construction and cultivation have approximate future-value credit.
* Relationships, witnesses, and quantities are deliberately simplified. No consent negotiation, promises, exclusion, institutions, reproduction, or multigenerational dynamics exist.
* A single matched scarcity or noise run does not estimate a general causal effect. The scarcity condition changes three parameters together. The narrow cache-reversal diagnostic is not a proof that all repetitive behavior is useful.

The highest-value next mechanisms would be **better causal attribution and calibrated uncertainty**, followed by **context-specific outcome learning and short multi-step plans**. A resident should distinguish a source that aged from a source that was unreliable, and a failed route from a generally bad resource-acquisition strategy. Requests, permission, and explicit commitments could then introduce expectations with physical stakes without defining betrayal or friendship as actions. Paired parameter ablations over many seeds should precede new high-level narrative claims. Save/load and richer causal inspection would make those investigations easier to repeat.

Implementation details and formulas are documented in [Mechanisms.md](Mechanisms.md); launching and controls are in [the experiment README](../README.md).
