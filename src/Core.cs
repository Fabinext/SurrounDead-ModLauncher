// SurrounDead Mod Launcher - core helpers: JSON, HTTP, paths, versions, Steam detection.
// C# 5 (built with the .NET Framework csc, see build.cmd).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace SDModLauncher
{
    static class J
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = 8 * 1024 * 1024;
            return s.DeserializeObject(text) as Dictionary<string, object>;
        }

        public static object Get(Dictionary<string, object> d, string path)
        {
            object cur = d;
            foreach (string p in path.Split('.'))
            {
                var m = cur as Dictionary<string, object>;
                if (m == null || !m.TryGetValue(p, out cur)) return null;
            }
            return cur;
        }

        public static string Str(Dictionary<string, object> d, string path)
        {
            object v = Get(d, path);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static long Long(Dictionary<string, object> d, string path)
        {
            object v = Get(d, path);
            if (v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }

        public static bool Bool(Dictionary<string, object> d, string path)
        {
            object v = Get(d, path);
            if (v is bool) return (bool)v;
            string s = v == null ? "" : v.ToString().Trim().ToLowerInvariant();
            return s == "true" || s == "1";
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string path)
        {
            return Get(d, path) as Dictionary<string, object>;
        }

        public static List<object> Arr(Dictionary<string, object> d, string path)
        {
            var a = Get(d, path) as object[];
            return a == null ? new List<object>() : new List<object>(a);
        }

        public static List<string> StrList(Dictionary<string, object> d, string path)
        {
            var list = new List<string>();
            foreach (object o in Arr(d, path))
                if (o != null) list.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
            return list;
        }

        public static string Write(object o)
        {
            return new JavaScriptSerializer().Serialize(o);
        }

        public static T Read<T>(string text)
        {
            return new JavaScriptSerializer().Deserialize<T>(text);
        }
    }

    static class Paths
    {
        // SDML_DATA overrides the data folder (used by the self tests)
        public static string Data = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SDML_DATA"))
            ? Environment.GetEnvironmentVariable("SDML_DATA")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDModLauncher");
        public static string Cache { get { return Sub("cache"); } }
        public static string Installed { get { return Sub("installed"); } }
        public static string Backup { get { return Sub("backup"); } }

        public static string Sub(string name)
        {
            string p = Path.Combine(Data, name);
            Directory.CreateDirectory(p);
            return p;
        }

        // Game config folder of SurrounDead (allowed target for ini_set)
        public static string GameConfig
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"SurrounDead\Saved\Config");
            }
        }

        public static string SafeName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        // True if full is the same as or inside root (both absolute)
        public static bool IsInside(string full, string root)
        {
            string r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string f = Path.GetFullPath(full);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
    }

    static class Hash
    {
        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return Hex(sha.ComputeHash(fs));
        }

        public static string Sha1Text(string s)
        {
            using (var sha = SHA1.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")));
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        public static bool IsSha256(string s)
        {
            return s != null && Regex.IsMatch(s.Trim(), "^[0-9a-fA-F]{64}$");
        }
    }

    static class Ver
    {
        // Compares dotted versions numerically ("0.10.0" > "0.9.3"); text parts compare ordinally.
        public static int Compare(string a, string b)
        {
            string[] pa = Split(a), pb = Split(b);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                string x = i < pa.Length ? pa[i] : "0", y = i < pb.Length ? pb[i] : "0";
                long nx, ny;
                bool ix = long.TryParse(x, out nx), iy = long.TryParse(y, out ny);
                int c = ix && iy ? nx.CompareTo(ny) : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            return 0;
        }

        static string[] Split(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            return v.Length == 0 ? new string[0] : v.Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    static class Net
    {
        public const string UserAgent = "SDModLauncher/1";
        public static bool AllowLocal;   // only for tests / --catalog with a local file

        static Net()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        static bool IsLocal(string url)
        {
            return url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                   (url.Length > 2 && url[1] == ':') || url.StartsWith("\\\\");
        }

        static string LocalPath(string url)
        {
            return url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;
        }

        static HttpWebRequest Request(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only https links are allowed");
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = UserAgent;
            r.Timeout = 20000;
            r.ReadWriteTimeout = 30000;
            r.AllowAutoRedirect = true;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return r;
        }

        public static string GetText(string url)
        {
            if (IsLocal(url))
            {
                if (!AllowLocal) throw new InvalidOperationException("Local files are not allowed");
                return File.ReadAllText(LocalPath(url));
            }
            var r = Request(url);
            r.Headers["Cache-Control"] = "no-cache";
            using (var resp = (HttpWebResponse)r.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // Downloads to dest (via dest.part). progress(done, total) may be null.
        public static void Download(string url, string dest, long maxBytes, Action<long, long> progress)
        {
            string part = dest + ".part";
            if (IsLocal(url))
            {
                if (!AllowLocal) throw new InvalidOperationException("Local files are not allowed");
                File.Copy(LocalPath(url), part, true);
            }
            else
            {
                var r = Request(url);
                using (var resp = (HttpWebResponse)r.GetResponse())
                {
                    long total = resp.ContentLength;
                    if (total > maxBytes) throw new InvalidOperationException("Download is too large");
                    using (var src = resp.GetResponseStream())
                    using (var dst = File.Create(part))
                    {
                        var buf = new byte[81920];
                        long done = 0;
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            done += n;
                            if (done > maxBytes) throw new InvalidOperationException("Download is too large");
                            dst.Write(buf, 0, n);
                            if (progress != null) progress(done, total);
                        }
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }
    }

    class GameInfo
    {
        public string Root;     // folder that holds SurrounDead.exe
        public string Build;    // Steam buildid, null if unknown
        public string Branch;   // "" = default branch
    }

    static class Steam
    {
        public const string AppId = "1645820";
        const string GameExe = "SurrounDead.exe";
        static readonly string[] Processes = { "SurrounDead-Win64-Shipping", "SurrounDead" };

        public static GameInfo Find(string manualPath)
        {
            string root = null;
            if (!string.IsNullOrWhiteSpace(manualPath) && File.Exists(Path.Combine(manualPath, GameExe)))
                root = Path.GetFullPath(manualPath);
            if (root == null)
            {
                foreach (string lib in SteamLibraries())
                {
                    string dir = Path.Combine(lib, @"steamapps\common\SurrounDead");
                    if (File.Exists(Path.Combine(dir, GameExe))) { root = Path.GetFullPath(dir); break; }
                }
            }
            if (root == null) return null;
            var g = new GameInfo { Root = root, Branch = "" };
            ReadManifest(root, g);
            return g;
        }

        // <library>\steamapps\common\SurrounDead -> <library>\steamapps\appmanifest_1645820.acf
        static void ReadManifest(string root, GameInfo g)
        {
            try
            {
                DirectoryInfo common = Directory.GetParent(root);
                DirectoryInfo steamapps = common == null ? null : common.Parent;
                if (steamapps == null) return;
                string acf = Path.Combine(steamapps.FullName, "appmanifest_" + AppId + ".acf");
                if (!File.Exists(acf)) return;
                Dictionary<string, object> v = Vdf.Parse(File.ReadAllText(acf));
                var state = Vdf.Obj(v, "AppState");
                if (state == null) return;
                g.Build = Vdf.Str(state, "buildid");
                var user = Vdf.Obj(state, "UserConfig");
                string beta = user == null ? null : Vdf.Str(user, "BetaKey");
                g.Branch = NormalizeBranch(beta);
            }
            catch (Exception) { }
        }

        public static string NormalizeBranch(string b)
        {
            b = (b ?? "").Trim();
            return b.Equals("public", StringComparison.OrdinalIgnoreCase) ? "" : b;
        }

        static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();
            AddRegistryValue(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryValue(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddRegistryValue(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            var libs = new List<string>(roots);
            foreach (string root in roots)
            {
                string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch (Exception) { }
            }
            return libs;
        }

        static void AddRegistryValue(List<string> list, RegistryKey hive, string key, string name)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(key))
                {
                    string v = k == null ? null : k.GetValue(name) as string;
                    if (!string.IsNullOrEmpty(v)) list.Add(v.Replace('/', '\\'));
                }
            }
            catch (Exception) { }
        }

        public static bool IsRunning()
        {
            foreach (string p in Processes)
                if (Process.GetProcessesByName(p).Length > 0) return true;
            return false;
        }

        public static void Launch()
        {
            Process.Start("steam://rungameid/" + AppId);
        }
    }

    // Minimal Valve KeyValues (.acf/.vdf) reader: quoted keys/values and { } blocks.
    static class Vdf
    {
        public static Dictionary<string, object> Parse(string text)
        {
            int i = 0;
            return Block(text, ref i);
        }

        static Dictionary<string, object> Block(string t, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                string key = Token(t, ref i);
                if (key == null || key == "}") return d;
                int save = i;
                string next = Token(t, ref i);
                if (next == null) return d;
                if (next == "{") d[key] = Block(t, ref i);
                else d[key] = next;
                if (next == "}") { i = save; }
            }
        }

        // Returns a quoted string's content, "{" or "}", or null at the end.
        static string Token(string t, ref int i)
        {
            while (i < t.Length)
            {
                char c = t[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
                if (c == '{' || c == '}') { i++; return c.ToString(); }
                if (c == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < t.Length && t[i] != '"')
                    {
                        if (t[i] == '\\' && i + 1 < t.Length) { i++; }
                        sb.Append(t[i]);
                        i++;
                    }
                    i++;
                    return sb.ToString();
                }
                int start = i;
                while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != '{' && t[i] != '}') i++;
                return t.Substring(start, i - start);
            }
            return null;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as string : null;
        }
    }
}
