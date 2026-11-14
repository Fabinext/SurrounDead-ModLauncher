// SurrounDead Mod Launcher - entry point.
// Test-only arguments: --catalog <url or local file>  --game <folder with SurrounDead.exe>
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SDModLauncher
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            string catalog = Catalog.DefaultUrl, game = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--catalog") catalog = args[i + 1];
                else if (args[i] == "--game") game = args[i + 1];
            }
            bool first;
            using (var mutex = new Mutex(true, "SDModLauncher.SingleInstance", out first))
            {
                if (!first) return 0;
                try { SetProcessDPIAware(); } catch (Exception) { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new LauncherForm(catalog, game));
            }
            return 0;
        }
    }
}
