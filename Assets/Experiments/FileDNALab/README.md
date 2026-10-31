# File DNA Laboratory — The Pit / Phase 2

Open **`Assets/Experiments/FileDNALab/Scenes/ThePit.unity`** in Unity **6000.4.1f1**, enter Play Mode and press **Start / resume**. Menu: **File DNA Lab → Open The Pit**. Developmental Genetics is the default. A begins as raw recipe text; B begins as an explicitly authored instruction tape demonstrating local rhythm inheritance. There are no anatomy templates, predefined creature classes, combat policy or hash-seeded creature generator.

Phase 2 adds inherited growth, surface-port assembly, branching/return, literal and block repetition, local neural modules, phase inheritance, a developmental trace, strict versioned saves and same-file comparison. [Phase2Diagnosis.md](Documentation/Phase2Diagnosis.md) records the original problems; [DevelopmentalLanguage.md](Documentation/DevelopmentalLanguage.md) specifies the actual new language. [Phase2Report.md](Documentation/Phase2Report.md) reports measured results, including failures and limitations.

## Import and compare

Each source panel has **Legacy / Developmental** selections. Import any ordinary local file using **Browse**, or paste its absolute path and press **Import**. The chosen mode interprets its actual bytes. TXT, PNG, audio, archives, unknown binary files and executables all work as binary input; none are executed or semantically parsed. Accepted files are streamed completely in a background task. Default limit: **256 MiB**, without silent truncation. Oversized/unreadable input produces an error and retains the previous matchup, paused.

Changing a panel's mode explicitly reconstructs that **same decoded source** using the other interpreter. For a side-by-side match using identical bytes, select the organism, open **SOURCE**, and press **COMPARE THESE BYTES / Legacy vs Dev**. A becomes Legacy and B becomes Developmental; the selected biome is retained. Press Start to observe. Both sides use the same match rules, with no advantage correction. SOURCE shows distinct content fingerprint, interpreter version and expressed signature.

**Next built-in sample** cycles Phase 1 inputs plus seven labelled Phase 2 grammar fixtures. The Phase 2 fixtures also exist as real raw files in `Samples/`, usable with Browse/Import. Their names describe authored demonstrations; the interpreter never uses a sample name or selects a body type. The corpus/visual evidence explicitly distinguishes authored demonstrations from arbitrary text, images and binary inputs.

## Observer controls and inspector

- **Start / resume**, **Pause**, **Reset / rerun**. Space toggles pause when a text field does not own focus; R resets. Reset restores the same bytes, versions, construction and biome, ready to start.
- Speed: 0.1×–8×. The clock measures actual simulated time; slow hardware can run below the requested rate. Change the next timeout (10–180 s), then reset to apply.
- Wheel over the chamber zooms; middle drag pans; **Fit bodies** frames the platform and retained parts.
- Click a mass, choose A/B, use **Inspect next mass**, or select it in the anatomy tree.
- **BODY** displays physical properties, ancestry, current state, actual birth event, inherited modifier chain, surface ports, joint parameters, associated circuit components and native birth-overlap count.
- **CIRCUIT** shows resolved sensors/neurons/connections, local roles, inheritance IDs, oscillator phase advances and live motor signals. Missing references are discarded, never replaced with useful connections.
- **PROGRAM** lists actual developmental events, source byte regions, REPEAT triggers, blocked buds, terminated growth, missing roles and component-budget losses. Legacy still shows its original codon program, including duplicate suppression.
- **SOURCE** shows file statistics, raw content identity, version, expressed identity, save and compare actions. **HISTORY** retains the last 100 completed matches, preserving older entries; new entries also record both grammar versions and expressed identities.
- Physical overlays show segment IDs, connections, contact points and signed motor activity. Neural overlays draw actual synapses between their host parts. Within-part connections are easier to inspect in CIRCUIT.

Development is deterministic and bounded at import, then instantiated as real 2D bodies. It is not an animation of growth or a predetermined gait. Identical files, interpreter versions and limits construct the same organism. Long-term physics is not guaranteed bit-identical across machines or Unity versions.

## Biomes and outcomes

**Neutral** provides standardized gravity and platform conditions. Import a third raw file for **File** biome mode, or choose **Paired** for the original symmetric lane-wise mixture of both contestants' tapes. The environmental mapping remains FDNA-1. Changing the organism interpreter does not rewrite its source tape or its environment. Gravity, wind/drag, friction, bounce, span, slope, environmental cycle, central obstacle and optional orange updraft tile are displayed.

Every part retains its contestant's ownership when a joint breaks. Crossing y < −11, |x| > 65 or y > 55 irreversibly loses that part's mass for the match. Loss of at least **60% of original total mass** eliminates a contestant. The root has no privileged survival role. A two-second grace allows double knockouts. Both retaining enough mass at timeout is a draw; immobility is not a hidden loss. Non-finite physics is quarantined and counted separately. Poor designs can still collapse, roll over, detach, fling themselves off or do nothing.

Developmental buds use inherited proportions and physical surface ports. An obstructed bud fails at its encoded location and is recorded. The builder does not search for a viable position. Adjacent collisions are encoded by JOINT and can be globally disabled through the observer's serialized **Honor Encoded Adjacent Collisions** field; Legacy retains its original collision rule. No shared physics settings, input settings, layer configuration or Time.timeScale change is required.

## Save and reload without silent migration

SOURCE → **Save portable DNA** writes `<SHA256>.<interpreter>.fdna` under `Application.persistentDataPath/FileDNALab`; the exact path appears in the status line. Paste it into a contestant field and choose **Load DNA**. New `FDNA-PROGRAM-2` archives preserve interpreter version and expression limits as well as the reduced source tape. Bare Phase 1 `FDNA-1` archives always load as **Legacy**. Unsupported versions are rejected. Changing interpretation is an explicit action.

**Import** always interprets raw file bytes, even if the file happens to be an archive; **Load DNA** explicitly restores an archive. Archives store reduced codons, ranges, statistics and claimed original SHA, not the full source file or authenticated provenance. Re-import the original to verify its content fingerprint. The old format did not save nondefault expression limits, so old bare archives use the original defaults. Match history remains `history.json` in the same save directory. Automated validation suppresses history writes.

## Validation and builds

**File DNA Lab → Phase 2 → Validate both genetic modes** runs a 395-input comparison when the supplied repository samples are available (391 deterministic fixtures/demonstrations plus real files). It checks both modes, archives, lineage and circuits, then enters real Play Mode for native construction and physics. When invoked inside Play Mode, it pauses/restores the current match while checking separate physics scenes. Results default to `Logs/FileDNALab/`: CSV inputs/anatomy, native overlap audit, isolated motion trials, environment comparisons and readable summaries. Corpus size can change when real project files change; `inputs.csv` records exact names, sizes and hashes.

**Phase 2 → Build Windows developmental observer** writes `Builds/FileDNALabPhase2/File DNA Lab.exe`, preserving the previous Phase 1 build folder. No new package is required. The original validation entry points remain available for Legacy regression checks.

For batch Unity use `-executeMethod FileDNALab.Editor.LabTools.ValidatePhase2` or `.ValidatePhase2AndBuild`. **Omit `-quit`**: these methods exit batch Unity after Play Mode checks finish. Optional arguments: `-fdna-evidence <directory>`, `-fdna-source-root <repository root>`, `-fdna-build <exe path>`. Supply the source root when using an isolated project copy.

For a built Windows player use `-fdna-phase2 -fdna-source-root <repository root> -fdna-evidence <directory>`. Run with normal graphics, **without `-batchmode` or `-nographics`**, to record same-input Legacy/Developmental birth/after screenshots and a compact layout. Native construction checks cover the full corpus; the fixed motion subset and visual selection rules are recorded with their evidence. Black framebuffers are rejected. The original `-fdna-smoke` runner remains supported.

The Editor's native Browse and Windows standalone dialog are provided without a package; standalone non-Windows platforms use path entry. Native modal-dialog interaction remains a manual check. See [Phase 2 evidence](Documentation/Phase2Evidence/README.md) for recorded runtime results and screenshots. The original [Phase 1 report](Documentation/ExperimentReport.md), [FDNA-1 grammar](Documentation/GeneticLanguage.md) and [Phase 1 evidence](Documentation/Evidence/README.md) are retained.
