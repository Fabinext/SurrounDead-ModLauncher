// SurrounDead Mod Launcher - catalog/manifest model and the install engine.
// The engine is the fixed contract of the exe: new mods only need a catalog line and a version.json.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SDModLauncher
{
    class ModEntry
    {
        public string Id, Name, Author, Manifest, Homepage, Icon, Banner, Description;
    }

    class Catalog
    {
        public const int LauncherVersion = 2;
        public const string DefaultUrl = "https://raw.githubusercontent.com/Fabinext/SurrounDead-ModLauncher/main/catalog.json";

        public int MinLauncher;
        public int LatestLauncher;
        public string LauncherUrl, Notice;
        public List<ModEntry> Mods = new List<ModEntry>();

        public bool LauncherTooOld { get { return MinLauncher > LauncherVersion; } }
        public bool LauncherUpdate { get { return LatestLauncher > LauncherVersion; } }

        public static Catalog FromJson(string text)
        {
            var d = J.Parse(text);
            if (d == null) throw new InvalidDataException("Catalog is not valid JSON");
            var c = new Catalog
            {
                MinLauncher = (int)J.Long(d, "min_launcher"),
                LatestLauncher = (int)J.Long(d, "launcher.version"),
                LauncherUrl = J.Str(d, "launcher.url"),
                Notice = J.Str(d, "notice"),
            };
            foreach (object o in J.Arr(d, "mods"))
            {
                var m = o as Dictionary<string, object>;
                if (m == null) continue;
                var e = new ModEntry
                {
                    Id = Paths.SafeName(J.Str(m, "id")),
                    Name = J.Str(m, "name"),
                    Author = J.Str(m, "author"),
                    Manifest = J.Str(m, "manifest"),
                    Homepage = J.Str(m, "homepage"),
                    Icon = J.Str(m, "icon"),
                    Banner = J.Str(m, "banner"),
                    Description = J.Str(m, "description"),
                };
                if (string.IsNullOrEmpty(J.Str(m, "id")) || string.IsNullOrEmpty(e.Manifest)) continue;
                if (string.IsNullOrEmpty(e.Name)) e.Name = e.Id;
                c.Mods.Add(e);
            }
            return c;
        }

        // Network first, cached copy when offline
        public static Catalog Load(string url)
        {
            string cache = Path.Combine(Paths.Cache, "catalog.json");
            try
            {
                string text = Net.GetText(url);
                Catalog c = FromJson(text);
                File.WriteAllText(cache, text);
                return c;
            }
            catch (Exception)
            {
                if (File.Exists(cache)) return FromJson(File.ReadAllText(cache));
                throw;
            }
        }
    }

    class IniSet
    {
        public string File, Section, Key, Value;
    }

    class Manifest
    {
        static readonly string[] KnownInstallKeys =
            { "unzip_to", "enabled_by", "keep_if_present", "ue4ss_dll_sha256", "off_by_default", "ini_set" };

        public string Name, Version, Released, AppId, Build, Branch, Notes, Icon, Banner;
        public bool RequiresExactBuild;
        public string Url, File, Sha256;
        public long Size;
        public string EnabledBy, Ue4ssSha256;
        public List<string> KeepIfPresent = new List<string>(), OffByDefault = new List<string>();
        public List<IniSet> Ini = new List<IniSet>();
        public List<string> Changelog = new List<string>(), Screenshots = new List<string>();
        public string Unsupported;   // set when the manifest needs a newer launcher

        public static Manifest FromJson(string text)
        {
            var d = J.Parse(text);
            if (d == null) throw new InvalidDataException("version.json is not valid JSON");
            var m = new Manifest
            {
                Name = J.Str(d, "name"),
                Version = J.Str(d, "version"),
                Released = J.Str(d, "released"),
                AppId = J.Str(d, "game.steam_app_id"),
                Build = J.Str(d, "game.build"),
                Branch = Steam.NormalizeBranch(J.Str(d, "game.branch")),
                RequiresExactBuild = J.Bool(d, "game.requires_exact_build"),
                Url = J.Str(d, "download.url"),
                File = J.Str(d, "download.file"),
                Size = J.Long(d, "download.size"),
                Sha256 = (J.Str(d, "download.sha256") ?? "").Trim().ToLowerInvariant(),
                EnabledBy = J.Str(d, "install.enabled_by"),
                Ue4ssSha256 = (J.Str(d, "install.ue4ss_dll_sha256") ?? "").Trim().ToLowerInvariant(),
                KeepIfPresent = J.StrList(d, "install.keep_if_present"),
                OffByDefault = J.StrList(d, "install.off_by_default"),
                Notes = J.Str(d, "notes"),
                Icon = J.Str(d, "icon"),
                Banner = J.Str(d, "banner"),
            };
            foreach (object o in J.Arr(d, "install.ini_set"))
            {
                var x = o as Dictionary<string, object>;
                if (x == null) continue;
                m.Ini.Add(new IniSet { File = J.Str(x, "file"), Section = J.Str(x, "section"), Key = J.Str(x, "key"), Value = J.Str(x, "value") });
            }
            // changelog: one string (lines) or a list of strings; leading "-" or "*" bullets are dropped
            object cl = J.Get(d, "changelog");
            var lines = cl is string ? new List<string>(((string)cl).Replace("\r", "").Split('\n')) : J.StrList(d, "changelog");
            foreach (string l in lines)
            {
                string t = l.Trim().TrimStart('-', '*', '\u2022').Trim();
                if (t.Length > 0 && m.Changelog.Count < 30) m.Changelog.Add(t);
            }
            foreach (string u in J.StrList(d, "screenshots"))
                if (m.Screenshots.Count < 6 && !string.IsNullOrWhiteSpace(u)) m.Screenshots.Add(u.Trim());
            var install = J.Obj(d, "install");
            if (install != null)
                foreach (string k in install.Keys)
                    if (!KnownInstallKeys.Contains(k)) m.Unsupported = k;
            int schema = (int)J.Long(d, "schema");
            if (schema > 1) m.Unsupported = "schema " + schema;
            return m;
        }

        public static Manifest Load(ModEntry e)
        {
            string cache = Path.Combine(Paths.Sub(@"cache\manifests"), e.Id + ".json");
            try
            {
                string text = Net.GetText(e.Manifest);
                Manifest m = FromJson(text);
                System.IO.File.WriteAllText(cache, text);
                return m;
            }
            catch (Exception)
            {
                if (System.IO.File.Exists(cache)) return FromJson(System.IO.File.ReadAllText(cache));
                throw;
            }
        }

        // null = fits the installed game, otherwise a short reason
        public string Problem(GameInfo g)
        {
            if (Unsupported != null) return "needs a newer launcher";
            if (!string.IsNullOrEmpty(AppId) && AppId != Steam.AppId) return "not a SurrounDead mod";
            if (!Hash.IsSha256(Sha256) || string.IsNullOrEmpty(Url)) return "broken version.json";
            if (g == null) return "game not found";
            if (!string.Equals(Branch ?? "", g.Branch ?? "", StringComparison.OrdinalIgnoreCase))
                return Branch == "" ? "needs: default branch" : "needs: " + Branch;
            if (RequiresExactBuild && !string.IsNullOrEmpty(Build) && g.Build != null && Build != g.Build)
                return "needs build " + Build;
            return null;
        }
    }

    // Written to %LOCALAPPDATA%\SDModLauncher\installed\<id>.json
    class InstallRecord
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string GameRoot { get; set; }
        public string EnabledBy { get; set; }
        public string ZipPath { get; set; }
        public List<string> Files { get; set; }
        public List<string> Backups { get; set; }
        public List<string> Shared { get; set; }
        public List<string> Dirs { get; set; }     // folders this mod created (removed again when empty)
        public List<IniSet> Ini { get; set; }

        public InstallRecord()
        {
            Files = new List<string>(); Backups = new List<string>(); Shared = new List<string>(); Ini = new List<IniSet>(); Dirs = new List<string>();
        }

        static string PathOf(string id) { return Path.Combine(Paths.Installed, Paths.SafeName(id) + ".json"); }

        public static InstallRecord Load(string id)
        {
            string p = PathOf(id);
            if (!File.Exists(p)) return null;
            try { return J.Read<InstallRecord>(File.ReadAllText(p)); } catch (Exception) { return null; }
        }

        public void Save() { File.WriteAllText(PathOf(Id), J.Write(this)); }
        public void Delete() { string p = PathOf(Id); if (File.Exists(p)) File.Delete(p); }

        public static List<InstallRecord> All()
        {
            var list = new List<InstallRecord>();
            foreach (string f in Directory.GetFiles(Paths.Installed, "*.json"))
            {
                try { var r = J.Read<InstallRecord>(File.ReadAllText(f)); if (r != null && r.Id != null) list.Add(r); }
                catch (Exception) { }
            }
            return list;
        }
    }

    class Settings
    {
        public string GamePath { get; set; }
        public string ActiveMod { get; set; }
        public bool AutoUpdate { get; set; }
        public bool CloseOnLaunch { get; set; }

        public Settings() { AutoUpdate = true; CloseOnLaunch = true; }

        static string FilePath { get { return Path.Combine(Paths.Data, "settings.json"); } }

        public static Settings Load()
        {
            try { if (File.Exists(FilePath)) return J.Read<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
            catch (Exception) { }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Paths.Data);
            File.WriteAllText(FilePath, J.Write(this));
        }
    }

    class InstallError : Exception
    {
        public InstallError(string msg) : base(msg) { }
    }

    static class ModInstaller
    {
        public const string Ue4ssDll = "SurrounDead/Binaries/Win64/ue4ss/UE4SS.dll";
        const long MaxZip = 1L << 30;

        static string Norm(string rel)
        {
            return (rel ?? "").Replace('\\', '/').Trim().TrimStart('/');
        }

        // Absolute path for a zip/manifest path below the game root, or null if it would leave it.
        public static string Target(string root, string rel)
        {
            rel = Norm(rel);
            if (rel.Length == 0 || rel.Contains(":") || Path.IsPathRooted(rel.Replace('/', '\\'))) return null;
            foreach (string part in rel.Split('/'))
                if (part == "..") return null;
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', '\\'))); }
            catch (Exception) { return null; }
            return Paths.IsInside(full, root) ? full : null;
        }

        static bool Matches(string rel, List<string> patterns)
        {
            rel = Norm(rel);
            foreach (string p0 in patterns)
            {
                string p = Norm(p0);
                if (p.Length == 0) continue;
                if (p.EndsWith("/") ? rel.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                                    : rel.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Throws InstallError if any entry would be written outside the game folder
        public static List<ZipArchiveEntry> CheckZip(ZipArchive zip, string root)
        {
            var files = new List<ZipArchiveEntry>();
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) continue;
                if (Target(root, e.FullName) == null) throw new InstallError("Unsafe path in mod archive: " + e.FullName);
                files.Add(e);
            }
            if (files.Count == 0) throw new InstallError("Mod archive is empty");
            return files;
        }

        // Downloads (or reuses) the mod zip and verifies size and sha256
        public static string GetZip(ModEntry e, Manifest m, Action<long, long> progress)
        {
            if (!Hash.IsSha256(m.Sha256)) throw new InstallError("version.json has no sha256");
            string dir = Paths.Sub(@"cache\zips");
            string zip = Path.Combine(dir, Paths.SafeName(e.Id + "-" + m.Version) + ".zip");
            if (File.Exists(zip) && Hash.Sha256File(zip) == m.Sha256) return zip;
            Net.Download(m.Url, zip, MaxZip, progress);
            if (m.Size > 0 && new FileInfo(zip).Length != m.Size) { File.Delete(zip); throw new InstallError("Download has the wrong size"); }
            if (Hash.Sha256File(zip) != m.Sha256) { File.Delete(zip); throw new InstallError("Download checksum does not match"); }
            foreach (string old in Directory.GetFiles(dir, Paths.SafeName(e.Id + "-") + "*.zip"))
                if (!old.Equals(zip, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(old); } catch (Exception) { } }
            return zip;
        }

        static bool Ue4ssMismatch(string root, Manifest m)
        {
            if (!Hash.IsSha256(m.Ue4ssSha256)) return false;
            string dll = Target(root, Ue4ssDll);
            return dll == null || !File.Exists(dll) || Hash.Sha256File(dll) != m.Ue4ssSha256;
        }

        static void WriteEntry(ZipArchiveEntry entry, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string tmp = target + ".sdml";
            entry.ExtractToFile(tmp, true);
            if (File.Exists(target)) { File.SetAttributes(target, FileAttributes.Normal); File.Delete(target); }
            File.Move(tmp, target);
        }

        static void BackupFile(string id, string root, string rel, string target, InstallRecord rec)
        {
            if (!File.Exists(target) || rec.Backups.Contains(rel)) return;
            string dst = Path.Combine(Paths.Backup, Paths.SafeName(id), rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(target, dst, true);
            rec.Backups.Add(rel);
        }

        static string IniPath(string root, string spec)
        {
            spec = (spec ?? "").Replace('\\', '/');
            string baseDir, rest;
            if (spec.StartsWith("{game}/", StringComparison.OrdinalIgnoreCase)) { baseDir = root; rest = spec.Substring(7); }
            else if (spec.StartsWith("{config}/", StringComparison.OrdinalIgnoreCase)) { baseDir = Paths.GameConfig; rest = spec.Substring(9); }
            else return null;
            if (!rest.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return null;
            return Target(baseDir, rest);
        }

        static bool CleanIniText(string s)
        {
            return s != null && s.IndexOfAny(new[] { '\r', '\n', '[', ']' }) < 0;
        }

        public static void Install(ModEntry e, Manifest m, GameInfo g, bool makeActive, Action<long, long> progress)
        {
            if (Steam.IsRunning()) throw new InstallError("Close the game first");
            string problem = m.Problem(g);
            if (problem != null) throw new InstallError(problem);
            string root = g.Root;
            string zipPath = GetZip(e, m, progress);

            InstallRecord old = InstallRecord.Load(e.Id);
            var rec = new InstallRecord { Id = e.Id, Version = m.Version, GameRoot = root, EnabledBy = m.EnabledBy, ZipPath = zipPath };
            if (old != null) { rec.Backups.AddRange(old.Backups); if (old.Dirs != null) rec.Dirs.AddRange(old.Dirs); }
            bool replaceShared = Ue4ssMismatch(root, m);
            bool otherMods = InstallRecord.All().Any(r => r.Id != e.Id);

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                List<ZipArchiveEntry> files = CheckZip(zip, root);
                foreach (ZipArchiveEntry entry in files)
                {
                    string rel = Norm(entry.FullName);
                    string target = Target(root, rel);
                    bool shared = Matches(rel, m.KeepIfPresent);
                    if (shared)
                    {
                        if (File.Exists(target) && !replaceShared) continue;
                        if (!rec.Shared.Contains(rel)) rec.Shared.Add(rel);
                    }
                    bool ownedBefore = old != null && (old.Files.Contains(rel) || old.Shared.Contains(rel));
                    if (!ownedBefore) BackupFile(e.Id, root, rel, target, rec);
                    TrackDirs(root, target, rec);
                    WriteEntry(entry, target);
                    if (!shared) rec.Files.Add(rel);
                }
            }

            // files of the previous version that the new one no longer ships
            if (old != null)
                foreach (string f in old.Files)
                    if (!rec.Files.Contains(f)) RemoveFile(root, f, rec);

            if (old == null)
                foreach (string p in m.OffByDefault) SwitchOff(root, p);

            foreach (IniSet s in m.Ini)
            {
                string path = IniPath(root, s.File);
                if (path == null || !CleanIniText(s.Section) || !CleanIniText(s.Key) || (s.Value != null && !CleanIniText(s.Value)))
                    throw new InstallError("Invalid ini_set entry");
                IniFile.Set(path, s.Section, s.Key, s.Value);
                rec.Ini.Add(s);
            }

            rec.Save();
            if (makeActive) SetActive(e.Id, root);
            else SetEnabled(root, rec.EnabledBy, false);
        }

        // Remembers every folder above target that does not exist yet (relative to the game root)
        static void TrackDirs(string root, string target, InstallRecord rec)
        {
            if (rec.Dirs == null) rec.Dirs = new List<string>();
            string rootFull = Path.GetFullPath(root).TrimEnd('\\');
            string dir = Path.GetDirectoryName(target);
            while (dir != null && dir.Length > rootFull.Length && Paths.IsInside(dir, root) && !Directory.Exists(dir))
            {
                string rel = Norm(dir.Substring(rootFull.Length + 1));
                if (!rec.Dirs.Contains(rel)) rec.Dirs.Add(rel);
                dir = Path.GetDirectoryName(dir);
            }
        }

        // Deletes the folders this mod created, deepest first, but only when they are empty
        static void RemoveDirs(string root, InstallRecord rec)
        {
            if (rec.Dirs == null) return;
            foreach (string rel in rec.Dirs.OrderByDescending(d => d.Length).ToList())
            {
                string full = Target(root, rel);
                try
                {
                    if (full != null && Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any())
                        Directory.Delete(full);
                }
                catch (Exception) { }
            }
        }

        static void RemoveFile(string root, string rel, InstallRecord rec)
        {
            string target = Target(root, rel);
            if (target == null) return;
            if (File.Exists(target)) { File.SetAttributes(target, FileAttributes.Normal); File.Delete(target); }
            string off = target + ".off";   // switched off by off_by_default
            if (File.Exists(off)) { File.SetAttributes(off, FileAttributes.Normal); File.Delete(off); }
            if (rec.Backups.Contains(rel))
            {
                string bak = Path.Combine(Paths.Backup, Paths.SafeName(rec.Id), rel.Replace('/', '\\'));
                if (File.Exists(bak)) { Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Move(bak, target); }
                rec.Backups.Remove(rel);
            }
        }

        static void SwitchOff(string root, string p)
        {
            string target = Target(root, p);
            if (target == null) return;
            if (Norm(p).EndsWith("/") || Directory.Exists(target))
            {
                string en = Path.Combine(target, "enabled.txt");
                if (File.Exists(en)) { string off = en + ".off"; if (File.Exists(off)) File.Delete(off); File.Move(en, off); }
            }
            else if (File.Exists(target) && !target.EndsWith(".off", StringComparison.OrdinalIgnoreCase))
            {
                string off = target + ".off";
                if (File.Exists(off)) File.Delete(off);
                File.Move(target, off);
            }
        }

        static void SetEnabled(string root, string enabledBy, bool on)
        {
            if (string.IsNullOrEmpty(enabledBy)) return;
            string f = Target(root, enabledBy);
            if (f == null) return;
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                if (!File.Exists(f)) File.WriteAllText(f, "");
            }
            else if (File.Exists(f)) File.Delete(f);
        }

        // One active mod: enable the chosen one, disable every other installed mod
        public static void SetActive(string id, string root)
        {
            foreach (InstallRecord r in InstallRecord.All())
                SetEnabled(root, r.EnabledBy, r.Id == id);
        }

        // Re-applies the chosen mod's shared files (UE4SS) when they do not match its version.json
        public static void EnsureShared(ModEntry e, Manifest m, GameInfo g, Action<long, long> progress)
        {
            if (m == null || !Ue4ssMismatch(g.Root, m)) return;
            string zipPath = GetZip(e, m, progress);
            InstallRecord rec = InstallRecord.Load(e.Id);
            if (rec == null) return;
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in CheckZip(zip, g.Root))
                {
                    string rel = Norm(entry.FullName);
                    if (!Matches(rel, m.KeepIfPresent)) continue;
                    string target = Target(g.Root, rel);
                    BackupFile(e.Id, g.Root, rel, target, rec);
                    WriteEntry(entry, target);
                    if (!rec.Shared.Contains(rel)) rec.Shared.Add(rel);
                }
            }
            rec.Save();
        }

        public static void Uninstall(string id, string root)
        {
            if (Steam.IsRunning()) throw new InstallError("Close the game first");
            InstallRecord rec = InstallRecord.Load(id);
            if (rec == null) return;
            bool others = InstallRecord.All().Any(r => r.Id != id);
            SetEnabled(root, rec.EnabledBy, false);
            foreach (string f in rec.Files.ToList()) RemoveFile(root, f, rec);
            if (!others)
                foreach (string f in rec.Shared.ToList()) RemoveFile(root, f, rec);
            foreach (IniSet s in rec.Ini)
            {
                string path = IniPath(root, s.File);
                if (path != null && File.Exists(path)) IniFile.Set(path, s.Section, s.Key, null);
            }
            RemoveDirs(root, rec);
            rec.Delete();
            try { Directory.Delete(Path.Combine(Paths.Backup, Paths.SafeName(id)), true); } catch (Exception) { }
        }
    }

    static class IniFile
    {
        // value null removes the key (same logic as the Last Signal installer)
        public static void Set(string path, string section, string key, string value)
        {
            var lines = new List<string>(File.Exists(path) ? File.ReadAllLines(path) : new string[0]);
            string header = "[" + section + "]";
            int start = lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
            if (start < 0)
            {
                if (value == null) return;
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add(header);
                lines.Add(key + "=" + value);
            }
            else
            {
                int end = start + 1;
                while (end < lines.Count && !lines[end].TrimStart().StartsWith("[")) end++;
                int found = -1;
                for (int i = start + 1; i < end; i++)
                    if (lines[i].Split('=')[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) found = i;
                if (value == null) { if (found >= 0) lines.RemoveAt(found); }
                else if (found >= 0) lines[found] = key + "=" + value;
                else
                {
                    int insert = end;
                    while (insert > start + 1 && lines[insert - 1].Trim().Length == 0) insert--;
                    lines.Insert(insert, key + "=" + value);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }
    }
}
