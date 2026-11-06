using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VeloxBoost.Core
{
    public enum Risk { Safe, Moderate, Advanced }

    public sealed class Tweak
    {
        public string Id;
        public string Category;
        public string NamePt, NameEn;
        public Risk Risk;
        public bool IsAction;          // limpeza / comando único (não é um interruptor)
        public bool NeedsReboot;
        public List<TAction> Actions = new();

        public string Name => L.I.Lang == "pt" ? NamePt : NameEn;

        public bool Applicable
        {
            get { try { return Actions.Any(a => a.Applicable); } catch { return false; } }
        }

        public bool IsApplied()
        {
            if (IsAction) return false;
            try
            {
                var ap = Actions.Where(a => a.Applicable).ToList();
                return ap.Count > 0 && ap.All(a => a.IsApplied());
            }
            catch { return false; }
        }

        public string Details => string.Join("  •  ", Actions.Select(a => a.Describe()));
    }

    // ───────────────────────── Ações ─────────────────────────

    public abstract class TAction
    {
        public virtual bool Applicable => true;
        public abstract bool IsApplied();
        /// <returns>bytes libertados (só limpeza)</returns>
        public abstract long Apply();
        public abstract void Revert();
        public abstract string Describe();
    }

    /// <summary>Escreve um valor no registo e guarda o original para reverter.</summary>
    public sealed class RegAction : TAction
    {
        public readonly string Hive, Path, Name;
        readonly Func<object> _value;
        readonly bool _onlyIfKeyExists;

        public RegAction(string hive, string path, string name, Func<object> value, bool onlyIfKeyExists = false)
        { Hive = hive; Path = path; Name = name; _value = value; _onlyIfKeyExists = onlyIfKeyExists; }

        RegistryKey Root => Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;

        List<string> Paths()
        {
            int i = Path.IndexOf("\\*", StringComparison.Ordinal);
            if (i < 0) return new List<string> { Path };
            string parent = Path.Substring(0, i), rest = Path.Substring(i + 2);
            var list = new List<string>();
            using var k = Root.OpenSubKey(parent);
            if (k != null) foreach (var s in k.GetSubKeyNames()) list.Add(parent + "\\" + s + rest);
            return list;
        }

        public override bool Applicable
        {
            get
            {
                try
                {
                    var p = Paths();
                    if (p.Count == 0) return false;
                    if (!_onlyIfKeyExists) return true;
                    using var k = Root.OpenSubKey(p[0]);
                    return k != null;
                }
                catch { return false; }
            }
        }

        static RegistryValueKind KindOf(object v) => v switch
        {
            int => RegistryValueKind.DWord,
            long => RegistryValueKind.QWord,
            byte[] => RegistryValueKind.Binary,
            string[] => RegistryValueKind.MultiString,
            _ => RegistryValueKind.String
        };

        static bool Same(object a, object b)
        {
            if (a == null || b == null) return false;
            if (a is byte[] x && b is byte[] y) return x.SequenceEqual(y);
            if (a is int ai && b is int bi) return ai == bi;
            if (a is long al && b is long bl) return al == bl;
            return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public override bool IsApplied()
        {
            var target = _value();
            var paths = Paths();
            if (paths.Count == 0) return false;
            foreach (var p in paths)
            {
                using var k = Root.OpenSubKey(p);
                if (k == null || !Same(k.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), target)) return false;
            }
            return true;
        }

        public override long Apply()
        {
            var v = _value();
            foreach (var p in Paths())
            {
                string bkey = $"reg|{Hive}|{p}|{Name}";
                if (!BackupStore.Has(bkey))
                {
                    var e = new BackupEntry();
                    using (var k = Root.OpenSubKey(p))
                    {
                        object cur = k?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (cur != null)
                        {
                            e.Existed = true;
                            e.Kind = (int)k.GetValueKind(Name);
                            e.Data = Ser(cur);
                        }
                    }
                    e.CreatedKey = FirstMissing(p);
                    BackupStore.Set(bkey, e);
                }
                using var w = Root.CreateSubKey(p, true);
                w.SetValue(Name, v, KindOf(v));
            }
            return 0;
        }

        public override void Revert()
        {
            foreach (var p in Paths())
            {
                string bkey = $"reg|{Hive}|{p}|{Name}";
                var e = BackupStore.Get(bkey);
                if (e == null) continue;
                using (var w = Root.OpenSubKey(p, true))
                {
                    if (w != null)
                    {
                        if (e.Existed) w.SetValue(Name, De(e.Data), (RegistryValueKind)e.Kind);
                        else w.DeleteValue(Name, false);
                    }
                }
                if (!e.Existed && !string.IsNullOrEmpty(e.CreatedKey)) TryDeleteEmptyTree(e.CreatedKey);
                BackupStore.Remove(bkey);
            }
        }

        string FirstMissing(string path)
        {
            var parts = path.Split('\\');
            string acc = "";
            foreach (var part in parts)
            {
                acc = acc.Length == 0 ? part : acc + "\\" + part;
                using var k = Root.OpenSubKey(acc);
                if (k == null) return acc;
            }
            return null;
        }

        void TryDeleteEmptyTree(string path)
        {
            try
            {
                using (var k = Root.OpenSubKey(path))
                {
                    if (k == null || !IsEmpty(k)) return;
                }
                Root.DeleteSubKeyTree(path, false);
            }
            catch { }
        }

        static bool IsEmpty(RegistryKey k)
        {
            if (k.ValueCount > 0) return false;
            foreach (var s in k.GetSubKeyNames())
            {
                using var c = k.OpenSubKey(s);
                if (c != null && !IsEmpty(c)) return false;
            }
            return true;
        }

        static string Ser(object v) => v switch
        {
            int i => "d:" + i,
            long l => "q:" + l,
            byte[] b => "b:" + Convert.ToBase64String(b),
            string[] m => "m:" + string.Join("\u0001", m),
            _ => "s:" + v
        };

        static object De(string s)
        {
            string t = s.Substring(0, 2), d = s.Substring(2);
            return t switch
            {
                "d:" => int.Parse(d),
                "q:" => long.Parse(d),
                "b:" => Convert.FromBase64String(d),
                "m:" => d.Length == 0 ? Array.Empty<string>() : d.Split('\u0001'),
                _ => d
            };
        }

        public override string Describe()
        {
            object v; try { v = _value(); } catch { v = "?"; }
            string shown = v is byte[] b ? BitConverter.ToString(b) : v is int i && i < 0 ? "0x" + i.ToString("X8") : v?.ToString();
            string n = Name.Length == 0 ? "(Padrão)" : Name;
            return $"{Hive}\\{Path}\\{n} = {shown}";
        }
    }

    /// <summary>Executa um utilitário do Windows (netsh, bcdedit, fsutil, ipconfig…) — sem PowerShell.</summary>
    public sealed class CmdAction : TAction
    {
        readonly string _exe, _apply, _revert;
        public CmdAction(string exe, string apply, string revert) { _exe = exe; _apply = apply; _revert = revert; }
        string Key => $"cmd|{_exe}|{_apply}";

        public override bool IsApplied() => BackupStore.Has(Key);
        public override long Apply()
        {
            var r = Sys.Run(_exe, _apply);
            if (r.Code != 0) throw new Exception(Sys.Trim(r.Output));
            if (_revert != null) BackupStore.Set(Key, new BackupEntry { Existed = true });
            return 0;
        }
        public override void Revert()
        {
            if (_revert != null) Sys.Run(_exe, _revert);
            BackupStore.Remove(Key);
        }
        public override string Describe() => $"{_exe} {_apply}";
    }

    /// <summary>Altera uma definição do plano de energia ativo (powercfg).</summary>
    public sealed class PowerAction : TAction
    {
        readonly string _sub, _setting; readonly int _value;
        public PowerAction(string sub, string setting, int value) { _sub = sub; _setting = setting; _value = value; }
        string Key => $"pwr|{_sub}|{_setting}";

        (int ac, int dc)? Query()
        {
            var r = Sys.Run("powercfg.exe", $"/q SCHEME_CURRENT {_sub} {_setting}");
            if (r.Code != 0) return null;
            var m = Regex.Matches(r.Output, @"0x([0-9a-fA-F]{8})");
            if (m.Count < 2) return null;
            return (Convert.ToInt32(m[m.Count - 2].Groups[1].Value, 16), Convert.ToInt32(m[m.Count - 1].Groups[1].Value, 16));
        }

        public override bool Applicable { get { try { return Query() != null; } catch { return false; } } }
        public override bool IsApplied() { var q = Query(); return q != null && q.Value.ac == _value; }

        void Set(int ac, int dc)
        {
            Sys.Run("powercfg.exe", $"/setacvalueindex SCHEME_CURRENT {_sub} {_setting} {ac}");
            Sys.Run("powercfg.exe", $"/setdcvalueindex SCHEME_CURRENT {_sub} {_setting} {dc}");
            Sys.Run("powercfg.exe", "/setactive SCHEME_CURRENT");
        }

        public override long Apply()
        {
            var q = Query() ?? throw new Exception("powercfg");
            if (!BackupStore.Has(Key)) BackupStore.Set(Key, new BackupEntry { Existed = true, Data = $"{q.ac},{q.dc}" });
            Set(_value, _value);
            return 0;
        }

        public override void Revert()
        {
            var e = BackupStore.Get(Key);
            if (e == null) return;
            var p = e.Data.Split(',');
            Set(int.Parse(p[0]), int.Parse(p[1]));
            BackupStore.Remove(Key);
        }

        public override string Describe() => $"powercfg {_sub} {_setting} = {_value}";
    }

    /// <summary>Ativa/desativa uma tarefa agendada via Task Scheduler COM.</summary>
    public sealed class TaskAction : TAction
    {
        readonly string _path;
        public TaskAction(string path) { _path = path; }
        string Key => "task|" + _path;

        dynamic GetTask()
        {
            var t = Type.GetTypeFromProgID("Schedule.Service");
            dynamic svc = Activator.CreateInstance(t);
            svc.Connect();
            int i = _path.LastIndexOf('\\');
            string folder = i <= 0 ? "\\" : _path.Substring(0, i);
            dynamic f = svc.GetFolder(folder);
            return f.GetTask(_path.Substring(i + 1));
        }

        public override bool Applicable { get { try { return GetTask() != null; } catch { return false; } } }
        public override bool IsApplied() { try { return !(bool)GetTask().Enabled; } catch { return false; } }
        public override long Apply()
        {
            var t = GetTask();
            if (!BackupStore.Has(Key)) BackupStore.Set(Key, new BackupEntry { Existed = (bool)t.Enabled });
            t.Enabled = false;
            return 0;
        }
        public override void Revert()
        {
            var e = BackupStore.Get(Key);
            if (e == null) return;
            GetTask().Enabled = e.Existed;
            BackupStore.Remove(Key);
        }
        public override string Describe() => "Task: " + _path;
    }

    /// <summary>Apaga ficheiros de uma pasta (ignora ficheiros em uso).</summary>
    public sealed class CleanAction : TAction
    {
        readonly string _dir, _pattern;
        public CleanAction(string dir, string pattern = "*") { _dir = dir; _pattern = pattern; }
        string Dir => Environment.ExpandEnvironmentVariables(_dir);
        public override bool Applicable => Directory.Exists(Dir);
        public override bool IsApplied() => false;
        public override void Revert() { }
        public override string Describe() => Path.Combine(_dir, _pattern);

        public override long Apply()
        {
            long freed = 0;
            var root = new DirectoryInfo(Dir);
            if (!root.Exists) return 0;
            var opts = new EnumerationOptions { RecurseSubdirectories = _pattern == "*", IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in SafeFiles(root, _pattern, opts))
            {
                try { long len = f.Length; f.Attributes = FileAttributes.Normal; f.Delete(); freed += len; } catch { }
            }
            if (_pattern == "*")
            {
                foreach (var d in SafeDirs(root))
                {
                    try { d.Delete(true); } catch { }
                }
            }
            return freed;
        }

        static IEnumerable<FileInfo> SafeFiles(DirectoryInfo d, string pattern, EnumerationOptions o)
        {
            try { return d.EnumerateFiles(pattern, o).ToList(); } catch { return Enumerable.Empty<FileInfo>(); }
        }

        static IEnumerable<DirectoryInfo> SafeDirs(DirectoryInfo d)
        {
            try { return d.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList(); }
            catch { return Enumerable.Empty<DirectoryInfo>(); }
        }

        public long Size()
        {
            try
            {
                var root = new DirectoryInfo(Dir);
                if (!root.Exists) return 0;
                return root.EnumerateFiles(_pattern, new EnumerationOptions { RecurseSubdirectories = _pattern == "*", IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Sum(f => { try { return f.Length; } catch { return 0L; } });
            }
            catch { return 0; }
        }
    }

    /// <summary>Ação livre com funções próprias.</summary>
    public sealed class CustomAction : TAction
    {
        readonly Func<bool> _isApplied; readonly Action _apply, _revert; readonly string _desc; readonly Func<bool> _applicable;
        public CustomAction(string desc, Func<bool> isApplied, Action apply, Action revert, Func<bool> applicable = null)
        { _desc = desc; _isApplied = isApplied; _apply = apply; _revert = revert; _applicable = applicable; }
        public override bool Applicable { get { try { return _applicable?.Invoke() ?? true; } catch { return false; } } }
        public override bool IsApplied() => _isApplied();
        public override long Apply() { _apply(); return 0; }
        public override void Revert() => _revert?.Invoke();
        public override string Describe() => _desc;
    }

    // ───────────────────────── Backup ─────────────────────────

    public sealed class BackupEntry
    {
        public bool Existed { get; set; }
        public int Kind { get; set; }
        public string Data { get; set; }
        public string CreatedKey { get; set; }
    }

    public static class BackupStore
    {
        static readonly object Lock = new();
        static Dictionary<string, BackupEntry> _d;
        static string File => System.IO.Path.Combine(Sys.DataDir, "backup.json");

        static Dictionary<string, BackupEntry> D
        {
            get
            {
                if (_d != null) return _d;
                try { _d = System.IO.File.Exists(File) ? JsonSerializer.Deserialize<Dictionary<string, BackupEntry>>(System.IO.File.ReadAllText(File)) : null; }
                catch { try { System.IO.File.Copy(File, File + ".corrupt", true); } catch { } }
                return _d ??= new Dictionary<string, BackupEntry>();
            }
        }

        public static int Count { get { lock (Lock) return D.Count; } }
        public static bool Has(string k) { lock (Lock) return D.ContainsKey(k); }
        public static BackupEntry Get(string k) { lock (Lock) return D.TryGetValue(k, out var e) ? e : null; }
        public static void Set(string k, BackupEntry e) { lock (Lock) { D[k] = e; Save(); } }
        public static void Remove(string k) { lock (Lock) { if (D.Remove(k)) Save(); } }

        static void Save()
        {
            try
            {
                var tmp = File + ".tmp";
                System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(D, new JsonSerializerOptions { WriteIndented = true }));
                System.IO.File.Move(tmp, File, true);
            }
            catch (Exception ex) { Sys.Log("backup save: " + ex.Message); }
        }
    }

    // ───────────────────────── Utilitários ─────────────────────────

    public static class Sys
    {
        public static string DataDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VeloxBoostData");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static (int Code, string Output) Run(string exe, string args, int timeoutMs = 60000)
        {
            try
            {
                string path = exe.Contains('\\') ? exe : Path.Combine(Environment.SystemDirectory, exe);
                if (!System.IO.File.Exists(path)) path = exe;
                var psi = new ProcessStartInfo(path, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "timeout"); }
                return (p.ExitCode, o.Result + e.Result);
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }

        public static string Trim(string s) { s = (s ?? "").Trim(); return s.Length > 160 ? s.Substring(0, 160) + "…" : s; }

        static readonly object LogLock = new();
        public static void Log(string msg)
        {
            try { lock (LogLock) System.IO.File.AppendAllText(Path.Combine(DataDir, "log.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}"); } catch { }
        }
    }
}
