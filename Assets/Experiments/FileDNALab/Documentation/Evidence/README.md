# Verified evidence — October 30, 2026

Unity 6000.4.1f1 compiled the actual project and built `Builds/FileDNALab/File DNA Lab.exe`. The scene list supplied to the builder contained only `Assets/Experiments/FileDNALab/Scenes/ThePit.unity`. No package or build-scene configuration change is part of the delivered source.

- [genetics.txt](genetics.txt): Unity genetics checks, 228 inputs, 18,230,666 source bytes, 50 folded files and 179 unique expressed genomes. Includes full segment and actuator distributions.
- [physics.txt](physics.txt): actual Unity **Play Mode** simulation, 14 autonomous matches across three environmental modes, plus targeted rule/replay checks. Four naturally broken joints, one speed clamp and no non-finite physics states.
- [player-genetics.txt](player-genetics.txt) and [player-physics.txt](player-physics.txt): independent checks executed inside the built Windows player, with the same distributions and physics measurements.
- [player-complete.txt](player-complete.txt): the actual observer scene advanced autonomously for 4.5 simulated seconds, paused, and captured its rendered scene.
- [build-sha256.txt](build-sha256.txt): hashes of the built executable and the three player assemblies containing this experiment.

These are recorded results, not a claim that all possible files or all 228 genotypes were exhaustively played. All 228 undergo definition/graph checks; 14 matchups and additional targeted cases exercise native Unity physics. Native browse-dialog interaction remains a manual check. The largest permitted anatomy and every cross-platform runtime combination have not been exhaustively stress tested.

The final **graphics-enabled** Windows run used Direct3D 11 on an NVIDIA RTX 3050 Laptop GPU. Both 1600×900 and 1280×720 captures were opened and visually inspected; joint/contact/motor overlays remain inside the chamber. The authored chain visibly coils on the platform beside the inert recipe body. No automatic approach-to-opponent behavior is present.

Screenshots and full process logs are generated artifacts beneath `Logs/FileDNALab/PlayerRelease/`:

- [1600×900 observer](../../../../../Logs/FileDNALab/PlayerRelease/the-pit-desktop.png)
- [1280×720 observer](../../../../../Logs/FileDNALab/PlayerRelease/the-pit-1280.png)
- [Chamber viewport](../../../../../Logs/FileDNALab/PlayerRelease/the-pit-world.png)

The generated Logs and Builds directories are ignored by the existing repository configuration. Retained text evidence here survives without those directories. Recreate them with the README validation/build commands. Screenshot validation rejects a black framebuffer; launch the player without `-batchmode`/`-nographics`.

The project was clean before implementation. Unity's build processors temporarily serialized URP prefilter/runtime settings and player preloads/batching defaults; those build-only changes were removed after Unity exited. Final tracked-file diff is empty outside the newly added FileDNALab folder and its `.meta`. Null Menagerie, Terrarium and existing scenes remain unchanged. No Git commit was made.
