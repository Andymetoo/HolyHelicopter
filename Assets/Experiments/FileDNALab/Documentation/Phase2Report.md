# Phase 2 implementation report — Emergent Morphogenesis

Scene: **`Assets/Experiments/FileDNALab/Scenes/ThePit.unity`**, Unity 6000.4.1f1. Open it, enter Play Mode, select Legacy or Developmental for each source, and press Start. Developmental is the default. SOURCE → **COMPARE THESE BYTES / Legacy vs Dev** generates the same input on both sides. The Phase 2 Windows build is `Builds/FileDNALabPhase2/File DNA Lab.exe`; the Phase 1 build folder remains separate.

## 1. What was actually wrong

[Phase2Diagnosis.md](Phase2Diagnosis.md) records the initial investigation and later native/visual findings. The Phase 1 interpreter was deterministic and content-driven, but each GROW/BRANCH called a fresh `Body(...)` assignment for almost every physical trait. Its cursor did not constitute trait inheritance. BRANCH could target any previous part via modulo, orientation frames were inconsistent, duplicate codons were suppressed, and actuator/neural addresses were largely unrelated to growth sites. Native assembly separated neighbours by bounding half-diagonals, used distant midpoint anchors, contracted every spring to 55% of centre separation, and did not exclude older branches from a new body's volume.

Two additional defects were found by testing rather than inferred from parameter counts. A .012 m initial developmental hull skin still allowed an approximately .008 m native penetration; the corrected skin is .04 m and native separation queries verify every accepted body. Initial screenshots also showed sprites at stale, unshifted construction positions before the first simulation step, while native bodies and labels occupied their spawn lanes. Independent nonphysical silhouettes now follow native positions/rotations immediately. The fix deliberately avoids changing Rigidbody Transforms or their synchronization queue, preserving the Legacy physical setup. Native pose/sprite agreement and the original knockout/grace regression checks cover this distinction.

These defects are distinguished from intentional failure: a correctly constructed creature may still be inert, weak, self-colliding after motion starts, or incapable of retaining its body.

## 2. The new developmental language

`DevelopmentalInterpreter` is an engine-independent, stateful growth interpreter. It carries inherited proportions, shape, materials, joint settings, strength-per-weight, surface gap, growth heading/twist and a local circuit blueprint. EXTEND produces a related descendant. BUD saves a frame and develops a smaller branch; RETURN restores that growth point. PROPORTION/MATERIAL/JOINT modify inherited state. TURN changes its frame. REPEAT replays actual preceding codons, including physical and neural operations. ANCESTOR resumes an ancestral body; END terminates or returns a branch. A single undivided mass remains valid and may grow without ever creating a joint.

The same mechanism can produce straight or curling chains, repeated branches, radial growth, uneven appendages, elastic collections or no growth. None is an interpreter-selected archetype. There is no symmetry constraint, fitness search, automatic gait, enemy detection policy or controller added to improve viability. Development occurs at import; this pass does not animate live cell division.

Full argument mappings, equations, limits and edge cases are in [DevelopmentalLanguage.md](DevelopmentalLanguage.md). The original `Core/Interpreter.cs` is byte-for-byte identical to the preserved Phase 1 source; direct signature regression checks use recorded Phase 1 expectations. Legacy follows the original native layout/joint choices. The shared Ready-state visual-publication fix does not substitute new Legacy anatomy.

## 3. Actual bytes, inheritance and neural development

The existing binary reader remains unchanged. Both modes receive identical reduced codons from the same file. It streams the entire accepted file, keeps contiguous source ranges and statistics, and uses documented positional lane folding above 8192 bytes. The SHA is identity only. Developmental codons use low-nibble opcodes and a byte-15 promoter; no pseudorandom generator participates in expression. Filenames/extensions/timestamps do not participate.

Repeated codons execute instead of being methylated. A REPEAT records both the replayed source region and the region that requested the replay. The current growth point has four named neural roles, an optional actuator and bounded connection patterns. At each physical birth, its prospective module is copied with the body. Encoded oscillator phase advances accumulate along descendants. Connections name local, parent or founder roles, rather than modulo addresses that jump through an unrelated final graph. Missing roles remain missing. Roles and component caps are reported per body in the actual development trace.

The runtime controller still uses bounded recurrent tanh signals and torque/speed-limited motors. Shared rhythm or local reaction can emerge from repeated circuits; there is no hidden traveling-wave or walking routine. Genome definitions contain the actual resolved nodes, motors, synapses and outputs; those are what the inspector displays.

New saves contain the exact grammar version and expression limits around the original reduced tape. Old FDNA-1 archives always open in Legacy, never silently opt into Developmental. Both versions can be explicitly selected for the same decoded file. Unsupported versions are rejected. Existing match history remains readable; new records include interpreter and expressed identities. Validation suppresses history writes.

## 4. Physical assembly

New births use support ports on actual circles, capsules and boxes, with a gap along the encoded heading. A conservative convex test rejects an intersecting bud at its proposed location; it does not search alternate angles or rearrange the body. This exclusion rule records obstruction and can leave an extremely limited organism. Inherited traits also do not imply a viable growth path.

Hinge/fixed anchors coincide between nearby surface ports. Spring/distance anchors use the ports, with deliberate spring preload only when its encoded rest multiplier differs from one. Frequency/damping and relative strength are bounded; Developmental masses use actual primitive area times density. Adjacent collision is encoded per joint, with a serialized global override. Legacy keeps its original collision rule and rectangle-area mass proxy. Nonadjacent self-collision remains enabled after birth, so moving limbs can still collide and destroy their own structure.

The lab continues to use native Unity 2D physics in private scenes. There is no custom fluid solver. Torque/acceleration/velocity clamps and numerical-fault counts remain visible. The arena rules, root-neutral mass-loss criterion, timeout/draw/double-knockout logic and original third-file biome mapping remain in place. No ecosystem or kingdom simulation was implemented.

## 5. Same-input quantitative comparison

The fixed corpus contains **395 inputs**, including **343 distinct raw contents**. There are 384 Phase 1-style generated inputs, seven authored Phase 2 grammar demonstrations, and four real repository files: the preserved original interpreter source, Null Menagerie's README, package JSON and the existing URP PNG icon. The nine authored demonstrations (including the two older tapes) are marked; 386 cases are not authored instruction demonstrations. Exact byte counts and SHA-256 identities are in `Phase2Evidence/inputs.csv`. Inputs include empty, one-byte, short/padded, incremental, repeated, sparse, text and high-entropy content, with files through 1 MiB. Every input is expressed under both modes.

| Measurement | Legacy | Developmental |
|---|---:|---:|
| Distinct expressed genomes | 298 | 301 |
| Quantized structural signatures | 292 | 294 |
| Body count range | 1–40 | 1–33 |
| Single-body specimens | 231 | 229 |
| Multi-part specimens | 164 | 166 |
| Branching specimens | 100 | 87 |
| Maximum growth depth | 34 | 32 |
| Parent/child edges | 2,701 | 901 |
| Same-shape edges | 1,482 / 54.9% | 812 / 90.1% |
| Same-material edges | 893 / 33.1% | 715 / 79.4% |
| Same-joint-type edges | 1,418 / 52.5% | 746 / 82.8% |
| Both dimension ratios within .75–1.3334 | 1,153 / 42.7% | 493 / 54.7% |
| Mean absolute parent log-size change | .40460 | .22226 |
| Mean within-body size coefficient of variation | .12768 | .09918 |
| Zero-actuator specimens | 280 | 309 |
| Repeated local motif occurrences | 864 | 184 |
| Specimens at the 40-mass cap | 2 | 0 |
| Distinct-content phenotype collisions beyond first member | 45 | 42 |

The strongest improvement is **relatedness**, not a large increase in signature count or better fighting. Adjacent size change is about 45% lower, while shape/material/joint inheritance rises substantially. The new mode has more shallow, limited bodies and fewer large assemblies. It does not merely produce endless identical chains: 87 specimens branch; the deepest long chain is uncommon. Among non-authored inputs, 159/386 develop multiple masses and 84 branch, versus Legacy's 157/386 and 96.

The repeated-motif total is **lower**, even per edge. This measurement includes relative-proportion bins, joint kinds and associated controller-role patterns, and is sensitive to reduced body size and evolving modules. The explicit repeat/inheritance mechanism works, but these results do not establish a universal increase in repeated motifs across arbitrary files. More zero-motor outcomes are another real bias, not an unreported improvement. No anatomical quotas were introduced to change these numbers.

The .04 m spatial rule rejected 3,275 attempted buds. Developmental lost ten node allocations at the neural cap; Legacy rejected fourteen growth instructions and twelve node allocations at its caps. Both are reported as lost expression. Across the corpus, 343 distinct raw contents produced 342 distinct reduced tapes: one reduction/padding collision. Distinct contents can also share a phenotype through silence or unused arguments. Raising the fold budget from 512 to 1024 codons changed expression for all three fixed high-entropy probes in both modes; the folding budget is biologically consequential and saved with new archives.

All detailed segment/joint/depth/branch/motor/connection distributions and per-specimen values are retained in `morphogenesis.txt` and `anatomy.csv`. A signature comparison alone is not used as evidence of coherence.

## 6. Native physics results

The native audit instantiated **790 genotypes / 4,392 physical masses**, covering the entire corpus in both modes. It used Collider2D.Distance at explicit native poses, rejected invalid queries, and treated penetration beyond .003 m as an overlap. It also verifies Ready-state visual/native pose agreement and new hinge/fixed anchor coincidence.

| Initial native collision audit | Legacy | Developmental |
|---|---:|---:|
| Overlapping pairs | 2,509 | 0 |
| Affected specimens | 88 | 0 |

No Legacy overlap was fixed or reclassified as a new-rule success. Accepted Developmental bodies were measured natively; the conservative hull's own prediction is not the evidence.

Seventy isolated neutral-arena trials use **35 identical inputs per mode**, with no physical opponent. The subset was fixed before runtime: indices 0–7, 11, 23 and every 24th-index selection listed in the evidence, all authored new fixtures and all real files. Each runs for eight simulated seconds unless eliminated. Horizontal movement is measured after two seconds of settling, separately from falling.

A **locomotion candidate** requires more than .5 m net horizontal travel, at least one second of arena-surface contact, at least 80% retained mass, an active actuator at tick 300 and at least 7.9 simulated seconds. This is an observational threshold, not proof of a gait, a claim of intelligence or a fitness function. It never feeds back into the genome.

| Motion observation | Legacy | Developmental |
|---|---:|---:|
| Locomotion candidates | 4 | 4 |
| Immobile | 23 | 20 |
| Containment/structural failure | 2 | 1 |
| Active motors without locomotion threshold | 5 | 2 |
| Passive drift | 1 | 8 |
| Active actuator observations at tick 300 | 89 | 70 |
| Broken joints | 75 | 94 |
| Speed-clamp events | 400 | 50 |
| Numerical faults | 0 | 0 |

Coherence does not imply robustness. Developmental broke **more** joints despite having no birth overlaps and fewer speed-clamp events. We changed inherited circuits, surface anchors, masses and relative strength together; attributing the break increase to any one change requires an ablation. Failed motion remains a legitimate outcome. Horizontal motion of a shattered-but-retained body is also not evidence of useful walking.

Three additional same-genotype comparisons used neutral versus a third file containing uniform FF bytes. All produced a nontrivial physical trajectory difference while retaining the same expressed genome identity. The old biome decoding/mixing is unchanged. The original fourteen-match Phase 1 physics suite separately covers pause/ready, replay, finite control, root-neutral loss, single loss, double knockout, timeout and grace behavior.

These checks ran in actual Unity Play Mode and in the graphics-enabled Windows player. Static tests were also run under Mono, but are not presented as gameplay evidence. The isolated Editor encountered a Unity Search startup exception; validation scheduling uses Editor update rather than the shared delay-call delegate so an unrelated package callback cannot discard queued checks. The final standalone evidence does not depend on that Editor service.

## 7. Representative actual organisms and failures

Each comparison photograph uses the **same raw file on both sides**, A Legacy / B Developmental, with visible versions and native overlap counts. Fixed visual indices include minimal inputs, the original tapes, raw text/noise/incremental data, all new demonstrations, original interpreter C# and the real PNG. Every locomotion candidate from **either** mode's fixed trial subset is also included. Exact identities and selection rules accompany the screenshots; an authored success is never presented as a discovery from an unrelated real file.

- **Seven-byte incremental source, corpus-71.bin:** bytes 71–77. Developmental interprets the first opcode as a local actuator on a single sphere, with no neural network. Ground friction and angular drive produce about 4.05 m of post-settle travel in the isolated trial. Legacy interprets those same bytes as a sensor-only minimal organism. This is a simple powered-rotor abstraction, not learned walking.
- **Local rhythm tape:** seven inherited capsules with seven local motors and oscillator outputs in Developmental, versus two masses with no motors in Legacy. The phases differ through an encoded per-descendant increment. The tape is explicitly authored to demonstrate the grammar. Coordination does not guarantee locomotion.
- **Branch/return tape:** related branched boxes in Developmental; native birth exclusion succeeds, but its articulated structure can subsequently collapse and break. This is useful evidence of a coherent body that is still bad at being a body.
- **Asymmetric tape:** unequal, motorized branches; its isolated trial moves approximately −2.73 m while breaking joints. This is recorded as a motion candidate with structural damage, not a healthy gait.
- **Obstructed curl:** repeated identical EXTEND codons generate curling relatives until buds encounter older tissue. Blocked attempts stay in the trace. Legacy suppresses the adjacent repeated codons. Neither version is supplied a useful locomotion controller.
- **Elastic modules:** an authored repeated spring assembly with inherited phase/motor modules; approximately −1.42 m post-settle travel in the isolated trial, without a generic wave algorithm.
- **Real URP PNG:** Legacy has 29 masses and 68 native birth overlaps; Developmental has 20 related masses and none. Both can still become disordered after gravity, contacts and motors act. The image is read as bytes, not recognized as an image or assigned an image-species class.
- **Recipe/incremental/zero data:** minimal or inert outcomes remain common and valid. The same new grammar that makes modular structures can produce a solitary mass or an unused circuit.

Visual birth frames are important: a coherent arrangement need not remain standing after six seconds. After frames show actual outcomes rather than a curated static pose. All motion/collision values remain available independently of the selected screenshots.

## 8. Remaining limitations and useful next experiments

The grammar has changed its biases rather than removed all biases. Fixed 16-byte framing, promoter thresholds, four local node roles, a twelve-frame branch stack and conservative spatial exclusion all affect what can grow. Many files remain minimal and motorless. Anatomy is still a tree, development is performed before spawning, and no cyclic tissue attachments, live remodeling, fluid continuum, metabolism or conserved energy system exists. Powered root/detached torques remain an abstraction. Long-term native physics is not promised bit-identical across processors or Unity versions. A native modal file-dialog click-through and non-Windows builds remain manual/unperformed checks.

The next useful work is a **matched ablation**: keep each genome fixed and separately vary joint strength-per-weight, motor torque-per-mass and spring damping, measuring breakage and post-settle motion. That would explain failures without optimizing creatures for winning. A second experiment should compare the same byte contents across several biomes and codon budgets, documenting neutral mutations and alignment sensitivity. Broader real-file inputs and small source edits would test developmental locality better than adding more demonstration tapes.

Before Petri Dish reproduction, add explicit energy accounting and mutations over the versioned developmental program. Mini Kingdoms should reuse these embodied structures and local circuits rather than replace them with generic inhabitants. Neither future simulation nor automatic evolutionary fitness is included here.
