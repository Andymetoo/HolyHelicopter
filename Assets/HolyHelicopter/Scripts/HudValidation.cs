using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace HolyHelicopter
{
    /// <summary>Opt-in presentation captures; never starts during ordinary play.</summary>
    public sealed class HudValidation : MonoBehaviour
    {
        [Serializable] sealed class Capture
        {
            public string view, path;
            public int requestedWidth, requestedHeight, actualWidth, actualHeight;
            public bool passed;
            public string error;
        }

        [Serializable] sealed class Report
        {
            public string suite = "Holy Helicopter UI resolution captures";
            public string utc, graphicsDevice, outputDirectory;
            public string scope = "Frozen presentation scenes with representative UI values. PNG existence and requested dimensions are checked; readability requires visual inspection.";
            public bool success;
            public List<Capture> captures = new List<Capture>();
            public List<string> errors = new List<string>();
        }

        static bool launched;
        Game game;
        Report report;
        string directory;
        bool showPause;
        readonly Horde.ThreatRadarPoint[] threatPreview = new Horde.ThreatRadarPoint[48];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LaunchIfRequested()
        {
            if (launched || !HasArgument("-holy-ui-test")) return;
            launched = true;
            var runner = new GameObject("Explicit CLI HUD resolution validation");
            DontDestroyOnLoad(runner);
            runner.AddComponent<HudValidation>();
        }

        static bool HasArgument(string flag)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Argument(string flag)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
                if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            report = new Report { utc = DateTime.UtcNow.ToString("o"), graphicsDevice = SystemInfo.graphicsDeviceName };
            try
            {
                string requested = Argument("-holy-ui-directory");
                if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathRooted(requested))
                    throw new ArgumentException("-holy-ui-directory must specify an absolute output directory.");
                directory = Path.GetFullPath(requested);
                report.outputDirectory = directory;
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception)
            {
                Debug.LogError("HOLY_UI_RESULT failed: " + exception.Message);
                Application.Quit(1);
                yield break;
            }

            Application.logMessageReceived += CaptureError;
            for (int i = 0; i < 180 && Game.I == null; i++) yield return null;
            game = Game.I;
            if (game == null || game.Flight == null || game.Hud == null)
            {
                report.errors.Add("Playable scene did not initialize."); Finish(); yield break;
            }
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                report.errors.Add("UI captures require a desktop graphics player without -batchmode or -nographics.");
                Finish(); yield break;
            }

            game.enabled = false;
            game.Begin();
            game.SetCursor(true);
            game.Audio.Muted = true;
            game.ShowDebug = game.ShowHelp = false;
            game.Hull = 74;
            game.Fuel = 38;
            game.Salvage = 612;
            game.Kills = 1427;
            game.Combo = game.BestCombo = 64;
            game.Elapsed = 192;
            game.SuppliesCollected = 7;
            game.Passengers = 2;
            game.Rescued = 1;
            // No PlayerPrefs or persistent settings are changed. The test exits after these frames.
            int[,] sizes = { { 1024, 576 }, { 1280, 720 }, { 1600, 900 }, { 1920, 1080 } };
            for (int i = 0; i < sizes.GetLength(0); i++)
            {
                int width = sizes[i, 0], height = sizes[i, 1];
                PrepareFlight();
                yield return SaveCapture("flight", width, height);
                PrepareArmory();
                yield return SaveCapture("armory", width, height);
            }

            PrepareFlight();
            game.Hud.ShowChart = true;
            yield return SaveCapture("chart", 1024, 576);
            yield return SaveCapture("chart", 1600, 900);
            game.Hud.ShowChart = false;

            PrepareAirThreat();
            float priorTimeScale = Time.timeScale;
            Time.timeScale = 0; // Preserve the real charge/flight trail while the screenshot is presented.
            yield return SaveCapture("air-threat", 1600, 900);
            Time.timeScale = priorTimeScale;

            PrepareWideView();
            game.Hud.enabled = false;
            yield return SaveCapture("countryside-wide", 1600, 900);
            game.Hud.enabled = true;

            PrepareArmory();
            game.Started = false;
            yield return SaveCapture("title", 1024, 576);
            game.Started = true;
            showPause = game.Paused = true;
            yield return SaveCapture("pause", 1024, 576);
            showPause = game.Paused = false;
            game.ShowHelp = true;
            yield return SaveCapture("manual", 1024, 576);
            game.ShowHelp = false;
            Finish();
        }

        void LateUpdate()
        {
            // Desktop resizes can briefly change focus; keep the requested presentation state.
            if (game != null) game.Paused = showPause;
        }

        void PrepareFlight()
        {
            game.Started = true;
            game.Paused = game.GameOver = game.Won = false;
            game.Hud.ShowChart = false;
            game.SetCursor(true);
            Survivor survivor = null;
            foreach (Survivor candidate in game.Survivors)
                if (candidate.Alive) { survivor = candidate; break; }
            Vector3 target = survivor != null ? survivor.Position : game.Crypts[0].Position;
            Vector3 position = World.Ground(target.x + 15, target.z - 36) + Vector3.up * 22;
            Frame(position, target + Vector3.up * 1.7f);
            game.RescueProgress = 0;
            game.RescueTarget = survivor == null ? "" : "RESCUE: DESCEND BELOW 46 FT";
            game.Message("Find the waving civilians. Hook supplies below 46 ft. Keep moving near swarms.");
        }

        void PrepareArmory()
        {
            game.Started = true;
            game.Paused = game.GameOver = game.Won = false;
            game.Hud.ShowChart = false;
            game.SetCursor(true);
            Vector3 position = World.BasePosition + Vector3.up * 7;
            Frame(position, position + new Vector3(0, -5, 35));
            game.RescueProgress = 0;
            game.RescueTarget = "";
            game.MessageUntil = 0;
        }

        void Frame(Vector3 position, Vector3 target)
        {
            Vector3 direction = target - position;
            float heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(-direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            game.Flight.Teleport(position, heading, pitch);
            game.Arsenal.Tick(0, false);
        }

        void PrepareAirThreat()
        {
            game.Hud.ShowChart = false;
            game.Started = true;
            game.Paused = game.GameOver = game.Won = false;
            game.SetCursor(true);
            game.RescueTarget = "";
            game.RescueProgress = 0;
            game.Combo = 0;
            game.Hull = 100;
            Vector3 crypt = game.Crypts[0].Position;
            Vector3 position = World.Ground(crypt.x + 16, crypt.z - 78) + Vector3.up * 31;
            Frame(position, crypt + Vector3.up * 21);
            // Use the actual territorial defenders and their real telegraphed attack state.
            for (int frame = 0; frame < 300; frame++)
            {
                game.Elapsed += 1f / 60;
                game.Horde.Tick(1f / 60);
                if (game.Horde.ChargingGargoyles > 0 || game.Horde.DivingGargoyles > 0) break;
            }
            int count = game.Horde.GetThreatRadarPoints(position, 230, threatPreview);
            for (int i = 0; i < count; i++)
                if (threatPreview[i].Heavy)
                {
                    Frame(position, Vector3.Lerp(threatPreview[i].Position, crypt + Vector3.up * 15, .25f));
                    break;
                }
            game.Message("STONE WINGS / Read the amber attack line. Turn or change altitude, then counterattack.");
        }

        void PrepareWideView()
        {
            game.Hud.ShowChart = false;
            game.Started = true;
            game.Paused = game.GameOver = game.Won = false;
            game.RescueTarget = "";
            game.MessageUntil = 0;
            Vector3 position = World.Ground(170, -10) + Vector3.up * 56;
            Frame(position, World.Ground(-300, 300) + Vector3.up * 14);
        }

        IEnumerator SaveCapture(string view, int width, int height)
        {
            var capture = new Capture
            {
                view = view, requestedWidth = width, requestedHeight = height,
                path = Path.Combine(directory, "HolyHelicopter-ui-" + width + "x" + height + "-" + view + ".png")
            };
            report.captures.Add(capture);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            for (int i = 0; i < 8; i++) yield return null;
            capture.actualWidth = Screen.width;
            capture.actualHeight = Screen.height;
            bool requested = false;
            try
            {
                if (File.Exists(capture.path)) File.Delete(capture.path);
                ScreenCapture.CaptureScreenshot(capture.path);
                requested = true;
            }
            catch (Exception exception) { capture.error = exception.Message; }
            if (!requested) yield break;
            for (int i = 0; i < 240; i++)
            {
                yield return null;
                if (i >= 8 && File.Exists(capture.path) && new FileInfo(capture.path).Length > 10000) break;
            }
            try
            {
                if (!File.Exists(capture.path) || new FileInfo(capture.path).Length <= 10000)
                    throw new IOException("Screenshot was not written or is unexpectedly empty.");
                // Read PNG IHDR rather than allocating another full-size graphics texture.
                byte[] bytes = File.ReadAllBytes(capture.path);
                int pngWidth = ReadBigEndian(bytes, 16), pngHeight = ReadBigEndian(bytes, 20);
                if (pngWidth != width || pngHeight != height)
                    throw new IOException($"Requested {width}x{height}; PNG was {pngWidth}x{pngHeight}.");
                capture.passed = true;
            }
            catch (Exception exception) { capture.error = exception.Message; }
        }

        static int ReadBigEndian(byte[] bytes, int offset)
        {
            return bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
        }

        void CaptureError(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                report.errors.Add(message);
        }

        void Finish()
        {
            report.success = report.errors.Count == 0 && report.captures.Count >= 15;
            foreach (Capture capture in report.captures) if (!capture.passed) report.success = false;
            Application.logMessageReceived -= CaptureError;
            try { File.WriteAllText(Path.Combine(directory, "HolyHelicopter-ui-report.json"), JsonUtility.ToJson(report, true)); }
            catch (Exception exception) { report.success = false; Debug.LogError(exception); }
            Debug.Log("HOLY_UI_RESULT " + (report.success ? "passed" : "failed") + " / " + report.captures.Count + " captures");
            Application.Quit(report.success ? 0 : 1);
        }

        void OnDestroy() { Application.logMessageReceived -= CaptureError; }
    }
}
