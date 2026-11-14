// Pixel art helpers: 5x7 font, small sprites, placeholder art. Everything is drawn in code (no assets).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SDModLauncher
{
    static class Pixel
    {
        static readonly Dictionary<char, byte[]> Font = new Dictionary<char, byte[]>
        {
            {'A',new byte[]{0x0E,0x11,0x11,0x1F,0x11,0x11,0x11}}, {'B',new byte[]{0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E}},
            {'C',new byte[]{0x0E,0x11,0x10,0x10,0x10,0x11,0x0E}}, {'D',new byte[]{0x1E,0x11,0x11,0x11,0x11,0x11,0x1E}},
            {'E',new byte[]{0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F}}, {'F',new byte[]{0x1F,0x10,0x10,0x1E,0x10,0x10,0x10}},
            {'G',new byte[]{0x0E,0x11,0x10,0x17,0x11,0x11,0x0F}}, {'H',new byte[]{0x11,0x11,0x11,0x1F,0x11,0x11,0x11}},
            {'I',new byte[]{0x0E,0x04,0x04,0x04,0x04,0x04,0x0E}}, {'J',new byte[]{0x07,0x02,0x02,0x02,0x02,0x12,0x0C}},
            {'K',new byte[]{0x11,0x12,0x14,0x18,0x14,0x12,0x11}}, {'L',new byte[]{0x10,0x10,0x10,0x10,0x10,0x10,0x1F}},
            {'M',new byte[]{0x11,0x1B,0x15,0x15,0x11,0x11,0x11}}, {'N',new byte[]{0x11,0x11,0x19,0x15,0x13,0x11,0x11}},
            {'O',new byte[]{0x0E,0x11,0x11,0x11,0x11,0x11,0x0E}}, {'P',new byte[]{0x1E,0x11,0x11,0x1E,0x10,0x10,0x10}},
            {'Q',new byte[]{0x0E,0x11,0x11,0x11,0x15,0x12,0x0D}}, {'R',new byte[]{0x1E,0x11,0x11,0x1E,0x14,0x12,0x11}},
            {'S',new byte[]{0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E}}, {'T',new byte[]{0x1F,0x04,0x04,0x04,0x04,0x04,0x04}},
            {'U',new byte[]{0x11,0x11,0x11,0x11,0x11,0x11,0x0E}}, {'V',new byte[]{0x11,0x11,0x11,0x11,0x11,0x0A,0x04}},
            {'W',new byte[]{0x11,0x11,0x11,0x15,0x15,0x15,0x0A}}, {'X',new byte[]{0x11,0x11,0x0A,0x04,0x0A,0x11,0x11}},
            {'Y',new byte[]{0x11,0x11,0x0A,0x04,0x04,0x04,0x04}}, {'Z',new byte[]{0x1F,0x01,0x02,0x04,0x08,0x10,0x1F}},
            {'0',new byte[]{0x0E,0x11,0x13,0x15,0x19,0x11,0x0E}}, {'1',new byte[]{0x04,0x0C,0x04,0x04,0x04,0x04,0x0E}},
            {'2',new byte[]{0x0E,0x11,0x01,0x02,0x04,0x08,0x1F}}, {'3',new byte[]{0x1F,0x02,0x04,0x02,0x01,0x11,0x0E}},
            {'4',new byte[]{0x02,0x06,0x0A,0x12,0x1F,0x02,0x02}}, {'5',new byte[]{0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E}},
            {'6',new byte[]{0x06,0x08,0x10,0x1E,0x11,0x11,0x0E}}, {'7',new byte[]{0x1F,0x01,0x02,0x04,0x08,0x08,0x08}},
            {'8',new byte[]{0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E}}, {'9',new byte[]{0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C}},
            {'.',new byte[]{0,0,0,0,0,0x0C,0x0C}}, {'-',new byte[]{0,0,0,0x1F,0,0,0}}, {'?',new byte[]{0x0E,0x11,0x01,0x02,0x04,0,0x04}},
            {'>',new byte[]{0x08,0x04,0x02,0x01,0x02,0x04,0x08}}, {'/',new byte[]{0x01,0x01,0x02,0x04,0x08,0x10,0x10}},
            {':',new byte[]{0,0x0C,0x0C,0,0x0C,0x0C,0}}, {'!',new byte[]{0x04,0x04,0x04,0x04,0x04,0,0x04}},
        };

        public static int TextWidth(string s, int px)
        {
            return s.Length == 0 ? 0 : (s.Length * 6 - 1) * px;
        }

        public static int TextHeight(int px) { return 7 * px; }

        public static void Text(Graphics g, string s, int x, int y, int px, Color c)
        {
            using (var b = new SolidBrush(c))
            {
                foreach (char ch0 in s.ToUpperInvariant())
                {
                    byte[] rows;
                    if (Font.TryGetValue(ch0, out rows))
                        for (int r = 0; r < 7; r++)
                            for (int col = 0; col < 5; col++)
                                if ((rows[r] & (0x10 >> col)) != 0) g.FillRectangle(b, x + col * px, y + r * px, px, px);
                    x += 6 * px;
                }
            }
        }

        public static readonly string[] Tower = {
            "....##......", "...#..#.....", "....##......", "....##......", "...#..#.....", "...#..#.....",
            "..######....", "..#....#....", ".#......#...", ".########...", "#........#..", "##########.." };
        public static readonly string[] Gear = { "....#....", ".#.###.#.", "..#####..", ".##...##.", "###...###", ".##...##.", "..#####..", ".#.###.#.", "....#...." };
        public static readonly string[] Close = { "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#" };
        public static readonly string[] Minimize = { ".......", ".......", ".......", ".......", ".......", ".......", "#######" };
        public static readonly string[] Play = { "#.....", "###...", "#####.", "######", "#####.", "###...", "#....." };
        public static readonly string[] Down = { "...#...", "...#...", "...#...", "#######", ".#####.", "..###..", "...#..." };
        public static readonly string[] Check = { "......#", ".....##", "#...##.", "##.##..", ".###...", "..#...." };
        public static readonly string[] Trash = { "..###..", "#######", ".#####.", ".#.#.#.", ".#.#.#.", ".#.#.#.", ".#####." };
        public static readonly string[] Folder = { "###.....", "########", "#......#", "#......#", "#......#", "########" };
        public static readonly string[] Refresh = { "..####..", ".#....#.", "#....###", "#.....#.", "#.......", ".#....#.", "..####.." };
        public static readonly string[] Back = { "...#...", "..##...", ".######", "#######", ".######", "..##...", "...#..." };

        public static void Sprite(Graphics g, string[] rows, int x, int y, int px, Color c)
        {
            using (var b = new SolidBrush(c))
                for (int r = 0; r < rows.Length; r++)
                    for (int col = 0; col < rows[r].Length; col++)
                        if (rows[r][col] == '#') g.FillRectangle(b, x + col * px, y + r * px, px, px);
        }

        public static Size SpriteSize(string[] rows, int px) { return new Size(rows[0].Length * px, rows.Length * px); }

        public static Bitmap SpriteBitmap(string[] rows, int px, Color c, Color back)
        {
            var bmp = new Bitmap(rows[0].Length * px, rows.Length * px);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(back);
                Sprite(g, rows, 0, 0, px, c);
            }
            return bmp;
        }

        // Low-res placeholder banner: forest, ruins and a radio mast, tinted per mod
        public static Bitmap PlaceholderBanner(string seed)
        {
            int h = 0;
            foreach (char ch in seed ?? "") h = h * 31 + ch;
            bool warm = (h & 1) == 0;
            Color sky = warm ? Color.FromArgb(33, 26, 20) : Color.FromArgb(22, 32, 42);
            Color far = warm ? Color.FromArgb(42, 33, 25) : Color.FromArgb(28, 42, 54);
            Color near = warm ? Color.FromArgb(58, 44, 30) : Color.FromArgb(36, 56, 74);
            var bmp = new Bitmap(160, 50);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var bf = new SolidBrush(far))
            using (var bn = new SolidBrush(near))
            {
                g.Clear(sky);
                for (int i = 0; i < 160; i += 3) { int t = 10 + Math.Abs((i * 29 + h) % 14); g.FillRectangle(bf, i, 50 - t, 3, t); }
                int rx = 20 + Math.Abs(h % 90);
                g.FillRectangle(bf, rx, 18, 14, 32); g.FillRectangle(bf, rx + 16, 24, 10, 26);
                g.FillRectangle(bn, 132, 6, 1, 34); g.FillRectangle(bn, 129, 14, 7, 1); g.FillRectangle(bn, 128, 22, 9, 1);
                for (int i = 0; i < 160; i += 5) { int t = 4 + Math.Abs((i * 13 + h) % 7); g.FillRectangle(bn, i, 50 - t, 4, t); }
            }
            return bmp;
        }

        public static void DrawImageCover(Graphics g, Image img, Rectangle dst, bool pixelArt)
        {
            float sx = (float)dst.Width / img.Width, sy = (float)dst.Height / img.Height;
            float s = Math.Max(sx, sy);
            float w = dst.Width / s, h = dst.Height / s;
            var src = new RectangleF((img.Width - w) / 2f, (img.Height - h) / 2f, w, h);
            g.InterpolationMode = pixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, dst, src, GraphicsUnit.Pixel);
            g.InterpolationMode = InterpolationMode.Default;
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }
    }
}
