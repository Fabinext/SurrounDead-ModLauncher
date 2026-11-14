// SurrounDead Mod Launcher - main window (custom drawn, borderless).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SDModLauncher
{
    class ModView
    {
        public ModEntry Entry;
        public Manifest Manifest;
        public InstallRecord Record;
        public string Problem;
        public Image Icon, Banner;
        public Bitmap Placeholder;
        public List<Image> Shots = new List<Image>();

        public bool Installed { get { return Record != null; } }
        public bool HasUpdate { get { return Record != null && Manifest != null && Ver.Compare(Manifest.Version, Record.Version) > 0; } }
    }

    static class Images
    {
        const long Max = 2 * 1024 * 1024;

        public static Image Load(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                string p = Path.Combine(Paths.Sub(@"cache\images"), Hash.Sha1Text(url) + ".img");
                if (!File.Exists(p)) Net.Download(url, p, Max, null);
                byte[] b = File.ReadAllBytes(p);
                bool png = b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
                bool jpg = b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
                if (!png && !jpg) { File.Delete(p); return null; }
                using (var ms = new MemoryStream(b))
                using (Image img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch (Exception) { return null; }
        }
    }

    class LauncherForm : Form
    {
        static readonly Color Bg = Color.FromArgb(20, 20, 20), Panel = Color.FromArgb(17, 17, 17), Bar = Color.FromArgb(15, 15, 15),
            Line = Color.FromArgb(38, 38, 38), Txt = Color.FromArgb(216, 214, 207), Muted = Color.FromArgb(138, 136, 127),
            Dim = Color.FromArgb(85, 85, 85), Amber = Color.FromArgb(242, 169, 0), AmberLight = Color.FromArgb(255, 210, 94),
            AmberDark = Color.FromArgb(183, 127, 0), Green = Color.FromArgb(95, 184, 90), Red = Color.FromArgb(226, 86, 79),
            Sel = Color.FromArgb(31, 31, 31), Field = Color.FromArgb(30, 30, 30);

        readonly string catalogUrl, gameOverride;
        readonly Settings settings = Settings.Load();
        readonly List<ModView> mods = new List<ModView>();
        readonly List<KeyValuePair<Rectangle, Action>> hits = new List<KeyValuePair<Rectangle, Action>>();
        readonly Font uiFont = new Font("Segoe UI", 9.5f), uiBold = new Font("Segoe UI Semibold", 10f), smallFont = new Font("Segoe UI", 8.5f), linkFont = new Font("Segoe UI", 8.5f, FontStyle.Underline);
        readonly Timer anim = new Timer { Interval = 80 };
        Catalog catalog;
        GameInfo game;
        int sel;
        bool busy, loading = true, showSettings, needAdmin;
        float progress = -1;
        string status;
        DateTime removeArmedUntil;
        Point mouse;
        Image viewer;
        DateTime lastGameCheck;
        float S = 1f;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public LauncherForm(string catalogUrl, string gameOverride)
        {
            this.catalogUrl = catalogUrl;
            this.gameOverride = gameOverride;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            Text = "SurrounDead Mods";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            using (Graphics g = CreateGraphics()) S = g.DpiX / 96f;
            ClientSize = new Size(Sc(960), Sc(600));
            using (Bitmap ico = Pixel.SpriteBitmap(Pixel.Tower, 3, Amber, Color.FromArgb(20, 20, 20))) Icon = System.Drawing.Icon.FromHandle(ico.GetHicon());
            anim.Tick += (s, e) => { if (busy) Invalidate(); };
            anim.Start();
            KeyPreview = true;
            Shown += (s, e) => Reload(true);
        }

        int Sc(int v) { return (int)Math.Round(v * S); }
        int Px(int v) { return Math.Max(1, (int)Math.Round(v * S)); }
        ModView Current { get { return sel >= 0 && sel < mods.Count ? mods[sel] : null; } }

        // ------------------------------------------------------------------ background work

        void Ui(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch (Exception) { }
        }

        void Work(Action job, Action done)
        {
            if (busy) return;
            busy = true; status = null; needAdmin = false; progress = -1;
            Invalidate();
            Task.Factory.StartNew(() =>
            {
                string err = null; bool admin = false;
                try { job(); }
                catch (Exception ex) { err = Explain(ex, out admin); Log(ex); }
                Ui(() =>
                {
                    busy = false; progress = -1;
                    if (err != null) { status = err; needAdmin = admin; }
                    if (done != null) done();
                    Invalidate();
                });
            });
        }

        static string Explain(Exception ex, out bool admin)
        {
            admin = false;
            if (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            if (ex is InstallError) return ex.Message;
            if (ex is UnauthorizedAccessException) { admin = true; return "No write access to the game folder"; }
            if (ex is WebException) return "Download failed - check your connection";
            if (ex is IOException) return "A file is in use - close the game and try again";
            return "Something went wrong (see launcher.log)";
        }

        static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Paths.Data, "launcher.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); }
            catch (Exception) { }
        }

        void Progress(long done, long total)
        {
            if (total <= 0) return;
            float p = (float)done / total;
            if (Math.Abs(p - progress) < 0.02f && p < 1) return;
            progress = p;
            Ui(Invalidate);
        }

        void Reload(bool startup)
        {
            loading = true;
            Work(() =>
            {
                Net.AllowLocal = Net.AllowLocal || IsLocal(catalogUrl);
                GameInfo g = Steam.Find(string.IsNullOrEmpty(gameOverride) ? settings.GamePath : gameOverride);
                Catalog c = Catalog.Load(catalogUrl);
                var list = new List<ModView>();
                foreach (ModEntry e in c.Mods)
                {
                    var v = new ModView { Entry = e, Record = InstallRecord.Load(e.Id), Placeholder = Pixel.PlaceholderBanner(e.Id) };
                    try { v.Manifest = Manifest.Load(e); v.Problem = v.Manifest.Problem(g); }
                    catch (Exception ex) { Log(ex); v.Problem = "offline"; }
                    list.Add(v);
                }
                Ui(() =>
                {
                    game = g; catalog = c;
                    string selId = Current != null ? Current.Entry.Id : settings.ActiveMod;
                    mods.Clear(); mods.AddRange(list);
                    sel = Math.Max(0, mods.FindIndex(m => m.Entry.Id == selId));
                    loading = false;
                    Invalidate();
                });
                foreach (ModView v in list)
                {
                    ModView mv = v;
                    string icon = mv.Manifest != null && !string.IsNullOrEmpty(mv.Manifest.Icon) ? mv.Manifest.Icon : mv.Entry.Icon;
                    string banner = mv.Manifest != null && !string.IsNullOrEmpty(mv.Manifest.Banner) ? mv.Manifest.Banner : mv.Entry.Banner;
                    Image ii = Images.Load(icon), bb = Images.Load(banner);
                    Ui(() => { mv.Icon = ii; mv.Banner = bb; Invalidate(); });
                    if (mv.Manifest != null)
                        foreach (string su in mv.Manifest.Screenshots)
                        {
                            Image si = Images.Load(su);
                            if (si != null) Ui(() => { mv.Shots.Add(si); Invalidate(); });
                        }
                }
                if (startup && settings.AutoUpdate && g != null)
                    foreach (ModView v in list)
                        if (v.HasUpdate && v.Problem == null)
                        {
                            ModInstaller.Install(v.Entry, v.Manifest, g, settings.ActiveMod == v.Entry.Id, Progress);
                            v.Record = InstallRecord.Load(v.Entry.Id);
                        }
            }, () => { loading = false; });
        }

        static bool IsLocal(string url)
        {
            return url != null && (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || (url.Length > 2 && url[1] == ':'));
        }

        void InstallOrActivate(ModView v)
        {
            if (game == null || v.Manifest == null) return;
            Work(() =>
            {
                if (!v.Installed || v.HasUpdate)
                    ModInstaller.Install(v.Entry, v.Manifest, game, true, Progress);
                else
                {
                    if (Steam.IsRunning()) throw new InstallError("Close the game first");
                    ModInstaller.EnsureShared(v.Entry, v.Manifest, game, Progress);
                    ModInstaller.SetActive(v.Entry.Id, game.Root);
                }
                v.Record = InstallRecord.Load(v.Entry.Id);
                settings.ActiveMod = v.Entry.Id;
                settings.Save();
            }, null);
        }

        void Remove(ModView v)
        {
            if (DateTime.Now > removeArmedUntil) { removeArmedUntil = DateTime.Now.AddSeconds(3); Invalidate(); return; }
            removeArmedUntil = DateTime.MinValue;
            if (game == null) return;
            Work(() =>
            {
                ModInstaller.Uninstall(v.Entry.Id, game.Root);
                v.Record = null;
                if (settings.ActiveMod == v.Entry.Id) { settings.ActiveMod = null; settings.Save(); }
            }, null);
        }

        void PlayGame()
        {
            if (game == null) { showSettings = true; Invalidate(); return; }
            ModView active = mods.FirstOrDefault(m => m.Entry.Id == settings.ActiveMod);
            Work(() =>
            {
                if (active != null && settings.AutoUpdate && active.HasUpdate && active.Problem == null)
                {
                    ModInstaller.Install(active.Entry, active.Manifest, game, true, Progress);
                    active.Record = InstallRecord.Load(active.Entry.Id);
                }
                Steam.Launch();
            }, () => { if (status == null && settings.CloseOnLaunch) Close(); });
        }

        void PickFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Select the SurrounDead folder (the one with SurrounDead.exe)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(d.SelectedPath, "SurrounDead.exe"))) { status = "SurrounDead.exe not found in that folder"; Invalidate(); return; }
                settings.GamePath = d.SelectedPath;
                settings.Save();
                Reload(false);
            }
        }

        void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(a => "\"" + a + "\"")));
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ input

        void Hit(Rectangle r, Action a) { hits.Add(new KeyValuePair<Rectangle, Action>(r, a)); }

        Action HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Key.Contains(p)) return hits[i].Value;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = e.Location;
            Cursor = HitAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-1, -1); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Action a = HitAt(e.Location);
            if (a != null) { a(); Invalidate(); return; }
            if (e.Y < Sc(34)) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            hits.Clear();
            int W = ClientSize.Width, H = ClientSize.Height, titleH = Sc(34), bottomH = Sc(58);
            g.Clear(Bg);
            using (var p = new Pen(Line)) g.DrawRectangle(p, 0, 0, W - 1, H - 1);

            // title bar
            Fill(g, Bar, new Rectangle(1, 1, W - 2, titleH - 1));
            HLine(g, titleH, 0, W);
            int px2 = Px(2);
            Pixel.Sprite(g, Pixel.Tower, Sc(12), (titleH - 12 * px2) / 2, px2, Amber);
            int tx = Sc(12) + 13 * px2;
            Pixel.Text(g, "SURROUNDEAD", tx, (titleH - 7 * px2) / 2, px2, Amber);
            Pixel.Text(g, "MODS", tx + Pixel.TextWidth("SURROUNDEAD ", px2), (titleH - 7 * px2) / 2, px2, Muted);
            IconButton(g, Pixel.Close, W - Sc(30), titleH, () => Close());
            IconButton(g, Pixel.Minimize, W - Sc(58), titleH, () => { WindowState = FormWindowState.Minimized; });
            IconButton(g, Pixel.Gear, W - Sc(90), titleH, () => { showSettings = !showSettings; viewer = null; });
            IconButton(g, Pixel.Refresh, W - Sc(120), titleH, () => { if (!busy) { viewer = null; Reload(false); } });
            // Discord invite comes from catalog.json ("discord"), so it can change without a new launcher
            if (catalog != null && IsDiscord(catalog.Discord))
            {
                string invite = catalog.Discord;
                IconButton(g, Pixel.Discord, W - Sc(152), titleH, () => Open(invite), Color.FromArgb(88, 101, 242));
            }

            int top = titleH + 1;
            if (catalog != null && (!string.IsNullOrEmpty(catalog.Notice) || catalog.LauncherTooOld))
            {
                int nh = Sc(24);
                Fill(g, Color.FromArgb(32, 27, 14), new Rectangle(1, top, W - 2, nh));
                string n = catalog.LauncherTooOld ? "A new launcher version is required" : catalog.Notice;
                TextRenderer.DrawText(g, n, smallFont, new Rectangle(Sc(14), top, W - Sc(28), nh), Amber, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (catalog.LauncherTooOld && !string.IsNullOrEmpty(catalog.LauncherUrl))
                    Hit(new Rectangle(1, top, W - 2, nh), () => Open(catalog.LauncherUrl));
                top += nh;
            }
            int bottom = H - bottomH;

            if (showSettings) PaintSettings(g, top, bottom, W);
            else { PaintMain(g, top, bottom, W); PaintViewer(g, top, bottom, W); }
            PaintBottom(g, bottom, W, H);
        }

        void PaintMain(Graphics g, int top, int bottom, int W)
        {
            int leftW = Sc(210);
            Fill(g, Panel, new Rectangle(1, top, leftW, bottom - top));
            VLine(g, leftW + 1, top, bottom);

            if (loading && mods.Count == 0) Pixel.Text(g, "...", Sc(20), top + Sc(20), Px(2), Dim);
            else if (mods.Count == 0) Pixel.Text(g, "NO MODS", Sc(20), top + Sc(20), Px(2), Dim);

            int y = top + Sc(8), rowH = Sc(48);
            for (int i = 0; i < mods.Count; i++)
            {
                ModView v = mods[i];
                var r = new Rectangle(1, y, leftW, rowH);
                if (i == sel) { Fill(g, Sel, r); Fill(g, Amber, new Rectangle(1, y, Sc(3), rowH)); }
                else if (r.Contains(mouse)) Fill(g, Color.FromArgb(24, 24, 24), r);
                var ir = new Rectangle(Sc(14), y + Sc(8), Sc(32), Sc(32));
                Fill(g, Color.FromArgb(29, 29, 29), ir);
                if (v.Icon != null) Pixel.DrawImageCover(g, v.Icon, ir, v.Icon.Width <= 64);
                else Pixel.Sprite(g, Pixel.Tower, ir.X + (ir.Width - 12 * Px(2)) / 2, ir.Y + (ir.Height - 12 * Px(2)) / 2, Px(2), Dim);
                Color nameC = v.Problem != null ? Muted : Txt;
                TextRenderer.DrawText(g, v.Entry.Name, uiBold, new Rectangle(Sc(56), y, leftW - Sc(84), rowH), nameC, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Fill(g, DotColor(v), new Rectangle(leftW - Sc(22), y + (rowH - Sc(8)) / 2, Sc(8), Sc(8)));
                int idx = i;
                Hit(r, () => { sel = idx; removeArmedUntil = DateTime.MinValue; viewer = null; });
                y += rowH + Sc(2);
            }

            ModView cur = Current;
            var banner = new Rectangle(leftW + 2, top, W - leftW - 3, Sc(220));
            if (cur == null) return;
            if (cur.Banner != null) Pixel.DrawImageCover(g, cur.Banner, banner, false);
            else Pixel.DrawImageCover(g, cur.Placeholder, banner, true);
            using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillRectangle(shade, banner.X, banner.Bottom - Sc(64), banner.Width, Sc(64));
            int px4 = Px(4);
            string name = cur.Entry.Name.ToUpperInvariant();
            while (name.Length > 3 && Pixel.TextWidth(name, px4) > banner.Width - Sc(40)) px4 = Math.Max(Px(2), px4 - 1);
            Pixel.Text(g, name, banner.X + Sc(20), banner.Bottom - Sc(56), px4, Color.FromArgb(244, 242, 234));
            string ver = cur.Manifest == null ? "" : cur.HasUpdate ? "v" + cur.Record.Version + "  →  v" + cur.Manifest.Version : "v" + cur.Manifest.Version;
            if (!string.IsNullOrEmpty(cur.Entry.Author)) ver += (ver.Length > 0 ? "   ·   " : "") + cur.Entry.Author;
            TextRenderer.DrawText(g, ver, smallFont, new Point(banner.X + Sc(20), banner.Bottom - Sc(22)), Color.FromArgb(185, 183, 174), TextFormatFlags.NoPrefix);

            int ax = banner.X + Sc(20), ay = banner.Bottom + Sc(22);
            PaintDetails(g, cur, ax, ay + Sc(54), bottom, W);
            if (busy) return;
            if (cur.Problem != null)
            {
                int w = Chip(g, cur.Problem == "offline" ? "OFFLINE" : cur.Problem.StartsWith("needs a newer") ? "UPDATE LAUNCHER" : "WRONG GAME VERSION", ax, ay, Red);
                if (cur.Problem.StartsWith("needs")) TextRenderer.DrawText(g, cur.Problem, smallFont, new Rectangle(ax + w + Sc(14), ay, Sc(300), Sc(34)), Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (cur.Installed) TrashButton(g, cur, W, ay);
                return;
            }
            bool active = cur.Installed && settings.ActiveMod == cur.Entry.Id;
            if (!cur.Installed) Button(g, "INSTALL", Pixel.Down, ax, ay, true, () => InstallOrActivate(cur));
            else if (cur.HasUpdate) Button(g, "UPDATE", Pixel.Down, ax, ay, true, () => InstallOrActivate(cur));
            else if (!active) Button(g, "ACTIVATE", Pixel.Check, ax, ay, true, () => InstallOrActivate(cur));
            else Chip(g, "ACTIVE", ax, ay, Green);
            if (cur.Installed) TrashButton(g, cur, W, ay);
        }

        // Changelog lines and screenshot thumbnails below the action row (any mod state)
        void PaintDetails(Graphics g, ModView cur, int x, int y, int bottom, int W)
        {
            Manifest m = cur.Manifest;
            if (m == null) return;
            int right = W - Sc(24), tw = Sc(128), th = Sc(72);
            int shotsTop = bottom - Sc(16) - th;
            int maxY = cur.Shots.Count > 0 ? shotsTop - Sc(10) : bottom - Sc(10);
            int lineH = Sc(19), ly = y;
            if (m.Changelog.Count > 0)
            {
                Pixel.Text(g, "CHANGES", x, ly, Px(2), Dim);
                ly += Sc(22);
                foreach (string line in m.Changelog)
                {
                    if (ly + lineH > maxY) break;
                    Fill(g, Amber, new Rectangle(x, ly + (lineH - Sc(4)) / 2, Sc(4), Sc(4)));
                    TextRenderer.DrawText(g, line, smallFont, new Rectangle(x + Sc(14), ly, right - x - Sc(14), lineH), Txt,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    ly += lineH;
                }
                ly += Sc(4);
            }
            if (!string.IsNullOrEmpty(m.Notes) && ly + Sc(20) <= maxY)
            {
                var lr = new Rectangle(x, ly, Sc(110), Sc(20));
                TextRenderer.DrawText(g, m.Changelog.Count > 0 ? "Full changelog" : "Changelog", lr.Contains(mouse) ? linkFont : smallFont, lr, Muted, TextFormatFlags.NoPrefix | TextFormatFlags.Left);
                string url = m.Notes;
                Hit(lr, () => Open(url));
            }
            int tx = x;
            foreach (Image img in cur.Shots)
            {
                if (tx + tw > right) break;
                var r = new Rectangle(tx, shotsTop, tw, th);
                Fill(g, Color.FromArgb(29, 29, 29), r);
                Pixel.DrawImageCover(g, img, r, false);
                using (var p = new Pen(r.Contains(mouse) ? Amber : Line, Px(1))) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                Image im = img;
                Hit(r, () => { viewer = im; });
                tx += tw + Sc(8);
            }
        }

        // Full view of a screenshot over the main area; click or Esc closes it
        void PaintViewer(Graphics g, int top, int bottom, int W)
        {
            if (viewer == null) return;
            var area = new Rectangle(1, top, W - 2, bottom - top);
            using (var b = new SolidBrush(Color.FromArgb(240, 10, 10, 10))) g.FillRectangle(b, area);
            float s = Math.Min((float)(area.Width - Sc(40)) / viewer.Width, (float)(area.Height - Sc(40)) / viewer.Height);
            int iw = (int)(viewer.Width * s), ih = (int)(viewer.Height * s);
            var ir = new Rectangle(area.X + (area.Width - iw) / 2, area.Y + (area.Height - ih) / 2, iw, ih);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(viewer, ir);
            g.InterpolationMode = InterpolationMode.Default;
            Hit(area, () => { viewer = null; });
        }

        // Game updated while the launcher was open: re-read build/branch when the window gets focus again
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (busy || catalog == null || (DateTime.Now - lastGameCheck).TotalSeconds < 2) return;
            lastGameCheck = DateTime.Now;
            GameInfo gi = Steam.Find(string.IsNullOrEmpty(gameOverride) ? settings.GamePath : gameOverride);
            bool changed = (gi == null) != (game == null) || (gi != null && (gi.Build != game.Build || gi.Branch != game.Branch || gi.Root != game.Root));
            if (!changed) return;
            game = gi;
            foreach (ModView v in mods) if (v.Manifest != null) v.Problem = v.Manifest.Problem(gi);
            status = null;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && viewer != null) { viewer = null; Invalidate(); }
        }


        void PaintSettings(Graphics g, int top, int bottom, int W)
        {
            int x = Sc(30), y = top + Sc(24), w = W - Sc(60), rowH = Sc(46);
            string path = game != null ? game.Root : "Not found";
            SettingRow(g, "Game folder", x, y, w, rowH);
            var pr = new Rectangle(x + Sc(180), y, w - Sc(230), rowH);
            TextRenderer.DrawText(g, path, smallFont, pr, game != null ? Muted : Red, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
            IconBox(g, Pixel.Folder, x + w - Sc(34), y + (rowH - Sc(28)) / 2, PickFolder);
            y += rowH;
            SettingRow(g, "Auto-update", x, y, w, rowH);
            Toggle(g, x + w - Sc(46), y + (rowH - Sc(18)) / 2, settings.AutoUpdate, () => { settings.AutoUpdate = !settings.AutoUpdate; settings.Save(); });
            y += rowH;
            SettingRow(g, "Close on launch", x, y, w, rowH);
            Toggle(g, x + w - Sc(46), y + (rowH - Sc(18)) / 2, settings.CloseOnLaunch, () => { settings.CloseOnLaunch = !settings.CloseOnLaunch; settings.Save(); });
            y += rowH + Sc(20);
            IconBox(g, Pixel.Back, x, y, () => { showSettings = false; });
            string v = "Launcher v" + Catalog.LauncherVersion;
            if (catalog != null && catalog.LauncherUpdate) v += "  ·  update available";
            var vr = new Rectangle(x + w - Sc(260), bottom - Sc(30), Sc(260), Sc(20));
            TextRenderer.DrawText(g, v, smallFont, vr, Dim, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            if (catalog != null && catalog.LauncherUpdate && !string.IsNullOrEmpty(catalog.LauncherUrl)) Hit(vr, () => Open(catalog.LauncherUrl));
        }

        void PaintBottom(Graphics g, int bottom, int W, int H)
        {
            Fill(g, Bar, new Rectangle(1, bottom, W - 2, H - bottom - 1));
            HLine(g, bottom, 0, W);
            int mid = bottom + (H - bottom) / 2;
            string info = game == null ? "Game not found" : "Build " + (game.Build ?? "?") + (game.Branch != "" ? "  ·  " + game.Branch : "");
            Color infoC = game == null ? Red : Dim;
            if (status != null) { info = status; infoC = Red; }
            var ir = new Rectangle(Sc(14), bottom, W / 2 - Sc(40), H - bottom);
            TextRenderer.DrawText(g, info, smallFont, ir, infoC, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int playW = Sc(150), playX = W - playW - Sc(14);
            if (needAdmin)
            {
                var ar = new Rectangle(playX - Sc(160), mid - Sc(16), Sc(146), Sc(32));
                OutlineButton(g, "RUN AS ADMIN", ar, RestartAsAdmin);
            }
            else if (busy)
            {
                int bx = W / 2 - Sc(20), bw = playX - Sc(24) - bx, seg = 20, gap = Px(2);
                int sw = (bw - gap * (seg - 1)) / seg;
                for (int i = 0; i < seg; i++)
                {
                    bool on = progress >= 0 ? i < progress * seg : ((Environment.TickCount / 90) % seg) == i;
                    Fill(g, on ? Amber : Color.FromArgb(32, 32, 32), new Rectangle(bx + i * (sw + gap), mid - Sc(4), sw, Sc(8)));
                }
            }
            var pr = new Rectangle(playX, mid - Sc(19), playW, Sc(38));
            AmberBox(g, pr, !busy);
            int px3 = Px(3);
            Size ps = Pixel.SpriteSize(Pixel.Play, px3);
            int tw = ps.Width + Sc(10) + Pixel.TextWidth("PLAY", px3);
            int sx = pr.X + (pr.Width - tw) / 2;
            Color ink = busy ? Color.FromArgb(90, 70, 20) : Color.FromArgb(26, 19, 0);
            Pixel.Sprite(g, Pixel.Play, sx, pr.Y + (pr.Height - ps.Height) / 2, px3, ink);
            Pixel.Text(g, "PLAY", sx + ps.Width + Sc(10), pr.Y + (pr.Height - 7 * px3) / 2, px3, ink);
            if (!busy) Hit(pr, PlayGame);
        }

        // ------------------------------------------------------------------ widgets

        Color DotColor(ModView v)
        {
            if (v.Problem != null) return v.Installed ? Red : Color.FromArgb(70, 40, 38);
            if (!v.Installed) return Dim;
            if (v.HasUpdate) return Amber;
            return settings.ActiveMod == v.Entry.Id ? Green : Muted;
        }

        void AmberBox(Graphics g, Rectangle r, bool enabled)
        {
            bool hover = enabled && r.Contains(mouse);
            Fill(g, enabled ? (hover ? AmberLight : Amber) : Color.FromArgb(120, 90, 20), r);
            int b = Px(2);
            using (var p = new Pen(enabled ? AmberLight : Color.FromArgb(140, 110, 40), b))
                g.DrawRectangle(p, r.X + b / 2, r.Y + b / 2, r.Width - b, r.Height - b);
            if (enabled)
            {
                Fill(g, AmberDark, new Rectangle(r.X + b, r.Bottom - b - Px(3), r.Width - 2 * b, Px(3)));
                Fill(g, AmberDark, new Rectangle(r.Right - b - Px(3), r.Y + b, Px(3), r.Height - 2 * b));
            }
        }

        void Button(Graphics g, string label, string[] icon, int x, int y, bool amber, Action a)
        {
            int px = Px(2);
            Size isz = Pixel.SpriteSize(icon, px);
            int w = Sc(28) + isz.Width + Sc(8) + Pixel.TextWidth(label, px), h = Sc(36);
            var r = new Rectangle(x, y, w, h);
            AmberBox(g, r, true);
            Color ink = Color.FromArgb(26, 19, 0);
            Pixel.Sprite(g, icon, x + Sc(14), y + (h - isz.Height) / 2, px, ink);
            Pixel.Text(g, label, x + Sc(14) + isz.Width + Sc(8), y + (h - 7 * px) / 2, px, ink);
            Hit(r, a);
        }

        int Chip(Graphics g, string label, int x, int y, Color c)
        {
            int px = Px(2), w = Sc(24) + Pixel.TextWidth(label, px), h = Sc(36);
            using (var p = new Pen(c, Px(2))) g.DrawRectangle(p, x + Px(1), y + Px(1), w - Px(2), h - Px(2));
            Pixel.Text(g, label, x + Sc(12), y + (h - 7 * px) / 2, px, c);
            return w;
        }

        void OutlineButton(Graphics g, string label, Rectangle r, Action a)
        {
            bool hover = r.Contains(mouse);
            using (var p = new Pen(hover ? Amber : Dim, Px(2))) g.DrawRectangle(p, r.X + Px(1), r.Y + Px(1), r.Width - Px(2), r.Height - Px(2));
            int px = Px(2);
            Pixel.Text(g, label, r.X + (r.Width - Pixel.TextWidth(label, px)) / 2, r.Y + (r.Height - 7 * px) / 2, px, hover ? Amber : Txt);
            Hit(r, a);
        }

        void TrashButton(Graphics g, ModView v, int W, int y)
        {
            bool armed = DateTime.Now < removeArmedUntil;
            int h = Sc(36);
            if (armed)
            {
                int px = Px(2), w = Sc(24) + Pixel.TextWidth("REMOVE?", px);
                var r = new Rectangle(W - Sc(24) - w, y, w, h);
                Chip(g, "REMOVE?", r.X, r.Y, Red);
                Hit(r, () => Remove(v));
            }
            else
            {
                var r = new Rectangle(W - Sc(24) - h, y, h, h);
                int px = Px(2);
                Size s = Pixel.SpriteSize(Pixel.Trash, px);
                Pixel.Sprite(g, Pixel.Trash, r.X + (h - s.Width) / 2, r.Y + (h - s.Height) / 2, px, r.Contains(mouse) ? Red : Dim);
                Hit(r, () => Remove(v));
            }
        }

        static bool IsDiscord(string url)
        {
            return url != null && (url.StartsWith("https://discord.gg/", StringComparison.OrdinalIgnoreCase) ||
                                   url.StartsWith("https://discord.com/invite/", StringComparison.OrdinalIgnoreCase));
        }

        void IconButton(Graphics g, string[] icon, int cx, int titleH, Action a)
        {
            IconButton(g, icon, cx, titleH, a, Txt);
        }

        void IconButton(Graphics g, string[] icon, int cx, int titleH, Action a, Color hoverColor)
        {
            int px = Px(2);
            Size s = Pixel.SpriteSize(icon, px);
            var r = new Rectangle(cx - Sc(13), 1, Sc(26), titleH - 2);
            bool hover = r.Contains(mouse);
            if (hover) Fill(g, Color.FromArgb(32, 32, 32), r);
            Pixel.Sprite(g, icon, r.X + (r.Width - s.Width) / 2, (titleH - s.Height) / 2, px, hover ? hoverColor : Muted);
            Hit(r, a);
        }

        void IconBox(Graphics g, string[] icon, int x, int y, Action a)
        {
            var r = new Rectangle(x, y, Sc(34), Sc(28));
            bool hover = r.Contains(mouse);
            using (var p = new Pen(hover ? Amber : Dim, Px(2))) g.DrawRectangle(p, r.X + Px(1), r.Y + Px(1), r.Width - Px(2), r.Height - Px(2));
            int px = Px(2);
            Size s = Pixel.SpriteSize(icon, px);
            Pixel.Sprite(g, icon, r.X + (r.Width - s.Width) / 2, r.Y + (r.Height - s.Height) / 2, px, hover ? Amber : Txt);
            Hit(r, a);
        }

        void SettingRow(Graphics g, string label, int x, int y, int w, int h)
        {
            TextRenderer.DrawText(g, label, uiFont, new Rectangle(x, y, Sc(180), h), Txt, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            HLine(g, y + h - 1, x, x + w);
        }

        void Toggle(Graphics g, int x, int y, bool on, Action a)
        {
            var r = new Rectangle(x, y, Sc(40), Sc(18));
            using (var p = new Pen(on ? Amber : Dim, Px(2))) g.DrawRectangle(p, r.X + Px(1), r.Y + Px(1), r.Width - Px(2), r.Height - Px(2));
            int k = Sc(10);
            Fill(g, on ? Amber : Color.FromArgb(110, 110, 110), new Rectangle(on ? r.Right - k - Sc(4) : r.X + Sc(4), r.Y + (r.Height - k) / 2, k, k));
            Hit(r, a);
        }

        static void Fill(Graphics g, Color c, Rectangle r) { using (var b = new SolidBrush(c)) g.FillRectangle(b, r); }
        static void HLine(Graphics g, int y, int x0, int x1) { using (var p = new Pen(Line)) g.DrawLine(p, x0, y, x1, y); }
        static void VLine(Graphics g, int x, int y0, int y1) { using (var p = new Pen(Line)) g.DrawLine(p, x, y0, x, y1); }

        static void Open(string url)
        {
            if (url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                try { Process.Start(url); } catch (Exception) { }
        }
    }
}
