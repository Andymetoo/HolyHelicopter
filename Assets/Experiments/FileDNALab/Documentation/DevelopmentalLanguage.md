# FDNA-DEV-2: developmental genetics

The language is implemented in engine-independent `Core/DevelopmentalInterpreter.cs`. `Core/Interpreter.cs`, its FDNA-1 grammar and the Legacy physical construction path remain available. This is a different interpretation of the same raw bytes, not a visual skin or a hash-seeded generator. A content fingerprint never participates in growth or wiring.

## Input, loss and bounds

Both modes use the **same unchanged BinaryDna reader and environmental DNA mapping** described in [GeneticLanguage.md](GeneticLanguage.md). At default limits, files up to 8192 bytes become literal 16-byte codons, with a zero-padded final codon. Larger files are partitioned across their full length into 512 contiguous regions. Each region becomes 16 lanes using lane mean plus an alternating-row sum modulo 256. Each original byte is read and contributes to that reduction, but the reduction is lossy: paired rows may cancel, different inputs can reduce to the same tape, promoters can silence instructions, and many arguments are unused by a given opcode. Global entropy/frequency/repetition statistics remain inspection data; they are not secretly converted into creature classes.

For each codon `opcode = b0 & 15`. A developmental promoter is active when **b15 ≤127**, rather than the Legacy threshold of 95. Identical consecutive codons are **not** suppressed. An instruction may therefore repeat physical features, alter a developmental state repeatedly, terminate growth, or do nothing depending on its actual opcode. A uniform zero file asks only for REST.

Default limits remain 40 masses, 64 nodes, 24 motors, 192 synapses and 192 output connections. Development adds **2048 executed instruction steps**, a 12-frame branch stack, four named node roles per local circuit and eight connection patterns per local circuit. MaxDevelopmentSteps is configurable from 16 to 8192. Repeated instructions count against the execution budget; nested REPEAT operations are suppressed. Rejected births, missing roles, local/global component caps, silent promoters and source codons not visited after an execution limit are recorded. Reading the whole file is not a claim that every byte is expressed.

## The inherited growth state

A single constitutive physical mass is created from the first codon. It is not a head, central brain or obligatory living core. Empty input creates one inert circle, diameter .45, density .4, with no sensors, neurons, motors or connections. The first codon is still considered as an instruction after birth.

The developmental turtle carries a current growth point, world growth heading, relative twist, future proportions/shape, density/friction/restitution/damping, joint/attachment settings, strength-per-weight ratio, a local circuit blueprint, pending modifier origins and a terminated-tip flag. Growth copies these traits instead of assigning unrelated values. A new part becomes the next growth point; its local circuit copies the parent's current blueprint, with encoded phase advance. A saved branch frame preserves its prospective traits and circuit. Returning to an ancestor uses that body's physical properties and its actual current circuit blueprint.

`u(b)=b/255`; `s(b)=(b−127.5)/127.5`; `scale(x)=2^x`. All changes below are clamped to the documented safety ranges.

### Constitutive traits

The first region sets shape `b1 %3`, width `.45+1.75*u(b2)`, height `.45+1.75*u(b3)`, density `.4+1.8*u(b4)`, friction `.2+.9*u(b5)`, restitution `.35*u(b6)` and rotation `90*s(b7)` degrees. Initial joint kind is `b8 %4`, limit `30+150*u(b9)`, frequency `.75+5*u(b10)` Hz, strength ratio `.5+10*u(b11)`, linear damping `.05+.6*u(b12)` and angular damping `.1+.8*u(b13)`. Initial gap is .065 m, spring rest multiplier 1 and joint damping ratio .5. No circuit is supplied.

For circles the physical diameter is width. Developmental mass uses the **actual primitive area** times density: circle `π*w²/4`, capsule `short*(long−short)+π*short²/4`, box `w*h`, clamped to .05–15 kg. Legacy retains its original rectangle-area mass proxy. Body dimensions clamp to .18–3.6 m. The initially undivided root can grow through PROPORTION, including a large single-body outcome.

## Instruction table

| Opcode | Developmental operation |
|---|---|
| 0 REST | No change. |
| 1 EXTEND | Inherit the current growth state. Multiply width/height by `scale(.12*s(b1/b2))`; turn heading by `22*s(b3)` degrees; add `12*s(b4)` relative twist for this birth. Grow at the chosen surface port, then use the child's orientation as the continued frame. |
| 2 BUD | Save the current growth frame; resume even a terminated point. Turn by `150*s(b1)`; scale width/height by `.55+.45*u(b2/b3)`; attempt one child, then grow within that branch. Does not jump to an arbitrary body index. |
| 3 RETURN | Restore a saved branch frame; otherwise return to the physical parent, or remain at the root. |
| 4 PROPORTION | Future width/height multiply by `scale(.65*s(b1/b2))`. `b3%4`: keep/circle/capsule/box. If bit0 of b4 is set and there is still only one mass, also resize that undivided mass. Existing multi-part anatomy is never retroactively rearranged. |
| 5 MATERIAL | Future density multiplies by `scale(.7*s(b1))`; add `.35*s(b2)` friction, `.15*s(b3)` restitution, `.7*s(b4)` linear damping, and `s(b5)` angular damping. Retains all other inherited values. |
| 6 JOINT | Future link `b1%4`: hinge/spring/fixed/distance. Add `60*s(b2)` angle limit; multiply frequency by `scale(s(b3))`; damping `.1+.85*u(b4)`; spring rest multiplier `1+.25*s(b5)`; strength ratio multiplies by `scale(.8*s(b6))`; adjacent collision from bit0 b7; port gap `.065+.18*u(b8)`. |
| 7 ACTUATOR | Create/replace the local and future motor blueprint: speed `60+480*u(b1)` deg/s, torque-per-mass coefficient `.2+12*u(b2)`, bias `s(b3)`. Bit7 b4 removes it instead. There is no global modulo body address. |
| 8 SENSOR | Create/replace local role `b1%4`, sensor kind `b2%7` (contact, angle, orientation, velocity x/y, proximity, height). Bias `s(b5)`, memory `.95*u(b6)`. The common node fields also retain frequency `.05+3*u(b2)`, phase `2π*u(b3)`, phase advance `π*s(b4)`; those fields drive oscillators only. |
| 9 OSCILLATOR | Same named local role and node parameters as SENSOR; input is its inherited oscillator. Each descendant copies phase **plus** encoded phase advance. |
| 10 NEURON | Local role `b1%4`; bias `s(b2)`, memory `.95*u(b3)`. No external sensory input. |
| 11 CONNECT | Add a local wiring pattern. Source role `b1&3`, source scope `(b1>>2)%3`: local/parent/founder. Destination is local motor when bit7 b2 is set, otherwise local node role `b2&3`; weight `3*s(b3)`. Missing roles are dropped explicitly, never redirected to arbitrary nodes. |
| 12 TURN | Turn future world heading by `90*s(b1)` and add `45*s(b2)` to inherited relative twist, bounded ±90°. |
| 13 REPEAT | Replay the preceding `min(regionIndex,1+b1%8)` source codons for `1+b2%4` additional passes. Each replay keeps both its own source region and the triggering REPEAT region. Nested repeats are recorded but not expanded. |
| 14 ANCESTOR | Walk `1+b1%8` parent edges (stopping at root), restore that body's inherited growth template, resume it, and set heading to its physical angle plus `180*s(b2)`. |
| 15 END | End the current branch by restoring a saved frame; otherwise terminate the current growth tip. Later BUD or ANCESTOR can resume growth. Remaining file instructions are still decoded. |

Growth is constrained by space and execution limits, not by viability or an anatomical quota. All three primitives, all four joint types, rigid or floppy branches, single masses and motorless organisms use this same language. No "worm", "spider", symmetry, gait, opponent-seeking or fitness mode exists in the interpreter.

## Physical surface assembly

`GrowthGeometry.Support` computes actual convex primitive surface ports in the selected growth direction. Boxes use a support face/corner; circles use radius; capsules use a radius plus their axial line segment. The child is placed so its opposing surface port sits the encoded gap beyond the parent's port. Child orientation follows the inherited growth frame and encoded twist, rather than an unrelated absolute rotation.

A conservative 32-sided convex hull with .04 m skin tests the proposed part against **all existing parts**. The skin covers the polygon approximation and native convex collider margin; the native audit remains the authority. An earlier .012 m skin failed the native audit by about .008 m and was corrected. If obstructed, the bud fails at its encoded location. The developer does not choose an alternative angle, move an older part, compress the creature, add a limb or repair its controller. Subsequent byte instructions can still turn or return to a different growth point. This is a local developmental exclusion rule, not a viable-animal layout algorithm. AABB rejection bounds most collision-check work; complexity remains capped.

Native 2D colliders instantiate the accepted plan. Hinge and fixed anchors meet at the midpoint of the two nearby surface ports. Spring/distance links use the ports themselves, not coincident pivots or remote centres. Spring rest length is the gap times its inherited multiplier; only an encoded deviation from 1 preloads it. Distance rest length is the initial port separation. Frequency is bounded .25–8 Hz, damping .1–.95; strength is `mass*9.81*strengthRatio`, bounded 15–1815 N. This scales structural strength with mass without preventing weak limbs from breaking. Break torque retains the .6-times-force convention.

Adjacent collision is encoded per joint and can be globally disabled with the observer's serialized `honorEncodedAdjacentCollisions`; Legacy always retains its original disabled-adjacent rule. Nonadjacent self collision remains active. Speed, torque, acceleration and non-finite-state safeguards from Phase 1 remain in effect. There is no change to shared Physics2D settings or global Time.timeScale.

The native birth audit calls Collider2D.Distance at explicit Rigidbody2D poses and counts separation below −.003 m as penetration; invalid queries are failures, not zero overlaps. A negative separation represents actual overlapping colliders. See Unity's [Collider2D.Distance](https://docs.unity3d.com/kr/6000.0/ScriptReference/Collider2D.Distance.html) and [ColliderDistance2D](https://docs.unity3d.com/ja/6000.0/ScriptReference/ColliderDistance2D.html) documentation. Native measurements and screenshots, not the hull approximation alone, establish construction evidence.

## Neural development

Neural role definitions and connection patterns are snapshots taken during physical growth, not a generic controller attached afterward. Local role numbers preserve structural meaning as a branch repeats. A CONNECT definition can link a child's role to the corresponding parent's role or to the founder's role. It can also do nothing when a role does not exist. Role replacement affects the current module and later descendants; previously grown descendants keep their earlier template.

After growth, those captured modules receive bounded runtime node/motor indices. This is allocation and reference resolution of already developed circuits, not new AI. Actual parent-node and parent-motor inheritance IDs are recorded when the corresponding definition still exists. Definition events, birth events and source regions remain separate. Later role replacement can make an original parent role unavailable; the trace records missing-role outcomes and cap losses per body.

Inherited oscillators retain rate and gain but advance phase at birth, making common rhythms or traveling phase differences possible. There is no wave generator outside the encoded oscillator/phase/module rules. Runtime uses the original recurrent tanh update and motor summation. Torque is `coefficient*mass*max(width,height)/2`, bounded .05–35 N m. Powered rotors still have no metabolic energy budget; detached/root actuators remain the Phase 1 abstraction.

## Versioned archives and inspector truth

New saves use an `FDNA-PROGRAM-2` envelope containing the exact interpreter version, mode, all expression limits and the original FDNA-1 reduced-tape archive. Both Legacy and Developmental saves replay their recorded mode and limits. Old bare FDNA-1 archives always load as **Legacy** with original default limits; the old format never stored nondefault limits. Switching mode requires an explicit observer selection. Unsupported versions are rejected. Filenames and timestamps remain outside identity.

SOURCE distinguishes raw content fingerprint, interpreter and expressed signature. BODY lists physical parent, growth depth, birth event, actual inherited-modifier event chain, ports, native overlap count and local components. PROGRAM shows ordered state changes, replay origins, blocked births, missing roles and limit losses. CIRCUIT lists the resolved graph and live signals. Existing history records load as Legacy when version fields are absent; new entries retain both versions and expressed signatures. Validation suppresses history writes.
