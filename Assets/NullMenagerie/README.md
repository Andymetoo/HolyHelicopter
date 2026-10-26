# NULL MENAGERIE

Every file contains a monster. This independent Unity prototype turns local bytes into living ASCII specimens: six anatomical families, curious mutations, and short real-time duels inside a terminal containment pit. All prototype assets live in this directory. Holy Helicopter's scenes, gameplay, packages, settings, and build-scene list are not required to change.

**Play.** Open `Assets/NullMenagerie/Scenes/NullMenagerie.unity` in Unity **6000.4.1f1**, then press Play. Six synthetic specimens are ready immediately, covering every body and ability. Select a cabinet card, inspect it, choose **ASSIGN / A** or **ASSIGN / B**, select stances, and **RELEASE THE CREATURES**. A 1600 × 960 Game view is a good starting size; the interface scales to fit other resolutions.

**Discover.** Use **DISCOVER A FILE** for the Windows native picker, or **PATH** to paste a full local file path. The Unity Editor also accepts a file dropped into the Game view. Choose **FIELD GUIDE** to see the selected file's measurements, fingerprint, and mutation explanation. The cabinet holds 36 specimens for the current session; the guide can remove a specimen that is not assigned to either fighter. Reimporting an identical signal selects its existing card.

| Control | Action |
| --- | --- |
| Space | Start a duel, pause, or resume |
| I | Open the file picker |
| R | Start another bout after the result |
| M | Toggle synthesized sound |
| Escape | Close the path dialog or field guide |
| TEMPO | Switch between normal and double speed |
| RESET PIT | End the current experiment and choose another pairing |

**File genetics.** `FileAnalyzer` measures normalized byte entropy, Deflate compressibility, repetition at short intervals, readable ASCII proportion, zeros, byte transitions, line density, and byte diversity. `SpecimenGenerator` uses logarithmic size (`log2(bytes) / 32`, clamped), so enormous files gain diminishing bulk. Bulk raises health while reducing movement and attack frequency; repetition and compression strengthen the shell; readable data extends reach; chaotic bytes accelerate specials. File family adds anatomy and fighting preferences. Fingerprints supply names, color variation, anatomical details, and rare exceptions rather than arbitrarily assigning the combat statistics.

| Ancestry / ability | Behavior |
| --- | --- |
| Executables and video / EXECUTE | Charge, knockback, and stun |
| Audio and strongly repeating unknown data / ECHO | Three delayed sonic pulses |
| Archives / UNPACK SWARM | Six staggered byte projectiles |
| Images / RESTORE POINT | Self-repair and protective shield |
| Text and documents / READ / CONSUME | Ranged vitality drain |
| Other unknown data / BAD ADDRESS | Blink behind the rival, disrupt its special timer, and strike through armor |

**Unexpected life.** Empty or nearly zero-filled files become **Hollow** wraiths that phase away once below half health. Exceptionally repeating files can become **Ouroboros**, shedding a fatal body and returning once. Rare fingerprints produce a **Crown** shield or **Twin** heart that echoes every third strike. These forms have tradeoffs as well as benefits. Try a blank file: absence has an anatomy here.

**Combat.** Fighters hunt, circle, retreat, wind up, attack, stagger, and die. **HUNT** trades protection for pressure; **EVADE** trades damage and tempo for protection and distance. Specials have distinct timing and effects, with glyph projectiles, impact flashes, particles, brief hit pauses, and synthesized sound. Rising pressure and eventual garbage collection prevent endless defensive matches. A deterministic 60 Hz simulation runs independently of rendering; rematches start with fresh health and a new bout seed.

**Privacy and determinism.** Import reads local files only. This prototype never executes imports, uploads contents, or changes original bytes. It does not decode media or document formats, so malformed and unknown formats still work as raw data. File analysis runs off the main thread. Files up to 256 KiB are read completely; larger files contribute sixteen evenly distributed 16 KiB windows, including the beginning and end. Read volume and analysis memory stay bounded regardless of source size. SHA-256 covers the versioned sample, exact file size, and lowercase extension. Base-name, path, and timestamp changes do not change genetics; changing the extension can. **Changes outside sampled windows can remain undetected**, so this is a specimen fingerprint, not a whole-file integrity checker. Locked, missing, changing, network, device, symbolic-link, and cloud-placeholder paths return recoverable errors; use the original local file.

**Build and implementation.** Use **Null Menagerie / Build Windows prototype** for the isolated Windows build. `Runtime` separates file analysis, generation, anatomical glyph rendering, combat, native import, audio, and application UI. There are no added packages, downloaded art assets, or runtime network services. Native picker interop follows Microsoft's [OPENFILENAMEW reference](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/ns-commdlg-openfilenamew); the dedicated scene build uses Unity's [BuildPipeline.BuildPlayer API](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/BuildPipeline.BuildPlayer.html).

The standalone is `Builds/NullMenagerie/Null Menagerie.exe`. Builds pass an explicit scene list and preserve shared project settings, including application metadata and default window mode. Add `-screen-fullscreen 0 -screen-width 1600 -screen-height 960` to launch windowed. The delivered build is a release build with development-player discovery disabled.

**Validation.** The dependency-free genetics harness executed **34 passing checks**, including deterministic identity, source preservation, empty/unknown/locked/missing files, cancellation, and bounded 8 MiB sampling. All **36 starter pairings** completed in 9.5-22.7 simulated seconds; all six abilities, all four mutations, 30/60 Hz deterministic replay, bounds, death, and fresh rematch checks passed. All runtime scripts compiled against the installed Unity assemblies without warnings or errors. Actual Windows picker tests passed cancel, exact Unicode-path selection, unchanged source SHA-256, and unchanged working directory. Validation harnesses and logs live under ignored `Logs/NullMenagerie`; use **Null Menagerie / Validate prototype** to run the core checks again in Unity.

Unity **6000.4.1f1** also executed the full core suite and built Windows successfully with zero build errors. A visible Windows player test on an RTX 3050 rendered the cabinet, animated combat, inspection and result flow, completed a bout, started a rematch, imported a real fixture asynchronously, deduplicated it, and recovered from a missing-file error with **zero runtime errors**. Screenshots were inspected and font loading and nested panel placement were corrected. Final screenshots and the smoke report are under `Logs/NullMenagerie/Final`; build logs are under `Logs/NullMenagerie`. Validation used an isolated project copy with the existing packages and render settings; the original project settings and Holy Helicopter edits were preserved. Audible playback quality and exhaustive balance across arbitrary real-world files were not validated.

**Scope.** Implemented: import, discovery animation, procedural creatures, inspection, session cabinet, stances, duels, results, replay, sound, and Windows picker. Deferred: native drag-and-drop in the standalone player, saved collections, team battles, tournaments, folder batches, and media-specific decoding. Balance is prototype tuning rather than a promise that every file has equal strength. Extremely slow or failing storage can still delay its background read; canceling cannot interrupt an operating-system read already in progress.
