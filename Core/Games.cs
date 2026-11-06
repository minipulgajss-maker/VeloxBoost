using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VeloxBoost.Core
{
    /// <summary>Finds installed games (Steam libraries, Epic manifests, Riot default folders).</summary>
    public static class GameLocator
    {
        static List<string> _steamLibs;
        static Dictionary<string, string> _epic;

        static IEnumerable<string> Drives() =>
            DriveInfo.GetDrives().Where(d => { try { return d.DriveType == DriveType.Fixed && d.IsReady; } catch { return false; } }).Select(d => d.RootDirectory.FullName);

        public static List<string> SteamLibraries()
        {
            if (_steamLibs != null) return _steamLibs;
            var libs = new List<string>();
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var steam = (k?.GetValue("SteamPath") as string)?.Replace('/', '\\');
                if (!string.IsNullOrEmpty(steam))
                {
                    libs.Add(steam);
                    var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch { }
            return _steamLibs = libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Steam(string relative)
        {
            foreach (var lib in SteamLibraries())
            {
                var p = Path.Combine(lib, "steamapps", "common", relative);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>Epic Games: AppName/DisplayName → full exe path, from launcher manifests.</summary>
        public static string Epic(string nameContains, string exeName)
        {
            if (_epic == null)
            {
                _epic = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, "*.item"))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                                var r = doc.RootElement;
                                string name = r.GetProperty("DisplayName").GetString();
                                string loc = r.GetProperty("InstallLocation").GetString();
                                _epic[name] = loc;
                            }
                            catch { }
                        }
                }
                catch { }
            }
            foreach (var kv in _epic.Where(e => e.Key.Contains(nameContains, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var hit = Directory.EnumerateFiles(kv.Value, exeName, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 }).FirstOrDefault();
                    if (hit != null) return hit;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Checks the same relative path on every fixed drive (Riot, Battle.net, etc.).</summary>
        public static string AnyDrive(params string[] relatives)
        {
            foreach (var d in Drives())
                foreach (var r in relatives)
                {
                    var p = Path.Combine(d, r);
                    if (File.Exists(p)) return p;
                }
            return null;
        }
    }

    /// <summary>A registry tweak whose value NAME is a game's exe path, found on demand.</summary>
    public sealed class GamePathAction : TAction
    {
        readonly Func<string> _find; readonly Func<string, RegAction> _make;
        string _path; bool _searched; RegAction _inner;
        public GamePathAction(Func<string> find, Func<string, RegAction> make) { _find = find; _make = make; }

        RegAction Inner
        {
            get
            {
                if (!_searched) { _searched = true; try { _path = _find(); } catch { _path = null; } if (_path != null) _inner = _make(_path); }
                return _inner;
            }
        }
        public override bool Applicable => Inner != null;
        public override bool IsApplied() => Inner?.IsApplied() ?? false;
        public override long Apply() { if (Inner == null) throw new Exception("Game not found"); return Inner.Apply(); }
        public override void Revert() => Inner?.Revert();
        public override string Describe() => Inner?.Describe() ?? "(game not installed)";
    }
}
