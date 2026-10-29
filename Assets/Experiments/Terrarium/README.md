# Terrarium

An isolated artificial-life observatory for Holy Helicopter. Open **[Scenes/Terrarium.unity](Scenes/Terrarium.unity)**. Everything needed by this experiment lives below this folder; it does not install a global bootstrap, change project settings, or depend on another experiment.

[Measured results and causal examples](Documentation/ExperimentReport.md) · [Rules and limitations](Documentation/Mechanisms.md) · [Evidence files](Documentation/Evidence/README.md)

## Launch and observe

1. Open the scene in Unity 6. Allow the two Terrarium assemblies to import.
2. Enter Play Mode when you are ready. The default seed is **7**, one of the measured seeds in the report. This implementation was developed without launching Unity or entering Play Mode; the static and headless validation described below does not replace that first runtime check.
3. Use a large Game view (1440 by 900 recommended). The scene presents a text grid, population roster, inspector, and event history.
4. Pause to inspect a moment, step one tick, or accelerate through 1x, 4x, 16x, 64x, and 256x. Space pauses and `.` steps. Choose an inhabitant on the map or roster. Follow magnifies the area around that inhabitant. The display reports achieved ticks per second; acceleration is limited by a per-frame CPU budget.
5. Switch between the observer's actual world state and the selected inhabitant's remembered world. A belief is a timestamped estimate, not a live read of its target.
6. Inspect **Why** for the chosen action, its perceived circumstances, expected benefit, and the twelve highest-scoring rejected alternatives. Earlier/later buttons navigate retained decisions. Inspect memories and directional relationships to follow accumulated consequences. Click an event or a cause link to inspect its witnesses, sources, and retained later consequences. History can follow the latest event or filter to the selected inhabitant.
7. Restart with the same seed to repeat the initial conditions, or enter another seed. Configuration fields on the scene's Terrarium Observer are applied when a new world is created.

The UI is an observer. Selecting a person or reading the truth map does not feed information to inhabitants. Simulation time advances only through explicit fixed ticks; rendering speed does not change the decisions within a tick.

## The behavioral foundation

Inhabitants have continuous motivational weights and deficits for food, water, rest, company, exposure, and uncertainty. Each has limited inventory and its own beliefs, experience, and relationship estimates. There are no personality classes or event scripts for named emotions.

Candidates use shared action primitives: consume, drink, acquire, deposit, give, converse, rest, construct, tend, and explore. The acquire primitive applies to both unowned resources and owned stock. Its desirability can include perceived ownership, anticipated social costs, urgency, travel, and learned outcomes. Ownership is a social attribution; it is not an invisible physical barrier.

Agents perceive locally. Resource beliefs retain a location, estimated stock, perceived owner, confidence, observation time, and information source. Person beliefs store visible cues rather than granting access to another mind. Conversations can transmit uncertain information. Revisiting a place can reveal that an expectation was wrong. Action outcomes update future expectations.

Relationships are stored separately in each direction. Trust, affinity, familiarity, attachment, expected care, and exchanges accumulate from interactions. A's estimate of B need not match B's estimate of A. These quantities affect decisions; labels such as friendship, betrayal, or jealousy are not simulation events.

## The physical foundation

The seeded grid contains walkable ground, obstacles, renewable resources, water sources, storage, and shelter. Objects expose reusable affordance flags and quantitative properties for nourishment, hydration, portability, construction material, protection, renewal, and cultivation. The prototype uses a small set of object kinds built from those properties.

Resource depletion, replenishment, food spoilage, exposure, weather, and physical needs operate continuously. Acquiring stock changes another person's opportunities. Depositing food makes reserves available in the shared physical world. Giving changes the receiver's resources and both participants' experience. Building and tending can change later costs and opportunities. Those are the connections the experiment is intended to investigate.

This is deliberately not a chemistry engine, a civilization game, or a hidden drama generator. Environmental cycles are physical processes; no subsystem schedules social incidents.

## Code and reproducibility

* `Core/` is a Unity-independent C# assembly with explicit seeded randomness. `Simulation.Step()` is the only time driver. `Model.cs` contains inspectable state and configuration.
* `Presentation/` contains the scene-local IMGUI observer. The core does not reference this assembly or Unity.
* `Validation/` contains a standalone harness, mechanism checks, and scripts. No packages or network restore are required. Generated executables belong in `Temp/Terrarium`, outside imported assets.
* `Documentation/` contains the measured experiment report and selected evidence.

Reproduction means the same code, configuration, seed, and runtime give the same state after the same number of ticks. Independent worlds have independent random streams. Cross-platform bit-identical floating-point arithmetic, save/load, and replay across code changes are not promised. The state digest is a diagnostic, not a save file.

Events, individual memories, and decision records have configured retention limits. Event IDs remain stable when old history is evicted; a causal reference may point to an event outside the visible window. The headless trace can preserve the complete event stream for a run.

## Headless experiments

From the project root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'Assets/Experiments/Terrarium/Validation/Run-Headless.ps1'
```

Requires Windows, an installed .NET SDK, and the .NET Framework 4.x reference assemblies already present on this machine. The process-local policy option permits these local scripts to run without changing your saved PowerShell policy. Default results go to `Temp/Terrarium/results`.

The harness checks deterministic stepping, isolated random streams, configuration validation, state invariants, and bounded histories, then measures activity and outcomes across eight seeds for 12,000 ticks each. It also runs scarcity, abundance, noisy communication, and a small world with very short histories. Add `-Ticks 2000 -TraceTicks 500` for a shorter investigation, or `-OutputDirectory 'Temp/Terrarium/my-run'` to preserve another result. Consult `Documentation/ExperimentReport.md` for the exact recorded invocation and results.

To compile the Unity presentation against installed editor reference assemblies without starting the editor:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'Assets/Experiments/Terrarium/Validation/Check-UnityCompilation.ps1'
```

Supply `-UnityEditorRoot` if Unity is installed outside its usual Hub location. This check uses installed DLLs only. It does not import the scene, render the interface, or test mouse and keyboard input.

For a custom headless simulation, the essential API is:

```csharp
var config = new Terrarium.Core.SimulationConfig {
    Population = 16, ResourceAbundance = 0.7, VisionRadius = 3
};
var world = new Terrarium.Core.Simulation(config, 7);
world.Step(12000);
// Inspect world.Agents, Objects, Events, Statistics and StateDigest().
```

The constructor copies and validates configuration. Public state is inspectable and allows controlled test interventions; ordinary observers must treat it as read-only. There is no supported live configuration mutation or save/load format in this version.

## Useful experiments

Keep a seed fixed and vary one configuration value at a time. Resource abundance changes initial opportunities; regrowth changes sustained supply; spoilage changes the usefulness of reserves; perception radius changes how quickly private estimates become stale; communication accuracy changes transmitted estimates; learning rate changes adaptation to outcomes. Do not treat differences between unrelated seeds as causal proof of a parameter effect.

Watch for sequences, not just totals: a remembered supply, an action selected using that belief, an actual result that differs from expectation, and a later action affected by the updated estimate. Compare both directions of a relationship after an observed transfer. Use event IDs, decision records, and seeds when reporting an example.

## Remaining runtime checks

Open this scene and verify import without compile errors, the initial map, control responsiveness, pause/single-step, acceleration, selection/follow, all inspector tabs and scroll areas, belief/truth rendering, same-seed restart, and an invalid seed entry. Check readability at your preferred Game view resolution and sustained frame time at the highest speed. These checks require Unity and were intentionally not performed automatically.

The experiment report separates observed mechanisms from hypotheses and records limitations and suggested next steps. The existence of a variable called attachment is not evidence that a human-like attachment behavior emerged.
