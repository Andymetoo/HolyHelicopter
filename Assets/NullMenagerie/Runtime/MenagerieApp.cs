using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NullMenagerie
{
    /// <summary>Scene-owned application. No runtime initialization hooks or shared project changes.</summary>
    public sealed class MenagerieApp : MonoBehaviour
    {
        public readonly List<Specimen> Collection = new List<Specimen>();
        public CombatSimulation Battle { get; private set; }
        public int SelectedIndex { get; private set; }
        public int LeftIndex { get; private set; }
        public int RightIndex { get; private set; } = 1;
        readonly Color ink = new Color(.025f, .042f, .066f), panel = new Color(.043f, .065f, .09f);
        readonly Color border = new Color(.13f, .22f, .26f), paper = new Color(.88f, .9f, .83f);
        readonly Color muted = new Color(.64f, .75f, .77f), cyan = new Color(.34f, .92f, .79f);
        readonly Color amber = new Color(1f, .68f, .32f), pink = new Color(.98f, .43f, .58f);
        Font font;
        bool ownsFont;
        GUIStyle label, field;
        readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        MenagerieAudio sound;
        CancellationTokenSource lifetime;
        Task<Specimen> import;
        Task<string> picker;
        Stance leftStance, rightStance;
        string message = "Six resident specimens. Choose one to inspect, or discover a file of your own.";
        string pathInput = "";
        bool modal, help, paused, resultSound, fast;
        float revealAt = -10, shake, hitStop, finishedAt = -1;
        int lastEvent, bout;
        Vector2 scroll;
        float uiTime;
        bool errorMessage;
        int renderedFrames;
        int previousTargetFrameRate;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;
        bool previousRunInBackground;
        public bool Busy => import != null || picker != null;
        bool Fighting => Battle != null && !Battle.Finished;
        Specimen Selected => Collection[SelectedIndex];

        void Awake()
        {
            lifetime = new CancellationTokenSource();
            previousTargetFrameRate = Application.targetFrameRate;
            previousRunInBackground = Application.runInBackground;
            previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
            Application.targetFrameRate = 60;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Collection.AddRange(SpecimenGenerator.Samples());
            var cameraObject = new GameObject("Null Menagerie / backdrop camera");
            cameraObject.transform.SetParent(transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ink;
            camera.cullingMask = 0; camera.depth = 10;
            cameraObject.AddComponent<AudioListener>();
            sound = gameObject.AddComponent<MenagerieAudio>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, 18);
            ownsFont = font != null;
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (HasArgument("-null-smoke")) StartCoroutine(RuntimeSmoke());
        }

        void Update()
        {
            uiTime += UnityEngine.Time.unscaledDeltaTime;
            float dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, .05f);
            shake = Mathf.MoveTowards(shake, 0, dt * 24);
            if (picker != null && picker.IsCompleted)
            {
                var done = picker; picker = null;
                if (done.IsFaulted) Notify(done.Exception.GetBaseException().Message, true);
                else if (!string.IsNullOrEmpty(done.Result)) ImportPath(done.Result);
                else Notify("Discovery cancelled. The cabinet is waiting.");
            }
            if (import != null && import.IsCompleted)
            {
                var done = import; import = null;
                if (done.IsCanceled) Notify("Discovery cancelled.");
                else if (done.IsFaulted) Notify("Cannot read this file: " + done.Exception.GetBaseException().Message, true);
                else AddSpecimen(done.Result);
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && !modal && !help && GUIUtility.keyboardControl == 0)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) { if (Fighting) paused = !paused; else StartBattle(); }
                if (keyboard.rKey.wasPressedThisFrame && !Fighting) StartBattle();
                if (keyboard.iKey.wasPressedThisFrame && !Busy) PickFile();
                if (keyboard.mKey.wasPressedThisFrame) sound.Muted = !sound.Muted;
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { modal = false; help = false; GUIUtility.keyboardControl = 0; }
            if (Fighting && !paused && !modal && !help && picker == null)
            {
                if (hitStop > 0) hitStop -= dt;
                else Battle.Step(dt * (fast ? 2 : 1));
                ConsumeEvents();
                if (Battle.Finished && !resultSound) { resultSound = true; finishedAt = uiTime; sound.Play("result"); }
            }
        }

        public void ImportPath(string path)
        {
            if (Busy) return;
            path = (path ?? "").Trim().Trim('"');
            if (path.Length == 0) { Notify("Paste the full path to a local file.", true); return; }
            var token = lifetime.Token;
            import = Task.Run(() => SpecimenGenerator.Generate(FileAnalyzer.Analyze(path, token)), token);
            modal = false; GUIUtility.keyboardControl = 0;
            Notify("Listening to the bytes... extracting a bounded local sample.");
        }

        void AddSpecimen(Specimen specimen)
        {
            int found = Collection.FindIndex(s => s.Source.Fingerprint == specimen.Source.Fingerprint);
            if (found >= 0)
            {
                SelectedIndex = found; revealAt = uiTime;
                Notify("A familiar signal. This unchanged file is already in your cabinet.");
            }
            else if (Collection.Count >= 36) Notify("The cabinet holds 36 specimens. Remove an unassigned specimen to make room.", true);
            else
            {
                Collection.Add(specimen); SelectedIndex = Collection.Count - 1; revealAt = uiTime;
                scroll.x = Mathf.Max(0, Collection.Count * 244 - 1504);
                Notify("DISCOVERED / " + specimen.Name + " / " + specimen.Epithet);
                sound.Play("discover");
            }
        }

        void PickFile()
        {
            if (Busy) return;
            picker = NativeFilePicker.Pick(); Notify("Choose any local file. Its original bytes stay untouched.");
        }

        public void StartBattle()
        {
            if (Fighting || Collection.Count < 2) return;
            Battle = new CombatSimulation(Collection[LeftIndex], Collection[RightIndex], leftStance, rightStance, (uint)++bout);
            lastEvent = 0; paused = false; resultSound = false; hitStop = 0; finishedAt = -1;
            Notify("Containment released. Watch their windups, and follow the signal.");
            sound.Play("special");
        }

        void ConsumeEvents()
        {
            foreach (var e in Battle.Events)
            {
                if (e.Id <= lastEvent) continue;
                lastEvent = e.Id;
                if (e.Kind == "hit") { shake = Mathf.Min(5, 1 + e.Value * .12f); hitStop = .025f; sound.Play("hit"); }
                else if (e.Kind == "special" || e.Kind == "mutation") { shake = 3; sound.Play("special"); }
            }
        }

        void Notify(string text, bool error = false) { message = text; errorMessage = error; }

        void OnGUI()
        {
            if (Collection.Count == 0) return;
            if (Event.current.type == EventType.Repaint) renderedFrames++;
            EnsureStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 960f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1600 * scale) * .5f, (Screen.height - 960 * scale) * .5f), Quaternion.identity, Vector3.one * scale);
            Box(new Rect(0, 0, 1600, 960), ink);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !modal && !help;
            Header(); Inspector(); Arena(); Deployment(); Cabinet();
            Text(new Rect(32, 918, 1420, 24), (Busy ? "[~] " : errorMessage ? "[!] " : "[>] ") + Clean(message, 148), 14, errorMessage ? pink : muted);
            Text(new Rect(1440, 918, 130, 24), "LOCAL / V.01", 12, muted, TextAnchor.MiddleRight);
            GUI.enabled = previousEnabled;
            if (modal) ImportModal();
            if (help) HelpModal();
            GUI.matrix = oldMatrix; GUI.color = oldColor;
#if UNITY_EDITOR
            // Editor drag-and-drop is supported without installing a native window hook.
            Event e = Event.current;
            if (!modal && !help && (e.type == EventType.DragUpdated || e.type == EventType.DragPerform))
            {
                UnityEditor.DragAndDrop.visualMode = UnityEditor.DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform && UnityEditor.DragAndDrop.paths.Length > 0 && !Busy)
                { UnityEditor.DragAndDrop.AcceptDrag(); ImportPath(UnityEditor.DragAndDrop.paths[0]); }
                e.Use();
            }
#endif
        }

        void Header()
        {
            Text(new Rect(32, 20, 600, 23), "N M  /  INSTITUTE OF UNLIKELY LIFE", 12, cyan);
            Text(new Rect(28, 41, 720, 52), "NULL MENAGERIE", 41, paper);
            Text(new Rect(34, 98, 880, 22), "Every file contains a monster. Some were better left unopened.", 14, muted);
            if (Button(new Rect(1100, 36, 242, 43), Busy ? "READING SIGNAL..." : "+ DISCOVER A FILE", cyan, !Busy)) PickFile();
            if (Button(new Rect(1354, 36, 94, 43), "PATH", border)) { modal = true; pathInput = ""; }
            if (Button(new Rect(1460, 36, 108, 43), sound.Muted ? "SOUND OFF" : "SOUND ON", border)) sound.Muted = !sound.Muted;
            if (Button(new Rect(1390, 91, 178, 25), "FIELD GUIDE  [?]", border)) help = true;
            Text(new Rect(1098, 90, 280, 24), "OFFLINE  /  READ-ONLY IMPORT", 11, muted);
        }

        void Inspector()
        {
            var s = Selected; var p = s.Source;
            Panel(new Rect(32, 142, 324, 568));
            Text(new Rect(50, 156, 288, 22), "01 / SPECIMEN OBSERVATORY", 12, muted);
            Text(new Rect(50, 186, 288, 30), Clean(s.Name, 23).ToUpperInvariant(), 22, paper);
            Text(new Rect(50, 218, 288, 20), s.Epithet, 12, Accent(s));
            Box(new Rect(49, 246, 290, 166), ink);
            for (int i = 0; i < 4; i++) Line(58, 280 + i * 34, 330, 280 + i * 34, new Color(.08f, .13f, .16f));
            CreatureArt.Draw(new Rect(49, 248, 290, 164), s, uiTime, true, 0, 0, 1, 0, Mathf.Clamp01((uiTime - revealAt) / 1.1f));
            Text(new Rect(58, 391, 270, 18), s.Mutation == MutationKind.None ? "SIGNAL STABLE / " + s.Body.ToString().ToUpperInvariant() : "ANOMALY / " + s.Mutation.ToString().ToUpperInvariant(), 10, s.Mutation == MutationKind.None ? muted : amber);
            Stat(50, 425, "VITALITY", s.Health, 220, cyan);
            Stat(202, 425, "POWER", s.Power, 25, amber);
            Stat(50, 468, "ARMOR", s.Armor * 100, 50, cyan, "%");
            Stat(202, 468, "SPEED", s.Speed, 18, amber);
            Text(new Rect(50, 512, 288, 23), s.AbilityName.ToUpperInvariant(), 14, Accent(s));
            Text(new Rect(50, 538, 288, 49), s.AbilityDescription, 12, paper, TextAnchor.UpperLeft, true);
            Text(new Rect(50, 591, 288, 48), s.OriginDescription, 11, muted, TextAnchor.UpperLeft, true);
            if (Button(new Rect(50, 655, 137, 35), "ASSIGN / A", cyan, !Fighting)) { LeftIndex = SelectedIndex; Battle = null; }
            if (Button(new Rect(201, 655, 137, 35), "ASSIGN / B", amber, !Fighting)) { RightIndex = SelectedIndex; Battle = null; }
        }

        void Arena()
        {
            Rect r = new Rect(376, 142, 832, 568);
            Panel(r);
            Text(new Rect(396, 157, 510, 24), "02 / THE CONTAINMENT PIT", 12, muted);
            Text(new Rect(960, 157, 226, 24), Battle == null ? "AWAITING SPECIMENS" : "BOUT " + bout.ToString("00") + " / " + Battle.Time.ToString("00.0") + "s", 12, Battle == null ? muted : cyan, TextAnchor.MiddleRight);
            var a = Collection[LeftIndex]; var b = Collection[RightIndex];
            float ha = Battle == null ? 1 : Battle.Fighters[0].Health / a.Health;
            float hb = Battle == null ? 1 : Battle.Fighters[1].Health / b.Health;
            Text(new Rect(398, 193, 330, 25), "A  " + a.Name.ToUpperInvariant(), 16, cyan);
            Text(new Rect(858, 193, 328, 25), b.Name.ToUpperInvariant() + "  B", 16, amber, TextAnchor.MiddleRight);
            Bar(new Rect(398, 226, 330, 6), ha, cyan); Bar(new Rect(858, 226, 328, 6), hb, amber);
            Text(new Rect(745, 190, 94, 33), "VS", 24, muted, TextAnchor.MiddleCenter);
            Rect pit = new Rect(390, 251, 804, 332);
            GUI.BeginGroup(pit);
            Vector2 jitter = new Vector2(Mathf.Sin(uiTime * 72), Mathf.Cos(uiTime * 61)) * shake;
            for (int i = 0; i < 25; i++) Line(i * 36 + jitter.x, 0, i * 36 + jitter.x, 332, new Color(.065f, .106f, .13f));
            for (int i = 0; i < 11; i++) Line(0, i * 36 + jitter.y, 804, i * 36 + jitter.y, new Color(.065f, .106f, .13f));
            for (int i = 0; i < 22; i++)
            {
                float x = 15 + i * 37;
                Text(new Rect(x, 285, 30, 20), i % 3 == 0 ? "+" : ":", 14, border, TextAnchor.MiddleCenter);
            }
            Line(15, 307, 789, 307, border);
            Text(new Rect(18, 310, 180, 18), "SECTOR 00 / NULL", 10, muted);
            Text(new Rect(560, 310, 225, 18), "NO FILES WERE HARMED", 10, muted, TextAnchor.MiddleRight);
            DrawFighter(a, 0, jitter); DrawFighter(b, 1, jitter);
            if (Battle != null) DrawEffects();
            if (Battle == null)
            {
                Text(new Rect(0, 12, 804, 23), "TWO SIGNALS ENTER. ONE SIGNAL REMAINS.", 12, muted, TextAnchor.MiddleCenter);
                Text(new Rect(0, 259, 804, 20), "Choose stances. Break containment.", 12, muted, TextAnchor.MiddleCenter);
            }
            if (paused && Fighting) CenterBadge("SIMULATION PAUSED", cyan);
            if (Battle != null && Battle.Finished)
            {
                Box(new Rect(152, 77, 500, 117), new Color(.02f, .035f, .052f, .95f));
                Outline(new Rect(152, 77, 500, 117), Battle.Winner == 0 ? cyan : amber);
                Text(new Rect(164, 89, 476, 23), "CONTAINMENT RESTORED", 12, muted, TextAnchor.MiddleCenter);
                Text(new Rect(164, 116, 476, 34), Battle.Winner < 0 ? "MUTUAL ANNIHILATION" : Battle.Fighters[Battle.Winner].Specimen.Name.ToUpperInvariant(), 25, Battle.Winner == 0 ? cyan : amber, TextAnchor.MiddleCenter);
                Text(new Rect(164, 157, 476, 23), Battle.Winner < 0 ? "Some signals cancel each other out." : "SURVIVES THE PIT  /  " + Battle.Time.ToString("0.0") + " SECONDS", 12, paper, TextAnchor.MiddleCenter);
            }
            GUI.EndGroup();
            Line(396, 594, 1188, 594, border);
            Text(new Rect(398, 605, 770, 17), "LIVE TELEMETRY", 10, muted);
            if (Battle == null) Text(new Rect(398, 636, 770, 49), "Claws, scripts, static, teeth.\nThe cabinet is peaceful. The pit is less so.", 13, muted);
            else
            {
                int y = 0;
                for (int i = Battle.Events.Count - 1; i >= 0 && y < 3; i--)
                {
                    var e = Battle.Events[i];
                    if (e.Kind == "windup" || e.Kind == "projectile") continue;
                    Text(new Rect(398, 630 + 21 * y++, 790, 21), e.Time.ToString("00.0") + " > " + Clean(e.Text, 91), 12, e.Kind == "mutation" ? pink : e.Kind == "special" ? amber : muted);
                }
            }
        }

        void DrawFighter(Specimen s, int index, Vector2 jitter)
        {
            var f = Battle == null ? null : Battle.Fighters[index];
            float x = 40 + (f == null ? (index == 0 ? 23 : 77) : f.X) * 7.24f + jitter.x;
            float y = 185 + (f == null ? 0 : f.Y * 2) + jitter.y;
            Color team = index == 0 ? cyan : amber;
            Line(x - 57, y + 76, x + 57, y + 76, new Color(team.r, team.g, team.b, .4f), 2);
            Text(new Rect(x - 30, y + 78, 60, 20), index == 0 ? "[ A ]" : "[ B ]", 11, team, TextAnchor.MiddleCenter);
            if (f != null && f.Windup > 0)
            {
                Outline(new Rect(x - 95, y - 95, 190, 175), new Color(team.r, team.g, team.b, .4f + .3f * Mathf.Sin(uiTime * 25)));
                Text(new Rect(x - 130, y - 120, 260, 23), f.Action.ToUpperInvariant(), 12, team, TextAnchor.MiddleCenter);
            }
            if (f != null && f.Shield > 0) Text(new Rect(x - 150, y - 91, 300, 26), "{     BUFFER " + Mathf.CeilToInt(f.Shield) + "     }", 13, cyan, TextAnchor.MiddleCenter);
            CreatureArt.Draw(new Rect(x - 140, y - 125, 280, 220), s, uiTime, f == null ? index == 0 : f.Facing > 0,
                f == null ? 0 : f.AttackFlash, f == null ? 0 : f.HurtFlash,
                f == null ? 1 : f.Health / s.Health, f == null ? 0 : f.DeathTime + (!f.Alive && finishedAt >= 0 ? uiTime - finishedAt : 0));
            if (f != null && f.Alive)
            {
                Bar(new Rect(x - 47, y + 103, 94, 3), f.SpecialCharge, team);
                if (f.Stun > 0) Text(new Rect(x - 80, y - 101, 160, 24), "* DESYNC *", 14, pink, TextAnchor.MiddleCenter);
            }
        }

        void DrawEffects()
        {
            foreach (var e in Battle.Events)
            {
                float age = Battle.Time - e.Time + (Battle.Finished && finishedAt >= 0 ? uiTime - finishedAt : 0);
                if (age < 0 || age > .85f) continue;
                float x = 40 + e.X * 7.24f, y = 185 + e.Y * 2;
                Color c = e.Kind == "heal" ? cyan : e.Kind == "mutation" ? pink : amber;
                c.a = 1 - age / .85f;
                if (e.Kind == "hit" || e.Kind == "heal")
                {
                    Text(new Rect(x - 55, y - 80 - age * 70, 110, 40), (e.Kind == "heal" ? "+" : "-") + Mathf.RoundToInt(e.Value), 24, c, TextAnchor.MiddleCenter);
                    for (int i = 0; i < 7; i++)
                    {
                        float angle = i * 2.399f + e.Id;
                        float radius = 20 + age * 110;
                        Text(new Rect(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius * .6f, 22, 25), i % 2 == 0 ? "+" : "#", 17, c);
                    }
                }
                else if (e.Kind == "special" || e.Kind == "mutation")
                {
                    Text(new Rect(x - 180, 35 + age * 15, 360, 30), e.Kind == "mutation" ? "! " + Clean(e.Text, 32) : Clean(e.Text, 34), 16, c, TextAnchor.MiddleCenter);
                    Outline(new Rect(x - 45 - age * 140, y - 40 - age * 60, 90 + age * 280, 80 + age * 120), c);
                }
                else if (e.Kind == "projectile" && e.Target >= 0 && e.Target < 2)
                {
                    float t = Mathf.Clamp01(age / Mathf.Max(.05f, e.Duration));
                    float fromX = 40 + e.FromX * 7.24f, fromY = 185 + e.FromY * 2;
                    Line(fromX, fromY, Mathf.Lerp(fromX, x, t), Mathf.Lerp(fromY, y, t), new Color(c.r, c.g, c.b, c.a * .4f), 2);
                    Text(new Rect(Mathf.Lerp(fromX, x, t) - 20, Mathf.Lerp(fromY, y, t) - 15, 40, 30), "<*>", 21, c);
                }
            }
        }

        void Deployment()
        {
            Panel(new Rect(1228, 142, 340, 568));
            Text(new Rect(1246, 157, 304, 24), "03 / BREAK CONTAINMENT", 12, muted);
            Slot(1246, 196, LeftIndex, "A", cyan, ref leftStance);
            Slot(1246, 327, RightIndex, "B", amber, ref rightStance);
            Line(1246, 461, 1550, 461, border);
            Text(new Rect(1246, 474, 304, 23), Fighting ? paused ? "THE PIT IS PAUSED" : "THE SIGNAL IS ALIVE" : Battle != null ? "ANOTHER EXPERIMENT?" : "READY TO RELEASE", 14, paper);
            Text(new Rect(1246, 502, 304, 43), Fighting ? "Watch the outlined windups.\nA brief opening can change everything." : "Stances change risk, reach and tempo.\nFundamental traits stay with the file.", 12, muted);
            if (Button(new Rect(1246, 553, 304, 49), Fighting ? paused ? "RESUME  [SPACE]" : "PAUSE  [SPACE]" : Battle == null ? "RELEASE THE CREATURES" : "REMATCH  [R]", cyan))
            { if (Fighting) paused = !paused; else StartBattle(); }
            if (Button(new Rect(1246, 614, 146, 33), fast ? "TEMPO / 2X" : "TEMPO / 1X", border)) fast = !fast;
            if (Button(new Rect(1404, 614, 146, 33), "RESET PIT", border, Battle != null)) { Battle = null; paused = false; Notify("Specimens returned to containment. Assign a new pairing."); }
            Text(new Rect(1246, 664, 304, 23), "[SPACE] fight / pause  [I] import", 11, muted);
        }

        void Slot(float x, float y, int index, string slot, Color c, ref Stance stance)
        {
            var s = Collection[index];
            Box(new Rect(x, y, 304, 116), ink);
            Box(new Rect(x, y, 3, 116), c);
            Text(new Rect(x + 13, y + 8, 278, 26), slot + " / " + s.Name.ToUpperInvariant(), 16, c);
            Text(new Rect(x + 13, y + 38, 278, 22), s.Body + " / " + s.AbilityName, 12, muted);
            for (int i = 0; i < 3; i++)
            {
                string name = i == 0 ? "BALANCED" : i == 1 ? "HUNT" : "EVADE";
                if (Button(new Rect(x + 12 + 96 * i, y + 77, 88, 26), name, (int)stance == i ? c : border, !Fighting, 10)) stance = (Stance)i;
            }
        }

        void Cabinet()
        {
            Text(new Rect(32, 729, 600, 23), "04 / THE CABINET   " + Collection.Count.ToString("00") + "/36 SIGNALS", 12, muted);
            Text(new Rect(735, 729, 831, 23), "CLICK TO INSPECT  /  ASSIGN ABOVE  /  SCROLL TO EXPLORE", 11, muted, TextAnchor.MiddleRight);
            scroll = GUI.BeginScrollView(new Rect(32, 762, 1536, 142), scroll, new Rect(0, 0, Mathf.Max(1530, Collection.Count * 244), 123), false, false);
            for (int i = 0; i < Collection.Count; i++)
            {
                var s = Collection[i]; Rect r = new Rect(i * 244, 0, 232, 121);
                Box(r, i == SelectedIndex ? new Color(.075f, .13f, .15f) : panel);
                Outline(r, i == SelectedIndex ? cyan : border);
                CreatureArt.Draw(new Rect(r.x + 2, 22, 111, 93), s, uiTime, true);
                Text(new Rect(r.x + 12, 6, 215, 23), Clean(s.Name, 23).ToUpperInvariant(), 13, i == SelectedIndex ? paper : muted);
                Text(new Rect(r.x + 119, 40, 105, 20), s.Source.Family.ToString().ToUpperInvariant(), 10, Accent(s));
                Text(new Rect(r.x + 119, 62, 105, 20), Size(s.Source.Size), 11, muted);
                Text(new Rect(r.x + 119, 84, 105, 20), (i == LeftIndex ? "[A] " : "") + (i == RightIndex ? "[B] " : "") + (s.Mutation != MutationKind.None ? "RARE" : ""), 11, amber);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { SelectedIndex = i; revealAt = uiTime - 2; GUIUtility.keyboardControl = 0; }
            }
            GUI.EndScrollView();
        }

        void ImportModal()
        {
            Box(new Rect(0, 0, 1600, 960), new Color(0, 0, 0, .78f));
            Panel(new Rect(400, 285, 800, 344));
            Text(new Rect(432, 312, 736, 38), "LISTEN TO A FILE", 28, paper);
            Text(new Rect(432, 365, 736, 49), "Paste a full local file path. Any extension is welcome.\nWe sample the bytes; we never execute or change the source.", 15, muted);
            GUI.SetNextControlName("source-path");
            pathInput = GUI.TextField(new Rect(432, 435, 736, 43), pathInput, 32768, field);
            if (Button(new Rect(432, 501, 230, 43), "DISCOVER", cyan, !Busy)) ImportPath(pathInput);
            if (Button(new Rect(678, 501, 230, 43), "BROWSE FILES", border, !Busy)) { modal = false; PickFile(); }
            if (Button(new Rect(924, 501, 244, 43), "CLOSE  [ESC]", border)) { modal = false; GUIUtility.keyboardControl = 0; }
            Text(new Rect(432, 572, 736, 28), "256 KiB maximum sample / local storage only / no network requests", 12, muted);
        }

        void HelpModal()
        {
            Box(new Rect(0, 0, 1600, 960), new Color(0, 0, 0, .84f));
            Panel(new Rect(260, 122, 1080, 716));
            Text(new Rect(296, 151, 1008, 42), "FIELD NOTES / WHAT LIVES IN THE BYTES", 26, paper);
            Text(new Rect(296, 213, 465, 420),
                "01  DISCOVER\nAny local file is a specimen. Images, songs,\ncode, archives, empty files. Unknown is fine.\nChoose a card; assign it to A or B.\n\n02  READ THE ANATOMY\nSize gives bulk, with diminishing returns.\nEntropy favors volatile, evasive creatures.\nRepetition builds shells and regenerative\nabilities. Readable text develops ranged\nattacks. Power always has a tradeoff.\n\n03  RELEASE\nHUNT closes distance and trades armor for\ndamage. EVADE keeps space and trades damage\nfor protection. BALANCED follows its nature.\nWindups give warning; impacts interrupt.\nPressure eventually ends a stubborn bout.", 16, muted, TextAnchor.UpperLeft);
            var s = Selected; var p = s.Source;
            Text(new Rect(820, 213, 480, 26), "CURRENT SIGNAL / " + Clean(p.FileName, 32), 15, cyan);
            Text(new Rect(820, 258, 480, 207),
                "Size          " + Size(p.Size) + "\n" +
                "Bytes read    " + p.SampleBytes.ToString("N0") + (p.Sampled ? " (sampled)" : " (complete)") + "\n" +
                "Entropy       " + (p.Entropy * 100).ToString("0.0") + "%\n" +
                "Compression   " + (p.Compressibility * 100).ToString("0.0") + "%\n" +
                "Repetition    " + (p.Repetition * 100).ToString("0.0") + "%\n" +
                "Readable      " + (p.Printable * 100).ToString("0.0") + "%\n" +
                "Byte alphabet " + p.DistinctBytes + "/256\n" +
                "Identity      " + p.Fingerprint.Substring(0, Math.Min(16, p.Fingerprint.Length)), 14, paper);
            Text(new Rect(820, 495, 466, 116), s.Mutation == MutationKind.None ? "NO ANOMALY DETECTED\n\nTry a blank file, a repeated phrase, or\nsomething you have not opened in years." : "ANOMALY: " + s.Mutation.ToString().ToUpperInvariant() + "\n\n" + s.MutationDescription, 15, amber, TextAnchor.UpperLeft, true);
            Text(new Rect(296, 679, 1008, 49), "Unchanged bytes + extension = unchanged creature. Renaming the basename is harmless.\nLarge files use distributed samples; edits outside those samples may be invisible.", 13, muted);
            if (Button(new Rect(296, 755, 230, 43), "BACK TO THE PIT", cyan)) help = false;
            if (Button(new Rect(820, 755, 466, 43), "REMOVE SELECTED SPECIMEN", border, !Fighting && SelectedIndex != LeftIndex && SelectedIndex != RightIndex && Collection.Count > 2))
            {
                int removed = SelectedIndex; Collection.RemoveAt(removed);
                if (LeftIndex > removed) LeftIndex--; if (RightIndex > removed) RightIndex--;
                SelectedIndex = Mathf.Clamp(removed, 0, Collection.Count - 1); Battle = null; help = false;
            }
        }

        // Helpers keep the interface in a fixed design space, letterboxed on any aspect ratio.
        void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { font = font, richText = false, padding = new RectOffset(), clipping = TextClipping.Clip };
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 17, padding = new RectOffset(12, 12, 9, 9) };
            field.normal.textColor = paper;
        }
        void Text(Rect r, string value, int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
        {
            if (!styles.TryGetValue(size, out var style)) { style = new GUIStyle(label) { fontSize = size }; styles[size] = style; }
            style.normal.textColor = color; style.alignment = anchor; style.wordWrap = wrap;
            GUI.Label(r, value ?? "", style);
        }
        void Box(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }
        void Outline(Rect r, Color color)
        { Box(new Rect(r.x, r.y, r.width, 1), color); Box(new Rect(r.x, r.yMax - 1, r.width, 1), color); Box(new Rect(r.x, r.y, 1, r.height), color); Box(new Rect(r.xMax - 1, r.y, 1, r.height), color); }
        void Panel(Rect r) { Box(r, panel); Outline(r, border); }
        void Bar(Rect r, float value, Color color) { Box(r, border); Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), color); }
        bool Button(Rect r, string text, Color color, bool enabled = true, int size = 13)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Box(r, hover ? new Color(color.r * .3f, color.g * .3f, color.b * .3f) : ink);
            Outline(r, enabled ? color : border);
            Text(r, text, size, enabled ? color == border ? paper : color : muted, TextAnchor.MiddleCenter);
            bool click = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (click) GUIUtility.keyboardControl = 0;
            return click;
        }
        void Stat(float x, float y, string name, float value, float max, Color c, string suffix = "")
        { Text(new Rect(x, y, 139, 19), name, 10, muted); Text(new Rect(x, y, 139, 19), value.ToString("0") + suffix, 12, paper, TextAnchor.UpperRight); Bar(new Rect(x, y + 25, 136, 3), value / max, c); }
        void Line(float x1, float y1, float x2, float y2, Color color, float width = 1)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(y2 - y1, x2 - x1) * Mathf.Rad2Deg, new Vector2(x1, y1));
            Box(new Rect(x1, y1, Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2)), width), color); GUI.matrix = m;
        }
        void CenterBadge(string value, Color c) { Box(new Rect(212, 100, 380, 53), ink); Outline(new Rect(212, 100, 380, 53), c); Text(new Rect(212, 100, 380, 53), value, 18, c, TextAnchor.MiddleCenter); }
        static Color Accent(Specimen s) { return Color.HSVToRGB(s.Hue, .52f, .95f); }
        static string Size(long bytes) { return bytes < 1024 ? bytes + " B" : bytes < 1048576 ? (bytes / 1024f).ToString("0.0") + " KiB" : bytes < 1073741824 ? (bytes / 1048576f).ToString("0.0") + " MiB" : (bytes / 1073741824f).ToString("0.00") + " GiB"; }
        static string Clean(string text, int max)
        {
            if (text == null) return "";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return text.Length <= max ? text : text.Substring(0, Math.Max(0, max - 3)) + "...";
        }
        static bool HasArgument(string value) { return Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0; }
        static string Argument(string key)
        { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        IEnumerator RuntimeSmoke()
        {
            Application.runInBackground = true;
            sound.Muted = true;
            string folder = Argument("-null-output") ?? Path.Combine(Application.persistentDataPath, "NullMenagerie", "Validation");
            Directory.CreateDirectory(folder);
            var errors = new List<string>();
            Application.LogCallback capture = (condition, trace, type) => { if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 32 && !errors.Contains(condition)) errors.Add(condition); };
            Application.logMessageReceived += capture;
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "01-cabinet.png"));
            yield return new WaitForSecondsRealtime(.5f);
            StartBattle();
            yield return new WaitForSecondsRealtime(5);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "02-combat.png"));
            while (!Battle.Finished) yield return null;
            yield return new WaitForSecondsRealtime(.6f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "03-result.png"));
            // CaptureScreenshot records at presentation; keep the result alive until that frame lands.
            yield return new WaitForSecondsRealtime(.4f);
            bool complete = Battle.Finished && Battle.Time > 0;
            Battle = null; StartBattle();
            bool replay = Battle != null && !Battle.Finished;
            yield return new WaitForSecondsRealtime(.3f);
            Battle = null;
            string fixture = Path.Combine(folder, "discovery-test.unknown");
            File.WriteAllText(fixture, "A file is a small house for a very strange animal.\n");
            int before = Collection.Count;
            ImportPath(fixture);
            while (Busy) yield return null;
            bool imported = Collection.Count == before + 1;
            ImportPath(fixture);
            while (Busy) yield return null;
            bool deduplicated = Collection.Count == before + 1;
            ImportPath(Path.Combine(folder, "not-a-real-file.missing"));
            while (Busy) yield return null;
            bool recovered = errorMessage;
            SelectedIndex = Collection.Count - 1; help = true;
            yield return new WaitForSecondsRealtime(.3f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "04-inspection.png"));
            yield return new WaitForSecondsRealtime(.4f);
            File.WriteAllText(Path.Combine(folder, "runtime-smoke.txt"), "Unity " + Application.unityVersion + "\nGraphics " + SystemInfo.graphicsDeviceName + "\nRendered UI frames: " + renderedFrames + "\nEngine rendered frames: " + UnityEngine.Time.renderedFrameCount + "\nPipeline: " + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "Built-in") + "\nCompleted bout: " + complete + "\nReplay started: " + replay + "\nAsync import: " + imported + "\nDuplicate identity: " + deduplicated + "\nImport error recovery: " + recovered + "\nRuntime errors: " + errors.Count + "\n" + string.Join("\n", errors));
            Application.logMessageReceived -= capture;
            Application.Quit(complete && replay && imported && deduplicated && recovered && errors.Count == 0 && (Application.isBatchMode || renderedFrames > 10) ? 0 : 1);
        }

        void OnDestroy()
        {
            lifetime?.Cancel(); lifetime?.Dispose();
            Application.targetFrameRate = previousTargetFrameRate;
            Application.runInBackground = previousRunInBackground;
            Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
            if (font && ownsFont) Destroy(font);
            CreatureArt.Release();
        }
    }
}
