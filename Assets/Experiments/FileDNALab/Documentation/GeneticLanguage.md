# FDNA-1 genetic instruction language

The core assembly has **no Unity engine references**. `BinaryDna` reads raw streams into `DnaDocument`, `Interpreter.Express` produces reusable `Genome` definitions, and `Biome.Decode` uses the same document to produce environmental parameters. The Pit owns physics, match rules and observer UI. There is no dependency on Null Menagerie's profile, seed, art or combat code. Petri Dish and Mini Kingdoms are not implemented; they can instantiate the same segments, materials, neural graph and actuators without importing arena rules.

## Reading and bounded reduction

Defaults: 256 MiB per file; 512 codons; 40 segments including the constitutive mass; 64 neural nodes; 192 synapses; 192 motor-output connections; 24 actuators. These are serialized on the scene observer. Supported configurable ceilings are 1 GiB, 4096 codons, 80 segments, 128 nodes, 512 connections and 80 actuators. Runtime mass, dimensions, acceleration, torque and speed retain hard safety bounds. Import rejects oversized files **before reading**, never truncates them, and streams every accepted byte once in 64 KiB buffers. Work is O(file size), resident decode memory O(codons × 256), independent of file size. The process holds only one region histogram at a time, so actual histogram memory is O(256); each stored region has 16 gene bytes and trace/statistical fields.

For lengths ≤ 8192 with default limits, consecutive 16-byte blocks are literal codons. The last block is zero padded. Empty input yields one all-zero codon with a zero-length source range. The first codon always expresses one constitutive physical mass, even when its instruction is silent. This is the minimum physical substrate, not a prescribed head or brain.

For larger files, 512 contiguous regions partition the **entire** original stream: region i spans `[floor(N*i/K), floor(N*(i+1)/K))`, K = MaxCodons. Within each region, source index j contributes to lane `j mod 16`. For each lane, accumulate its sum, count and an alternating row sum: rows 0,2,4… add and rows 1,3,5… subtract. The expressed lane is:

```
mean = floor(sum / count)
phase = alternating_row_sum % 256        // signed remainder
lane = (mean + phase + 256) % 256
```

This is a deliberately lossy positional reduction, **not a cryptographic digest or random seed**. Every byte contributes to a lane; changing a distant byte can change the corresponding region. Some edits necessarily collide after reduction or land in silent/unused fields. Region ordering and row phase matter; the original byte offsets remain available. Regions retain local entropy, adjacent repetition and mean absolute byte transitions. Whole-stream frequency counts, entropy, repetition, transition and SHA-256 are also computed. Entropy is Shannon entropy / 8; transition is mean absolute adjacent difference / 255. Repetition is adjacent equality fraction, a run/compressibility proxy; no full compression parser is used. Statistics are inspection evidence, not creature classes. The SHA identifies exact raw content and never participates in construction, wiring or biome parameters. Filename, extension and timestamps never influence expression.

## Development

For each codon b[0..15], `opcode = b[0] & 15`. Its promoter is active only when **b[15] ≤ 95**. A codon identical to its immediately preceding codon is methylated and skipped, preventing uniform/repeated tapes from blindly saturating every cap. The PROGRAM inspector records every instruction and its actual effect, silence or cap. A cursor starts at mass S0. Instructions execute in source order; creation and physical modification target the developing body, not an archetype.

`u(b) = b/255`, `s(b) = (b−127.5)/127.5`. Segment and motor targets resolve by modulo available segment count at expression time. Deferred neural references resolve modulo the **final** graph size. If there are no nodes or motors, impossible connections are discarded. No components are added to make the graph useful.

| Opcode | Operation and arguments |
|---|---|
| 0,15 | REST: no operation |
| 1 | GROW: new body attached to cursor; cursor becomes child |
| 2 | BRANCH: new body attached to `b1 % segmentCount`; cursor becomes child |
| 3 | SEEK: cursor becomes `b1 % segmentCount` |
| 4 | MATERIAL: cursor density `.15+5*u(b2)`, friction `1.5*u(b3)`, bounce `.8*u(b4)`, linear damping `3*u(b5)`, angular damping `4*u(b6)` |
| 5 | JOINT: cursor link `b1 % 4`, stiffness `.25+9*u(b2)` Hz, strength `15+1800*u(b3)` N, anchor offset `.9*s(b4)` |
| 6 | MOTOR: target `b1 % segmentCount`, speed `30+690*u(b2)` deg/s, torque `.05+34.95*u(b3)` N m, bias `s(b4)` |
| 7 | SENSOR: segment `b1 % segmentCount`, kind `b2 % 7`, bias `s(b3)`, rate `.05+5*u(b4)`, phase `2π*u(b5)`, memory `.98*u(b6)` |
| 8 | OSCILLATOR: same node fields; input is `sin(2π*t*rate+phase)` |
| 9 | NEURON: same node fields; no external sensory input |
| 10 | WIRE: deferred node b1 → node b2, weight `3*s(b3)` |
| 11 | OUTPUT: deferred node b1 → motor b2, gain `2*s(b3)` |
| 12 | POSE: cursor initial rotation `180*s(b1)`, attachment direction `180*s(b2)`, gap `.02+.4*u(b3)` |
| 13 | LIMIT: cursor joint angle ±`180*u(b1)` degrees, strength `15+1800*u(b2)` |
| 14 | RESERVE: no operation, space for a future versioned extension |

The constitutive mass and GROW/BRANCH use this same body mapping:

| Property | Mapping |
|---|---|
| Shape | b2 % 3: circle / capsule / box |
| Width and height | `.24 + 1.8*u(b3 or b4)` world units; circle uses width for both physical axes |
| Density and mass | `.15+4*u(b5)`; mass proxy `clamp(width*height*density, .05, 15)` kg, including circles/capsules |
| Friction / restitution | `1.4*u(b6)` / `.7*u(b7)` |
| Linear / angular damping | `1.5*u(b8)` / `2*u(b14)` |
| Joint | b9 % 4: hinge / spring / fixed / distance |
| Direction / initial rotation | `180*s(b10)` / `180*s(b11)` degrees |
| Anchor lateral modifier | `.8*s(b12)` |
| Gap | `.03+.3*u(b13)` |
| Angle / frequency / break force | ±`180*u(b14)` degrees / `.25+9*u(b8)` Hz / `15+1800*u(b13)` N |

Morphology is an arbitrary rooted development tree with unrestricted branching. Roots are physical masses, not vital organs. Segment IDs and parent IDs are stable. Structure, material, joint/limit and pose retain separate source-region origins, so subsequent modifiers do not erase their provenance. Single property modifiers may supersede prior values within the same category; the inspector identifies the last contributing category operation, and PROGRAM preserves the complete history.

## Physical expression

Segments use native `Rigidbody2D` plus circle, capsule or box colliders. All adjacent body spacing uses the sum of bounding half-diagonals plus encoded gap, oriented by parent rotation and encoded direction. The body is then translated to its lane with its lowest bounding extent two metres above the platform. Neither side is mirrored, rescaled, balanced or repaired. Very wide creatures may overhang the platform at spawn. Nonadjacent self collision remains enabled; adjacent connected collisions are disabled. Hinges pivot at the midpoint plus encoded world-vertical anchor shift. Spring rest distance is 55% of initial parent/child separation. Distance joints preserve initial centre distance. Fixed joints use encoded frequency. Break torque is 60% of encoded break force. Spring damping ratio is `clamp01(.3*linearDamping)`; fixed damping ratio is .6. Hinge limits are disabled when encoded limit ≥170°.

Per-body gravityScale is zero. Environment gravity, wind and viscous resistance are applied explicitly as mass-scaled accelerations in a private local 2D physics scene at 60 Hz. No global physics settings, input mode, layer configuration or Time.timeScale is changed. The environment's acceleration is clamped to 80 m/s². Translation is bounded at 35 m/s and angular speed at ±1080°/s after each step; clamp events are counted. Non-finite state is quarantined and counted as numerical failure, rather than being reported as a legitimate physical outcome. These constraints protect the solver, not viability. A badly proportioned design is still allowed to break or fall.

## Neural execution

Sensors are contact (0/1), hinge angle /180, sine of orientation, horizontal/vertical velocity /12, directional proximity, or world height /10. Proximity is a six-unit ray along the host segment's local right axis, beginning just outside its bounding extent; it senses any physical surface, including other parts, without identifying enemies. Inputs are computed from the prior completed physics step.

At each fixed tick, all synapses read the previous tick's signals. Each node computes:

```
incoming = Σ(weight * previousSourceSignal)
target = tanh(clamp(externalInput + incoming + bias, -12, 12))
signal = clamp(memory * previousSignal + (1-memory) * target, -1, 1)
drive[m] = clamp(motorBias[m] + Σ(outputGain * currentNodeSignal), -1, 1)
```

Nodes, including sensors and oscillators, can receive genetically encoded recurrent connections. A node with no outputs can have no practical effect. A motor can operate from a constant encoded bias without any nodes. Multiple motors on one mass combine target speeds weighted by encoded torque, with total torque capped at 35 N m. Hinges use Unity's velocity motor. Other targets apply a bounded angular velocity servo torque, with equal/opposite torque to a still-connected parent; an unattached/root actuator is an abstract powered rotor. No linear thrust, automatic gait or target-directed force is supplied. There is no energy budget in this phase. CIRCUIT reports per-gene drive contributions; BODY reports the combined physical target and torque cap. Long-term physics is not claimed to be bit-identical across CPUs or Unity versions.

## Environmental DNA

Let m[j] be the mean of lane j /255 over **all** regions, and c the middle codon. The exact mapping is:

| Parameter | Mapping |
|---|---|
| Gravity | x = `5*(m0−.5)`, y = `−1−15*m1` |
| Ground friction / bounce | `1.5*m2` / `.65*m3` |
| Resistance / wind | `3*m4` / `7*(m5−.5)` |
| Platform width / slope | `16+16*m6` / `12*(m7−.5)` degrees |
| Wind cycle | angular frequency `.1+1.5*m8`; force multiplier `.65+.35*sin(t*frequency)` |
| Central obstacle | when c9 <96, height `2.2*m10`, otherwise absent |
| Updraft tile | when c11 <48, strength `.5+2*m12`, otherwise absent; at x = `.31*width`, local upward acceleration `15*strength` |

Neutral: gravity (0,−9.81), friction .65, restitution .05, zero drag/wind/obstacle/updraft, width 23, level platform. Paired mode averages bytes from corresponding decoded codons (shorter tape wraps), then uses the same biome mapping; this is experimental, symmetric physical mixing. Original source identities are retained in the biome label. Contestant genomes remain untouched.

## Identity and reuse

Content SHA-256 identifies raw bytes; `Genome.Signature()` separately hashes a canonical invariant-culture listing of **expressed physical and neural parameters**. Neither fingerprint drives development. Content can differ while expressing the same organism, as with empty input and a zero byte. This is intentional many-to-one genotype expression. Archives store FDNA-1, claimed identity, reduced codons, byte ranges and statistics with bounded input validation; they are not signed provenance certificates. Change the grammar version before changing established mapping semantics in future work.
