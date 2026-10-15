using UnityEngine;

namespace HolyHelicopter
{
    // Original, deliberately chunky 32 x 40 pixel monster animation cells.
    internal static class MonsterAtlas
    {
        const int W = 32, H = 40;
        static readonly Color32 Bone = new Color32(222, 226, 180, 255);
        static readonly Color32 BoneShadow = new Color32(123, 155, 153, 255);
        static readonly Color32 Ink = new Color32(23, 29, 49, 255);
        static readonly Color32 Eyes = new Color32(255, 202, 67, 255);

        public static Texture2D Create()
        {
            var atlas = new Texture2D(W * 5, H * 4, TextureFormat.RGBA32, false)
            {
                name = "Original pixel monster atlas", filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp, anisoLevel = 0
            };
            var pixels = new Color32[W * 5 * H * 4];
            for (int kind = 0; kind < 5; kind++)
            for (int frame = 0; frame < 4; frame++)
            {
                var cell = new Color32[W * H];
                int step = frame == 0 ? -2 : frame == 2 ? 2 : 0;
                int bob = frame % 2;
                switch ((EnemyKind)kind)
                {
                    case EnemyKind.Skeleton: Skeleton(cell, step, bob); break;
                    case EnemyKind.Zombie: Zombie(cell, step, bob); break;
                    case EnemyKind.Vampire: Vampire(cell, step, bob); break;
                    case EnemyKind.Bat: Bat(cell, frame); break;
                    case EnemyKind.Necromancer: Necromancer(cell, step, bob); break;
                }
                // A one-pixel cool dark outline keeps crowds legible against the grass.
                var outline = (Color32[])cell.Clone();
                for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int p = y * W + x;
                    if (cell[p].a != 0) continue;
                    if (cell[p - 1].a != 0 || cell[p + 1].a != 0 || cell[p - W].a != 0 || cell[p + W].a != 0)
                        outline[p] = Ink;
                }
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    pixels[(frame * H + y) * W * 5 + kind * W + x] = outline[y * W + x];
            }
            atlas.SetPixels32(pixels);
            atlas.Apply(false, true);
            return atlas;
        }

        static void Skeleton(Color32[] p, int step, int bob)
        {
            Rect(p, 12, 25 + bob, 9, 10, Bone);
            Rect(p, 10, 28 + bob, 13, 5, Bone);
            Rect(p, 12, 30 + bob, 3, 3, Ink); Rect(p, 18, 30 + bob, 3, 3, Ink);
            Dot(p, 13, 31 + bob, Eyes); Dot(p, 19, 31 + bob, Eyes);
            Rect(p, 15, 27 + bob, 2, 2, BoneShadow);
            for (int x = 12; x <= 20; x += 2) Dot(p, x, 25 + bob, Ink);
            Rect(p, 15, 13 + bob, 3, 12, BoneShadow);
            for (int y = 17; y <= 23; y += 3) Rect(p, 10, y + bob, 13, 2, Bone);
            Rect(p, 10, 13 + bob, 13, 3, Bone);
            Line(p, 10, 23 + bob, 6 - bob, 15 + step, Bone, 2);
            Line(p, 6 - bob, 15 + step, 9, 12 + step, BoneShadow, 2);
            Line(p, 22, 23 + bob, 25 + bob, 16 - step, Bone, 2);
            Line(p, 25 + bob, 16 - step, 22, 12 - step, BoneShadow, 2);
            Line(p, 12, 13 + bob, 11 - step, 8, Bone, 2);
            Line(p, 11 - step, 8, 10 + step, 2 + bob, BoneShadow, 2);
            Line(p, 20, 13 + bob, 20 + step, 8, Bone, 2);
            Line(p, 20 + step, 8, 21 - step, 2 - bob, Bone, 2);
            Rect(p, 8 + step, 1 + bob, 5, 2, Bone); Rect(p, 20 - step, 1, 5, 2, Bone);
        }

        static void Zombie(Color32[] p, int step, int bob)
        {
            Color32 skin = new Color32(128, 183, 94, 255), rot = new Color32(68, 110, 77, 255);
            Color32 shirt = new Color32(103, 91, 132, 255), pants = new Color32(52, 66, 80, 255);
            Rect(p, 11, 25 + bob, 11, 11, skin); Rect(p, 10, 33 + bob, 12, 4, rot);
            Rect(p, 12, 30 + bob, 3, 2, Ink); Rect(p, 18, 29 + bob, 3, 2, Eyes);
            Rect(p, 15, 25 + bob, 6, 2, Ink); Dot(p, 19, 26 + bob, Bone);
            Rect(p, 10, 12 + bob, 13, 14, shirt); Rect(p, 15, 16 + bob, 3, 5, rot);
            Line(p, 10, 24 + bob, 5, 21 - step, shirt, 4);
            Line(p, 5, 21 - step, 3, 25 - step, skin, 3);
            Line(p, 22, 24 + bob, 27, 20 + step, shirt, 4);
            Line(p, 27, 20 + step, 29, 24 + step, skin, 3);
            Line(p, 13, 13, 12 + step, 3, pants, 4); Line(p, 20, 13, 21 - step, 3, pants, 4);
            Rect(p, 8 + step, 1, 6, 3, rot); Rect(p, 20 - step, 1, 6, 3, rot);
        }

        static void Vampire(Color32[] p, int step, int bob)
        {
            Color32 cape = new Color32(100, 53, 114, 255), red = new Color32(210, 66, 98, 255);
            Color32 pale = new Color32(190, 209, 230, 255);
            for (int y = 5; y < 27 + bob; y++)
            {
                int spread = (27 - y) / 3;
                Rect(p, 11 - spread, y, 12 + spread * 2, 1, cape);
            }
            Rect(p, 13, 25 + bob, 8, 10, pale); Rect(p, 12, 33 + bob, 10, 4, Ink);
            Rect(p, 15, 31 + bob, 3, 4, Ink); Rect(p, 13, 29 + bob, 2, 2, red); Rect(p, 19, 29 + bob, 2, 2, red);
            Dot(p, 14, 25 + bob, Bone); Dot(p, 19, 25 + bob, Bone);
            Rect(p, 14, 11, 6, 14, red); Rect(p, 16, 17, 2, 7, Bone);
            Line(p, 10, 27, 6, 31, cape, 3); Line(p, 22, 27, 27, 31, cape, 3);
            Line(p, 14, 11, 12 + step, 2, Ink, 3); Line(p, 20, 11, 22 - step, 2, Ink, 3);
        }

        static void Bat(Color32[] p, int frame)
        {
            Color32 wing = new Color32(144, 100, 179, 255), membrane = new Color32(78, 62, 120, 255);
            int lift = frame == 0 ? 10 : frame == 2 ? -5 : 3;
            for (int x = 2; x < 30; x++)
            {
                int outward = Mathf.Abs(16 - x);
                int top = 20 + lift * outward / 14;
                int bottom = 16 - outward / 3 + ((x % 5 == 0) ? 3 : 0);
                if (top < bottom) { int swap = top; top = bottom; bottom = swap; }
                Rect(p, x, bottom, 1, top - bottom + 1, membrane);
                Dot(p, x, top, wing);
            }
            Rect(p, 12, 14, 8, 12, wing); Rect(p, 12, 25, 2, 5, wing); Rect(p, 18, 25, 2, 5, wing);
            Rect(p, 13, 22, 2, 2, Eyes); Rect(p, 17, 22, 2, 2, Eyes);
            Rect(p, 14, 18, 4, 2, Ink); Dot(p, 14, 19, Bone); Dot(p, 17, 19, Bone);
        }

        static void Necromancer(Color32[] p, int step, int bob)
        {
            Color32 robe = new Color32(61, 133, 141, 255), trim = new Color32(154, 236, 164, 255);
            for (int y = 2; y < 28; y++)
            {
                int width = 8 + (28 - y) / 3;
                Rect(p, 16 - width / 2, y, width, 1, robe);
            }
            Line(p, 16, 26, 16 + step, 3, trim, 2);
            Rect(p, 11, 25 + bob, 12, 11, robe); Rect(p, 13, 27 + bob, 8, 8, Ink);
            Rect(p, 13, 31 + bob, 3, 2, trim); Rect(p, 18, 31 + bob, 3, 2, trim);
            Rect(p, 15, 27 + bob, 4, 2, Bone);
            Line(p, 25, 3, 25, 32, new Color32(156, 124, 95, 255), 2);
            Rect(p, 22, 31, 7, 6, Bone); Rect(p, 23, 33, 2, 2, Ink); Dot(p, 27, 34, Ink);
            Rect(p, 24, 37, 3, 2, trim); Line(p, 21, 22, 26, 20, trim, 2);
        }

        static void Dot(Color32[] p, int x, int y, Color32 color)
        {
            if (x >= 0 && x < W && y >= 0 && y < H) p[y * W + x] = color;
        }
        static void Rect(Color32[] p, int x, int y, int width, int height, Color32 color)
        {
            for (int yy = y; yy < y + height; yy++)
            for (int xx = x; xx < x + width; xx++) Dot(p, xx, yy, color);
        }
        static void Line(Color32[] p, int x0, int y0, int x1, int y1, Color32 color, int thickness)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int s = 0; s <= steps; s++)
            {
                float t = steps == 0 ? 0 : (float)s / steps;
                Rect(p, Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), thickness, thickness, color);
            }
        }
    }
}
