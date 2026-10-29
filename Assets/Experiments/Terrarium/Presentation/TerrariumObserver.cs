using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Terrarium.Core;
using UnityEngine;
using Terrain = Terrarium.Core.Terrain;

namespace Terrarium.Presentation
{
    /// <summary>Scene-owned observer. Simulation advances only through explicit fixed ticks.</summary>
    public sealed class TerrariumObserver : MonoBehaviour
    {
        [Header("Restart applies these simulation parameters")]
        [SerializeField] private SimulationConfig configuration = new SimulationConfig();
        [SerializeField] private int initialSeed = 7;
        [SerializeField] private bool startPaused = false;
        [SerializeField, Range(1, 20)] private int ticksPerSecond = 4;

        private const float CanvasWidth = 1440, CanvasHeight = 900;
        private static readonly int[] Speeds = { 1, 4, 16, 64, 256 };
        private static readonly string[] Tabs = { "Needs", "Why", "Memories", "Relations", "Beliefs", "Events" };
        private readonly Color background = new Color(.035f, .055f, .061f);
        private readonly Color panel = new Color(.055f, .083f, .089f);
        private readonly Color inset = new Color(.038f, .062f, .068f);
        private readonly Color line = new Color(.15f, .24f, .25f);
        private readonly Color paper = new Color(.89f, .92f, .84f);
        private readonly Color muted = new Color(.54f, .67f, .66f);
        private readonly Color accent = new Color(.58f, .88f, .65f);
        private readonly Color amber = new Color(.94f, .73f, .43f);
        private readonly Color red = new Color(.95f, .47f, .41f);
        private readonly Color blue = new Color(.44f, .72f, .83f);
        private Simulation simulation;
        private bool paused, beliefMap, follow, onlySelectedEvents, tail = true, compareTruth = true, help;
        private int speedIndex, selectedId, tab, selectedEventId = -1, decisionTick = -1;
        private int tileX = -1, tileY = -1;
        private string seedInput = "7", notice = "", error = "";
        private double accumulator, rateElapsed;
        private long rateTicks;
        private float actualRate;
        private Vector2 logScroll;
        private Vector2 helpScroll;
        private float helpHeight = 800;
        private readonly Vector2[] scroll = new Vector2[6];
        private float contentHeight = 1200;
        private Font font;
        private bool ownsFont;
        private GUIStyle normal, small, heading, title, glyph, button, field, wrap, tiny, tooltip;
        private readonly Stopwatch frameBudget = new Stopwatch();
        private int lastLogCount = -1, lastLogEventId = -1;
        private Camera backdrop;

        public Simulation World { get { return simulation; } }

        private void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Courier New" }, 16);
            ownsFont = font != null;
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var cameraObject = new GameObject("Terrarium / observer backdrop");
            cameraObject.transform.SetParent(transform, false);
            backdrop = cameraObject.AddComponent<Camera>();
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = background;
            backdrop.cullingMask = 0;
            backdrop.orthographic = true;
            backdrop.depth = 10;
            cameraObject.AddComponent<AudioListener>();
            paused = startPaused;
            Restart(initialSeed);
        }

        private void OnDestroy()
        {
            if (ownsFont && font) Destroy(font);
            if (backdrop) Destroy(backdrop.gameObject);
        }

        private void Restart(int seed)
        {
            try
            {
                if (configuration == null) configuration = new SimulationConfig();
                Simulation next = new Simulation(configuration.Copy(), seed);
                simulation = next;
                initialSeed = seed;
                seedInput = seed.ToString(CultureInfo.InvariantCulture);
                selectedId = simulation.Agents.Count > 0 ? simulation.Agents[0].Id : -1;
                selectedEventId = -1;
                decisionTick = -1;
                tileX = tileY = -1;
                accumulator = rateElapsed = 0;
                rateTicks = 0;
                actualRate = 0;
                logScroll = Vector2.zero;
                for (int i = 0; i < scroll.Length; i++) scroll[i] = Vector2.zero;
                lastLogCount = -1;
                error = "";
                notice = "Fresh world. Click an inhabitant, then inspect WHY.";
            }
            catch (Exception ex)
            {
                error = "Cannot restart: " + ex.Message;
                paused = true;
                UnityEngine.Debug.LogException(ex, this);
            }
        }

        private void Update()
        {
            if (simulation == null || paused || GUIUtility.hotControl != 0) return;
            // Hold the visible snapshot during a pointer drag/click so moving agents
            // and expiring history cannot change IMGUI controls between press and release.
            double delta = Math.Min(UnityEngine.Time.unscaledDeltaTime, .25f);
            accumulator += delta * ticksPerSecond * Speeds[speedIndex];
            // Bounded debt and a wall-clock budget keep acceleration responsive.
            // Achieved ticks/sec is shown instead of pretending the requested rate was reached.
            accumulator = Math.Min(accumulator, 512);
            frameBudget.Restart();
            int advanced = 0;
            while (accumulator >= 1 && advanced < 256 && frameBudget.Elapsed.TotalMilliseconds < 12)
            {
                simulation.Step();
                accumulator -= 1;
                advanced++;
            }
            rateElapsed += UnityEngine.Time.unscaledDeltaTime;
            rateTicks += advanced;
            if (rateElapsed >= .5)
            {
                actualRate = (float)(rateTicks / rateElapsed);
                rateElapsed = 0;
                rateTicks = 0;
            }
        }

        private void EnsureStyles()
        {
            if (normal != null) return;
            normal = MakeStyle(13, paper);
            small = MakeStyle(11, muted);
            tiny = MakeStyle(10, muted);
            heading = MakeStyle(15, paper, FontStyle.Bold);
            title = MakeStyle(31, paper, FontStyle.Bold);
            glyph = MakeStyle(15, paper, FontStyle.Bold);
            glyph.alignment = TextAnchor.MiddleCenter;
            button = MakeStyle(12, paper);
            button.alignment = TextAnchor.MiddleCenter;
            field = new GUIStyle(GUI.skin.textField);
            field.font = font;
            field.fontSize = 13;
            field.normal.textColor = paper;
            field.focused.textColor = paper;
            field.padding = new RectOffset(8, 8, 5, 4);
            wrap = MakeStyle(12, paper);
            wrap.wordWrap = true;
            tooltip = MakeStyle(12, paper);
            tooltip.wordWrap = true;
            tooltip.padding = new RectOffset(10, 10, 8, 8);
        }

        private GUIStyle MakeStyle(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var result = new GUIStyle(GUI.skin.label);
            result.font = font;
            result.fontSize = size;
            result.fontStyle = weight;
            result.normal.textColor = color;
            result.padding = new RectOffset(0, 0, 0, 0);
            result.clipping = TextClipping.Clip;
            return result;
        }

        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            float scale = Math.Max(.1f, Math.Min(Screen.width / CanvasWidth, Screen.height / CanvasHeight));
            ApplyReadableText(scale);
            float ox = (Screen.width - CanvasWidth * scale) * .5f;
            float oy = (Screen.height - CanvasHeight * scale) * .5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            Fill(new Rect(0, 0, CanvasWidth, CanvasHeight), background);
            HandleKeys();
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !help;
            DrawHeader();
            if (simulation != null)
            {
                DrawWorld(new Rect(24, 126, 864, 538));
                DrawInspector(new Rect(906, 126, 510, 538));
                DrawHistory(new Rect(24, 678, 1392, 180));
            }
            DrawFooter();
            GUI.enabled = wasEnabled;
            if (help) DrawHelp();
            if (!string.IsNullOrEmpty(GUI.tooltip)) DrawTooltip(GUI.tooltip);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void ApplyReadableText(float scale)
        {
            // Detailed panels wrap and scroll, so preserve readable physical font sizes
            // at the project's 1024px Game view rather than shrinking every label to 8px.
            float effectiveScale = Math.Max(.67f, scale);
            normal.fontSize = Math.Max(13, Mathf.CeilToInt(11 / effectiveScale));
            small.fontSize = Math.Max(11, Mathf.CeilToInt(10 / effectiveScale));
            tiny.fontSize = Math.Max(10, Mathf.CeilToInt(9 / effectiveScale));
            heading.fontSize = Math.Max(15, Mathf.CeilToInt(13 / effectiveScale));
            button.fontSize = Math.Max(12, Mathf.CeilToInt(10 / effectiveScale));
            field.fontSize = normal.fontSize;
            wrap.fontSize = Math.Max(12, Mathf.CeilToInt(11 / effectiveScale));
            tooltip.fontSize = wrap.fontSize;
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Escape) { help = false; GUI.FocusControl(null); e.Use(); return; }
            if (GUI.GetNameOfFocusedControl() == "TerrariumSeed") return;
            if (e.keyCode == KeyCode.Space) { paused = !paused; accumulator = 0; e.Use(); }
            else if (e.keyCode == KeyCode.Period && simulation != null) { paused = true; simulation.Step(); e.Use(); }
        }

        private void DrawHeader()
        {
            Text(new Rect(24, 17, 410, 42), "TERRARIUM", title);
            Text(new Rect(25, 61, 560, 20), "AN OBSERVATORY FOR AN UNWRITTEN WORLD", small);
            if (simulation != null)
            {
                int alive = 0;
                for (int i = 0; i < simulation.Agents.Count; i++) if (simulation.Agents[i].Alive) alive++;
                Text(new Rect(600, 20, 530, 24), "DAY " + (simulation.Tick / simulation.Config.DayLength + 1).ToString("000") +
                    "   " + TimeOfDay(simulation.Tick) + "   /   " + alive + " LIVING", heading);
                Text(new Rect(600, 48, 630, 22), simulation.Weather.Description + "   cold " + Percent(simulation.Weather.Cold) +
                    "   rain " + Percent(simulation.Weather.Rain) + "   |   tick " + simulation.Tick, small);
            }
            if (Button(new Rect(1312, 24, 104, 30), "FIELD GUIDE", help)) help = !help;
            Fill(new Rect(24, 83, 1392, 1), line);
            if (Button(new Rect(24, 92, 104, 26), paused ? "> RESUME" : "II PAUSE", !paused)) { paused = !paused; accumulator = 0; }
            if (Button(new Rect(134, 92, 66, 26), "STEP") && simulation != null) { paused = true; accumulator = 0; simulation.Step(); }
            for (int i = 0; i < Speeds.Length; i++)
                if (Button(new Rect(217 + i * 54, 92, 48, 26), Speeds[i] + "x", speedIndex == i)) speedIndex = i;
            Text(new Rect(498, 98, 174, 20), paused ? "CLOCK PAUSED" : actualRate.ToString("0", CultureInfo.InvariantCulture) + " ticks / sec", small);
            Text(new Rect(724, 97, 48, 20), "SEED", small);
            GUI.SetNextControlName("TerrariumSeed");
            seedInput = GUI.TextField(new Rect(768, 91, 132, 28), seedInput, 12, field);
            if (Button(new Rect(907, 92, 102, 26), "RESTART"))
            {
                int seed;
                if (int.TryParse(seedInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) Restart(seed);
                else error = "Seed must be a signed 32-bit integer.";
                GUI.FocusControl(null);
            }
            if (Button(new Rect(1015, 92, 106, 26), "RANDOM SEED"))
            {
                // Only choosing a new seed uses outside entropy.
                Restart(Guid.NewGuid().GetHashCode());
                GUI.FocusControl(null);
            }
            Text(new Rect(1144, 97, 272, 20), "Space pause   . single tick", small);
        }

        private Agent Selected() { return selectedId < 0 ? null : simulation.GetAgent(selectedId); }

        private void SelectAgent(int id)
        {
            if (id == selectedId) return;
            selectedId = id;
            decisionTick = -1;
            for (int i = 0; i < scroll.Length; i++) scroll[i] = Vector2.zero;
            notice = "Inspecting " + AgentName(id) + ". The map can show this inhabitant's remembered world.";
        }

        private void DrawWorld(Rect rect)
        {
            Panel(rect);
            Text(new Rect(rect.x + 14, rect.y + 12, 305, 25), beliefMap ? "THE REMEMBERED WORLD" : "THE SHARED WORLD", heading);
            if (Button(new Rect(rect.x + 355, rect.y + 10, 114, 26), "WORLD TRUTH", !beliefMap)) beliefMap = false;
            if (Button(new Rect(rect.x + 475, rect.y + 10, 122, 26), "AGENT BELIEF", beliefMap)) beliefMap = true;
            if (Button(new Rect(rect.x + 605, rect.y + 10, 92, 26), follow ? "FOLLOWING" : "FOLLOW", follow)) follow = !follow;
            Text(new Rect(rect.x + 707, rect.y + 16, 145, 20), follow ? "zoomed to selected" : "whole landscape", small);
            Agent selected = Selected();
            int width = simulation.Config.Width, height = simulation.Config.Height;
            int viewWidth = follow ? Math.Min(width, 22) : width;
            int viewHeight = follow ? Math.Min(height, 16) : height;
            int startX = follow && selected != null ? Math.Max(0, Math.Min(width - viewWidth, selected.X - viewWidth / 2)) : 0;
            int startY = follow && selected != null ? Math.Max(0, Math.Min(height - viewHeight, selected.Y - viewHeight / 2)) : 0;
            Rect map = new Rect(rect.x + 14, rect.y + 46, rect.width - 28, 357);
            float cw = map.width / viewWidth, ch = map.height / viewHeight;
            Fill(map, inset);
            for (int y = startY; y < startY + viewHeight; y++)
            {
                for (int x = startX; x < startX + viewWidth; x++)
                {
                    Rect cell = new Rect(map.x + (x - startX) * cw, map.y + (y - startY) * ch, cw - 1, ch - 1);
                    int index = y * width + x;
                    bool known = !beliefMap || (selected != null && selected.KnownTerrain != null && index < selected.KnownTerrain.Length && selected.KnownTerrain[index]);
                    Terrain terrain = known ? (beliefMap ? selected.TerrainMemory[index] : simulation.Tiles[x, y].Terrain) : Terrain.Meadow;
                    Fill(cell, known ? TerrainColor(terrain) : new Color(.025f, .04f, .045f));
                    if (known && (terrain == Terrain.Rock || terrain == Terrain.Water))
                        Glyph(cell, terrain == Terrain.Rock ? "#" : "~", terrain == Terrain.Rock ? muted : blue, Math.Min(cw, ch));
                    if (x == tileX && y == tileY) Outline(cell, amber);
                    GUI.Label(cell, new GUIContent("", known ? "Cell " + x + "," + y + ": " + terrain + (beliefMap ? " (remembered)" : " (actual)") : "Unobserved terrain"));
                }
            }
            if (beliefMap && selected != null)
            {
                foreach (ObjectBelief belief in selected.Beliefs.Values)
                {
                    if (!InView(belief.X, belief.Y, startX, startY, viewWidth, viewHeight)) continue;
                    Rect cell = MapCell(map, belief.X, belief.Y, startX, startY, cw, ch);
                    Color color = ObjectColor(belief.Kind);
                    color.a = (float)Math.Max(.3, belief.Confidence);
                    Glyph(cell, ObjectSymbol(belief.Kind), color, Math.Min(cw, ch));
                    GUI.Label(cell, new GUIContent("", belief.Name + ": believed stock " + F(belief.EstimatedStock) + ", confidence " + Percent(belief.Confidence) + ", age " + (simulation.Tick - belief.ObservedTick) + " ticks"));
                }
                foreach (PersonBelief belief in selected.People.Values)
                {
                    if (belief.AgentId == selected.Id || !belief.Alive || !InView(belief.X, belief.Y, startX, startY, viewWidth, viewHeight)) continue;
                    Rect cell = MapCell(map, belief.X, belief.Y, startX, startY, cw, ch);
                    Color color = AgentColor(belief.AgentId);
                    if (simulation.Tick - belief.ObservedTick > 3) color.a = .45f;
                    DrawAgentMarker(cell, belief.AgentId, color, false, "Remembered " + AgentName(belief.AgentId) + ", seen " + (simulation.Tick - belief.ObservedTick) + " ticks ago. This position may be wrong.");
                }
                if (InView(selected.X, selected.Y, startX, startY, viewWidth, viewHeight))
                    DrawAgentMarker(MapCell(map, selected.X, selected.Y, startX, startY, cw, ch), selected.Id, AgentColor(selected.Id), true, selected.Name + ": self-location");
            }
            else
            {
                for (int i = 0; i < simulation.Objects.Count; i++)
                {
                    WorldObject obj = simulation.Objects[i];
                    if (!InView(obj.X, obj.Y, startX, startY, viewWidth, viewHeight)) continue;
                    Rect cell = MapCell(map, obj.X, obj.Y, startX, startY, cw, ch);
                    Glyph(cell, ObjectSymbol(obj.Kind), ObjectColor(obj.Kind), Math.Min(cw, ch));
                    GUI.Label(cell, new GUIContent("", obj.Name + " / stock " + F(obj.Stock) + " / owner " + Owner(obj.OwnerId)));
                }
                for (int i = 0; i < simulation.Agents.Count; i++)
                {
                    Agent agent = simulation.Agents[i];
                    if (!InView(agent.X, agent.Y, startX, startY, viewWidth, viewHeight)) continue;
                    Rect cell = MapCell(map, agent.X, agent.Y, startX, startY, cw, ch);
                    DrawAgentMarker(cell, agent.Id, agent.Alive ? AgentColor(agent.Id) : muted, agent.Id == selectedId,
                        agent.Name + (agent.Alive ? ": " + agent.Activity : ": deceased"));
                }
            }
            // Agent marker buttons get first refusal. A background cell must not steal
            // the mouse before its overlaid inhabitant can be selected.
            if (GUI.enabled && Event.current.type == EventType.MouseDown && Event.current.button == 0 && map.Contains(Event.current.mousePosition))
            {
                tileX = startX + (int)((Event.current.mousePosition.x - map.x) / cw);
                tileY = startY + (int)((Event.current.mousePosition.y - map.y) / ch);
                notice = TileDescription(tileX, tileY, selected);
                Event.current.Use();
            }
            Text(new Rect(rect.x + 14, rect.y + 411, rect.width - 28, 18),
                "~ water   # rock   * food   | material   o cache   ^ shelter   @ spring   A.. inhabitants", small);
            int columns = 8;
            float rosterWidth = (rect.width - 28) / columns;
            for (int i = 0; i < simulation.Agents.Count; i++)
            {
                Agent agent = simulation.Agents[i];
                int row = i / columns;
                if (row >= 3) break;
                Rect item = new Rect(rect.x + 14 + (i % columns) * rosterWidth, rect.y + 437 + row * 25, rosterWidth - 4, 23);
                Color old = button.normal.textColor;
                button.normal.textColor = agent.Alive ? AgentColor(agent.Id) : muted;
                if (Button(item, Symbol(agent.Id) + " " + agent.Name + (agent.Alive ? "" : " x"), selectedId == agent.Id)) SelectAgent(agent.Id);
                button.normal.textColor = old;
            }
            if (simulation.Agents.Count > 24)
                Text(new Rect(rect.x + 14, rect.y + 521, rect.width - 28, 15), "Population >24: select others on the map or with inspector arrows.", tiny);
            else
                Text(new Rect(rect.x + 14, rect.y + 514, rect.width - 28, 18), beliefMap ?
                    "MAP: selected agent's memory. Dim markers may be stale; observer panels remain truth." :
                    "MAP: actual shared state. Click a marker to inspect; FOLLOW magnifies the selected inhabitant.", small);
        }

        private void DrawAgentMarker(Rect rect, int id, Color color, bool selected, string description)
        {
            if (selected) Fill(rect, new Color(.22f, .34f, .26f));
            Glyph(rect, Symbol(id), color, Math.Min(rect.width, rect.height));
            if (selected) Outline(rect, accent);
            if (GUI.Button(rect, new GUIContent("", description), GUIStyle.none)) SelectAgent(id);
        }

        private void DrawInspector(Rect rect)
        {
            Panel(rect);
            Agent agent = Selected();
            if (agent == null) return;
            Text(new Rect(rect.x + 14, rect.y + 10, 310, 27), Symbol(agent.Id) + " / " + agent.Name.ToUpperInvariant(), heading, AgentColor(agent.Id));
            Text(new Rect(rect.x + 14, rect.y + 39, rect.width - 28, 22), agent.Alive ? agent.Activity : "Deceased / retained record", normal);
            if (Button(new Rect(rect.x + 406, rect.y + 10, 36, 26), "<")) CycleAgent(-1);
            if (Button(new Rect(rect.x + 448, rect.y + 10, 36, 26), ">")) CycleAgent(1);
            for (int i = 0; i < Tabs.Length; i++)
                if (Button(new Rect(rect.x + 12 + i * 81, rect.y + 70, 77, 27), Tabs[i], tab == i)) { tab = i; contentHeight = 1500; }
            Rect body = new Rect(rect.x + 14, rect.y + 109, rect.width - 28, rect.height - 121);
            float innerWidth = body.width - 20;
            scroll[tab] = GUI.BeginScrollView(body, scroll[tab], new Rect(0, 0, innerWidth, contentHeight));
            float y = 0;
            switch (tab)
            {
                case 0: DrawNeeds(agent, innerWidth, ref y); break;
                case 1: DrawWhy(agent, innerWidth, ref y); break;
                case 2: DrawMemories(agent, innerWidth, ref y); break;
                case 3: DrawRelationships(agent, innerWidth, ref y); break;
                case 4: DrawBeliefs(agent, innerWidth, ref y); break;
                case 5: DrawEventDetails(agent, innerWidth, ref y); break;
            }
            GUI.EndScrollView(!help);
            if (Event.current.type == EventType.Repaint) contentHeight = Math.Max(body.height - 1, y + 12);
        }

        private void DrawNeeds(Agent agent, float width, ref float y)
        {
            Paragraph("Deficits rise from 0 (satisfied) to 1 (urgent). These are the current internal state, visible to you as observer.", width, ref y, muted);
            NeedBar("HUNGER", agent.Needs.Hunger, width, ref y);
            NeedBar("THIRST", agent.Needs.Thirst, width, ref y);
            NeedBar("FATIGUE", agent.Needs.Fatigue, width, ref y);
            NeedBar("EXPOSURE", agent.Needs.Exposure, width, ref y);
            NeedBar("ISOLATION", agent.Needs.Loneliness, width, ref y);
            NeedBar("UNCERTAINTY", agent.Needs.Uncertainty, width, ref y);
            y += 8;
            Row("Health", Percent(agent.Health), width, ref y);
            Row("Carried food / materials", F(agent.Inventory.Food) + " / " + F(agent.Inventory.Materials), width, ref y);
            Row("Carrying capacity", F(agent.Inventory.Total) + " / " + F(agent.Inventory.Capacity), width, ref y);
            Row("Position / home object", agent.X + "," + agent.Y + " / " + (agent.HomeObjectId >= 0 ? "#" + agent.HomeObjectId : "none"), width, ref y);
            Section("CONTINUOUS MOTIVATIONAL WEIGHTS", width, ref y);
            Row("Food / water / rest", F(agent.Traits.FoodWeight) + " / " + F(agent.Traits.WaterWeight) + " / " + F(agent.Traits.RestWeight), width, ref y);
            Row("Social / comfort / curiosity", F(agent.Traits.SocialWeight) + " / " + F(agent.Traits.ComfortWeight) + " / " + F(agent.Traits.Curiosity), width, ref y);
            Row("Consideration / ownership", F(agent.Traits.Consideration) + " / " + F(agent.Traits.OwnershipRespect), width, ref y);
            Row("Risk tolerance / attachment", F(agent.Traits.RiskTolerance) + " / " + F(agent.Traits.AttachmentRate), width, ref y);
            Row("Desired reserve", F(agent.Traits.ReserveTarget), width, ref y);
            Section("LEARNED ACTION RETURNS", width, ref y);
            Paragraph("Experience updates expectations. Return estimates can be inaccurate; prediction error is actual minus expected.", width, ref y, muted);
            var kinds = new List<ActionKind>(agent.Learning.Keys);
            kinds.Sort();
            for (int i = 0; i < kinds.Count; i++)
            {
                ActionLearning learning = agent.Learning[kinds[i]];
                Row(learning.Kind + " [" + learning.Attempts + "]", F(learning.ExpectedReturn) + "  error " + Signed(learning.LastPredictionError), width, ref y);
            }
        }

        private void DrawWhy(Agent agent, float width, ref float y)
        {
            DecisionRecord record = agent.LastDecision;
            if (decisionTick >= 0)
            {
                record = null;
                for (int i = 0; i < agent.Decisions.Count; i++) if (agent.Decisions[i].Tick == decisionTick) { record = agent.Decisions[i]; break; }
                if (record == null) { decisionTick = -1; record = agent.LastDecision; }
            }
            if (record == null) { Paragraph("No decision yet. Advance one tick to observe the first evaluation.", width, ref y); return; }
            if (Button(new Rect(0, y, 92, 25), "< EARLIER")) SelectDecision(agent, record, -1);
            if (Button(new Rect(98, y, 88, 25), "LATER >")) SelectDecision(agent, record, 1);
            if (Button(new Rect(192, y, 128, 25), "LATEST / LIVE", decisionTick < 0)) decisionTick = -1;
            y += 35;
            Paragraph("DECISION AT " + TickLabel(record.Tick) + (decisionTick < 0 ? "  [following latest]" : "  [pinned]"), width, ref y, accent);
            if (record.Chosen != null)
            {
                ActionCandidate chosen = record.Chosen;
                Paragraph(chosen.Label ?? chosen.Kind.ToString(), width, ref y, accent, true);
                Paragraph(chosen.Reason ?? "No reason recorded.", width, ref y);
                Paragraph("CHOSEN SCORE " + F(chosen.Score) + "  /  expected benefit " + F(chosen.ExpectedValue) +
                    "  travel " + F(chosen.TravelCost) + "  risk " + F(chosen.RiskCost) +
                    "  social " + F(chosen.SocialCost) + "  confidence " + Percent(chosen.Confidence), width, ref y, accent);
            }
            Paragraph(record.PerceivedContext ?? "No context recorded.", width, ref y, muted);
            if (record.NeedsAtDecision != null)
            {
                Needs n = record.NeedsAtDecision;
                Paragraph("At choice: food " + Percent(n.Hunger) + ", water " + Percent(n.Thirst) + ", rest " + Percent(n.Fatigue) +
                    ", contact " + Percent(n.Loneliness) + ", exposure " + Percent(n.Exposure) + ", uncertainty " + Percent(n.Uncertainty), width, ref y, muted);
            }
            Paragraph(record.Completed ? "OUTCOME: " + record.Outcome : "IN PROGRESS: this is the reason for the current intention.", width, ref y, record.Completed ? blue : amber);
            Row("Expected / realized benefit", F(record.ExpectedUtility) + " / " + (record.Completed ? F(record.ActualUtility) : "pending"), width, ref y);
            if (record.EventId >= 0) EventLink(record.EventId, "Inspect outcome event", width, ref y);
            if (record.Chosen != null && record.Chosen.CauseEventId >= 0) EventLink(record.Chosen.CauseEventId, "Inspect prior cause", width, ref y);
            Section("TOP RETAINED LOSING ALTERNATIVES", width, ref y);
            Paragraph("Scores compare predicted need relief and learned return against travel, risk and social costs. Up to 12 losing candidates are retained; the full evaluated count is in the decision context.", width, ref y, muted);
            for (int i = 0; i < record.Alternatives.Count; i++)
            {
                ActionCandidate candidate = record.Alternatives[i];
                bool chosen = record.Chosen != null && candidate.Kind == record.Chosen.Kind && candidate.TargetObjectId == record.Chosen.TargetObjectId &&
                    candidate.TargetAgentId == record.Chosen.TargetAgentId && candidate.TargetX == record.Chosen.TargetX && candidate.TargetY == record.Chosen.TargetY;
                Fill(new Rect(0, y, width, 25), chosen ? new Color(.12f, .22f, .17f) : inset);
                Text(new Rect(8, y + 4, width - 85, 21), (chosen ? "> " : "  ") + (candidate.Label ?? candidate.Kind.ToString()), small, chosen ? accent : paper);
                Text(new Rect(width - 78, y + 4, 70, 21), F(candidate.Score), normal, chosen ? accent : paper);
                y += 31;
                Paragraph(candidate.Reason ?? "No reason recorded.", width, ref y, muted);
                Paragraph("value " + F(candidate.ExpectedValue) + "  travel " + F(candidate.TravelCost) + "  risk " + F(candidate.RiskCost) +
                    "  social " + F(candidate.SocialCost) + "  confidence " + Percent(candidate.Confidence), width, ref y, muted);
                if (candidate.CauseEventId >= 0) EventLink(candidate.CauseEventId, "Candidate evidence", width, ref y);
                y += 6;
            }
        }

        private void DrawMemories(Agent agent, float width, ref float y)
        {
            Paragraph("Bounded individual memory, newest first. Evaluations belong to this inhabitant; they are not a narrator's interpretation.", width, ref y, muted);
            for (int i = agent.Memories.Count - 1; i >= 0; i--)
            {
                MemoryRecord memory = agent.Memories[i];
                Section(TickLabel(memory.Tick) + "   event #" + memory.EventId, width, ref y);
                Paragraph(memory.Summary, width, ref y);
                Row("Valence / expected / actual", Signed(memory.Valence) + " / " + F(memory.Expected) + " / " + F(memory.Actual), width, ref y);
                EventLink(memory.EventId, "Inspect this event", width, ref y);
                if (memory.CauseEventId >= 0) EventLink(memory.CauseEventId, "Prior cause", width, ref y);
            }
            if (agent.Memories.Count == 0) Paragraph("No retained memories yet.", width, ref y);
        }

        private void DrawRelationships(Agent agent, float width, ref float y)
        {
            Paragraph("Each direction is learned independently. Trust and affinity are signed; familiarity, attachment and expectations accumulate through experience.", width, ref y, muted);
            var ids = new List<int>(agent.Relationships.Keys);
            ids.Sort(delegate(int a, int b) { int difference = agent.Relationships[b].Interactions.CompareTo(agent.Relationships[a].Interactions); return difference != 0 ? difference : a.CompareTo(b); });
            for (int i = 0; i < ids.Count; i++)
            {
                Relationship rel = agent.Relationships[ids[i]];
                Agent other = simulation.GetAgent(rel.OtherId);
                Section(agent.Name + " -> " + AgentName(rel.OtherId), width, ref y);
                Row("Trust / affinity", Signed(rel.Trust) + " / " + Signed(rel.Affinity), width, ref y);
                Row("Familiarity / attachment", F(rel.Familiarity) + " / " + F(rel.Attachment), width, ref y);
                Row("Expected care / balance", F(rel.ExpectedCare) + " / " + Signed(rel.ReciprocityBalance), width, ref y);
                Row("Interactions / last tick", rel.Interactions + " / " + rel.LastInteractionTick, width, ref y);
                Relationship reverse;
                if (other != null && other.Relationships.TryGetValue(agent.Id, out reverse))
                    Paragraph("Reverse direction: trust " + Signed(reverse.Trust) + ", affinity " + Signed(reverse.Affinity) + ", attachment " + F(reverse.Attachment) +
                        ". This is observer access to " + other.Name + "'s internal state.", width, ref y, muted);
                if (Button(new Rect(0, y, 155, 24), "INSPECT " + AgentName(rel.OtherId).ToUpperInvariant())) SelectAgent(rel.OtherId);
                if (rel.LastEventId >= 0 && Button(new Rect(162, y, 155, 24), "CAUSE #" + rel.LastEventId)) ShowEvent(rel.LastEventId);
                y += 34;
            }
            if (ids.Count == 0) Paragraph("No relationship estimates yet. Nearby encounters create individual evaluations.", width, ref y);
        }

        private void DrawBeliefs(Agent agent, float width, ref float y)
        {
            if (Button(new Rect(0, y, 205, 26), compareTruth ? "COMPARE WITH TRUTH: ON" : "COMPARE WITH TRUTH: OFF", compareTruth)) compareTruth = !compareTruth;
            y += 36;
            Paragraph("Beliefs are last observed or communicated estimates. The comparison is yours as observer; inhabitants cannot read it.", width, ref y, muted);
            var ids = new List<int>(agent.Beliefs.Keys); ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                ObjectBelief belief = agent.Beliefs[ids[i]];
                Section("#" + belief.ObjectId + " " + belief.Name, width, ref y);
                Row("Position / age", belief.X + "," + belief.Y + " / " + (simulation.Tick - belief.ObservedTick) + " ticks", width, ref y);
                Row("Believed stock / confidence", F(belief.EstimatedStock) + " / " + Percent(belief.Confidence), width, ref y);
                Row("Believed owner", Owner(belief.OwnerId), width, ref y);
                Paragraph("Source: " + (belief.Hearsay ? "communication from " + AgentName(belief.SourceAgentId) : "direct perception") +
                    "; properties " + belief.Properties, width, ref y, muted);
                if (compareTruth)
                {
                    WorldObject truth = simulation.GetObject(belief.ObjectId);
                    Paragraph(truth != null ? "TRUTH NOW: stock " + F(truth.Stock) + ", owner " + Owner(truth.OwnerId) +
                        ", location " + truth.X + "," + truth.Y : "TRUTH NOW: object no longer exists.", width, ref y, blue);
                }
                if (belief.SourceEventId >= 0) EventLink(belief.SourceEventId, belief.Hearsay ? "Communication source event" : "Witnessed source event", width, ref y);
            }
            Section("REMEMBERED PEOPLE", width, ref y);
            ids = new List<int>(agent.People.Keys); ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                PersonBelief belief = agent.People[ids[i]];
                Paragraph(AgentName(belief.AgentId) + " at " + belief.X + "," + belief.Y + ", age " + (simulation.Tick - belief.ObservedTick) +
                    ", apparent hunger " + Percent(belief.ApparentHunger) + ", food " + F(belief.ApparentFood) + ", confidence " + Percent(belief.Confidence) +
                    (belief.Alive ? "" : ", believed deceased"), width, ref y);
                if (compareTruth)
                {
                    Agent truth = simulation.GetAgent(belief.AgentId);
                    if (truth != null) Paragraph("TRUTH: " + truth.X + "," + truth.Y + ", hunger " + Percent(truth.Needs.Hunger) + ", food " + F(truth.Inventory.Food) +
                        (truth.Alive ? "" : ", deceased"), width, ref y, blue);
                }
                y += 5;
            }
        }

        private void DrawEventDetails(Agent agent, float width, ref float y)
        {
            WorldEvent selected = FindEvent(selectedEventId);
            if (selected == null)
            {
                Paragraph(selectedEventId >= 0 ? "Event #" + selectedEventId + " has left the bounded world history. Its reference may remain in a memory or belief." :
                    "Click any chronological history entry below, or a causal link in Why, Memories, Relations or Beliefs.", width, ref y, muted);
            }
            else
            {
                Section("EVENT #" + selected.Id + " / " + selected.Kind, width, ref y);
                Paragraph(TickLabel(selected.Tick) + " at cell " + selected.X + "," + selected.Y, width, ref y, muted);
                Paragraph(selected.Summary, width, ref y, paper, true);
                Row("Actor", AgentName(selected.ActorId), width, ref y);
                Row("Recipient", AgentName(selected.TargetAgentId), width, ref y);
                Row("Object / amount", (selected.ObjectId >= 0 ? "#" + selected.ObjectId : "none") + " / " + F(selected.Amount), width, ref y);
                var witnesses = new StringBuilder();
                for (int i = 0; i < selected.WitnessIds.Count; i++) { if (i > 0) witnesses.Append(", "); witnesses.Append(AgentName(selected.WitnessIds[i])); }
                Paragraph("Witnesses: " + (witnesses.Length > 0 ? witnesses.ToString() : "none recorded"), width, ref y, muted);
                if (selected.CauseEventId >= 0) EventLink(selected.CauseEventId, "Follow primary cause", width, ref y);
                if (selected.SecondaryCauseEventId >= 0) EventLink(selected.SecondaryCauseEventId, "Follow secondary cause", width, ref y);
                if (selected.ActorId >= 0 && Button(new Rect(0, y, 190, 25), "INSPECT ACTOR")) SelectAgent(selected.ActorId);
                y += 35;
                Section("LINKED LATER CONSEQUENCES", width, ref y);
                int consequences = 0;
                for (int i = 0; i < simulation.Events.Count; i++)
                {
                    WorldEvent consequence = simulation.Events[i];
                    if (consequence.CauseEventId != selected.Id && consequence.SecondaryCauseEventId != selected.Id) continue;
                    EventLink(consequence.Id, TickLabel(consequence.Tick) + " " + consequence.Kind, width, ref y);
                    consequences++;
                    if (consequences >= 30) break;
                }
                if (consequences == 0) Paragraph("No directly linked later event is retained. Links document recorded dependencies; they do not prove a counterfactual explanation.", width, ref y, muted);
            }
            Section("WORLD COUNTERS", width, ref y);
            SimulationStatistics s = simulation.Statistics;
            Row("Decisions / moves", s.Decisions + " / " + s.Moves, width, ref y);
            Row("Successful / failed actions", s.SuccessfulActions + " / " + s.FailedActions, width, ref y);
            Row("Conversations / gifts", s.Conversations + " / " + s.Gifts, width, ref y);
            Row("Other-owned acquisitions", s.OtherOwnedAcquisitions.ToString(), width, ref y);
            Row("Hearsay / corrected beliefs", s.HearsayBeliefs + " / " + s.BeliefCorrections, width, ref y);
            Row("Shelters / tending", s.SheltersBuilt + " / " + s.TendActions, width, ref y);
            Row("Food harvested / consumed", F(s.FoodHarvested) + " / " + F(s.FoodConsumed), width, ref y);
            Row("Food spoiled / deaths", F(s.FoodSpoiled) + " / " + s.Deaths, width, ref y);
        }

        private void DrawHistory(Rect rect)
        {
            Panel(rect);
            Text(new Rect(rect.x + 14, rect.y + 10, 390, 25), "CHRONOLOGICAL FIELD RECORD", heading);
            if (Button(new Rect(rect.x + rect.width - 402, rect.y + 9, 178, 26), onlySelectedEvents ? "SELECTED + WITNESSED" : "ALL INHABITANTS", onlySelectedEvents)) { onlySelectedEvents = !onlySelectedEvents; logScroll = Vector2.zero; }
            if (Button(new Rect(rect.x + rect.width - 216, rect.y + 9, 112, 26), "FOLLOW TAIL", tail)) { tail = !tail; lastLogCount = -1; }
            Text(new Rect(rect.x + rect.width - 96, rect.y + 16, 86, 18), simulation.Events.Count + " kept", small);
            Rect body = new Rect(rect.x + 14, rect.y + 43, rect.width - 28, rect.height - 54);
            var filtered = new List<WorldEvent>();
            for (int i = 0; i < simulation.Events.Count; i++)
            {
                WorldEvent ev = simulation.Events[i];
                if (!onlySelectedEvents || ev.ActorId == selectedId || ev.TargetAgentId == selectedId || ev.WitnessIds.Contains(selectedId)) filtered.Add(ev);
            }
            int lastId = filtered.Count > 0 ? filtered[filtered.Count - 1].Id : -1;
            if (tail && (filtered.Count != lastLogCount || lastId != lastLogEventId)) logScroll.y = Math.Max(0, filtered.Count * 23 - body.height);
            lastLogCount = filtered.Count; lastLogEventId = lastId;
            if (GUI.enabled && Event.current.type == EventType.ScrollWheel && body.Contains(Event.current.mousePosition)) tail = false;
            logScroll = GUI.BeginScrollView(body, logScroll, new Rect(0, 0, body.width - 20, Math.Max(body.height - 1, filtered.Count * 23)));
            int start = Math.Max(0, (int)(logScroll.y / 23) - 1);
            int end = Math.Min(filtered.Count, start + (int)(body.height / 23) + 4);
            for (int i = start; i < end; i++)
            {
                WorldEvent ev = filtered[i];
                Rect row = new Rect(0, i * 23, body.width - 20, 22);
                if (selectedEventId == ev.Id) Fill(row, new Color(.12f, .23f, .20f));
                else if (i % 2 == 0) Fill(row, inset);
                Text(new Rect(7, row.y + 4, 144, 18), "#" + ev.Id + "  " + TickLabel(ev.Tick), tiny);
                Text(new Rect(155, row.y + 3, 110, 19), ev.Kind.ToString(), small, EventColor(ev.Kind));
                Text(new Rect(274, row.y + 3, row.width - 282, 19), ev.Summary, small, paper);
                if (GUI.Button(row, new GUIContent("", ev.Summary), GUIStyle.none)) ShowEvent(ev.Id);
            }
            GUI.EndScrollView(!help);
        }

        private void DrawFooter()
        {
            Text(new Rect(25, 871, 1200, 20), string.IsNullOrEmpty(error) ? notice : error, small, string.IsNullOrEmpty(error) ? muted : red);
            Text(new Rect(1220, 871, 200, 20), "CAUSES, NOT SCRIPTS.", small, accent);
        }

        private void DrawHelp()
        {
            Fill(new Rect(0, 0, CanvasWidth, CanvasHeight), new Color(0, 0, 0, .75f));
            Rect card = new Rect(350, 145, 740, 610);
            Panel(card);
            Text(new Rect(card.x + 28, card.y + 24, 620, 36), "HOW TO OBSERVE", title);
            if (Button(new Rect(card.x + 635, card.y + 26, 76, 27), "CLOSE")) help = false;
            float y = 0, width = card.width - 56;
            helpScroll = GUI.BeginScrollView(new Rect(card.x + 28, card.y + 89, width, card.height - 102), helpScroll,
                new Rect(0, 0, width - 20, helpHeight));
            width -= 20;
            Paragraph("This world has no scheduled social drama. Need pressure, finite stocks, imperfect memory and evaluations of other inhabitants drive its choices.", width, ref y, paper, true);
            Section("WATCH", width, ref y);
            Paragraph("Click an inhabitant on the map or roster. FOLLOW magnifies and centers that inhabitant. Multiple inhabitants can share a cell; use the roster or inspector arrows to select them individually.", width, ref y);
            Section("ASK WHY", width, ref y);
            Paragraph("Why records the needs, perceived situation, candidate utilities and expected outcome at decision time. EARLIER pins a past decision. Outcome and prior-cause links open the event record.", width, ref y);
            Section("COMPARE KNOWLEDGE WITH REALITY", width, ref y);
            Paragraph("AGENT BELIEF draws remembered terrain, objects and people; locations may be stale. Beliefs compares estimates against truth. Observer panels can reveal information the inhabitant does not know.", width, ref y);
            Section("CONTROL TIME / REPEAT", width, ref y);
            Paragraph("Space pauses. A period advances one tick. Speeds request up to 256x; achieved ticks per second are shown. The same seed and Inspector configuration reproduce the same tick sequence. Restart applies configuration changes.", width, ref y);
            Section("FOLLOW EVIDENCE", width, ref y);
            Paragraph("History is chronological and bounded. Click a record to follow causes and later consequences. Scrolling stops following its tail. Old events expire even when a memory retains their ID. A large Game view (1440x900 preferred) makes detailed text easier to read.", width, ref y);
            GUI.EndScrollView();
            if (Event.current.type == EventType.Repaint) helpHeight = Math.Max(card.height - 103, y + 10);
        }

        private void CycleAgent(int direction)
        {
            int index = simulation.Agents.FindIndex(delegate(Agent a) { return a.Id == selectedId; });
            index = (index + direction + simulation.Agents.Count) % simulation.Agents.Count;
            SelectAgent(simulation.Agents[index].Id);
        }

        private void SelectDecision(Agent agent, DecisionRecord current, int direction)
        {
            int index = agent.Decisions.IndexOf(current);
            if (index < 0) index = agent.Decisions.Count - 1;
            index = Math.Max(0, Math.Min(agent.Decisions.Count - 1, index + direction));
            if (index >= 0 && index < agent.Decisions.Count) decisionTick = agent.Decisions[index].Tick;
            scroll[1] = Vector2.zero;
        }

        private void ShowEvent(int id) { selectedEventId = id; tab = 5; scroll[5] = Vector2.zero; contentHeight = 1500; }

        private WorldEvent FindEvent(int id)
        {
            for (int i = simulation.Events.Count - 1; i >= 0; i--) if (simulation.Events[i].Id == id) return simulation.Events[i];
            return null;
        }

        private void EventLink(int id, string label, float width, ref float y)
        {
            if (Button(new Rect(0, y, width, 24), label + "  ->  #" + id)) ShowEvent(id);
            y += 31;
        }

        private string TileDescription(int x, int y, Agent selected)
        {
            var result = new StringBuilder("Cell " + x + "," + y + (beliefMap ? " [belief]" : " [truth]"));
            if (beliefMap && selected != null)
            {
                foreach (ObjectBelief obj in selected.Beliefs.Values)
                    if (obj.X == x && obj.Y == y) result.Append(" / ").Append(obj.Name).Append(" stock~").Append(F(obj.EstimatedStock)).Append(" owner~").Append(Owner(obj.OwnerId));
            }
            else
            {
                for (int i = 0; i < simulation.Objects.Count; i++)
                {
                    WorldObject obj = simulation.Objects[i];
                    if (obj.X == x && obj.Y == y) result.Append(" / ").Append(obj.Name).Append(" stock ").Append(F(obj.Stock)).Append(" owner ").Append(Owner(obj.OwnerId));
                }
            }
            return result.ToString();
        }

        private string AgentName(int id) { Agent agent = id < 0 ? null : simulation.GetAgent(id); return agent == null ? "none" : agent.Name; }
        private string Owner(int id) { return id < 0 ? "unclaimed" : AgentName(id); }
        private static string F(double value) { return value.ToString("0.00", CultureInfo.InvariantCulture); }
        private static string Signed(double value) { return value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture); }
        private static string Percent(double value) { return (100 * value).ToString("0", CultureInfo.InvariantCulture) + "%"; }
        private string TickLabel(int tick) { return "d" + (tick / simulation.Config.DayLength + 1) + ":" + (tick % simulation.Config.DayLength).ToString("000"); }
        private string TimeOfDay(int tick)
        {
            int minutes = (tick % simulation.Config.DayLength) * 1440 / simulation.Config.DayLength;
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }
        private static string Symbol(int id) { return id < 26 ? ((char)('A' + id)).ToString() : (id < 36 ? ((char)('0' + id - 26)).ToString() : ((char)('a' + id - 36)).ToString()); }
        private static bool InView(int x, int y, int sx, int sy, int w, int h) { return x >= sx && y >= sy && x < sx + w && y < sy + h; }
        private static Rect MapCell(Rect map, int x, int y, int sx, int sy, float cw, float ch) { return new Rect(map.x + (x - sx) * cw, map.y + (y - sy) * ch, cw - 1, ch - 1); }

        private Color TerrainColor(Terrain terrain)
        {
            if (terrain == Terrain.Rock) return new Color(.11f, .14f, .14f);
            if (terrain == Terrain.Water) return new Color(.055f, .15f, .19f);
            if (terrain == Terrain.Soil) return new Color(.17f, .16f, .11f);
            return new Color(.095f, .145f, .105f);
        }

        private Color AgentColor(int id) { return Color.HSVToRGB((float)((id * .61803398875 + .12) % 1), .31f, .98f); }
        private string ObjectSymbol(ObjectKind kind)
        {
            switch (kind) { case ObjectKind.Spring: return "@"; case ObjectKind.BerryPatch: return "*"; case ObjectKind.Grove: return "|"; case ObjectKind.Cache: return "o"; default: return "^"; }
        }
        private Color ObjectColor(ObjectKind kind)
        {
            switch (kind) { case ObjectKind.Spring: return blue; case ObjectKind.BerryPatch: return accent; case ObjectKind.Grove: return amber; case ObjectKind.Cache: return paper; default: return new Color(.76f, .64f, .93f); }
        }
        private Color EventColor(EventKind kind)
        {
            if (kind == EventKind.Death) return red;
            if (kind == EventKind.Conversation || kind == EventKind.Transfer) return blue;
            if (kind == EventKind.Construction || kind == EventKind.Cultivation) return accent;
            if (kind == EventKind.Evaluation) return amber;
            return muted;
        }

        private void NeedBar(string label, double value, float width, ref float y)
        {
            Text(new Rect(0, y, 108, 20), label, small);
            Fill(new Rect(112, y + 3, width - 169, 10), inset);
            Fill(new Rect(112, y + 3, (width - 169) * Mathf.Clamp01((float)value), 10), value > .8 ? red : value > .55 ? amber : accent);
            Text(new Rect(width - 48, y, 48, 20), Percent(value), normal);
            y += 25;
        }
        private void Row(string label, string value, float width, ref float y)
        {
            Text(new Rect(0, y, width * .60f, 22), label, small);
            Text(new Rect(width * .60f, y, width * .40f, 22), value, small, paper);
            y += 23;
        }
        private void Section(string text, float width, ref float y)
        {
            y += 10; Fill(new Rect(0, y, width, 1), line); y += 10;
            Paragraph(text, width, ref y, accent, true);
        }
        private void Paragraph(string text, float width, ref float y) { Paragraph(text, width, ref y, paper); }
        private void Paragraph(string text, float width, ref float y, Color color, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color old = wrap.normal.textColor; FontStyle oldWeight = wrap.fontStyle;
            wrap.normal.textColor = color; wrap.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            float height = wrap.CalcHeight(new GUIContent(text), width);
            GUI.Label(new Rect(0, y, width, height), text, wrap);
            y += height + 9;
            wrap.normal.textColor = old; wrap.fontStyle = oldWeight;
        }
        private void Text(Rect rect, string text, GUIStyle style) { GUI.Label(rect, text ?? "", style); }
        private void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            Color old = style.normal.textColor; style.normal.textColor = color;
            GUI.Label(rect, text ?? "", style); style.normal.textColor = old;
        }
        private void Glyph(Rect rect, string text, Color color, float cellSize)
        {
            glyph.fontSize = Math.Max(8, (int)Math.Min(22, cellSize + 1));
            Text(rect, text, glyph, color);
        }
        private bool Button(Rect rect, string text, bool active = false)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Fill(rect, active ? new Color(.18f, .31f, .23f) : hover ? new Color(.14f, .22f, .23f) : new Color(.085f, .13f, .14f));
            Outline(rect, active ? accent : line);
            GUI.Label(rect, text, button);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }
        private void Panel(Rect rect) { Fill(rect, panel); Outline(rect, line); }
        private void Fill(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }
        private void Outline(Rect rect, Color color)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1), color);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), color);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
        private void DrawTooltip(string text)
        {
            float width = Math.Min(560, Math.Max(220, tooltip.CalcSize(new GUIContent(text)).x));
            float height = tooltip.CalcHeight(new GUIContent(text), width);
            Vector2 mouse = Event.current.mousePosition;
            Rect rect = new Rect(Math.Min(CanvasWidth - width - 12, mouse.x + 18), Math.Min(CanvasHeight - height - 10, mouse.y + 23), width, height);
            Fill(rect, background); Outline(rect, accent); GUI.Label(rect, text, tooltip);
        }
    }
}
