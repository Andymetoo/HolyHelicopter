using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>A deliberately broad canopy, with instruments drawn in a fixed design space.</summary>
    public sealed class Hud : MonoBehaviour
    {
        const float Width = 1600f, Height = 900f, RadarRange = 200f;
        static readonly Color Ink = new Color(.025f, .043f, .055f, .96f);
        static readonly Color Panel = new Color(.045f, .075f, .085f, .95f);
        static readonly Color Edge = new Color(.20f, .31f, .31f);
        static readonly Color Ivory = new Color(.91f, .91f, .78f);
        static readonly Color Muted = new Color(.65f, .78f, .75f);
        static readonly Color Gold = new Color(1f, .80f, .20f);
        static readonly Color Cyan = new Color(.20f, .77f, 1f);
        static readonly Color Red = new Color(1f, .31f, .28f);
        static readonly Color Hazard = new Color(1f, .62f, .18f);
        static readonly Color Green = new Color(.44f, .92f, .61f);
        static readonly Color Friendly = new Color(.20f, .96f, .92f);
        static readonly string[] CompassNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        static readonly string[] SupplyLabels = { "AMMO", "FUEL", "REPAIR", "SALVAGE" };
        readonly Vector3[] radarPoints = new Vector3[640];
        readonly Texture2D[] weaponIcons = new Texture2D[5];
        readonly Color32[] radarPixels = new Color32[192 * 192];
        readonly Color32[] radarBackground = new Color32[192 * 192];
        Game game;
        GUIStyle label;
        Texture2D disc, radarTexture;
        int radarCount;
        float radarNext, fps, smoothDelta;
        bool radarDirty = true;
        string tooltip;
        float uiScale;
        bool Compact => Screen.height <= 720;
        enum MarkerKind { Objective, Person, Crate }
        static readonly string[] StageNames = { "AWAKENING", "GATHERING", "ASSAULT", "MIDNIGHT" };

        public void Initialize(Game owner) { game = owner; }

        void Update()
        {
            smoothDelta = Mathf.Lerp(smoothDelta, Time.unscaledDeltaTime, .08f);
            fps = smoothDelta > .0001f ? 1f / smoothDelta : 0f;
            if (game == null || game.Flight == null || game.Horde == null) return;
            if (Time.unscaledTime >= radarNext)
            {
                radarNext = Time.unscaledTime + .13f;
                radarCount = game.Horde.GetRadarPoints(game.Flight.transform.position, RadarRange, radarPoints);
                radarDirty = true;
            }
        }

        void Prepare()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, padding = new RectOffset() };
            disc = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, name = "HUD disc" };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float radius = new Vector2(x - 31.5f, y - 31.5f).magnitude;
                    pixels[y * 64 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(32f - radius));
                }
            disc.SetPixels(pixels); disc.Apply();
            radarTexture = new Texture2D(192, 192, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Batched all-direction radar" };
            for (int y = 0; y < 192; y++)
                for (int x = 0; x < 192; x++)
                {
                    float distance = new Vector2(x - 95.5f, y - 95.5f).magnitude;
                    Color color = distance > 95 ? Color.clear : new Color(.065f, .12f, .13f);
                    if (distance <= 95 && (Mathf.Abs(distance - 94) < .8f || Mathf.Abs(distance - 47) < .6f || x == 96 || y == 96)) color = new Color(.16f, .28f, .25f);
                    radarBackground[y * 192 + x] = color;
                }
            for (int i = 0; i < weaponIcons.Length; i++) weaponIcons[i] = MakeIcon(i);
        }

        void OnDestroy()
        {
            if (disc != null) Destroy(disc);
            if (radarTexture != null) Destroy(radarTexture);
            foreach (Texture2D icon in weaponIcons) if (icon != null) Destroy(icon);
        }

        void OnGUI()
        {
            if (game == null || game.Flight == null || game.Arsenal == null) return;
            Prepare();
            Matrix4x4 old = GUI.matrix;
            Color previous = GUI.color;
            uiScale = Mathf.Min(Screen.width / Width, Screen.height / Height);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / Width, Screen.height / Height, 1f));
            GUI.color = Color.white;
            tooltip = null;
            if (!game.Started) DrawTitle();
            else
            {
                DrawWorldMarkers();
                DrawCockpit();
                DrawMission();
                DrawCompass();
                DrawCombat();
                DrawRadar();
                DrawWeapons();
                DrawStatus();
                DrawNotifications();
                if (game.AtBase && game.CursorFree && !game.ShowHelp && !game.Paused && !game.GameOver) DrawArmory();
                if (game.ShowHelp && !game.Paused && !game.GameOver) DrawHelp();
                if (game.Paused && !game.GameOver) DrawPause();
                if (game.GameOver) DrawEnd();
                if (game.ShowDebug)
                {
                    Text(new Rect(35, 603, 470, 22), $"{fps:0} FPS  /  {game.Horde.Count:N0} entities  /  F3 telemetry", 13, Muted);
                    Text(new Rect(35, 626, 470, 38), "F6  +1,000 UNDEAD   F7  +500 SALVAGE\nF8  RETURN & SERVICE   F9  SCREENSHOT", 11, Muted, TextAnchor.UpperLeft, true);
                }
                if (!string.IsNullOrEmpty(tooltip) && !game.Paused && !game.GameOver)
                {
                    Box(new Rect(415, 578, 770, 167), Ink, Edge);
                    Text(new Rect(438, 595, 724, 133), tooltip, 22, Ivory, TextAnchor.MiddleLeft, true);
                }
            }
            GUI.matrix = old;
            GUI.color = previous;
        }

        void DrawCockpit()
        {
            // Keep the centre and the downward firing arc completely open.
            float bank = Mathf.Clamp(Vector3.Dot(game.Flight.Velocity, game.Flight.transform.right) * .12f, -3f, 3f);
            Line(new Vector2(-15, 45), new Vector2(95 + bank, 711), 34, Ink);
            Line(new Vector2(1615, 45), new Vector2(1505 + bank, 711), 34, Ink);
            Line(new Vector2(-3, 45), new Vector2(108 + bank, 711), 3, Edge);
            Line(new Vector2(1603, 45), new Vector2(1492 + bank, 711), 3, Edge);
            Line(new Vector2(0, 720), new Vector2(190, 797), 23, Ink);
            Line(new Vector2(1600, 720), new Vector2(1410, 797), 23, Ink);
            Fill(new Rect(0, 891, Width, 9), Ink);
            Box(new Rect(371, 780, 856, 120), Ink, Edge);
            Fill(new Rect(371, 780, 856, 4), new Color(.31f, .35f, .27f));
            for (int i = 0; i < 5; i++)
            {
                Line(new Vector2(392 + i * 14, 789), new Vector2(385 + i * 14, 801), 3, new Color(.59f, .48f, .16f));
                Line(new Vector2(1151 + i * 14, 789), new Vector2(1144 + i * 14, 801), 3, new Color(.59f, .48f, .16f));
            }
            Screw(381, 887); Screw(1218, 887); Screw(105, 712); Screw(1495, 712);
            Text(new Rect(381, 816, 97, 26), "SALVAGE", 15, Muted, TextAnchor.MiddleCenter);
            Text(new Rect(379, 845, 101, 37), game.Salvage.ToString("N0"), 25, Gold, TextAnchor.MiddleCenter);
            Text(new Rect(1122, 816, 94, 26), "TAB", 24, Gold, TextAnchor.MiddleCenter);
            Text(new Rect(1114, 849, 110, 26), game.CursorFree ? "TO FLY" : "ASSIGN", 15, Muted, TextAnchor.MiddleCenter);
        }

        void DrawMission()
        {
            Box(new Rect(24, 24, 390, 157), new Color(.025f, .043f, .055f, .88f), Edge);
            Fill(new Rect(24, 24, 4, 157), Gold);
            Text(new Rect(43, 35, 350, 27), "OPERATION / MIDNIGHT MASS", 17, Gold);
            Text(new Rect(43, 68, 350, 30), $"CRYPTS  {game.ActiveCrypts} REMAIN", 24, Ivory);
            Text(new Rect(43, 105, 350, 27), $"DELIVERED  {game.Rescued} / {game.ExtractionQuota}", 21, game.Rescued >= game.ExtractionQuota ? Green : Ivory);
            Text(new Rect(43, 144, 350, 27), $"ABOARD {game.Passengers}/4    LOSSES {game.CivilianLosses}", 17, game.CivilianLosses > 0 ? Gold : Muted);
            if (game.CanExtract)
            {
                Box(new Rect(24, 194, 390, 48), Ink, Gold);
                Text(new Rect(36, 200, 366, 36), "RETURN TO SANCTUARY", 22, Gold, TextAnchor.MiddleCenter);
            }
        }

        void DrawCompass()
        {
            float heading = game.Flight.transform.eulerAngles.y;
            Fill(new Rect(487, 30, 626, 35), new Color(.025f, .043f, .055f, .35f));
            for (int i = 0; i < 24; i++)
            {
                float angle = i * 15;
                float delta = Mathf.DeltaAngle(heading, angle);
                if (Mathf.Abs(delta) > 63) continue;
                float x = 800 + delta * 4.6f;
                Fill(new Rect(x, 52, 1, i % 3 == 0 ? 10 : 5), Muted);
                if (i % 3 == 0) Text(new Rect(x - 23, 31, 46, 20), CompassNames[i / 3], 14, Ivory, TextAnchor.MiddleCenter);
            }
            Line(new Vector2(795, 24), new Vector2(800, 30), 2, Gold);
            Line(new Vector2(805, 24), new Vector2(800, 30), 2, Gold);
            Text(new Rect(757, 68, 86, 22), $"{heading:000}°", 12, Gold, TextAnchor.MiddleCenter);
            Vector3 toBase = World.BasePosition - game.Flight.transform.position;
            float deltaBase = Mathf.DeltaAngle(heading, Mathf.Atan2(toBase.x, toBase.z) * Mathf.Rad2Deg);
            string arrow = Mathf.Abs(deltaBase) < 14 ? "•" : deltaBase < 0 ? "<" : ">";
            Text(new Rect(620, 93, 360, 20), $"{arrow}  SANCTUARY  {new Vector2(toBase.x, toBase.z).magnitude:0} m  {arrow}", 12, Gold, TextAnchor.MiddleCenter);
        }

        void DrawCombat()
        {
            float ramp = Mathf.Clamp01(game.Elapsed / Mathf.Max(1, game.Balance.EscalationSeconds));
            int stage = ramp < .25f ? 0 : ramp < .625f ? 1 : ramp < 1f ? 2 : 3;
            Box(new Rect(1210, 24, 366, 157), new Color(.025f, .043f, .055f, .88f), Edge);
            Text(new Rect(1228, 35, 330, 27), $"{stage + 1:00} / {StageNames[stage]}", 19, stage >= 2 ? Red : Gold, TextAnchor.MiddleRight);
            Text(new Rect(1228, 68, 330, 36), $"{game.Kills:N0} EXORCISED", 26, Gold, TextAnchor.MiddleRight);
            Text(new Rect(1228, 111, 330, 28), $"{game.Horde.Count:N0} UNDEAD  /  {ClockText(game.Elapsed)}", 18, Ivory, TextAnchor.MiddleRight);
            Meter(new Rect(1228, 151, 330, 5), ramp, stage >= 2 ? Red : Gold);
            Text(new Rect(1228, 190, 330, 25), $"NEXT ARMY  {ClockText(game.Horde.NextAssaultIn)}", 16, Muted, TextAnchor.MiddleRight);
            bool hit = Time.time < game.HitUntil;
            Color reticle = game.Arsenal.FriendlyFireRisk ? Hazard : hit ? Gold : new Color(.82f, .95f, .85f, .87f);
            Fill(new Rect(785, 449, 8, 2), reticle); Fill(new Rect(807, 449, 8, 2), reticle);
            Fill(new Rect(799, 435, 2, 8), reticle); Fill(new Rect(799, 457, 2, 8), reticle);
            Fill(new Rect(799, 449, 2, 2), reticle);
            if (hit)
            {
                Line(new Vector2(780, 430), new Vector2(787, 437), 2, Gold);
                Line(new Vector2(820, 430), new Vector2(813, 437), 2, Gold);
                Line(new Vector2(780, 470), new Vector2(787, 463), 2, Gold);
                Line(new Vector2(820, 470), new Vector2(813, 463), 2, Gold);
            }
            if (game.Combo >= 4)
            {
                Text(new Rect(650, 494, 300, 38), $"{game.Combo:N0}  SOUL STREAK", 23, Gold, TextAnchor.MiddleCenter);
                if (game.Combo >= 50) Text(new Rect(650, 531, 300, 22), game.Combo >= 200 ? "DIVINE INTERVENTION" : "MASS EXORCISM", 12, Ivory, TextAnchor.MiddleCenter);
            }
            if (game.Horde.Boarders > 0)
                Alert($"{game.Horde.Boarders} BOARDERS  //  STRAFE HARD & CLIMB", Red, 595);
            else if (game.Horde.ActiveHexes > 0 || game.Horde.ChargingHexes > 0)
                Alert(game.Horde.ActiveHexes > 0 ? "HEX INBOUND / STRAFE OR CLIMB" : "HEX CHARGING / KEEP MOVING", Red, 595);
            else if (game.Horde.TowerCount > 0 && Vector3.Distance(game.Flight.transform.position, game.Horde.TowerPosition) < 65)
                Alert($"BONE TOWER RISING  //  {game.Horde.TowerHeight:0} m", Gold, 595);
            if (game.Arsenal.FriendlyFireRisk)
                Alert("CIVILIANS IN BLAST / CHECK YOUR FIRE", Hazard, 386);
            else if (game.Arsenal.Weapons[game.Arsenal.LeftSlot].Kind != DamageKind.Shock &&
                     game.Arsenal.Weapons[game.Arsenal.RightSlot].Kind != DamageKind.Shock &&
                     game.Arsenal.AimBeyondRange(game.Arsenal.LeftSlot) && game.Arsenal.AimBeyondRange(game.Arsenal.RightSlot))
                Text(new Rect(550, 403, 500, 27), "OUT OF RANGE / CLOSE IN", 18, Gold, TextAnchor.MiddleCenter);
            if (Time.time < game.HurtUntil)
            {
                Color hurt = new Color(.9f, .035f, .01f, .14f);
                Fill(new Rect(0, 0, 10, 900), hurt); Fill(new Rect(1590, 0, 10, 900), hurt);
                Fill(new Rect(0, 0, 1600, 8), hurt); Fill(new Rect(0, 892, 1600, 8), hurt);
                Fill(new Rect(0, 0, 1600, 900), new Color(.9f, .035f, .01f, .035f));
            }
        }

        void DrawRadar()
        {
            Box(new Rect(24, 650, 340, 250), Ink, Edge);
            Fill(new Rect(24, 650, 340, 4), Gold);
            Text(new Rect(42, 666, 304, 26), "RADAR / 200 m", 18, Ivory);
            Vector2 centre = new Vector2(135, 789);
            const float radius = 88f;
            if (radarDirty) RepaintRadar();
            Image(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), radarTexture, Color.white);
            Line(centre + new Vector2(0, -6), centre + new Vector2(-4, 4), 2, Ivory);
            Line(centre + new Vector2(0, -6), centre + new Vector2(4, 4), 2, Ivory);
            Text(new Rect(242, 711, 108, 25), "UNDEAD", 14, Red);
            Text(new Rect(242, 743, 108, 25), "CRYPT", 14, new Color(.88f, .43f, 1f));
            Text(new Rect(257, 775, 93, 25), "PERSON", 14, Friendly);
            MarkerGlyph(new Vector2(242, 787), Friendly, MarkerKind.Person, .65f);
            Text(new Rect(257, 807, 93, 25), "CRATE", 14, Cyan);
            MarkerGlyph(new Vector2(242, 819), Cyan, MarkerKind.Crate, .65f);
            Text(new Rect(242, 839, 108, 25), "HOME", 14, Gold);
            Text(new Rect(43, 874, 304, 24), "HEADING UP / ALL DIRECTIONS", 14, Muted);
        }

        void RepaintRadar()
        {
            radarDirty = false;
            System.Array.Copy(radarBackground, radarPixels, radarPixels.Length);
            Vector2 centre = new Vector2(95.5f, 95.5f);
            const float radius = 93f;
            Vector3 origin = game.Flight.transform.position;
            float angle = game.Flight.transform.eulerAngles.y * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            for (int i = 0; i < radarCount; i++)
            {
                Vector2 p = RadarPosition(radarPoints[i], origin, sine, cosine, radius, centre);
                if ((p - centre).sqrMagnitude <= radius * radius) RadarPixel(p, Red, 1);
            }
            foreach (Crypt crypt in game.Crypts)
                if (crypt.Active) RadarMark(crypt.Position, origin, sine, cosine, centre, radius, new Color(.88f, .43f, 1f), 6, true);
            foreach (Survivor survivor in game.Survivors)
                if (survivor.Alive) RadarMark(survivor.Position, origin, sine, cosine, centre, radius, Friendly, 5, false, MarkerKind.Person);
            foreach (Supply supply in game.Supplies)
                if (!supply.Collected) RadarMark(supply.Position, origin, sine, cosine, centre, radius, Cyan, 4, false, MarkerKind.Crate);
            RadarMark(World.BasePosition, origin, sine, cosine, centre, radius, Gold, 7, true);
            radarTexture.SetPixels32(radarPixels);
            radarTexture.Apply(false);
        }

        Vector2 RadarPosition(Vector3 point, Vector3 origin, float sine, float cosine, float radius, Vector2 centre)
        {
            Vector3 delta = point - origin;
            return centre + new Vector2(delta.x * cosine - delta.z * sine, -(delta.x * sine + delta.z * cosine)) * (radius / RadarRange);
        }

        void RadarMark(Vector3 position, Vector3 origin, float sine, float cosine, Vector2 centre, float radius, Color color, float size, bool clamp, MarkerKind kind = MarkerKind.Objective)
        {
            Vector2 offset = RadarPosition(position, origin, sine, cosine, radius, centre) - centre;
            if (offset.magnitude > radius)
            {
                if (!clamp) return;
                offset = offset.normalized * (radius - 3);
                color.a = .7f;
            }
            Vector2 p = centre + offset;
            if (kind == MarkerKind.Objective) RadarPixel(p, color, Mathf.RoundToInt(size * .5f));
            else
            {
                for (int y = -5; y <= 5; y++)
                    for (int x = -4; x <= 4; x++)
                    {
                        bool on = kind == MarkerKind.Person
                            ? y >= -5 && y <= -3 && Mathf.Abs(x) <= 1 || y >= -1 && y <= 2 && Mathf.Abs(x) <= 1 || y == 0 && Mathf.Abs(x) <= 3 || y >= 3 && y <= 5 && Mathf.Abs(x) == y - 2
                            : Mathf.Abs(x) <= 3 && Mathf.Abs(y) <= 3 && (Mathf.Abs(x) == 3 || Mathf.Abs(y) == 3 || x == y);
                        if (on) RadarPixel(p + new Vector2(x, y), color, 0);
                    }
            }
        }

        void RadarPixel(Vector2 point, Color32 color, int halfSize)
        {
            int px = Mathf.RoundToInt(point.x), py = 191 - Mathf.RoundToInt(point.y);
            for (int y = Mathf.Max(0, py - halfSize); y <= Mathf.Min(191, py + halfSize); y++)
                for (int x = Mathf.Max(0, px - halfSize); x <= Mathf.Min(191, px + halfSize); x++)
                    radarPixels[y * 192 + x] = color;
        }

        void DrawWeapons()
        {
            WeaponState[] weapons = game.Arsenal.Weapons;
            if (weapons == null) return;
            float left = 800 - weapons.Length * 63f;
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponState weapon = weapons[i];
                Rect slot = new Rect(left + i * 126, 795, 118, 98);
                bool hover = game.CursorFree && slot.Contains(Event.current.mousePosition);
                Box(slot, weapon.Unlocked ? new Color(.065f, .10f, .12f) : new Color(.035f, .048f, .057f), hover ? Ivory : Edge);
                if (i < weaponIcons.Length) Image(new Rect(slot.x + 36, slot.y + 5, 46, 46), weaponIcons[i], weapon.Unlocked ? weapon.Color : new Color(.40f, .49f, .47f));
                Text(new Rect(slot.x + 3, slot.y + 52, slot.width - 6, 26), weapon.ShortName.ToUpperInvariant(), 15, weapon.Unlocked ? Ivory : Muted, TextAnchor.MiddleCenter);
                if (weapon.Unlocked)
                {
                    Fill(new Rect(slot.x + 6, slot.y + 87, 106, 4), new Color(.12f, .17f, .18f));
                    Fill(new Rect(slot.x + 6, slot.y + 87, 106 * Mathf.Clamp01(weapon.Ammo / Mathf.Max(1, weapon.MaxAmmo)), 4), weapon.Color);
                    Text(new Rect(slot.x + 5, slot.y + 3, 27, 25), $"{weapon.Level}", 16, Gold);
                    if (i == game.Arsenal.LeftSlot)
                    {
                        Fill(new Rect(slot.x, slot.y, 3, slot.height), Cyan);
                        Fill(new Rect(slot.x, slot.y - 26, 55, 26), Cyan);
                        Text(new Rect(slot.x, slot.y - 26, 55, 26), "LMB", 16, Ink, TextAnchor.MiddleCenter);
                    }
                    if (i == game.Arsenal.RightSlot)
                    {
                        Fill(new Rect(slot.xMax - 3, slot.y, 3, slot.height), Red);
                        Fill(new Rect(slot.xMax - 55, slot.y - 26, 55, 26), Red);
                        Text(new Rect(slot.xMax - 55, slot.y - 26, 55, 26), "RMB", 16, Ink, TextAnchor.MiddleCenter);
                    }
                    if (hover)
                    {
                        tooltip = $"{weapon.Name.ToUpperInvariant()} / LEVEL {weapon.Level} / {weapon.Range:0}m RANGE\n{weapon.Description}\nLeft click assigns LMB. Right click assigns RMB.";
                        if (Event.current.type == EventType.MouseDown && Event.current.button <= 1 && !game.Paused && !game.GameOver)
                        {
                            game.Arsenal.Assign(i, Event.current.button == 1);
                            Event.current.Use();
                        }
                    }
                }
                else
                {
                    Text(new Rect(slot.x + 4, slot.y + 76, slot.width - 8, 22), "LOCKED", 14, Muted, TextAnchor.MiddleCenter);
                    if (hover) tooltip = $"{weapon.Name.ToUpperInvariant()} / {game.Arsenal.UnlockCost(i)} SALVAGE\n{weapon.Description}\nReturn to Sanctuary. TAB opens the armory.";
                }
            }
            if (game.CursorFree && !game.AtBase)
                Text(new Rect(415, 732, 770, 29), "LEFT / RIGHT CLICK ICONS TO ASSIGN / TAB TO FLY", 18, Ivory, TextAnchor.MiddleCenter);
        }

        void DrawStatus()
        {
            Box(new Rect(1236, 654, 340, 246), Ink, Edge);
            Fill(new Rect(1236, 654, 340, 4), Gold);
            Text(new Rect(1254, 669, 304, 27), game.AtBase ? "SANCTUARY / SERVICING" : "MERCY-ONE / AIRBORNE", 16, game.AtBase ? Green : Muted);
            Text(new Rect(1254, 705, 92, 27), "HULL", 17, Muted);
            Meter(new Rect(1327, 717, 144, 8), game.Hull / 100f, game.Hull < 30 ? Red : Green);
            Text(new Rect(1477, 701, 82, 34), $"{Mathf.Max(0, game.Hull):0}%", 25, game.Hull < 30 ? Red : Ivory, TextAnchor.MiddleRight);
            Text(new Rect(1254, 746, 92, 27), "FUEL", 17, Muted);
            Meter(new Rect(1327, 758, 144, 8), game.Fuel / 100f, game.Fuel < 20 ? Red : Gold);
            Text(new Rect(1477, 742, 82, 34), $"{Mathf.Max(0, game.Fuel):0}%", 25, game.Fuel < 20 ? Red : Ivory, TextAnchor.MiddleRight);
            Fill(new Rect(1254, 785, 304, 1), Edge);
            Text(new Rect(1254, 797, 160, 32), $"ALT {game.Flight.Altitude * 3.28084f:0} FT", 24, game.Flight.Altitude <= 14 ? Cyan : Ivory);
            Text(new Rect(1422, 799, 136, 30), $"{game.Flight.Velocity.magnitude * 3.6f:0} KM/H", 18, Muted, TextAnchor.MiddleRight);
            WeaponState[] weapons = game.Arsenal.Weapons;
            if (weapons != null && weapons.Length > 0)
            {
                WeaponState l = weapons[Mathf.Clamp(game.Arsenal.LeftSlot, 0, weapons.Length - 1)];
                WeaponState r = weapons[Mathf.Clamp(game.Arsenal.RightSlot, 0, weapons.Length - 1)];
                Text(new Rect(1254, 841, 148, 28), $"L {l.Ammo:0}/{l.MaxAmmo:0}", 16, l.Ammo <= 0 ? Red : Cyan);
                Text(new Rect(1408, 841, 150, 28), $"R {r.Ammo:0}/{r.MaxAmmo:0}", 16, Red, TextAnchor.MiddleRight);
            }
        }

        void DrawNotifications()
        {
            if (Time.time < game.MessageUntil && !string.IsNullOrEmpty(game.LastMessage))
            {
                Box(new Rect(440, 138, 720, 89), new Color(.025f, .043f, .055f, .88f), Edge);
                Text(new Rect(458, 148, 684, 69), game.LastMessage, 21, Ivory, TextAnchor.MiddleCenter, true);
            }
            if (!string.IsNullOrEmpty(game.RescueTarget))
            {
                Box(new Rect(455, 641, 690, 73), Ink, Friendly);
                MarkerGlyph(new Vector2(482, 671), Friendly, MarkerKind.Person, 1f);
                Text(new Rect(507, 650, 616, 43), game.RescueTarget, 22, Friendly, TextAnchor.MiddleCenter, true);
                if (game.RescueProgress > .01f) Meter(new Rect(475, 702, 650, 5), game.RescueProgress, Friendly);
            }
            else if (game.AtBase && !game.CursorFree)
                Alert("SERVICING / TAB OPENS ARMORY", Green, 662);
            else if (game.Fuel < 20)
                Alert("LOW FUEL / RETURN TO SANCTUARY", Red, 662);
            else if (LowAmmo())
                Alert("LOW AMMO / HOOK A CRATE OR RETURN", Gold, 662);
            else if (game.Passengers > 0)
                Alert($"{game.Passengers}/4 ABOARD / RETURN TO SANCTUARY", Friendly, 662);
            if (!Compact && !game.CursorFree)
                Text(new Rect(440, 732, 720, 28), "WASD MOVE / Q-E ALTITUDE / H HELP / ESC PAUSE", 16, Muted, TextAnchor.MiddleCenter);
        }

        bool LowAmmo()
        {
            WeaponState l = game.Arsenal.Weapons[game.Arsenal.LeftSlot], r = game.Arsenal.Weapons[game.Arsenal.RightSlot];
            return !game.AtBase && (l.Ammo < l.MaxAmmo * .15f || r.Ammo < r.MaxAmmo * .15f);
        }

        void DrawWorldMarkers()
        {
            if (game.Flight.Camera == null) return;
            Vector3 player = game.Flight.transform.position;
            foreach (Crypt crypt in game.Crypts)
            {
                if (!crypt.Active || Vector3.Distance(player, crypt.Position) > 600) continue;
                WorldMark(crypt.Position + Vector3.up * 12, crypt.Name.ToUpperInvariant(), new Color(.94f, .49f, .97f), crypt.Health / crypt.MaxHealth, true);
            }
            foreach (Survivor survivor in game.Survivors)
                if (survivor.Alive && Vector3.Distance(player, survivor.Position) < 200)
                    WorldMark(survivor.Position + Vector3.up * 3, "CIVILIAN", Friendly, -1, false, MarkerKind.Person);
            // Keep every pickup glyph, but name only the closest visible crate. Dense kill-site
            // drops otherwise cover the aiming area with overlapping labels. No transient lists.
            Supply captionedSupply = null;
            float nearestSupply = 85f * 85f;
            foreach (Supply supply in game.Supplies)
            {
                if (supply.Collected) continue;
                Vector3 marker = supply.Position + Vector3.up * 3;
                float distance = (marker - player).sqrMagnitude;
                if (distance >= nearestSupply) continue;
                Vector3 viewport = game.Flight.Camera.WorldToViewportPoint(marker);
                if (viewport.z <= 0 || viewport.x < .075f || viewport.x > .925f || viewport.y < .212f || viewport.y > .733f) continue;
                captionedSupply = supply;
                nearestSupply = distance;
            }
            foreach (Supply supply in game.Supplies)
                if (!supply.Collected && Vector3.Distance(player, supply.Position) < 100)
                    WorldMark(supply.Position + Vector3.up * 3, SupplyLabels[(int)supply.Kind], Cyan, -1, false, MarkerKind.Crate, supply == captionedSupply);
            if (Vector3.Distance(player, World.BasePosition) > 28)
                WorldMark(World.BasePosition + Vector3.up * 10, "SANCTUARY", Gold, -1, true);
        }

        void WorldMark(Vector3 world, string caption, Color color, float health, bool showDistance, MarkerKind kind = MarkerKind.Objective, bool captionAllowed = true)
        {
            Vector3 projected = game.Flight.Camera.WorldToScreenPoint(world);
            if (projected.z <= 0) return;
            Vector2 p = new Vector2(projected.x / Screen.width * Width, (1 - projected.y / Screen.height) * Height);
            if (p.x < 120 || p.x > 1480 || p.y < 240 || p.y > 710) return;
            float distance = Vector3.Distance(game.Flight.transform.position, world);
            if (Vector2.Distance(p, new Vector2(800, 450)) < 40) p.y -= 50;
            MarkerGlyph(p, color, kind, 1f);
            if (captionAllowed && (kind == MarkerKind.Objective || distance < 85))
            {
                float width = kind == MarkerKind.Objective ? 276 : 150;
                Fill(new Rect(p.x - width * .5f, p.y - 47, width, 29), new Color(.018f, .032f, .041f, .84f));
                Text(new Rect(p.x - width * .5f + 4, p.y - 45, width - 8, 26), caption + (showDistance ? $" {distance:0}m" : ""), 17, color, TextAnchor.MiddleCenter);
            }
            if (health >= 0) Meter(new Rect(p.x - 40, p.y - 14, 80, 4), health, color);
        }

        void MarkerGlyph(Vector2 point, Color color, MarkerKind kind, float scale)
        {
            if (kind == MarkerKind.Person)
            {
                Circle(point + new Vector2(0, -8) * scale, 3.5f * scale, color);
                Line(point + new Vector2(0, -3) * scale, point + new Vector2(0, 5) * scale, 3 * scale, color);
                Line(point + new Vector2(-6, 0) * scale, point + new Vector2(6, 0) * scale, 2.5f * scale, color);
                Line(point + new Vector2(0, 5) * scale, point + new Vector2(-5, 12) * scale, 2.5f * scale, color);
                Line(point + new Vector2(0, 5) * scale, point + new Vector2(5, 12) * scale, 2.5f * scale, color);
            }
            else if (kind == MarkerKind.Crate)
            {
                Box(new Rect(point.x - 8 * scale, point.y - 8 * scale, 16 * scale, 16 * scale), Ink, color);
                Line(point + new Vector2(-7, -7) * scale, point + new Vector2(7, 7) * scale, 2 * scale, color);
                Line(point + new Vector2(-7, 7) * scale, point + new Vector2(7, -7) * scale, 2 * scale, color);
            }
            else
            {
                Line(point + new Vector2(-7, 0) * scale, point + new Vector2(0, 7) * scale, 2 * scale, color);
                Line(point + new Vector2(7, 0) * scale, point + new Vector2(0, 7) * scale, 2 * scale, color);
            }
        }

        void DrawArmory()
        {
            Box(new Rect(330, 205, 940, 550), Ink, Gold);
            Text(new Rect(351, 223, 896, 36), $"SANCTUARY ARMORY / {game.Salvage} SALVAGE", 26, Gold);
            Text(new Rect(351, 268, 896, 28), "Free repairs, fuel and ammo. Assign triggers below. TAB returns to flight.", 20, Muted);
            WeaponState[] weapons = game.Arsenal.Weapons;
            if (weapons == null) return;
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponState weapon = weapons[i];
                float y = 309 + i * 86;
                if (i % 2 == 0) Fill(new Rect(342, y - 3, 916, 82), new Color(.07f, .10f, .11f, .8f));
                Image(new Rect(351, y + 12, 50, 50), weaponIcons[Mathf.Min(i, 4)], weapon.Color);
                Text(new Rect(416, y, 652, 28), $"{weapon.Name.ToUpperInvariant()} / {(weapon.Unlocked ? "LV " + weapon.Level : "LOCKED")} / {weapon.Range:0}m", 20, weapon.Color);
                Text(new Rect(416, y + 29, 652, 55), weapon.Description, 22, Ivory, TextAnchor.UpperLeft, true);
                int cost = weapon.Unlocked ? weapon.UpgradeCost : game.Arsenal.UnlockCost(i);
                Rect purchase = new Rect(1091, y + 14, 156, 49);
                bool maxed = weapon.Unlocked && weapon.Level >= 5;
                if (Button(purchase, maxed ? "MAX LEVEL" : $"{(weapon.Unlocked ? "UPGRADE" : "UNLOCK")} {cost}", !maxed && game.Salvage >= cost ? Gold : Muted, 18))
                {
                    if (maxed) game.Message("Maximum blessing reached. The bishop approves.");
                    else if (game.Salvage < cost) game.Message($"Need {cost - game.Salvage} more salvage for this blessing.");
                    else if (weapon.Unlocked) game.Arsenal.Upgrade(i);
                    else game.Arsenal.Unlock(i);
                }
            }
        }

        void DrawHelp()
        {
            Box(new Rect(320, 160, 960, 569), new Color(Ink.r, Ink.g, Ink.b, 1), Edge);
            Text(new Rect(351, 181, 896, 36), "MERCY-ONE / FIELD MANUAL", 28, Gold);
            HelpRow(239, "FLIGHT", "WASD moves. Mouse turns and aims. Q/E or scroll changes altitude.");
            HelpRow(309, "WEAPONS", "Hold LMB / RMB. TAB frees the cursor. Left / right click icons to assign.");
            HelpRow(379, "HOOK", "Below 46 ft: fly over crates. Slow near cyan people to rescue. Four seats.");
            HelpRow(449, "DANGER", "Strafe or climb to dodge hexes and shake boarders. Keep moving over swarms.");
            HelpRow(519, "MISSION", "Seal four crypts. Bring four people home if possible. Losses lower your grade.");
            HelpRow(589, "SANCTUARY", "Hover over the pad to deliver, repair, refuel and rearm. TAB opens upgrades.");
            Text(new Rect(351, 679, 896, 28), "H CLOSES / ESC PAUSES / F3 TELEMETRY / M MUTES", 18, Muted, TextAnchor.MiddleCenter);
        }

        void HelpRow(float y, string title, string description)
        {
            Text(new Rect(351, y, 156, 30), title, 19, Gold);
            Text(new Rect(522, y - 1, 725, 56), description, 22, Ivory, TextAnchor.UpperLeft, true);
        }

        void DrawTitle()
        {
            Fill(new Rect(0, 0, 1600, 900), new Color(.012f, .028f, .042f, .82f));
            Fill(new Rect(70, 94, 5, 674), Gold);
            Text(new Rect(108, 105, 750, 25), "A SUPERNATURAL SEARCH & DESTROY OPERATION", 15, Muted);
            Text(new Rect(101, 155, 810, 108), "HOLY", 101, Ivory);
            Text(new Rect(105, 264, 830, 94), "HELICOPTER", 79, Gold);
            Text(new Rect(111, 377, 750, 28), "OPERATION  //  MIDNIGHT MASS", 24, Ivory);
            Text(new Rect(112, 427, 710, 76), "The dead have risen.\nGive them a very good reason to lie back down.", 23, Muted, TextAnchor.UpperLeft, true);
            DrawHelicopter(new Vector2(713, 602), .83f);
            if (Button(new Rect(112, 633, 414, 70), "BEGIN THE EXORCISM  >", Gold, 22)) game.Begin();
            Text(new Rect(112, 724, 700, 60), "YOU ARE MERCY-ONE. YOUR ROTORS ARE A CROSS.\nYOUR FLIGHT PLAN IS A TERRIBLE IDEA.", 18, Muted, TextAnchor.UpperLeft, true);
            Box(new Rect(981, 158, 508, 619), new Color(.025f, .043f, .055f, .92f), Edge);
            Text(new Rect(1014, 187, 442, 27), "TONIGHT'S SERMON", 22, Gold);
            Brief(235, "01", "SHUT THE GATES", "Seal four crypts to stop their reinforcements.");
            Brief(338, "02", "BRING THEM HOME", "Aim to save four survivors. Every loss costs you.");
            Brief(441, "03", "COME BACK LOUDER", "Rearm at Sanctuary. Buy outrageous holy weapons.");
            Fill(new Rect(1014, 546, 442, 1), Edge);
            Text(new Rect(1014, 565, 442, 29), "WASD MOVE / MOUSE AIM / LMB-RMB FIRE", 18, Ivory);
            Text(new Rect(1014, 604, 442, 29), "Q-E OR SCROLL / ALTITUDE", 18, Ivory);
            Text(new Rect(1014, 643, 442, 29), "TAB / ASSIGN & ARMORY   H / HELP", 18, Ivory);
            Text(new Rect(1014, 692, 442, 61), "Hook below 46 ft. Slow down for people.\nKeep moving when skeletons pile up.", 20, Muted, TextAnchor.UpperLeft, true);
            Text(new Rect(111, 824, 1376, 29), Compact ? "SANCTUARY AVIATION DIVISION / EST. 1666" : "SANCTUARY AVIATION DIVISION / EST. 1666     •     ARCADE FLIGHT / THOUSANDS OF MONSTERS / ABSOLUTELY NO SUBTLETY", 16, Muted);
        }

        void Brief(float y, string number, string title, string description)
        {
            Text(new Rect(1014, y, 42, 30), number, 25, Gold);
            Text(new Rect(1072, y, 377, 28), title, 21, Ivory);
            Text(new Rect(1072, y + 32, 377, 61), description, 22, Muted, TextAnchor.UpperLeft, true);
        }

        void DrawPause()
        {
            Fill(new Rect(0, 0, 1600, 900), new Color(.01f, .02f, .03f, .76f));
            Box(new Rect(520, 170, 560, 560), Ink, Edge);
            Text(new Rect(554, 203, 492, 48), "A MOMENT OF SILENCE", 31, Gold, TextAnchor.MiddleCenter);
            Text(new Rect(554, 260, 492, 30), "FLIGHT PAUSED", 13, Muted, TextAnchor.MiddleCenter);
            Text(new Rect(582, 320, 330, 24), "MOUSE SENSITIVITY", 13, Ivory);
            Text(new Rect(930, 320, 88, 24), $"{game.Flight.Sensitivity / .085f:0.00}×", 13, Gold, TextAnchor.MiddleRight);
            game.Flight.Sensitivity = Slider(new Rect(582, 351, 436, 26), game.Flight.Sensitivity, .025f, .2f);
            if (Button(new Rect(582, 405, 210, 43), "INVERT Y  /  " + (game.Flight.InvertY ? "ON" : "OFF"), game.Flight.InvertY ? Gold : Muted, 12))
                game.Flight.InvertY = !game.Flight.InvertY;
            if (Button(new Rect(808, 405, 210, 43), "AUDIO  /  " + (game.Audio.Muted ? "MUTED" : "ON"), game.Audio.Muted ? Muted : Gold, 12))
                game.Audio.Muted = !game.Audio.Muted;
            if (Button(new Rect(582, 490, 436, 56), "RESUME THE SERMON", Gold, 19)) game.TogglePause();
            if (Button(new Rect(582, 566, 436, 44), "RESTART OPERATION", Muted, 14)) game.Restart();
            Text(new Rect(554, 662, 492, 48), "ESC RESUMES / H OPENS MANUAL\nM MUTES AUDIO", 17, Muted, TextAnchor.MiddleCenter, true);
        }

        float Slider(Rect rect, float value, float min, float max)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive, rect);
            Event current = Event.current;
            EventType type = current.GetTypeForControl(control);
            if (type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                GUIUtility.hotControl = control;
                value = Mathf.Lerp(min, max, Mathf.Clamp01((current.mousePosition.x - rect.x) / rect.width));
                current.Use();
            }
            else if (type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                value = Mathf.Lerp(min, max, Mathf.Clamp01((current.mousePosition.x - rect.x) / rect.width));
                current.Use();
            }
            else if (type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                current.Use();
            }
            float normalized = Mathf.InverseLerp(min, max, value);
            Fill(new Rect(rect.x, rect.center.y - 2, rect.width, 4), Edge);
            Fill(new Rect(rect.x, rect.center.y - 2, rect.width * normalized, 4), Gold);
            Box(new Rect(rect.x + rect.width * normalized - 6, rect.center.y - 10, 12, 20), Gold, Ivory);
            return value;
        }

        void DrawEnd()
        {
            Fill(new Rect(0, 0, 1600, 900), new Color(.01f, .02f, .03f, .85f));
            Box(new Rect(380, 165, 840, 570), Ink, game.Won ? Gold : Red);
            Text(new Rect(430, 210, 740, 25), "OPERATION MIDNIGHT MASS", 16, Muted, TextAnchor.MiddleCenter);
            Text(new Rect(410, 257, 780, 66), game.Won ? game.MissionGrade : "LAST RITES.", game.Won ? 43 : 48, game.Won ? Gold : Red, TextAnchor.MiddleCenter);
            string result = game.Won ? game.Rescued >= game.RequiredRescues
                ? "The crypts are silent. Your rescue goal is complete.\nSanctuary Aviation salutes your extremely loud service."
                : $"The crypts are sealed. {game.Rescued} people made it home.\nThe parish survived, but the rescue goal was not met."
                : game.DeathReason;
            Text(new Rect(430, 336, 740, 66), result, 22, Ivory, TextAnchor.MiddleCenter, true);
            Fill(new Rect(445, 417, 710, 1), Edge);
            EndStat(450, "EXORCISED", game.Kills.ToString("N0"));
            EndStat(690, "BROUGHT HOME", game.Rescued.ToString());
            EndStat(930, "BEST SOUL STREAK", game.BestCombo.ToString("N0"));
            Text(new Rect(425, 529, 750, 55), $"TIME {ClockText(game.Elapsed)} / CIVILIAN LOSSES {game.CivilianLosses}\n{game.SuppliesCollected} CRATES HOOKED / {game.Sortie} SORTIES", 19, Muted, TextAnchor.MiddleCenter, true);
            if (Button(new Rect(545, 598, 510, 64), "FLY ANOTHER SORTIE     >", Gold, 19)) game.Restart();
        }

        void EndStat(float x, string title, string value)
        {
            Text(new Rect(x, 438, 220, 24), title, 12, Muted, TextAnchor.MiddleCenter);
            Text(new Rect(x, 467, 220, 43), value, 32, Ivory, TextAnchor.MiddleCenter);
        }

        void DrawHelicopter(Vector2 p, float s)
        {
            Line(p + new Vector2(-90, 52) * s, p + new Vector2(91, 52) * s, 8 * s, Edge);
            Line(p + new Vector2(-45, 29) * s, p + new Vector2(-60, 52) * s, 5 * s, Edge);
            Line(p + new Vector2(55, 29) * s, p + new Vector2(65, 52) * s, 5 * s, Edge);
            Line(p + new Vector2(40, 0) * s, p + new Vector2(170, -37) * s, 17 * s, Ink);
            Line(p + new Vector2(157, -31) * s, p + new Vector2(171, -70) * s, 12 * s, Ink);
            Circle(p, 67 * s, Ink);
            Fill(new Rect(p.x - 66 * s, p.y - 28 * s, 131 * s, 58 * s), Ink);
            Fill(new Rect(p.x - 47 * s, p.y - 27 * s, 44 * s, 30 * s), new Color(.17f, .31f, .34f));
            Line(p + new Vector2(0, -60) * s, p + new Vector2(0, -79) * s, 7 * s, Edge);
            Line(p + new Vector2(-141, -78) * s, p + new Vector2(141, -78) * s, 12 * s, Gold);
            Line(p + new Vector2(-49, -105) * s, p + new Vector2(49, -51) * s, 12 * s, Gold);
            Fill(new Rect(p.x + 23 * s, p.y - 2 * s, 8 * s, 30 * s), Gold);
            Fill(new Rect(p.x + 13 * s, p.y + 8 * s, 28 * s, 8 * s), Gold);
        }

        void Alert(string value, Color color, float y)
        {
            Box(new Rect(455, y, 690, 43), new Color(.025f, .043f, .055f, .90f), color * .65f);
            Text(new Rect(467, y + 4, 666, 35), value, 21, color, TextAnchor.MiddleCenter);
        }

        static string ClockText(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{whole / 60:00}:{whole % 60:00}";
        }

        void Meter(Rect rect, float value, Color color)
        {
            Fill(rect, new Color(.12f, .19f, .19f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), color);
        }

        void Text(Rect rect, string text, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool wrap = false)
        {
            // Keep secondary type >=11.5 actual pixels at 576p. Large body copy is authored at 22px.
            label.fontSize = Mathf.Max(size, Mathf.CeilToInt(11.5f / Mathf.Max(.5f, uiScale)));
            label.alignment = align; label.normal.textColor = color; label.wordWrap = wrap;
            rect.height = Mathf.Max(rect.height, label.fontSize * 1.25f);
            GUI.Label(rect, text, label);
        }

        bool Button(Rect rect, string caption, Color color, int size)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Box(rect, hover ? new Color(color.r * .21f, color.g * .21f, color.b * .21f, 1) : Panel, color);
            if (hover) Fill(new Rect(rect.x, rect.y, 4, rect.height), color);
            Text(rect, caption, size, color, TextAnchor.MiddleCenter);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use(); return true;
            }
            return false;
        }

        void Fill(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }

        void Box(Rect rect, Color color, Color edge)
        {
            Fill(rect, color);
            Fill(new Rect(rect.x, rect.y, rect.width, 1), edge); Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), edge);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), edge); Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), edge);
        }

        void Image(Rect rect, Texture2D texture, Color color)
        {
            Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = old;
        }

        void Circle(Vector2 centre, float radius, Color color) { Image(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), disc, color); }

        void Ring(Vector2 centre, float radius, Color color, int segments)
        {
            Vector2 previous = centre + Vector2.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                Vector2 next = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Line(previous, next, 1, color); previous = next;
            }
        }

        void Screw(float x, float y)
        {
            Circle(new Vector2(x, y), 3.5f, Edge);
            Fill(new Rect(x - 2, y, 4, 1), Ink);
        }

        void Line(Vector2 a, Vector2 b, float thickness, Color color)
        {
            Matrix4x4 matrix = GUI.matrix;
            // Compose in design coordinates before resolution scaling. RotateAroundPivot rotates
            // the already-scaled matrix and scatters strokes at resolutions other than 1600x900.
            GUI.matrix = matrix * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg), Vector3.one);
            Fill(new Rect(0, -thickness * .5f, Vector2.Distance(a, b), thickness), color);
            GUI.matrix = matrix;
        }

        Texture2D MakeIcon(int kind)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Armament glyph " + kind };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool on = false;
                    if (kind == 0)
                    {
                        int barrel = (x - 5) / 8;
                        int dx = (x - 5) % 8;
                        on = x >= 5 && x < 28 && barrel < 3 && dx <= 4 && y >= 5 && y <= 24 + barrel * 2;
                        if (y > 23 + barrel * 2 && (dx == 0 || dx == 4)) on = false;
                        if (y == 8) on = false;
                    }
                    else if (kind == 1)
                    {
                        float dx = x - 15.5f, dy = y - 12;
                        on = dx * dx + dy * dy < 82 && y <= 15 || y > 15 && y < 29 && Mathf.Abs(dx) < (29 - y) * .65f;
                        if (x >= 12 && x <= 14 && y >= 7 && y <= 16 || x >= 10 && x <= 18 && y >= 11 && y <= 13) on = false;
                    }
                    else if (kind == 2)
                    {
                        on = x >= 12 && x <= 19 && y >= 8 && y < 23 || y >= 23 && y < 29 && Mathf.Abs(x - 15.5f) < (30 - y) * .7f;
                        on |= y >= 7 && y <= 13 && (x >= 7 && x < 12 || x > 19 && x <= 24) && y - 6 > Mathf.Abs(x - 15.5f) * .45f;
                        on |= y >= 2 && y <= 6 && x >= 14 && x <= 17;
                        if (y >= 17 && y <= 19 && x >= 14 && x <= 17) on = false;
                    }
                    else if (kind == 3)
                    {
                        float radius = new Vector2(x - 15.5f, y - 15.5f).magnitude;
                        on = radius < 7 || radius >= 10 && radius < 14 && (Mathf.Abs(x - 15.5f) < 1.5f || Mathf.Abs(y - 15.5f) < 1.5f || Mathf.Abs(Mathf.Abs(x - 15.5f) - Mathf.Abs(y - 15.5f)) < 1.5f);
                        if (radius < 3) on = false;
                    }
                    else
                    {
                        float radius = new Vector2(x - 15.5f, y - 15.5f).magnitude;
                        on = x >= 14 && x <= 17 && y >= 5 && y <= 27 || x >= 8 && x <= 23 && y >= 18 && y <= 21 || radius > 12 && radius < 14;
                    }
                    pixels[y * size + x] = on ? Color.white : Color.clear;
                }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
    }
}
