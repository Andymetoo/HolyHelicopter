using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace HolyHelicopter
{
    /// <summary>
    /// Standalone integration checks against the real runtime. Entirely dormant unless explicitly
    /// launched with -holy-smoke-test. Add -holy-preview to capture the scene without destructive tests.
    /// </summary>
    public sealed class SmokeValidation : MonoBehaviour
    {
        [Serializable] sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
            public double milliseconds;
        }

        [Serializable] sealed class Report
        {
            public string suite = "Holy Helicopter runtime integration";
            public string utc, unityVersion, graphicsDevice, reportPath, worldScreenshot, hudScreenshot;
            public bool previewOnly, stressPreview, success;
            public int initialEnemies, finalKills, passedChecks, failedChecks;
            public int simulationPopulation, simulationTicks, renderedFrames, renderPopulation;
            public double simulationTotalMilliseconds, simulationMillisecondsPerTick, renderFrameMilliseconds, renderFramesPerSecond;
            public string performanceScope = "Simulation timing measures Horde.Tick CPU work only, not FPS. Preview frame rate includes rendering, HUD, VSync and the application's 120 FPS cap; it is not an uncapped GPU benchmark.";
            public List<CheckResult> checks = new List<CheckResult>();
            public List<string> runtimeErrors = new List<string>();
        }

        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static bool launched;
        Report report;
        Game game;
        string outputPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LaunchIfRequested()
        {
            if (launched || !HasArgument("-holy-smoke-test")) return;
            launched = true;
            var runner = new GameObject("Explicit CLI smoke validation");
            DontDestroyOnLoad(runner);
            runner.AddComponent<SmokeValidation>();
        }

        static bool HasArgument(string flag)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            outputPath = Argument("-holy-report");
            if (string.IsNullOrWhiteSpace(outputPath)) outputPath = Path.Combine(Application.persistentDataPath, "HolyHelicopter-smoke.json");
            outputPath = Path.GetFullPath(outputPath);
            report = new Report
            {
                utc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName,
                reportPath = outputPath, previewOnly = HasArgument("-holy-preview"), stressPreview = HasArgument("-holy-stress")
            };
            report.performanceScope += report.stressPreview
                ? " Stress preview renders 3,000 active monsters and manually advances one real Horde.Tick(1/60) per sampled frame; elapsed mission time advances with it. Other gameplay remains frozen."
                : " Normal preview freezes simulation and samples rendering/HUD only.";
            Application.logMessageReceived += CaptureError;
            Check("Report destination", () => Directory.CreateDirectory(Path.GetDirectoryName(outputPath)));

            // Runtime bootstrapping and scene initialization may register in either order.
            for (int frame = 0; frame < 180 && Game.I == null; frame++) yield return null;
            game = Game.I;
            Check("Scene initialized", () =>
            {
                Require(game != null, "Game singleton was not created after 180 frames.");
                Require(game.World && game.Flight && game.Arsenal && game.Horde && game.Hud, "A gameplay subsystem is missing.");
                Require(game.Horde.Count >= 1500, "Expected at least 1,500 initial monsters; got " + game.Horde.Count);
                Require(game.Crypts.Count == 4 && game.ActiveCrypts == 4, "Expected four active crypts.");
                Require(game.Survivors.Count == 8, "Expected eight survivors.");
                Require(game.Supplies.Count >= 8, "Initial supply network is missing.");
                report.initialEnemies = game.Horde.Count;
            });
            if (game == null) { Finish(); yield break; }

            game.enabled = false;
            game.Begin();
            game.SetCursor(true);
            game.Audio.Muted = true;
            Check("Mission begins", () => Require(game.Started && !game.GameOver, "Begin did not start a live mission."));

            if (!Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Check("Preview composition", PreparePreview);
                // Time scale freezes effect lifetimes while render frames continue normally.
                float previousScale = Time.timeScale;
                Time.timeScale = 0;
                for (int i = 0; i < 30; i++) yield return null;
                report.renderPopulation = game.Horde.Count;
                double frameSeconds = 0;
                for (int i = 0; i < 120; i++)
                {
                    if (report.stressPreview)
                    {
                        game.Elapsed += 1f / 60;
                        game.Horde.Tick(1f / 60);
                    }
                    yield return null;
                    frameSeconds += Time.unscaledDeltaTime;
                    report.renderedFrames++;
                }
                report.renderFrameMilliseconds = frameSeconds * 1000 / report.renderedFrames;
                report.renderFramesPerSecond = report.renderedFrames / Math.Max(frameSeconds, .0001);
                AddResult("Rendered preview frame sampling", true,
                    report.renderFramesPerSecond.ToString("F1") + " observed FPS across " + report.renderedFrames + " frames; includes HUD and frame caps.", frameSeconds * 1000);
                yield return CaptureScreenshots();
                Time.timeScale = previousScale;
            }
            else AddResult("Screenshots and rendered frame sampling", true,
                Application.isBatchMode
                    ? "Skipped: Unity batch mode does not present/render reliable camera or HUD frames. Run without -batchmode to validate screenshots and render performance."
                    : "Skipped: null graphics device (-nographics).", 0);

            if (report.previewOnly) { Finish(); yield break; }

            Check("Terrain clearance, altitude limits and velocity", CheckFlight);
            Check("Analytic terrain aiming", CheckAim);
            Check("Armory purchases, upgrades and independent triggers", CheckArmory);
            Check("Ammo replenishes only owned weapons", CheckAmmo);
            Check("Horde capacity and area damage accounting", CheckCrowd);
            Check("Stationary swarm builds a destructible skeleton tower", CheckTower);
            Check("High/low supply hooking and full-resource preservation", CheckSupplies);
            Check("Interrupted rescue resets its target and visual", CheckRescueInterruption);
            Check("Rescue capacity, passenger delivery and base service", CheckRescueAndBase);
            Check("Four crypts plus four delivered civilians complete mission", CheckVictory);
            yield return null;
            Finish();
        }

        void PreparePreview()
        {
            Screen.SetResolution(1600, 900, false);
            Vector3 position = World.Ground(-91, -62) + Vector3.up * 24f;
            Vector3 target = World.Ground(-105, -13) + Vector3.up * 1.5f;
            game.Flight.Teleport(position);
            Vector3 direction = target - position;
            game.Flight.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0);
            game.Flight.Camera.transform.rotation = Quaternion.LookRotation(direction);
            Shader.SetGlobalVector("_HolyLampPosition", new Vector4(position.x, position.y, position.z, 1));
            Shader.SetGlobalVector("_HolyLampDirection", direction.normalized);
            game.Arsenal.Tick(0, false);
            game.Hull = 96; game.Fuel = 88;
            game.Message("MIDNIGHT MASS  /  Clear the cemetery. Bring the parish home.");
            // Produce real weapon feedback in the composed preview, preserving a believable scene.
            Invoke(game.Arsenal, "TryFire", 1);
            if (report.stressPreview && game.Horde.Count < 3000)
                game.Horde.SpawnHorde(World.Ground(-105, -13), 3000 - game.Horde.Count);
        }

        IEnumerator CaptureScreenshots()
        {
            string directory = Path.GetDirectoryName(outputPath);
            string stem = Path.GetFileNameWithoutExtension(outputPath);
            report.worldScreenshot = Path.Combine(directory, stem + "-world.png");
            report.hudScreenshot = Path.Combine(directory, stem + "-hud.png");
            Camera camera = game.Flight.Camera;
            RenderTexture previous = camera.targetTexture;
            RenderTexture target = null;
            bool setup = false;
            Check("World screenshot setup", () =>
            {
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "Smoke preview target" };
                target.Create();
                Require(target.IsCreated(), "RenderTexture creation failed.");
                camera.targetTexture = target;
                setup = true;
            });
            if (setup)
            {
                // Normal URP camera rendering is used; Camera.Render is unsupported by some SRP versions.
                for (int i = 0; i < 5; i++) yield return null;
                Check("World screenshot", () =>
                {
                    RenderTexture active = RenderTexture.active;
                    Texture2D image = null;
                    try
                    {
                        RenderTexture.active = target;
                        image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(report.worldScreenshot, image.EncodeToPNG());
                        Require(new FileInfo(report.worldScreenshot).Length > 10000, "World screenshot is unexpectedly empty.");
                        ValidateImage(image, "World screenshot");
                    }
                    finally
                    {
                        RenderTexture.active = active;
                        if (image != null) Destroy(image);
                    }
                });
            }
            camera.targetTexture = previous;
            if (target != null) { target.Release(); Destroy(target); }
            for (int i = 0; i < 4; i++) yield return null;
            bool requested = false;
            Check("HUD screenshot request", () =>
            {
                // The runner owns this generated output path. Remove stale captures before polling.
                if (File.Exists(report.hudScreenshot)) File.Delete(report.hudScreenshot);
                ScreenCapture.CaptureScreenshot(report.hudScreenshot);
                requested = true;
            });
            if (requested)
            {
                // CaptureScreenshot writes after presentation. No WaitForEndOfFrame: batch mode can hang on it.
                float deadline = Time.realtimeSinceStartup + 8f;
                for (int i = 0; i < 240 && Time.realtimeSinceStartup < deadline; i++)
                {
                    yield return null;
                    if (i >= 4 && File.Exists(report.hudScreenshot)) break;
                }
                Check("HUD screenshot", () =>
                {
                    Require(File.Exists(report.hudScreenshot) && new FileInfo(report.hudScreenshot).Length > 10000,
                        "ScreenCapture did not produce a nonempty HUD image. Check standalone graphics/presentation availability.");
                    var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        Require(image.LoadImage(File.ReadAllBytes(report.hudScreenshot)), "HUD screenshot PNG could not be decoded.");
                        ValidateImage(image, "HUD screenshot");
                    }
                    finally { Destroy(image); }
                });
            }
        }

        static void ValidateImage(Texture2D image, string name)
        {
            const int columns = 32, rows = 18;
            int visible = 0;
            float darkest = 1, brightest = 0;
            var colors = new HashSet<int>();
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                Color pixel = image.GetPixel(Mathf.Min(image.width - 1, (int)((x + .5f) * image.width / columns)),
                    Mathf.Min(image.height - 1, (int)((y + .5f) * image.height / rows)));
                float brightness = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                if (brightness > .025f) visible++;
                brightest = Mathf.Max(brightest, brightness); darkest = Mathf.Min(darkest, brightness);
                colors.Add((Mathf.RoundToInt(pixel.r * 31) << 10) | (Mathf.RoundToInt(pixel.g * 31) << 5) | Mathf.RoundToInt(pixel.b * 31));
            }
            Require(visible > columns * rows / 8 && brightest > .08f && brightest - darkest > .025f && colors.Count >= 8,
                name + " is black or effectively uniform: " + visible + "/" + (columns * rows) + " visible samples, "
                + colors.Count + " distinct sampled colors, brightness range " + darkest.ToString("F3") + "–" + brightest.ToString("F3") + ".");
        }

        void CheckFlight()
        {
            game.Fuel = 100;
            for (int i = 0; i < 32; i++)
            {
                float x = -290 + (i * 83 % 580), z = -280 + (i * 137 % 560);
                game.Flight.Teleport(World.Ground(x, z) - Vector3.up * 2);
                game.Flight.Tick(1f / 60, false);
                Require(game.Flight.Altitude >= 3.39f, "Terrain penetration at " + x + ", " + z);
            }
            game.Flight.Teleport(World.BasePosition + Vector3.up * 90);
            for (int i = 0; i < 600; i++) game.Flight.Tick(1f / 60, false);
            Require(game.Flight.Altitude <= 58.05f, "Maximum altitude clamp did not settle.");
            game.Flight.Teleport(World.Ground(-20, 50) + Vector3.up * 3.4f);
            typeof(FlightController).GetProperty("Velocity", InstanceMembers).SetValue(game.Flight, new Vector3(27, 0, 15));
            for (int i = 0; i < 120; i++)
            {
                Vector3 before = game.Flight.transform.position;
                game.Flight.Tick(1f / 60, false);
                Vector3 measured = (game.Flight.transform.position - before) * 60;
                Require(game.Flight.Altitude >= 3.39f, "Look-ahead flight penetrated terrain.");
                Require((measured - game.Flight.Velocity).sqrMagnitude < .02f, "Reported velocity differs from actual displacement.");
            }
            game.Flight.Teleport(World.Ground(45, 65) + Vector3.up * 25);
            Vector3 hover = game.Flight.transform.position;
            for (int i = 0; i < 240; i++) game.Flight.Tick(1f / 60, false);
            Require(Vector3.Distance(hover, game.Flight.transform.position) < .02f, "Uncommanded hover drift.");
        }

        void CheckAim()
        {
            for (int i = 0; i < 18; i++)
            {
                Vector3 origin = World.Ground(-270 + i * 29, 260 - i * 11) + Vector3.up * 37;
                Vector3 hit = game.Arsenal.TraceAim(origin, Vector3.down, 100);
                Require(Mathf.Abs(hit.y - World.Height(hit.x, hit.z) - .2f) < .08f, "Downward aiming missed analytic terrain.");
            }
            Crypt crypt = game.Crypts[0];
            Vector3 start = crypt.Position + new Vector3(0, 3, -35);
            Vector3 contact = game.Arsenal.TraceAim(start, Vector3.forward, 100);
            Require(Vector3.Distance(contact, crypt.Position + Vector3.up * 3) <= 5.1f, "Crypt aiming volume was not hit.");
        }

        void CheckArmory()
        {
            game.Flight.Teleport(World.BasePosition + Vector3.up * 6);
            Require(game.AtBase, "Helipad is not recognized as service area.");
            game.Salvage = 2000;
            int cost = game.Arsenal.UnlockCost(2), before = game.Salvage;
            Require(game.Arsenal.Unlock(2), "Could not buy blessed rockets at base.");
            Require(game.Salvage == before - cost && game.Arsenal.Weapons[2].Unlocked, "Unlock accounting is incorrect.");
            WeaponState rocket = game.Arsenal.Weapons[2];
            before = game.Salvage; cost = rocket.UpgradeCost; float oldAmmo = rocket.MaxAmmo;
            Require(game.Arsenal.Upgrade(2), "Could not upgrade rockets.");
            Require(rocket.Level == 2 && rocket.MaxAmmo > oldAmmo && game.Salvage == before - cost, "Upgrade accounting is incorrect.");
            game.Arsenal.Assign(2, false); game.Arsenal.Assign(1, true);
            Require(game.Arsenal.LeftSlot == 2 && game.Arsenal.RightSlot == 1, "Independent trigger assignment failed.");
            game.Arsenal.Assign(3, false);
            Require(game.Arsenal.LeftSlot == 2, "A locked weapon can be assigned.");
            game.Flight.Teleport(World.Ground(70, 40) + Vector3.up * 20);
            before = game.Salvage;
            Require(!game.Arsenal.Upgrade(2) && game.Salvage == before, "An upgrade was allowed away from base.");
        }

        void CheckAmmo()
        {
            foreach (WeaponState weapon in game.Arsenal.Weapons) weapon.Ammo = 0;
            game.Arsenal.Refill(.25f);
            foreach (WeaponState weapon in game.Arsenal.Weapons)
            {
                float expected = weapon.Unlocked ? weapon.MaxAmmo * .25f : 0;
                Require(Mathf.Abs(weapon.Ammo - expected) < .01f, "Wrong refill for " + weapon.Name);
            }
            game.Arsenal.Refill(1);
        }

        void CheckCrowd()
        {
            Vector3 center = World.Ground(250, 235);
            game.Horde.SpawnHorde(center, game.Horde.Capacity + 1000);
            Require(game.Horde.Count == game.Horde.Capacity, "Horde did not honor its capacity.");
            report.simulationPopulation = game.Horde.Count;
            report.simulationTicks = 120;
            var simulation = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < report.simulationTicks; i++)
            {
                game.Elapsed += 1f / 60;
                game.Horde.Tick(1f / 60);
            }
            simulation.Stop();
            report.simulationTotalMilliseconds = simulation.Elapsed.TotalMilliseconds;
            report.simulationMillisecondsPerTick = simulation.Elapsed.TotalMilliseconds / report.simulationTicks;
            Require(game.Horde.Count <= game.Horde.Capacity, "Simulation exceeded its population capacity.");
            game.Hull = 100;
            int before = game.Horde.Count, kills = game.Kills;
            int reported = game.Horde.DamageArea(center, 32, 10000, DamageKind.Rocket);
            Require(reported > 20, "Area weapon failed to clear a dense crowd.");
            Require(before - game.Horde.Count == reported, "Horde count delta disagrees with area kill result.");
            Require(game.Kills - kills == reported, "Kill notification accounting disagrees with area result.");
        }

        void CheckTower()
        {
            // Isolate the test crowd without touching civilian damage or spawning structures.
            game.Horde.DamageArea(Vector3.zero, 2000, 100000, DamageKind.Rocket);
            Require(game.Horde.Count == 0, "Could not clear existing monsters for the tower test.");
            float[] health = new float[game.Crypts.Count];
            for (int i = 0; i < health.Length; i++) { health[i] = game.Crypts[i].Health; game.Crypts[i].Health = 0; }
            try
            {
                Vector3 center = World.Ground(-245, 225);
                game.Flight.Teleport(center + Vector3.up * 27);
                game.Horde.SpawnHorde(center, 500);
                game.Elapsed = 12;
                for (int i = 0; i < 330; i++)
                {
                    game.Elapsed += 1f / 30;
                    game.Hull = 100;
                    game.Horde.Tick(1f / 30);
                }
                Require(game.Horde.TowerCount >= 15 && game.Horde.TowerHeight > 3, "A stationary dense swarm did not build a tower.");
                int before = game.Kills;
                Vector3 strike = game.Horde.TowerPosition + Vector3.up * (game.Horde.TowerHeight * .5f);
                int reported = game.Horde.DamageArea(strike, 4, 10000, DamageKind.Rocket);
                Require(reported > 0 && game.Horde.TowerCount == 0, "A direct explosive hit failed to destroy the tower.");
                Require(game.Kills - before == reported, "Tower kill accounting is inconsistent.");
                game.Horde.ClearBoarders();
                Require(game.Horde.Boarders == 0, "Boarder clearing failed.");
            }
            finally
            {
                for (int i = 0; i < health.Length; i++) game.Crypts[i].Health = health[i];
                game.Hull = 100;
            }
        }

        void CheckSupplies()
        {
            Supply fuel = game.Supplies.Find(s => !s.Collected && s.Kind == SupplyKind.Fuel);
            Require(fuel != null, "No fuel crate exists for pickup test.");
            game.Fuel = 40;
            game.Flight.Teleport(fuel.Position + Vector3.up * 25);
            Invoke(game, "TickSupplies", 1f);
            Require(!fuel.Collected && Mathf.Abs(game.Fuel - 40) < .01f, "Fuel was hooked from excessive altitude.");
            game.Flight.Teleport(fuel.Position + Vector3.up * 6);
            Invoke(game, "TickSupplies", .4f);
            Require(!fuel.Collected, "Supply pickup ignored winch duration.");
            Invoke(game, "TickSupplies", .4f);
            Require(fuel.Collected && game.Fuel > 40, "Low hover failed to collect fuel.");
            Supply repair = game.Supplies.Find(s => !s.Collected && s.Kind == SupplyKind.Repair);
            Require(repair != null, "No repair crate exists for preservation test.");
            game.Hull = 100;
            game.Flight.Teleport(repair.Position + Vector3.up * 6);
            Invoke(game, "TickSupplies", 1f);
            Require(!repair.Collected, "Full hull consumed a repair crate.");
            game.Hull = 40;
            Invoke(game, "TickSupplies", 1f);
            Require(repair.Collected && game.Hull > 40, "Repair crate did not restore damaged hull.");
        }

        void CheckRescueInterruption()
        {
            Survivor first = game.Survivors[0], second = game.Survivors[1];
            game.Flight.Teleport(first.Position + Vector3.up * 6);
            Invoke(game, "TickRescue", .6f);
            Require(game.RescueProgress > 0 && first.Visual.position.y > first.Position.y, "Rescue did not start lifting its target.");
            game.Flight.Teleport(first.Position + Vector3.up * 20);
            Invoke(game, "TickRescue", .1f);
            Require(game.RescueProgress == 0 && Vector3.Distance(first.Visual.position, first.Position) < .01f, "Interrupted rescue left progress or a suspended civilian.");
            game.Flight.Teleport(first.Position + Vector3.up * 6);
            Invoke(game, "TickRescue", .9f);
            game.Flight.Teleport(second.Position + Vector3.up * 6);
            Invoke(game, "TickRescue", .1f);
            Require(game.RescueProgress < .1f && Vector3.Distance(first.Visual.position, first.Position) < .01f, "Target switching retained progress or the old visual offset.");
            game.Flight.Teleport(second.Position + Vector3.up * 20);
            Invoke(game, "TickRescue", .1f);
        }

        void CheckRescueAndBase()
        {
            for (int i = 0; i < 4; i++)
            {
                Survivor survivor = game.Survivors[i];
                game.Flight.Teleport(survivor.Position + Vector3.up * 6);
                for (int frame = 0; frame < 40; frame++) Invoke(game, "TickRescue", .05f);
                Require(survivor.Rescued && game.Passengers == i + 1, "Could not rescue passenger " + (i + 1));
            }
            game.Flight.Teleport(game.Survivors[4].Position + Vector3.up * 6);
            Invoke(game, "TickRescue", 3f);
            Require(!game.Survivors[4].Rescued && game.Passengers == 4, "Four-seat capacity was exceeded.");
            game.Flight.Teleport(World.BasePosition + Vector3.up * 6);
            game.Hull = 25; game.Fuel = 25;
            foreach (WeaponState weapon in game.Arsenal.Weapons) if (weapon.Unlocked) weapon.Ammo = 0;
            int before = game.Salvage;
            Invoke(game, "TickBase", 1f);
            Require(game.Passengers == 0 && game.Rescued == 4, "Returning home did not deliver passengers.");
            Require(game.Salvage >= before + 300, "Delivered civilians did not award salvage.");
            Require(game.Hull > 25 && game.Fuel > 25 && game.Arsenal.Weapons[0].Ammo > 0, "Base did not service hull, fuel and ammo.");
            Invoke(game, "TickBase", 5f);
            Require(game.Hull >= 99.9f && game.Fuel >= 99.9f, "Base service did not reach full resources.");
        }

        void CheckVictory()
        {
            Require(!game.GameOver, "Mission ended before extraction validation: " + game.DeathReason);
            foreach (Crypt crypt in game.Crypts)
            {
                game.DamageWorld(crypt.Position + Vector3.up * 2.5f, 5, 10000, DamageKind.Rocket);
                Require(!crypt.Active && (crypt.Glow == null || !crypt.Glow.gameObject.activeSelf), "Crypt did not shut down when destroyed: " + crypt.Name);
            }
            Require(game.ActiveCrypts == 0 && game.Rescued >= game.RequiredRescues && game.CanExtract, "Completed mission prerequisites were not recognized.");
            game.Flight.Teleport(World.BasePosition + Vector3.up * 6);
            game.SetCursor(true);
            game.Paused = false;
            Invoke(game, "Update");
            Require(game.Won && game.GameOver && game.ExtractionCompleted, "Normal game update did not complete the mission at home.");
        }

        static void Invoke(object target, string method, params object[] arguments)
        {
            MethodInfo info = target.GetType().GetMethod(method, InstanceMembers);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            info.Invoke(target, arguments);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        void Check(string name, Action action)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { action(); AddResult(name, true, "Passed", watch.Elapsed.TotalMilliseconds); }
            catch (Exception exception)
            {
                while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
                AddResult(name, false, exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace, watch.Elapsed.TotalMilliseconds);
            }
        }

        void AddResult(string name, bool passed, string detail, double milliseconds)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail, milliseconds = milliseconds });
            if (passed) report.passedChecks++; else report.failedChecks++;
            Debug.Log("HOLY SMOKE " + (passed ? "PASS: " : "FAIL: ") + name + " — " + detail);
        }

        void CaptureError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.runtimeErrors.Add(type + ": " + message + "\n" + trace);
        }

        void Finish()
        {
            Application.logMessageReceived -= CaptureError;
            report.finalKills = game != null ? game.Kills : 0;
            report.success = report.failedChecks == 0 && report.runtimeErrors.Count == 0;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception) { report.success = false; Debug.LogError("Smoke report could not be written: " + exception); }
            Debug.Log("HOLY SMOKE RESULT: " + (report.success ? "PASS" : "FAIL") + " / " + report.passedChecks + " passed, " + report.failedChecks + " failed, " + report.runtimeErrors.Count + " runtime errors. " + outputPath);
            if (!Application.isEditor) Application.Quit(report.success ? 0 : 1);
        }

        void OnDestroy() { Application.logMessageReceived -= CaptureError; }
    }
}
