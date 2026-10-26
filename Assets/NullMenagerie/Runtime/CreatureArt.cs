using System;
using UnityEngine;

namespace NullMenagerie
{
    /// <summary>Small anatomical glyph canvases. No textures, sprites, global hooks or random Unity state.</summary>
    public static class CreatureArt
    {
        public const int Width = 41;
        public const int Height = 25;
        const byte Bone = 1, Skin = 2, Eye = 3, Rune = 4, Shade = 5;
        static Font font;
        static bool ownsFont;
        static GUIStyle style;
        static readonly string[] single = MakeSingleCharacters();
        static readonly Color Ivory = new Color(.94f, .92f, .79f);
        static readonly Color Amber = new Color(1f, .75f, .29f);
        static readonly Color Magenta = new Color(1f, .39f, .67f);
        static readonly Color[] colors = new Color[6];

        sealed class Canvas
        {
            public readonly char[,] Glyph = new char[Width, Height];
            public readonly byte[,] Ink = new byte[Width, Height];
            public readonly Specimen Specimen;
            public readonly int Beat, Sway;
            public readonly float Time;
            public int HeadX = 20, HeadY = 6, HeartX = 20, HeartY = 14;
            public Canvas(Specimen specimen, float time)
            {
                Specimen = specimen; Time = time;
                Beat = (int)Math.Floor(time * 5 + (specimen.Seed % 11) * .3);
                Sway = (int)Math.Round(Math.Sin(time * 3.1 + specimen.Seed % 17));
                for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) Glyph[x, y] = ' ';
            }
            public void Put(int x, int y, char glyph, byte ink = Bone)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height) return;
                Glyph[x, y] = glyph; Ink[x, y] = glyph == ' ' ? (byte)0 : ink;
            }
            public void Text(int x, int y, string text, byte ink = Bone)
            { for (int i = 0; i < text.Length; i++) Put(x + i, y, text[i], ink); }
            public void Line(int x0, int y0, int x1, int y1, char glyph, byte ink = Bone)
            {
                int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
                int error = dx - dy;
                for (int guard = 0; guard < 100; guard++)
                {
                    Put(x0, y0, glyph, ink);
                    if (x0 == x1 && y0 == y1) break;
                    int e = error * 2;
                    if (e > -dy) { error -= dy; x0 += sx; }
                    if (e < dx) { error += dx; y0 += sy; }
                }
            }
            public void Oval(int cx, int cy, int rx, int ry, byte ink = Bone, bool fill = true)
            {
                for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++)
                {
                    float d = x * x / (float)(rx * rx) + y * y / (float)(ry * ry);
                    if (d > 1.13f) continue;
                    bool edge = d > .65f;
                    char glyph;
                    if (edge) glyph = Math.Abs(x) > rx * .76f ? (x < 0 ? '(' : ')') : (y < 0 ? '_' : '=');
                    else if (!fill) continue;
                    else
                    {
                        uint h = Hash(Specimen.Seed, x + cx, y + cy);
                        glyph = Specimen.Armor > .24f ? "#%#=+"[(int)(h % 5)] : ":;.:+"[(int)(h % 5)];
                    }
                    Put(cx + x, cy + y, glyph, edge ? Bone : ink);
                }
            }
        }

        public static string[] Rasterize(Specimen specimen, float time = 0)
        {
            if (specimen == null) return Array.Empty<string>();
            Canvas c = Compose(specimen, time);
            var rows = new string[Height];
            var row = new char[Width];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++) row[x] = c.Glyph[x, y];
                rows[y] = new string(row);
            }
            return rows;
        }

        public static void Draw(Rect area, Specimen specimen, float time, bool faceRight = true,
            float attack = 0, float hurt = 0, float healthFraction = 1, float death = 0, float reveal = 1)
        {
            if (specimen == null || Event.current.type != EventType.Repaint || area.width <= 0 || area.height <= 0) return;
            Prepare();
            Canvas c = Compose(specimen, time);
            float alpha = 1 - Mathf.Clamp01((death - .25f) / 2.1f);
            if (alpha <= 0) return;
            // A 0.62 advance is the natural aspect of a monospace terminal character.
            float cellH = Mathf.Min(area.height / Height, area.width / (Width * .62f));
            float cellW = cellH * .62f;
            float pulse = Mathf.Sin(time * (2.1f + specimen.Speed * .055f) + (specimen.Seed % 51));
            float breath = 1f + pulse * .018f;
            float lean = Mathf.Clamp01(attack) * (faceRight ? 1 : -1) * cellW * 3;
            float bob = pulse * cellH * .28f;
            float originX = area.center.x - Width * cellW * .5f + lean;
            float originY = area.center.y - Height * cellH * .5f + bob;
            Color baseColor = Color.HSVToRGB(Mathf.Repeat(specimen.Hue, 1), .50f, .91f);
            colors[Bone] = Color.Lerp(Ivory, baseColor, .24f);
            colors[Skin] = baseColor;
            colors[Eye] = Color.Lerp(Amber, Color.white, .3f + Mathf.Sin(time * 4) * .2f);
            colors[Rune] = specimen.Mutation == MutationKind.None ? Color.Lerp(baseColor, Magenta, .35f) : Magenta;
            colors[Shade] = Color.Lerp(new Color(.16f, .26f, .30f), baseColor, .40f);

            Color previousColor = GUI.color;
            // Use local glyph coordinates: replacing GUI.matrix here scales Unity's nested clip
            // offsets as well, sending creatures outside arena groups and scrolling cabinet cards.
            style.fontSize = Mathf.Max(3, Mathf.RoundToInt(cellH));
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int sx = faceRight ? x : Width - 1 - x;
                char glyph = c.Glyph[sx, y];
                if (glyph == ' ') continue;
                if (!faceRight) glyph = Mirror(glyph);
                uint h = Hash(specimen.Seed, sx, y);
                // Damage removes peripheral flesh; eyes and structural outlines survive until disintegration.
                byte ink = c.Ink[sx, y];
                if (ink == Skin && h % 100 < (1 - healthFraction) * 34) continue;
                float scan = y / (float)Height;
                if (reveal < 1 && scan > reveal) continue;
                float dx = 0, dy = 0;
                if (death > 0)
                {
                    float velocity = (h % 11) * .11f + .6f;
                    dx = (x - 20) * death * .33f * cellW;
                    dy = (-death * 35 + death * death * 105) * velocity * cellH / 21f;
                    if (h % 7 < 2) glyph = '.';
                }
                if (hurt > .05f) dx += Mathf.Sin(h + time * 77) * Mathf.Clamp01(hurt) * cellW * .6f;
                Color color = Color.Lerp(colors[ink], Color.white, Mathf.Clamp01(hurt) * .85f);
                color.a = alpha;
                if (reveal < 1 && scan > reveal - .08f) { color = Color.white; glyph = "01#*"[(int)(h % 4)]; }
                style.normal.textColor = color;
                GUI.color = Color.white;
                float yBreath = (y - c.HeartY) * breath + c.HeartY;
                GUI.Label(new Rect(originX + x * cellW + dx, originY + yBreath * cellH + dy, cellW * 1.6f, cellH * 1.6f), single[glyph], style);
            }
            GUI.color = previousColor;
        }

        public static void Release()
        {
            if (font != null && ownsFont) UnityEngine.Object.Destroy(font);
            font = null; style = null; ownsFont = false;
        }

        static void Prepare()
        {
            if (style != null && font != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, 18);
            ownsFont = font != null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            style = new GUIStyle { font = font, fontSize = 18, richText = false, wordWrap = false, clipping = TextClipping.Overflow,
                padding = new RectOffset(), margin = new RectOffset(), alignment = TextAnchor.UpperLeft };
        }

        static Canvas Compose(Specimen s, float time)
        {
            var c = new Canvas(s, time);
            switch (s.Body)
            {
                case BodyKind.Beetle: Beetle(c); break;
                case BodyKind.Mantis: Mantis(c); break;
                case BodyKind.Wraith: Wraith(c); break;
                case BodyKind.Crab: Crab(c); break;
                case BodyKind.Serpent: Serpent(c); break;
                case BodyKind.Moth: Moth(c); break;
            }
            Mutate(c);
            return c;
        }

        static void Face(Canvas c, int x, int y, int halfWidth = 3)
        {
            c.HeadX = x; c.HeadY = y;
            c.Text(x - halfWidth - 1, y - 2, "/" + new string('^', halfWidth * 2 + 1) + "\\");
            c.Text(x - halfWidth - 1, y - 1, "|" + new string(' ', halfWidth * 2 + 1) + "|");
            c.Text(x - halfWidth - 1, y, "[" + new string(' ', halfWidth * 2 + 1) + "]");
            bool blink = c.Beat % 29 == 0;
            int eyes = Math.Max(1, Math.Min(c.Specimen.Eyes, halfWidth * 2 + 1));
            for (int i = 0; i < eyes; i++)
            {
                int ex = eyes == 1 ? x : x - halfWidth + (int)Math.Round(i * halfWidth * 2.0 / (eyes - 1));
                c.Put(ex, y - 1, blink ? '-' : (c.Specimen.Armor > .23f ? 'O' : 'o'), Eye);
            }
            c.Text(x - halfWidth, y, new string(c.Specimen.Power > 16 ? 'V' : ':', halfWidth * 2 + 1), Skin);
            c.Text(x - halfWidth, y + 1, "\\" + new string('_', halfWidth * 2 - 1) + "/");
            for (int i = 0; i < Math.Min(3, c.Specimen.Horns); i++)
            {
                int hx = x - halfWidth + i * Math.Max(1, halfWidth);
                c.Put(hx, y - 3, '^'); c.Put(hx, y - 4, '|', Rune);
            }
        }

        static void Beetle(Canvas c)
        {
            int bulk = (int)(Mathf.Clamp01(c.Specimen.Bulk) * 3);
            int radius = 7 + bulk;
            int pairs = Math.Max(2, Math.Min(4, c.Specimen.Limbs / 2));
            for (int i = 0; i < pairs; i++)
            {
                int y = 10 + i * 3;
                int step = ((c.Beat + i) % 4 < 2 ? 1 : -1);
                c.Line(20 - radius + 1, y, 7 - i, y + step, '/', Skin);
                c.Line(7 - i, y + step, 5 - i, y + 3 + step, '|');
                c.Text(3 - i, y + 3 + step, "_/", Bone);
                c.Line(20 + radius - 1, y, 33 + i, y - step, '\\', Skin);
                c.Line(33 + i, y - step, 35 + i, y + 3 - step, '|');
                c.Text(35 + i, y + 3 - step, "\\_", Bone);
            }
            c.Oval(20, 14, radius, 7, Skin);
            for (int y = 8; y <= 19; y++) { c.Put(20, y, '|'); if (y % 3 == 1) c.Text(17, y, "<--+-->", Bone); }
            Face(c, 20, 7, 4);
            c.Text(16, 3, "\\       /"); c.Text(15, 2, "\\         /", Rune);
            c.Text(18, 21, "v v v", Rune);
        }

        static void Mantis(Canvas c)
        {
            int swing = c.Sway;
            // Raised forearms terminate in scythes rather than generic stick arms.
            c.Line(18, 10, 10, 8 - swing, '/', Skin); c.Line(10, 8 - swing, 7, 4 - swing, '|');
            c.Text(4, 2 - swing, "___"); c.Text(3, 3 - swing, "/   )"); c.Text(3, 4 - swing, "|  /"); c.Put(5, 5 - swing, 'v', Rune);
            c.Line(22, 10, 30, 8 + swing, '\\', Skin); c.Line(30, 8 + swing, 33, 4 + swing, '|');
            c.Text(32, 2 + swing, "___"); c.Text(32, 3 + swing, "(   \\"); c.Text(33, 4 + swing, "\\  |"); c.Put(35, 5 + swing, 'v', Rune);
            c.Oval(20, 13, 3 + (c.Specimen.Bulk > .65f ? 1 : 0), 5, Skin);
            c.Text(19, 10, "[+]"); c.Text(19, 12, "[=]"); c.Text(19, 14, "[=]"); c.Text(19, 16, "[v]");
            for (int side = -1; side <= 1; side += 2)
            {
                int gait = side * c.Sway;
                c.Line(20 + side * 2, 15, 20 + side * 9, 18, side < 0 ? '/' : '\\');
                c.Line(20 + side * 9, 18, 20 + side * 11 + gait, 23, side < 0 ? '\\' : '/');
                c.Text(19 + side * 11 + gait, 23, side < 0 ? "/__" : "__\\");
                if (c.Specimen.Limbs > 4) c.Line(20 + side * 2, 17, 20 + side * 6, 22 - gait, '|', Skin);
            }
            Face(c, 20, 7, 3);
            c.Line(18, 3, 14, 0, '\\', Rune); c.Line(22, 3, 26, 0, '/', Rune);
            c.Put(14, 0, '*', Eye); c.Put(26, 0, '*', Eye);
        }

        static void Wraith(Canvas c)
        {
            int sway = c.Sway;
            // Ragged hood and hollow robe; a distinct floating silhouette without grounded legs.
            c.Text(17 + sway, 2, ".-^^-."); c.Text(15 + sway, 3, ".'      '.");
            for (int y = 4; y < 19; y++)
            {
                int radius = y < 9 ? 5 + (y - 4) / 2 : 9 - (y - 9) / 3;
                int drift = (int)Math.Round(Math.Sin(y * .6 + c.Time * 2) * (y > 12 ? 2 : 1));
                c.Put(20 - radius + drift, y, '/', Bone); c.Put(20 + radius + drift, y, '\\', Bone);
                for (int x = -radius + 2; x < radius - 1; x++) if ((x + y) % 3 == 0)
                    c.Put(20 + x + drift, y, y % 2 == 0 ? ':' : '.', Shade);
            }
            Face(c, 20 + sway, 8, 3);
            c.Text(16, 12, ".::(*)::.", Rune);
            for (int i = 0; i < 5; i++)
            {
                int x = 13 + i * 3;
                int bend = (int)Math.Round(Math.Sin(c.Time * 3 + i) * 2);
                c.Line(x, 17, x + bend, 21 + i % 3, i % 2 == 0 ? '~' : '|', i % 2 == 0 ? Skin : Shade);
                c.Put(x + bend + 1, 22 + i % 2, '.', Shade);
            }
            c.Line(11, 11, 7, 13 + sway, '~', Skin); c.Text(4, 14 + sway, "{::}", Rune);
            c.Line(29, 11, 33, 13 - sway, '~', Skin); c.Text(33, 14 - sway, "{::}", Rune);
            c.Put(8, 5 + sway, '.', Shade); c.Put(32, 6 - sway, '*', Rune);
        }

        static void Crab(Canvas c)
        {
            int shift = c.Sway;
            int rx = 8 + (int)(Mathf.Clamp01(c.Specimen.Bulk) * 3);
            for (int i = 0; i < 4; i++)
            {
                int y = 14 + i;
                int gait = (c.Beat + i) % 4 < 2 ? 1 : 0;
                c.Line(20 - rx + 1, y, 6 - i, y + 1 + gait, '/', Bone);
                c.Line(6 - i, y + 1 + gait, 5 - i, y + 3, '|', Skin);
                c.Line(20 + rx - 1, y, 34 + i, y + 2 - gait, '\\', Bone);
                c.Line(34 + i, y + 2 - gait, 35 + i, y + 3, '|', Skin);
            }
            c.Line(12, 13, 7, 10 + shift, '\\'); c.Line(28, 13, 33, 10 - shift, '/');
            c.Text(2, 5 + shift, "__    __"); c.Text(1, 6 + shift, "/  \\  /  \\");
            c.Text(1, 7 + shift, "|   ><   |"); c.Text(2, 8 + shift, "\\__  __/"); c.Text(5, 9 + shift, "||");
            c.Text(31, 5 - shift, "__    __"); c.Text(30, 6 - shift, "/  \\  /  \\");
            c.Text(30, 7 - shift, "|   ><   |"); c.Text(31, 8 - shift, "\\__  __/"); c.Text(34, 9 - shift, "||");
            c.Oval(20, 15, rx, 5, Skin);
            c.Text(14, 14, "[==|==|==|==]", Bone); c.Text(15, 16, "\\_vvvvvvv_/", Rune);
            for (int i = 0; i < Math.Max(2, Math.Min(4, c.Specimen.Eyes)); i++)
            {
                int x = 16 + i * 3;
                c.Line(x, 11, x + (i % 2 == 0 ? -1 : 1), 8, '|');
                c.Text(x - 1 + (i % 2 == 0 ? -1 : 1), 7, c.Beat % 29 == 0 ? "(-)" : "(O)", Eye);
            }
            c.HeadX = 20; c.HeadY = 10;
            for (int i = 0; i < c.Specimen.Horns; i++) c.Put(14 + i * 4, 10, '^', Rune);
        }

        static void Serpent(Canvas c)
        {
            int wave = c.Sway;
            // A thick continuous S curve, tapering to a barbed tail.
            for (int t = 0; t <= 25; t++)
            {
                float u = t / 25f;
                int x = (int)Math.Round(9 + u * 18 + Math.Sin(u * Math.PI * 2) * 7);
                int y = (int)Math.Round(21 - u * 12 + Math.Sin(u * Math.PI * 3 + c.Time * 2) * .7);
                int radius = t < 5 ? 1 : 2 + (c.Specimen.Bulk > .6f ? 1 : 0);
                c.Oval(x, y, radius + 1, radius, Skin);
                c.Put(x, y, t % 3 == 0 ? '+' : '=', Rune);
            }
            c.Text(5, 21, "<~", Rune);
            c.Oval(28, 10, 4, 4, Skin);
            Face(c, 28 + wave, 6, 4);
            c.Text(29 + wave, 8, "\\/\\/", Rune);
            c.Line(24 + wave, 3, 21 + wave, 0, '\\'); c.Line(32 + wave, 3, 35 + wave, 0, '/');
            c.Text(32 + wave, 6, "--<", Eye);
            for (int i = 0; i < Math.Min(3, c.Specimen.Limbs / 2); i++)
            {
                int x = 14 + i * 4;
                c.Text(x, 17 + i % 2 + wave, "/|", Bone);
            }
            c.HeartX = 24; c.HeartY = 13;
        }

        static void Moth(Canvas c)
        {
            int flap = c.Sway;
            int spread = 14 + (int)(Mathf.Clamp01(c.Specimen.Bulk) * 2);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int y = 4; y <= 19; y++)
                {
                    float curve = y < 11 ? (y - 3) / 7f : (21 - y) / 10f;
                    int edge = 4 + (int)(spread * curve) + flap;
                    for (int d = 3; d <= edge; d++)
                    {
                        int x = 20 + d * side;
                        char glyph = d == edge ? (side < 0 ? '/' : '\\') : ((d + y) % 4 == 0 ? ':' : '.');
                        byte ink = d == edge ? Bone : Shade;
                        if ((d + y * 2) % 7 == 0) { glyph = '*'; ink = Skin; }
                        c.Put(x, y, glyph, ink);
                    }
                    if (y % 4 == 0) c.Line(20 + 3 * side, 12, 20 + edge * side, y, side < 0 ? '/' : '\\', Skin);
                }
                int eyeX = 20 + side * (10 + flap);
                c.Text(eyeX - 2, 9, ".-O-.", Rune); c.Text(eyeX - 2, 10, "( @ )", Eye); c.Text(eyeX - 2, 11, "`---'", Rune);
                c.Line(20 + side, 3, 20 + side * 5, 0, side < 0 ? '\\' : '/', Bone);
                c.Put(20 + side * 5, 0, '*', Eye);
                c.Line(20 + side * 2, 16, 20 + side * 5, 22 - flap, '|', Skin);
                c.Put(20 + side * 5, 23 - flap, 'v', Bone);
            }
            c.Oval(20, 14, 2, 7, Skin);
            for (int y = 11; y < 20; y += 2) c.Text(19, y, "[=]", Bone);
            Face(c, 20, 7, 2);
            c.Text(19, 20, "\\V/", Rune);
        }

        static void Mutate(Canvas c)
        {
            switch (c.Specimen.Mutation)
            {
                case MutationKind.Crown:
                    int crownY = Math.Max(0, c.HeadY - 6);
                    c.Text(c.HeadX - 5, crownY, "*  ^ ^ ^  *", Rune);
                    c.Text(c.HeadX - 3, crownY + 1, "\\_|_|_/", Eye);
                    break;
                case MutationKind.Twin:
                    int x = c.HeadX > 24 ? c.HeadX - 10 : c.HeadX + 9;
                    int y = Math.Min(12, c.HeadY + 2);
                    c.Line(c.HeadX + (x > c.HeadX ? 3 : -3), y + 3, x, y + 1, '~', Rune);
                    c.Text(x - 2, y - 2, "/^^^\\", Rune); c.Text(x - 2, y - 1, "[o.o]", Eye);
                    c.Text(x - 2, y, "\\v_v/", Rune);
                    break;
                case MutationKind.Hollow:
                    for (int yy = -2; yy <= 2; yy++) for (int xx = -3; xx <= 3; xx++) c.Put(c.HeartX + xx, c.HeartY + yy, ' ');
                    c.Text(c.HeartX - 3, c.HeartY - 2, ".-----.", Rune);
                    c.Text(c.HeartX - 3, c.HeartY - 1, "|     |", Rune);
                    c.Text(c.HeartX - 3, c.HeartY, "|  .  |", Shade);
                    c.Text(c.HeartX - 3, c.HeartY + 1, "|     |", Rune);
                    c.Text(c.HeartX - 3, c.HeartY + 2, "`-----'", Rune);
                    break;
                case MutationKind.Ouroboros:
                    for (int i = 0; i < 29; i++)
                    {
                        float a = i / 28f * Mathf.PI * 1.8f + c.Time * .16f;
                        int px = 20 + (int)Math.Round(Math.Cos(a) * 18);
                        int py = 13 + (int)Math.Round(Math.Sin(a) * 10);
                        if (c.Glyph[Math.Max(0, Math.Min(Width - 1, px)), Math.Max(0, Math.Min(Height - 1, py))] == ' ')
                            c.Put(px, py, i % 3 == 0 ? '+' : ':', Rune);
                    }
                    c.Text(2, 14, ">o<", Eye);
                    break;
            }
            // High entropy produces discontinuous sparks. Repetition produces aligned ornament.
            if (c.Specimen.Volatility > .72f)
            {
                int phase = c.Beat % 7;
                c.Put(4 + phase, 4 + phase % 3, ':', Rune);
                c.Put(35 - phase % 3, 18 - phase, '*', Shade);
            }
            if (c.Specimen.Source != null && c.Specimen.Source.Repetition > .45f)
            {
                c.Put(c.HeartX - 1, c.HeartY, '[', Rune); c.Put(c.HeartX + 1, c.HeartY, ']', Rune);
            }
        }

        static uint Hash(uint seed, int x, int y)
        {
            unchecked { uint h = seed ^ (uint)(x * 374761393) ^ (uint)(y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); }
        }
        static char Mirror(char c)
        {
            switch (c)
            {
                case '/': return '\\'; case '\\': return '/'; case '(': return ')'; case ')': return '(';
                case '[': return ']'; case ']': return '['; case '<': return '>'; case '>': return '<';
                case '{': return '}'; case '}': return '{'; default: return c;
            }
        }
        static string[] MakeSingleCharacters()
        {
            var result = new string[128];
            for (int i = 0; i < result.Length; i++) result[i] = ((char)i).ToString();
            return result;
        }
    }
}
