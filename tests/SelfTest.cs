// Console self tests for the launcher core (no UI, no network, no real game folder).
// Run: tests\run_tests.cmd
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SDModLauncher
{
    static class SelfTest
    {
        static int fails, passes;

        static void Check(bool ok, string what)
        {
            if (ok) passes++; else { fails++; Console.WriteLine("FAIL: " + what); }
        }

        static void Throws(Action a, string contains, string what)
        {
            try { a(); Check(false, what + " (no exception)"); }
            catch (Exception ex) { Check(ex.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0, what + " (got: " + ex.Message + ")"); }
        }

        static string MakeZip(string path, Dictionary<string, string> files)
        {
            if (File.Exists(path)) File.Delete(path);
            using (ZipArchive z = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var kv in files)
                    using (var w = new StreamWriter(z.CreateEntry(kv.Key).Open())) w.Write(kv.Value);
            return path;
        }

        static string ManifestJson(string ver, string zip, string sha, string build, string branch, string extraInstall)
        {
            return "{\"schema\":1,\"name\":\"T\",\"version\":\"" + ver + "\",\"game\":{\"steam_app_id\":1645820,\"build\":\"" + build +
                   "\",\"branch\":\"" + branch + "\",\"requires_exact_build\":true},\"download\":{\"url\":\"" + zip.Replace("\\", "\\\\") +
                   "\",\"sha256\":\"" + sha + "\"},\"install\":{\"enabled_by\":\"SurrounDead/Binaries/Win64/ue4ss/Mods/T/enabled.txt\"," +
                   "\"keep_if_present\":[\"SurrounDead/Binaries/Win64/ue4ss/UE4SS.dll\",\"SurrounDead/Binaries/Win64/ue4ss/UE4SS-settings.ini\"]," +
                   "\"off_by_default\":[\"SurrounDead/Binaries/Win64/ue4ss/Mods/Extra/\"]" + extraInstall + "}}";
        }

        static int Main()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "sdml_selftest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            Paths.Data = Path.Combine(tmp, "data");
            Net.AllowLocal = true;
            try { Run(tmp); }
            catch (Exception ex) { fails++; Console.WriteLine("CRASH: " + ex); }
            try { Directory.Delete(tmp, true); } catch (Exception) { }
            Console.WriteLine(fails == 0 ? "ALL " + passes + " CHECKS PASSED" : fails + " FAILED, " + passes + " passed");
            return fails == 0 ? 0 : 1;
        }

        static void Run(string tmp)
        {
            // --- Vdf / Steam manifest
            string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"1645820\"\n\t\"buildid\"\t\t\"26471822\"\n\t\"UserConfig\"\n\t{\n\t\t\"BetaKey\"\t\t\"experimental\"\n\t}\n}\n";
            var v = Vdf.Parse(acf);
            Check(Vdf.Str(Vdf.Obj(v, "AppState"), "buildid") == "26471822", "acf buildid");
            Check(Vdf.Str(Vdf.Obj(Vdf.Obj(v, "AppState"), "UserConfig"), "BetaKey") == "experimental", "acf BetaKey");
            Check(Steam.NormalizeBranch("public") == "" && Steam.NormalizeBranch(null) == "", "branch normalize");

            // --- versions
            Check(Ver.Compare("0.10.0", "0.9.9") > 0, "0.10.0 > 0.9.9");
            Check(Ver.Compare("1.1.1", "1.1.1") == 0, "equal");
            Check(Ver.Compare("v1.2", "1.10") < 0, "1.2 < 1.10");

            // --- the friend's version.json (copied fixture)
            string fx = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "surrounsync_version.json");
            if (File.Exists(fx))
            {
                Manifest s = Manifest.FromJson(File.ReadAllText(fx));
                Check(s.Version == "1.1.1" && s.Build == "26471822" && s.Branch == "" && s.RequiresExactBuild, "SurrounSync manifest fields");
                Check(s.KeepIfPresent.Count == 7 && s.OffByDefault.Count == 3 && s.Unsupported == null, "SurrounSync install lists");
                Check(s.Problem(new GameInfo { Root = tmp, Build = "26471822", Branch = "" }) == null, "SurrounSync fits build");
                Check(s.Problem(new GameInfo { Root = tmp, Build = "26165965", Branch = "experimental" }) == "needs: default branch", "SurrounSync on experimental");
                Check(s.Problem(new GameInfo { Root = tmp, Build = "26000000", Branch = "" }) == "needs build 26471822", "SurrounSync old build");
            }
            else Check(false, "fixture missing: " + fx);
            Check(Manifest.FromJson("{\"install\":{\"run_script\":\"x\"}}").Unsupported == "run_script", "unknown install key");

            Manifest c1 = Manifest.FromJson("{\"changelog\":\"- Fixed A\\n* Added B\\n\\nC\",\"screenshots\":[\"https://a/1.png\",\"\",\"https://a/2.png\"]}");
            Check(c1.Changelog.Count == 3 && c1.Changelog[0] == "Fixed A" && c1.Changelog[1] == "Added B", "changelog as text");
            Check(c1.Screenshots.Count == 2, "screenshots list");
            Manifest c2 = Manifest.FromJson("{\"changelog\":[\"One\",\"- Two\"]}");
            Check(c2.Changelog.Count == 2 && c2.Changelog[1] == "Two", "changelog as list");

            // --- path guard
            string root = Path.Combine(tmp, @"lib\steamapps\common\SurrounDead");
            Directory.CreateDirectory(root);
            Check(ModInstaller.Target(root, "../evil.txt") == null, "zip slip ..");
            Check(ModInstaller.Target(root, "a/../../evil.txt") == null, "zip slip nested");
            Check(ModInstaller.Target(root, "C:/Windows/x.dll") == null, "absolute path");
            Check(ModInstaller.Target(root, "SurrounDead/Binaries/x.txt") != null, "normal path");

            // --- fake game install
            File.WriteAllText(Path.Combine(root, "SurrounDead.exe"), "");
            File.WriteAllText(Path.Combine(tmp, @"lib\steamapps\appmanifest_1645820.acf"), acf.Replace("experimental", "public"));
            GameInfo g = Steam.Find(root);
            Check(g != null && g.Build == "26471822" && g.Branch == "", "Steam.Find on fake library");
            string w64 = Path.Combine(root, @"SurrounDead\Binaries\Win64\ue4ss");
            Directory.CreateDirectory(Path.Combine(w64, "Mods"));
            File.WriteAllText(Path.Combine(w64, @"Mods\mods.txt"), "original");
            File.WriteAllText(Path.Combine(w64, "UE4SS-settings.ini"), "user settings");

            var files1 = new Dictionary<string, string>
            {
                { "SurrounDead/Binaries/Win64/ue4ss/Mods/T/Scripts/main.lua", "v1" },
                { "SurrounDead/Binaries/Win64/ue4ss/Mods/T/old.txt", "only in v1" },
                { "SurrounDead/Binaries/Win64/ue4ss/Mods/mods.txt", "from mod" },
                { "SurrounDead/Binaries/Win64/ue4ss/UE4SS.dll", "dll" },
                { "SurrounDead/Binaries/Win64/ue4ss/UE4SS-settings.ini", "mod settings" },
                { "SurrounDead/Binaries/Win64/ue4ss/Mods/Extra/enabled.txt", "" },
            };
            string z1 = MakeZip(Path.Combine(tmp, "t1.zip"), files1);
            var e = new ModEntry { Id = "t", Name = "T", Manifest = "x" };
            Manifest m1 = Manifest.FromJson(ManifestJson("1.0.0", z1, Hash.Sha256File(z1), "26471822", "", ""));
            ModInstaller.Install(e, m1, g, true, null);
            Check(File.ReadAllText(Path.Combine(w64, @"Mods\T\Scripts\main.lua")) == "v1", "mod file written");
            Check(File.Exists(Path.Combine(w64, @"Mods\T\enabled.txt")), "enabled_by created (active)");
            Check(File.ReadAllText(Path.Combine(w64, "UE4SS-settings.ini")) == "user settings", "keep_if_present kept");
            Check(File.Exists(Path.Combine(w64, @"Mods\Extra\enabled.txt.off")) && !File.Exists(Path.Combine(w64, @"Mods\Extra\enabled.txt")), "off_by_default");
            Check(File.ReadAllText(Path.Combine(w64, @"Mods\mods.txt")) == "from mod", "overwrite with backup");
            Check(InstallRecord.Load("t") != null && InstallRecord.Load("t").Version == "1.0.0", "record saved");

            // second mod becomes active, first gets disabled
            var e2 = new ModEntry { Id = "u", Name = "U", Manifest = "x" };
            string z2 = MakeZip(Path.Combine(tmp, "u1.zip"), new Dictionary<string, string> { { "SurrounDead/Binaries/Win64/ue4ss/Mods/U/main.lua", "u" } });
            Manifest mu = Manifest.FromJson(ManifestJson("1.0.0", z2, Hash.Sha256File(z2), "26471822", "", "").Replace("Mods/T/enabled.txt", "Mods/U/enabled.txt"));
            ModInstaller.Install(e2, mu, g, true, null);
            Check(File.Exists(Path.Combine(w64, @"Mods\U\enabled.txt")) && !File.Exists(Path.Combine(w64, @"Mods\T\enabled.txt")), "one active mod");
            ModInstaller.SetActive("t", root);
            Check(File.Exists(Path.Combine(w64, @"Mods\T\enabled.txt")) && !File.Exists(Path.Combine(w64, @"Mods\U\enabled.txt")), "switch active mod");

            // update: old.txt dropped
            var files2 = new Dictionary<string, string>(files1);
            files2.Remove("SurrounDead/Binaries/Win64/ue4ss/Mods/T/old.txt");
            files2["SurrounDead/Binaries/Win64/ue4ss/Mods/T/Scripts/main.lua"] = "v2";
            string z3 = MakeZip(Path.Combine(tmp, "t2.zip"), files2);
            Manifest m2 = Manifest.FromJson(ManifestJson("1.1.0", z3, Hash.Sha256File(z3), "26471822", "", ""));
            ModInstaller.Install(e, m2, g, true, null);
            Check(File.ReadAllText(Path.Combine(w64, @"Mods\T\Scripts\main.lua")) == "v2", "update written");
            Check(!File.Exists(Path.Combine(w64, @"Mods\T\old.txt")), "stale file removed on update");

            // bad archives / checksums / version
            string bad = MakeZip(Path.Combine(tmp, "bad.zip"), new Dictionary<string, string> { { "../../evil.txt", "x" } });
            Manifest mb = Manifest.FromJson(ManifestJson("9.0.0", bad, Hash.Sha256File(bad), "26471822", "", ""));
            Throws(() => ModInstaller.Install(new ModEntry { Id = "bad", Name = "B", Manifest = "x" }, mb, g, false, null), "Unsafe path", "zip slip rejected");
            Check(!File.Exists(Path.Combine(tmp, @"lib\steamapps\evil.txt")), "nothing written outside");
            Manifest ms = Manifest.FromJson(ManifestJson("9.0.1", z1, new string('0', 64), "26471822", "", ""));
            Throws(() => ModInstaller.Install(new ModEntry { Id = "sha", Name = "S", Manifest = "x" }, ms, g, false, null), "checksum", "sha256 mismatch rejected");
            Manifest mx = Manifest.FromJson(ManifestJson("9.0.2", z1, Hash.Sha256File(z1), "26165965", "experimental", ""));
            Throws(() => ModInstaller.Install(new ModEntry { Id = "ver", Name = "V", Manifest = "x" }, mx, g, false, null), "needs: experimental", "wrong branch rejected");

            // ini_set: allowed under {game}, rejected outside
            string iniOk = ",\"ini_set\":[{\"file\":\"{game}/SurrounDead/Config/UserEngine.ini\",\"section\":\"OnlineSubsystemSteam\",\"key\":\"bUseSteamNetworking\",\"value\":\"false\"}]";
            Manifest mi = Manifest.FromJson(ManifestJson("1.2.0", z3, Hash.Sha256File(z3), "26471822", "", iniOk));
            ModInstaller.Install(e, mi, g, true, null);
            string ini = Path.Combine(root, @"SurrounDead\Config\UserEngine.ini");
            Check(File.Exists(ini) && File.ReadAllText(ini).Contains("bUseSteamNetworking=false"), "ini_set applied");
            string iniBad = ",\"ini_set\":[{\"file\":\"C:/Windows/win.ini\",\"section\":\"a\",\"key\":\"b\",\"value\":\"c\"}]";
            Manifest mib = Manifest.FromJson(ManifestJson("1.3.0", z3, Hash.Sha256File(z3), "26471822", "", iniBad));
            Throws(() => ModInstaller.Install(e, mib, g, true, null), "ini_set", "ini_set outside rejected");

            // uninstall restores backups and removes files
            ModInstaller.Uninstall("u", root);
            ModInstaller.Uninstall("t", root);
            Check(!File.Exists(Path.Combine(w64, @"Mods\T\Scripts\main.lua")), "uninstall removed files");
            Check(!Directory.Exists(Path.Combine(w64, @"Mods\T")) && !Directory.Exists(Path.Combine(w64, @"Mods\U")), "uninstall removed mod folders");
            Check(!Directory.Exists(Path.Combine(w64, @"Mods\Extra")), "uninstall removed switched-off leftovers");
            Check(Directory.Exists(Path.Combine(w64, "Mods")) && File.Exists(Path.Combine(w64, "UE4SS-settings.ini")), "pre-existing folders and files kept");
            Check(File.ReadAllText(Path.Combine(w64, @"Mods\mods.txt")) == "original", "uninstall restored backup");
            Check(File.Exists(ini) && !File.ReadAllText(ini).Contains("bUseSteamNetworking"), "uninstall removed ini key");
            Check(InstallRecord.All().Count == 0, "no records left");
        }
    }
}
