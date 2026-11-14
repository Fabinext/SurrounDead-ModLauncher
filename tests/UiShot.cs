// Renders the launcher window off-screen into a PNG (UI check without screen capture).
// UiShot.exe <catalog> <gameFolder> <out.png> [settings]
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SDModLauncher
{
    static class UiShot
    {
        [STAThread]
        static int Main(string[] a)
        {
            var f = new LauncherForm(a[0], a[1]);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-6000, -6000);
            f.ShowInTaskbar = false;
            f.Show();
            if (a.Length > 3 && a[3] == "settings")
                typeof(LauncherForm).GetField("showSettings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(f, true);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 4000) { Application.DoEvents(); Thread.Sleep(20); }
            if (a.Length > 3 && a[3] == "sel1")
                typeof(LauncherForm).GetField("sel", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(f, 1);
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(a[2], System.Drawing.Imaging.ImageFormat.Png);
            }
            f.Close();
            return 0;
        }
    }
}
