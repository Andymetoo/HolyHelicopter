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
        static readonly Color Green = new Color(.44f, .92f, .61f);
        static readonly Color Friendly = new Color(.20f, .96f, .92f);
        static readonly string[] CompassNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
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
                if (game.AtBase && game.CursorFree && !game.Paused && !game.GameOver) DrawArmory();
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
                    Box(new Rect(455, 693, 690, 74), Ink, Edge);
                    Text(new Rect(473, 701, 654, 58), tooltip, 14, Ivory, TextAnchor.MiddleCenter, true);
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
            Box(new Rect(320, 796, 930, 104), Ink, Edge);
            Fill(new Rect(320, 796, 930, 4), new Color(.31f, .35f, .27f));
            for (int i = 0; i < 5; i++)
            {
                Line(new Vector2(339 + i * 16, 804), new Vector2(331 + i * 16, 818), 4, new Color(.59f, .48f, .16f));
                Line(new Vector2(1162 + i * 16, 804), new Vector2(1154 + i * 16, 818), 4, new Color(.59f, .48f, .16f));
            }
            Screw(327, 887); Screw(1241, 887); Screw(105, 712); Screw(1495, 712);
            Text(new Rect(338, 840, 140, 20), "MERCY // MH-01", 12, Muted);
            Text(new Rect(338, 861, 142, 20), "✚ SANCTIFIED AIR", 10, Gold);
            Text(new Rect(1106, 838, 126, 20), $"SALVAGE  {game.Salvage}", 14, Gold, TextAnchor.MiddleRight);
            Text(new Rect(1106, 862, 126, 18), game.CursorFree ? "TAB  RETURN TO FLIGHT" : "TAB  ARMORY / ASSIGN", 9, Muted, TextAnchor.MiddleRight);
        }

        void DrawMission()
        {
            Box(new Rect(32, 27, 330, 106), new Color(.025f, .043f, .055f, .78f), new Color(.16f, .24f, .24f, .8f));
            Fill(new Rect(32, 27, 4, 106), Gold);
            Text(new Rect(49, 37, 292, 21), "OPERATION  /  MIDNIGHT MASS", 13, Gold);
            Text(new Rect(49, 64, 292, 23), $"SEAL THE CRYPTS     {game.ActiveCrypts} REMAIN", 17, Ivory);
            Text(new Rect(49, 92, 300, 22), $"RESCUE CIVILIANS    {game.Rescued} / {game.RequiredRescues}" + (game.Passengers > 0 ? $"  (+{game.Passengers} aboard)" : ""), game.Passengers > 0 ? 12 : 14, game.Rescued >= game.RequiredRescues ? Green : Muted);
            if (game.CivilianLosses > 0)
                Text(new Rect(49, 139, 340, 20), $"LOSSES {game.CivilianLosses}  /  PROTECT THE REMAINING SURVIVORS", 10, Red);
            if (game.CanExtract)
            {
                Box(new Rect(32, 164, 330, 47), Ink, Gold);
                Text(new Rect(45, 171, 302, 31), "RETURN TO SANCTUARY TO EXTRACT", 14, Gold, TextAnchor.MiddleCenter);
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
            Box(new Rect(1280, 27, 288, 88), new Color(.025f, .043f, .055f, .78f), new Color(.16f, .24f, .24f, .8f));
            Text(new Rect(1297, 36, 252, 20), "EXORCISM COUNTER", 12, Muted, TextAnchor.MiddleRight);
            Text(new Rect(1297, 55, 252, 34), game.Kills.ToString("N0"), 29, Gold, TextAnchor.MiddleRight);
            Text(new Rect(1297, 91, 252, 18), $"{game.Horde.Count:N0} UNDEAD  /  {Mathf.FloorToInt(game.Elapsed / 60):00}:{Mathf.FloorToInt(game.Elapsed % 60):00}", 12, Muted, TextAnchor.MiddleRight);
            bool hit = Time.time < game.HitUntil;
            Color reticle = hit ? Gold : new Color(.82f, .95f, .85f, .87f);
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
            else if (game.Horde.TowerCount > 0 && Vector3.Distance(game.Flight.transform.position, game.Horde.TowerPosition) < 65)
                Alert($"BONE TOWER RISING  //  {game.Horde.TowerHeight:0} m", Gold, 595);
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
            Box(new Rect(27, 680, 273, 220), Ink, Edge);
            Fill(new Rect(27, 680, 273, 4), Gold);
            Text(new Rect(42, 689, 236, 19), "ALL-ROUND RADAR              200 m", 11, Muted);
            Vector2 centre = new Vector2(136, 797);
            const float radius = 82f;
            if (radarDirty) RepaintRadar();
            Image(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), radarTexture, Color.white);
            Line(centre + new Vector2(0, -6), centre + new Vector2(-4, 4), 2, Ivory);
            Line(centre + new Vector2(0, -6), centre + new Vector2(4, 4), 2, Ivory);
            Text(new Rect(235, 721, 54, 18), "HOSTILE", 8, Red);
            Text(new Rect(235, 741, 54, 18), "CRYPT", 8, new Color(.88f, .43f, 1f));
            Text(new Rect(235, 761, 54, 18), "RESCUE", 8, Friendly);
            Text(new Rect(235, 781, 54, 18), "SUPPLY", 8, Cyan);
            Text(new Rect(235, 801, 54, 18), "HOME", 8, Gold);
            Text(new Rect(43, 882, 240, 15), "▲ HEADING UP  /  REAR CONTACTS BELOW", 9, Muted);
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
                if (survivor.Alive) RadarMark(survivor.Position, origin, sine, cosine, centre, radius, Friendly, 5, false);
            foreach (Supply supply in game.Supplies)
                if (!supply.Collected) RadarMark(supply.Position, origin, sine, cosine, centre, radius, Cyan, 4, false);
            RadarMark(World.BasePosition, origin, sine, cosine, centre, radius, Gold, 7, true);
            radarTexture.SetPixels32(radarPixels);
            radarTexture.Apply(false);
        }

        Vector2 RadarPosition(Vector3 point, Vector3 origin, float sine, float cosine, float radius, Vector2 centre)
        {
            Vector3 delta = point - origin;
            return centre + new Vector2(delta.x * cosine - delta.z * sine, -(delta.x * sine + delta.z * cosine)) * (radius / RadarRange);
        }

        void RadarMark(Vector3 position, Vector3 origin, float sine, float cosine, Vector2 centre, float radius, Color color, float size, bool clamp)
        {
            Vector2 offset = RadarPosition(position, origin, sine, cosine, radius, centre) - centre;
            if (offset.magnitude > radius)
            {
                if (!clamp) return;
                offset = offset.normalized * (radius - 3);
                color.a = .7f;
            }
            Vector2 p = centre + offset;
            RadarPixel(p, color, Mathf.RoundToInt(size * .5f));
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
            float left = 800 - weapons.Length * 49f;
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponState weapon = weapons[i];
                Rect slot = new Rect(left + i * 98, 806, 90, 78);
                bool hover = game.CursorFree && slot.Contains(Event.current.mousePosition);
                Box(slot, weapon.Unlocked ? new Color(.065f, .10f, .12f) : new Color(.035f, .048f, .057f), hover ? Ivory : Edge);
                if (i < weaponIcons.Length) Image(new Rect(slot.x + 22, slot.y + 5, 44, 44), weaponIcons[i], weapon.Unlocked ? weapon.Color : new Color(.40f, .49f, .47f));
                Text(new Rect(slot.x + 3, slot.y + 46, slot.width - 6, 17), weapon.ShortName.ToUpperInvariant(), 10, weapon.Unlocked ? Ivory : Muted, TextAnchor.MiddleCenter);
                if (weapon.Unlocked)
                {
                    Fill(new Rect(slot.x + 6, slot.y + 68, 78, 3), new Color(.12f, .17f, .18f));
                    Fill(new Rect(slot.x + 6, slot.y + 68, 78 * Mathf.Clamp01(weapon.Ammo / Mathf.Max(1, weapon.MaxAmmo)), 3), weapon.Color);
                    Text(new Rect(slot.x + 4, slot.y + 3, 25, 16), $"{weapon.Level}", 11, Gold);
                    if (i == game.Arsenal.LeftSlot)
                    {
                        Fill(new Rect(slot.x, slot.y, 3, slot.height), Cyan);
                        Fill(new Rect(slot.x, slot.y - 16, 42, 16), Cyan);
                        Text(new Rect(slot.x, slot.y - 17, 42, 17), "LMB", 10, Ink, TextAnchor.MiddleCenter);
                    }
                    if (i == game.Arsenal.RightSlot)
                    {
                        Fill(new Rect(slot.xMax - 3, slot.y, 3, slot.height), Red);
                        Fill(new Rect(slot.xMax - 42, slot.y - 16, 42, 16), Red);
                        Text(new Rect(slot.xMax - 42, slot.y - 17, 42, 17), "RMB", 10, Ink, TextAnchor.MiddleCenter);
                    }
                    if (hover)
                    {
                        tooltip = $"{weapon.Name.ToUpperInvariant()}  /  LEVEL {weapon.Level}\n{weapon.Description}   •   LEFT CLICK = LMB   /   RIGHT CLICK = RMB";
                        if (Event.current.type == EventType.MouseDown && Event.current.button <= 1 && !game.Paused && !game.GameOver)
                        {
                            game.Arsenal.Assign(i, Event.current.button == 1);
                            Event.current.Use();
                        }
                    }
                }
                else
                {
                    Text(new Rect(slot.x + 4, slot.y + 64, slot.width - 8, 14), "BASE UNLOCK", 8, Muted, TextAnchor.MiddleCenter);
                    if (hover) tooltip = weapon.Name.ToUpperInvariant() + "\nReturn to Sanctuary and open the armory with TAB to unlock.";
                }
            }
            if (game.CursorFree && !game.AtBase)
                Text(new Rect(500, 768, 600, 22), "ASSIGN WEAPONS: LEFT / RIGHT CLICK AN ICON    •    TAB TO FLY", 12, Ivory, TextAnchor.MiddleCenter);
        }

        void DrawStatus()
        {
            Box(new Rect(1272, 722, 301, 178), Ink, Edge);
            Fill(new Rect(1272, 722, 301, 4), Gold);
            Text(new Rect(1290, 735, 260, 20), game.AtBase ? "SANCTUARY  /  SERVICING" : "MERCY-ONE  /  AIRBORNE", 12, game.AtBase ? Green : Muted);
            Meter(new Rect(1290, 768, 180, 8), game.Hull / 100f, game.Hull < 30 ? Red : Green);
            Text(new Rect(1290, 752, 180, 17), "HULL INTEGRITY", 10, Muted);
            Text(new Rect(1473, 755, 80, 28), $"{Mathf.Max(0, game.Hull):0}%", 22, game.Hull < 30 ? Red : Ivory, TextAnchor.MiddleRight);
            Meter(new Rect(1290, 803, 180, 8), game.Fuel / 100f, game.Fuel < 20 ? Red : Gold);
            Text(new Rect(1290, 786, 180, 17), "AVIATION FUEL", 10, Muted);
            Text(new Rect(1473, 791, 80, 28), $"{Mathf.Max(0, game.Fuel):0}%", 22, game.Fuel < 20 ? Red : Ivory, TextAnchor.MiddleRight);
            Fill(new Rect(1290, 823, 264, 1), Edge);
            Text(new Rect(1290, 833, 150, 22), $"ALT  {game.Flight.Altitude * 3.28084f:0} FT", 17, game.Flight.Altitude < 14 ? Cyan : Ivory);
            Text(new Rect(1435, 833, 117, 22), $"{game.Flight.Velocity.magnitude * 3.6f:0} KM/H", 14, Muted, TextAnchor.MiddleRight);
            WeaponState[] weapons = game.Arsenal.Weapons;
            if (weapons != null && weapons.Length > 0)
            {
                WeaponState l = weapons[Mathf.Clamp(game.Arsenal.LeftSlot, 0, weapons.Length - 1)];
                WeaponState r = weapons[Mathf.Clamp(game.Arsenal.RightSlot, 0, weapons.Length - 1)];
                Text(new Rect(1290, 867, 128, 18), $"L  {l.Ammo:0} / {l.MaxAmmo:0}", 11, Cyan);
                Text(new Rect(1417, 867, 136, 18), $"R  {r.Ammo:0} / {r.MaxAmmo:0}", 11, Red, TextAnchor.MiddleRight);
            }
        }

        void DrawNotifications()
        {
            if (Time.time < game.MessageUntil && !string.IsNullOrEmpty(game.LastMessage))
            {
                Box(new Rect(420, 139, 760, 47), new Color(.025f, .043f, .055f, .84f), Edge);
                Text(new Rect(435, 147, 730, 30), game.LastMessage.ToUpperInvariant(), 16, Ivory, TextAnchor.MiddleCenter, true);
            }
            if (game.RescueProgress > .01f)
            {
                Box(new Rect(610, 635, 380, 66), Ink, Cyan);
                Text(new Rect(622, 645, 356, 23), "HOOK DEPLOYED  /  " + game.RescueTarget, 13, Cyan, TextAnchor.MiddleCenter);
                Meter(new Rect(628, 678, 344, 5), game.RescueProgress, Cyan);
            }
            else if (game.AtBase && !game.CursorFree)
                Alert("SANCTUARY  //  SERVICING  •  TAB TO OPEN ARMORY", Green, 662);
            else if (game.Fuel < 20)
                Alert("LOW FUEL  //  RETURN TO SANCTUARY", Red, 662);
            else if (game.Passengers > 0)
                Text(new Rect(550, 675, 500, 20), $"{game.Passengers} CIVILIANS ABOARD  •  DELIVER TO SANCTUARY", 12, Green, TextAnchor.MiddleCenter);
            Text(new Rect(350, 885, 900, 14), "WASD  MOVE     MOUSE  AIM     Q / E  ALTITUDE     SCROLL  ALTITUDE     H  FIELD MANUAL     ESC  PAUSE", 9, Muted, TextAnchor.MiddleCenter);
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
                    WorldMark(survivor.Position + Vector3.up * 3, "RESCUE", Friendly, -1, false);
            foreach (Supply supply in game.Supplies)
                if (!supply.Collected && Vector3.Distance(player, supply.Position) < 100)
                    WorldMark(supply.Position + Vector3.up * 3, supply.Kind.ToString().ToUpperInvariant(), Cyan, -1, false);
            if (Vector3.Distance(player, World.BasePosition) > 28)
                WorldMark(World.BasePosition + Vector3.up * 10, "SANCTUARY", Gold, -1, true);
        }

        void WorldMark(Vector3 world, string caption, Color color, float health, bool showDistance)
        {
            Vector3 projected = game.Flight.Camera.WorldToScreenPoint(world);
            if (projected.z <= 0) return;
            Vector2 p = new Vector2(projected.x / Screen.width * Width, (1 - projected.y / Screen.height) * Height);
            if (p.x < 100 || p.x > 1500 || p.y < 160 || p.y > 718) return;
            float distance = Vector3.Distance(game.Flight.transform.position, world);
            if (Vector2.Distance(p, new Vector2(800, 450)) < 40) p.y -= 50;
            Line(p + new Vector2(-5, 0), p + new Vector2(0, 5), 1.5f, color);
            Line(p + new Vector2(5, 0), p + new Vector2(0, 5), 1.5f, color);
            Text(new Rect(p.x - 100, p.y - 25, 200, 19), caption + (showDistance ? $"  {distance:0}m" : ""), 11, color, TextAnchor.MiddleCenter);
            if (health >= 0) Meter(new Rect(p.x - 33, p.y - 6, 66, 3), health, color);
        }

        void DrawArmory()
        {
            Box(new Rect(420, 390, 760, 300), Ink, Gold);
            Text(new Rect(445, 410, 690, 30), "SANCTUARY ARMORY", 25, Gold);
            Text(new Rect(445, 447, 690, 22), $"{game.Salvage} SALVAGE  /  Hull, fuel and ammunition replenish while you hover over the pad.", 13, Muted);
            WeaponState[] weapons = game.Arsenal.Weapons;
            if (weapons == null) return;
            float spacing = 700f / Mathf.Max(1, weapons.Length);
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponState weapon = weapons[i];
                float x = 446 + i * spacing;
                Image(new Rect(x + 34, 487, 52, 52), weaponIcons[Mathf.Min(i, 4)], weapon.Color);
                Text(new Rect(x, 546, spacing - 12, 22), weapon.ShortName.ToUpperInvariant(), 13, Ivory, TextAnchor.MiddleCenter);
                Text(new Rect(x, 572, spacing - 12, 19), weapon.Unlocked ? $"LEVEL {weapon.Level}" : "LOCKED", 11, Muted, TextAnchor.MiddleCenter);
                int cost = weapon.Unlocked ? weapon.UpgradeCost : game.Arsenal.UnlockCost(i);
                Rect purchase = new Rect(x, 603, spacing - 12, 39);
                bool maxed = weapon.Unlocked && weapon.Level >= 5;
                if (Button(purchase, maxed ? "MAXIMUM BLESSING" : $"{(weapon.Unlocked ? "UPGRADE" : "UNLOCK")}  {cost}", !maxed && game.Salvage >= cost ? Gold : Muted, maxed ? 9 : 11))
                {
                    if (maxed) game.Message("Maximum blessing reached. The bishop approves.");
                    else if (game.Salvage < cost) game.Message($"Need {cost - game.Salvage} more salvage for this blessing.");
                    else if (weapon.Unlocked) game.Arsenal.Upgrade(i);
                    else game.Arsenal.Unlock(i);
                }
                if (purchase.Contains(Event.current.mousePosition)) tooltip = weapon.Description;
            }
            Text(new Rect(445, 661, 690, 18), "CLICK ICONS BELOW TO ASSIGN WEAPONS  /  TAB TO LAUNCH", 11, Muted, TextAnchor.MiddleCenter);
        }

        void DrawHelp()
        {
            Box(new Rect(430, 205, 740, 445), Ink, Edge);
            Text(new Rect(462, 227, 676, 31), "MERCY-ONE  /  FIELD MANUAL", 24, Gold);
            HelpRow(278, "FLIGHT", "WASD move · mouse turns & aims · Q/E descend / climb · scroll altitude");
            HelpRow(330, "FIRE", "Hold LMB / RMB. TAB releases the cursor. Left / right click an icon to assign.");
            HelpRow(382, "RESCUE", "Hover below 30 ft, low and slow over civilians or supplies. The winch is automatic.");
            HelpRow(434, "SURVIVE", "Keep moving near crowds. Bone towers reach up; climb or strafe to shed boarders.");
            HelpRow(486, "PROGRESS", "Destroy crypts, deliver survivors, return to the pad. TAB opens the base armory.");
            HelpRow(538, "CARE", "Civilians can die in explosions. Use precise fire around cyan rescue markers.");
            Text(new Rect(462, 604, 676, 22), "H CLOSES MANUAL     /     ESC PAUSES     /     F3 PERFORMANCE", 12, Muted, TextAnchor.MiddleCenter);
        }

        void HelpRow(float y, string title, string description)
        {
            Text(new Rect(462, y, 100, 21), title, 12, Gold);
            Text(new Rect(568, y - 1, 568, 38), description, 14, Ivory, TextAnchor.UpperLeft, true);
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
            if (Button(new Rect(112, 633, 414, 70), "BEGIN THE EXORCISM     >", Gold, 21)) game.Begin();
            Text(new Rect(112, 724, 700, 43), "YOU ARE MERCY-ONE.  YOUR ROTORS ARE A CROSS.\nYOUR FLIGHT PLAN IS A TERRIBLE IDEA.", 12, Muted, TextAnchor.UpperLeft, true);
            Box(new Rect(981, 158, 508, 579), new Color(.025f, .043f, .055f, .92f), Edge);
            Text(new Rect(1014, 187, 442, 27), "TONIGHT'S SERMON", 22, Gold);
            Brief(245, "01", "SHUT THE GATES", "Blast the four crypts to stem the endless undead tide.");
            Brief(330, "02", "BRING THEM HOME", "Winch up survivors. Deliver at least four to Sanctuary.");
            Brief(415, "03", "COME BACK LOUDER", "Refuel, rearm and buy increasingly unreasonable weapons.");
            Fill(new Rect(1014, 511, 442, 1), Edge);
            Text(new Rect(1014, 532, 442, 29), "WASD  FLY     MOUSE  AIM     LMB / RMB  FIRE", 13, Ivory);
            Text(new Rect(1014, 568, 442, 29), "Q / E  ALTITUDE     SCROLL  ALTITUDE", 13, Ivory);
            Text(new Rect(1014, 604, 442, 29), "TAB  CURSOR & ARMORY     H  FIELD MANUAL", 13, Ivory);
            Text(new Rect(1014, 661, 442, 43), "Hover low for pickups. Keep moving near crowds.\nMind the civilians. Mind the skeletons climbing up.", 13, Muted, TextAnchor.UpperLeft, true);
            Text(new Rect(111, 824, 1376, 21), "SANCTUARY AVIATION DIVISION   /   EST. 1666                                             ARCADE FLIGHT  ·  THOUSANDS OF MONSTERS  ·  ABSOLUTELY NO SUBTLETY", 11, Muted);
        }

        void Brief(float y, string number, string title, string description)
        {
            Text(new Rect(1014, y, 42, 30), number, 25, Gold);
            Text(new Rect(1072, y, 377, 24), title, 16, Ivory);
            Text(new Rect(1072, y + 30, 363, 39), description, 14, Muted, TextAnchor.UpperLeft, true);
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
            Text(new Rect(554, 676, 492, 27), "ESC TO RESUME  /  H FOR THE FIELD MANUAL  /  M MUTES AUDIO", 10, Muted, TextAnchor.MiddleCenter);
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
            Text(new Rect(410, 257, 780, 66), game.Won ? "THE NIGHT IS YOURS." : "LAST RITES.", 48, game.Won ? Gold : Red, TextAnchor.MiddleCenter);
            Text(new Rect(445, 338, 710, 54), game.Won ? "The crypts are silent. The living are home.\nSanctuary Aviation thanks you for your extremely loud service." : game.DeathReason, 17, Ivory, TextAnchor.MiddleCenter, true);
            Fill(new Rect(445, 417, 710, 1), Edge);
            EndStat(450, "EXORCISED", game.Kills.ToString("N0"));
            EndStat(690, "BROUGHT HOME", game.Rescued.ToString());
            EndStat(930, "BEST SOUL STREAK", game.BestCombo.ToString("N0"));
            Text(new Rect(445, 529, 710, 24), $"FLIGHT TIME  {Mathf.FloorToInt(game.Elapsed / 60):00}:{Mathf.FloorToInt(game.Elapsed % 60):00}    /    CIVILIAN LOSSES  {game.CivilianLosses}", 13, Muted, TextAnchor.MiddleCenter);
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
            Box(new Rect(550, y, 500, 36), new Color(.025f, .043f, .055f, .88f), color * .65f);
            Text(new Rect(562, y + 4, 476, 28), value, 13, color, TextAnchor.MiddleCenter);
        }

        void Meter(Rect rect, float value, Color color)
        {
            Fill(rect, new Color(.12f, .19f, .19f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), color);
        }

        void Text(Rect rect, string text, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool wrap = false)
        {
            label.fontSize = size; label.alignment = align; label.normal.textColor = color; label.wordWrap = wrap;
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
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - thickness * .5f, Vector2.Distance(a, b), thickness), color);
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
