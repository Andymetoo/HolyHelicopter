# File DNA Laboratory — Phase 1 report

Implemented on October 30, 2026, in Unity 6000.4.1f1. Scene: `Assets/Experiments/FileDNALab/Scenes/ThePit.unity`. The implementation is isolated in FileDNALab with four assemblies: engine-independent Core, Runtime, Validation and Editor. No new packages were installed. Null Menagerie was inspected for context; its scenes and assets were not modified, and the new lab has no assembly reference to it.

## Implemented systems

The interpreter executes versioned, ordered 16-byte codons into a constitutive mass, segment growth/branching, cursor movement, physical modifiers, joints, actuators, sensors, oscillators, neurons, recurrent synapses and motor outputs. Actual raw bytes determine properties and topology. A content SHA identifies a file; changing only that SHA in a decoded document leaves expression unchanged, as an explicit automated check. There is no seed-based creature generator. File metadata is outside biological identity.

Every accepted file is streamed completely. Inputs exceeding the codon budget undergo contiguous positional lane reduction over the whole file, retaining source intervals and local statistics. This bounds anatomy and neural execution while preserving useful sequential structure. It is lossy and not collision-free. DNA archives preserve the reduced program and claimed original fingerprint for reload and comparison without copying the source file.

The physical expression uses native 2D rigid bodies, circle/capsule/box colliders, hinge/spring/fixed/distance joints, break forces, per-segment materials, damping and bounded motors. The shape can collapse, self-collide, break, spin or remain motionless. Anatomy is a development tree, without body classes, symmetry enforcement, a privileged head, or a default brain. Genetically specified neural circuits run once per 60 Hz physics tick. There is no shared fighting or locomotion policy.

The arena occupies a private physics scene, with explicit gravity, wind, drag and bounded forcing. A third file or a symmetric mixture of contestant tapes produces a biome. The neutral biome is a standard comparison environment. Observer controls include file import, Windows native browsing, path entry, pause/resume, speed, reset, timeout, pan/zoom, selection, live body and circuit data, an instruction/source inspector, overlays, portable DNA saves and persistent match history.

Mass loss is the only biological elimination rule in Phase 1: 60% of original total mass beyond absorbing containment boundaries. Detached parts retain ownership. The root is not a vital organ. A two-second grace handles double knockouts. Both surviving at timeout is a draw; there is no inactivity penalty or tie-breaking stat score.

## Genetic diversity evidence

The final corpus contains **228 inputs**: 224 deterministic generated fixtures plus real project C# source, Markdown, package JSON and a PNG asset. It includes empty and one-byte files, near-boundary lengths, uniform bytes, incremental sequences, short repeated patterns, repeated text, sparse bytes and high-entropy fixtures, with lengths through 1 MiB. A separate 2 MiB +17-byte input checks sensitivity to a mutation at the very end of the file. Imported files are never executed.

Observed expression: **179 unique expressed genomes**, **1–40 segments**, **62 branching specimens**, **515 sensory nodes**, and **157 specimens with zero actuators**. Empty input and a zero byte have different content fingerprints but express the same minimal physical mass. Such many-to-one expression is legitimate. Nearly 70% motorless outcomes in this deliberately short/repetitive corpus should not be read as an estimate for a user's whole drive.

| Segment count | Specimens |
|---|---:|
| 1 | 131 |
| 2 | 20 |
| 3–10 | 18 |
| 11–20 | 8 |
| 21–30 | 33 |
| 31–39 | 16 |
| 40 (safety cap) | 2 |

Joint type totals: 312 hinge, 274 spring, 686 fixed, 359 distance. Shape totals: 575 circle, 427 capsule, 857 box. Motors span 0–24. The complete count distributions and fingerprints are recorded in `Evidence/genetics.txt`.

An initial promoter threshold of 159 saturated 47 specimens at 40 masses. Reducing the threshold to 95 left only 2 cap-saturated specimens in the final corpus, while retaining chains, branches and inert single masses. This was a grammar-level change, rather than an anatomy template or a forced target body size. The more selective grammar reduces some expression diversity in tiny files, which is an explicit tradeoff for better discrimination among larger tapes. The corpus includes the interpreter's own source file, so counts can change when that source changes; the checked evidence records the exact final distribution.

## Example organisms and surprises

| Raw input | Expressed result |
|---|---|
| Recipe.txt, ordinary text | 2 masses, 1 joint, 0 motors, 3 nodes; a passive object with unused nervous activity |
| Articulated tape.bin, authored grammar demonstration | 10 masses in a chain, 9 oscillatory actuators and 9 nodes |
| Branch circuit.bin, authored grammar demonstration | 10 masses, 9 actuators and 9 nodes, branching from a shared mass |
| Zero byte.bin | 1 mass, no joints, actuators or neurons |
| Incremental.bin | 1 mass with no useful circuit: the byte/codon alignment never asks for limb growth |
| Noise.bin | 3 masses, 2 joints, 2 actuators, 6 nodes and 4 synapses |
| Empty.bin | The minimum constitutive mass, with the same expressed signature as the zero byte |

The incremental sequence was a useful surprise: plenty of changing bytes do not imply a complex organism. The repetitive/aligned corpus also produced large motorless structures. Complexity and activity are separate consequences of instruction expression, not correlated upgrade scores. Another useful distinction is that a detached part can survive while its original parent falls; the mass-based rules allow that without treating the root as a head.

The two oscillator tapes are explicitly authored raw DNA examples for learning the grammar. They are not evidence that arbitrary files naturally evolve walking. The corpus report and noise/text examples provide the independent arbitrary-byte evidence.

## Automated and actual runtime validation

The core corpus passed under a standalone Mono harness, then under Unity. Checks cover deterministic expression, filename/extension independence, content mutation, same-histogram sequence reordering, whole-stream byte accounting, distant-tail influence, portable archives, valid references, complexity bounds, inert outcomes, file cap rejection and a truncated archive. The real local file reader is compared with in-memory byte decoding. Mutation of the document's fingerprint alone cannot change its expressed body or circuit.

Actual Unity **Play Mode**, not a static approximation, ran 14 autonomous matches for up to 15 simulated seconds each across neutral, file-derived and paired biomes. Two contests ended with B winning after A lost containment; 12 reached timeout draws. The run recorded **71 active actuator observations at tick 300, four broken joints, 60,662 contact samples, one speed clamp and zero numerical faults**. Short-horizon replay matched within .001 m on this build. Additional targeted cases validated single elimination, double knockout, root-neutral mass loss, pause/ready freezing, reproducible biome decoding and different trajectories for the same unmodified genotype in changed physical conditions.

These tests exposed and fixed two real implementation bugs: raw motor sums were not clamped for already-lost segments, and C# type matching still accepted destroyed Unity joint wrappers after a break. Both errors are distinct from legitimate nonviability and are now covered by the collapse checks. Distance-based links also use centre anchors so their rest distances are not accidentally measured between coincident hinge pivots.

`Evidence/physics.txt` records every matchup and the aggregate measurements. The Windows build uses only ThePit as its explicit scene. Player verification outputs independent genetics/physics reports and screenshots under `Logs/FileDNALab/PlayerRelease`; retained evidence and the final verification notes are listed in `Evidence/README.md`. A batch-mode player initially returned black framebuffers despite passing simulation checks. Screenshot validation therefore uses a graphics-enabled player, verifies non-black frame contents and advances the observer for at least 4.5 simulated seconds. This visual check also exposed a scaled-IMGUI clipping error in joint overlays, corrected by clipping lines explicitly in chamber coordinates.

## Limits and next experiments

This is a bounded physical prototype, not a promise of evolved intelligence. Important limitations:

- Fixed 16-byte framing has alignment biases. Changing the codon frame or repeat suppression in a future grammar version is a useful research direction.
- Regional reduction loses information. Some mutations change content identity without changing expressed traits; a phenotype is not an injective encoding of a large file.
- Anatomy is a connected development tree. Cyclic body links, growth after spawn, plasticity and shape mutation are future extensions.
- Softness is a jointed-mass approximation, not fluid or deformable continuum physics. Density uses a bounded area proxy, not exact collider-area integration.
- Powered rotors have no metabolism or energy depletion. Detached/root rotary actuators are an abstraction, not a conservation-complete internal flywheel model.
- Physics construction and genome expression are deterministic; long fights are not guaranteed bit-identical across different machines, operating systems or Unity versions. Reruns use the same physical setup and genetic circuit.
- There is no damage/health bar or separate incapacitation score. An inert but retained body can draw, which is intentional.
- Native browsing is implemented for Windows and Editor. Manual operation of the native modal dialog is not an automated test; other standalone platforms use path entry and were not built here.
- The overlay draws neural connectivity between host masses; connections within one mass overlap. CIRCUIT lists the exact graph and live values for those cases.

The most promising next work is a recorded comparison of the **same files across several biomes**, with trajectory and survival measures separated from genotype identity. A broader real-file corpus would reveal grammar alignment biases better than adding handcrafted creature classes. Then introduce an explicit, conserved energy budget and a mutation operator over reduced codons before implementing Petri Dish reproduction. Mini Kingdoms should reuse those embodied organisms and add environmental affordances and social signals rather than replacing them with human templates. Neither future simulation is included in this pass.
