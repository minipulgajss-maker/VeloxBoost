using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace VeloxBoost.Core
{
    // ───────────────────────── Definições ─────────────────────────
    public sealed class AppSettings
    {
        public string Language { get; set; } = "en";
        public int SettingsVersion { get; set; } = 2;
        public int Profile { get; set; } = 1;
        public bool StartWithWindows { get; set; } = false;
        public bool ReapplyOnStartup { get; set; } = false;
        public bool RestorePointFirst { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool CheckUpdates { get; set; } = true;
        public bool BoosterEnabled { get; set; } = true;
        public bool BoosterThrottleBackground { get; set; } = true;
        public string UpdateSource { get; set; } = "https://github.com/minipulgajss-maker/VeloxBoost";
        public List<string> Games { get; set; } = GameBooster.DefaultGames.ToList();
        public List<string> AppliedIds { get; set; } = new();
    }

    public static class Settings
    {
        public static AppSettings Current { get; private set; } = new();
        static string File => Path.Combine(Sys.DataDir, "settings.json");

        public static void Load()
        {
            try { if (System.IO.File.Exists(File)) Current = JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File)) ?? new(); }
            catch (Exception ex) { Sys.Log("settings load: " + ex.Message); Current = new(); }
            Current.Games ??= GameBooster.DefaultGames.ToList();
            Current.AppliedIds ??= new();
            if (Current.SettingsVersion < 2) { Current.Language = "en"; Current.SettingsVersion = 2; Save(); }
            if (string.IsNullOrWhiteSpace(Current.UpdateSource) || Current.UpdateSource.Contains("O-TEU-UTILIZADOR"))
                Current.UpdateSource = new AppSettings().UpdateSource;
        }

        public static void Save()
        {
            try
            {
                var tmp = File + ".tmp";
                System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
                System.IO.File.Move(tmp, File, true);
            }
            catch (Exception ex) { Sys.Log("settings save: " + ex.Message); }
        }
    }

    // ───────────────────────── Motor ─────────────────────────
    public sealed class RunResult { public int Ok, Failed; public long Freed; public bool NeedsReboot; public List<string> Errors = new(); }

    public static class Engine
    {
        static readonly SemaphoreSlim Gate = new(1, 1);

        public static async Task<RunResult> ApplyAsync(IEnumerable<Tweak> tweaks, IProgress<(int done, int total, string name)> progress = null)
        {
            var list = tweaks.ToList();
            var res = new RunResult();
            await Gate.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        progress?.Report((i, list.Count, t.Name));
                        if (!t.Applicable) continue;
                        if (!t.IsAction && t.IsApplied()) { Track(t, true); continue; }
                        bool ok = true;
                        foreach (var a in t.Actions)
                        {
                            try { if (a.Applicable) res.Freed += a.Apply(); }
                            catch (Exception ex) { ok = false; res.Errors.Add($"{t.NameEn}: {ex.Message}"); Sys.Log($"apply {t.Id}: {ex.Message}"); }
                        }
                        if (ok) { res.Ok++; if (t.NeedsReboot) res.NeedsReboot = true; if (!t.IsAction) Track(t, true); }
                        else res.Failed++;
                    }
                    progress?.Report((list.Count, list.Count, ""));
                });
            }
            finally { Gate.Release(); Settings.Save(); }
            return res;
        }

        public static async Task<RunResult> RevertAsync(IEnumerable<Tweak> tweaks)
        {
            var list = tweaks.Where(t => !t.IsAction).ToList();
            var res = new RunResult();
            await Gate.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var t in list)
                    {
                        bool ok = true;
                        foreach (var a in Enumerable.Reverse(t.Actions))
                        {
                            try { a.Revert(); }
                            catch (Exception ex) { ok = false; res.Errors.Add($"{t.NameEn}: {ex.Message}"); Sys.Log($"revert {t.Id}: {ex.Message}"); }
                        }
                        Track(t, false);
                        if (ok) res.Ok++; else res.Failed++;
                    }
                });
            }
            finally { Gate.Release(); Settings.Save(); }
            return res;
        }

        static void Track(Tweak t, bool applied)
        {
            lock (Settings.Current.AppliedIds)
            {
                Settings.Current.AppliedIds.Remove(t.Id);
                if (applied) Settings.Current.AppliedIds.Add(t.Id);
            }
        }

        /// <summary>Reaplica otimizações que o utilizador tinha ativas mas que o Windows desfez.</summary>
        public static Task<RunResult> ReapplyAsync()
        {
            var ids = new HashSet<string>(Settings.Current.AppliedIds);
            return ApplyAsync(Catalog.All.Where(t => ids.Contains(t.Id) && !t.IsAction));
        }

        public static bool CreateRestorePoint(string description)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", true))
                    k.SetValue("SystemRestorePointCreationFrequency", 0, Microsoft.Win32.RegistryValueKind.DWord);

                var scope = new ManagementScope(@"\\localhost\root\default");
                using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), new ObjectGetOptions());
                // garante que a proteção está ativa no disco do sistema
                try
                {
                    var en = cls.GetMethodParameters("Enable");
                    en["Drive"] = Path.GetPathRoot(Environment.SystemDirectory);
                    cls.InvokeMethod("Enable", en, null);
                }
                catch { }
                var p = cls.GetMethodParameters("CreateRestorePoint");
                p["Description"] = description;
                p["RestorePointType"] = 12; // MODIFY_SETTINGS
                p["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE
                var outP = cls.InvokeMethod("CreateRestorePoint", p, null);
                return Convert.ToInt32(outP["ReturnValue"]) == 0;
            }
            catch (Exception ex) { Sys.Log("restore point: " + ex.Message); return false; }
        }

        // Arranque com o Windows via tarefa agendada (corre como administrador sem pedir UAC)
        const string TaskName = "VeloxBoost Startup";
        public static void SetStartup(bool on)
        {
            try
            {
                dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                svc.Connect();
                dynamic root = svc.GetFolder("\\");
                if (!on) { try { root.DeleteTask(TaskName, 0); } catch { } return; }
                string exe = Environment.ProcessPath;
                string user = Environment.UserDomainName + "\\" + Environment.UserName;
                string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Esc(user)}</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{Esc(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>
  <Actions Context=""Author""><Exec><Command>""{Esc(exe)}""</Command><Arguments>--minimized</Arguments></Exec></Actions>
</Task>";
                root.RegisterTask(TaskName, xml, 6 /*CREATE_OR_UPDATE*/, null, null, 3 /*INTERACTIVE_TOKEN*/, null);
            }
            catch (Exception ex) { Sys.Log("startup task: " + ex.Message); }
        }
        static string Esc(string s) => System.Security.SecurityElement.Escape(s);
    }

    // ───────────────────────── Game Booster ─────────────────────────
    public sealed class GameBooster : IDisposable
    {
        public static readonly string[] DefaultGames =
        {
            "cs2", "csgo", "VALORANT-Win64-Shipping", "FortniteClient-Win64-Shipping", "r5apex", "r5apex_dx12", "League of Legends",
            "RobloxPlayerBeta", "Minecraft.Windows", "GTA5", "GTA5_Enhanced", "RDR2", "eldenring", "Overwatch", "RainbowSix", "RainbowSix_Vulkan",
            "cod", "RocketLeague", "dota2", "TslGame", "EscapeFromTarkov", "Cyberpunk2077", "witcher3", "BF2042", "destiny2", "Warframe.x64",
            "PathOfExile_x64", "Palworld-Win64-Shipping", "Marvel-Win64-Shipping", "FC25", "FC26", "ForzaHorizon5", "HaloInfinite", "Starfield", "bg3", "bg3_dx11",
            "RustClient", "DeadByDaylight-Win64-Shipping", "GenshinImpact", "ZenlessZoneZero", "StarRail", "WorldOfTanks", "Hearthstone", "Wow",
            "Diablo IV", "SoTGame", "HogwartsLegacy", "MonsterHunterWilds", "osu!", "FiveM", "javaw"
        };

        static readonly string[] Background =
        {
            "OneDrive", "SearchIndexer", "SearchHost", "PhoneExperienceHost", "Widgets", "WidgetService", "YourPhone",
            "GoogleDriveFS", "Dropbox", "CompPkgSrv", "backgroundTaskHost", "MicrosoftEdgeUpdate", "GoogleUpdate"
        };

        Timer _timer;
        readonly Dictionary<int, ProcessPriorityClass> _lowered = new();
        public string ActiveGame { get; private set; }
        public event Action<string> ActiveGameChanged;

        public void Start() { _timer ??= new Timer(_ => Tick(), null, 1000, 3000); }
        public void Stop() { _timer?.Dispose(); _timer = null; RestoreBackground(); SetActive(null); }

        void SetActive(string g)
        {
            if (g == ActiveGame) return;
            ActiveGame = g;
            ActiveGameChanged?.Invoke(g);
        }

        void Tick()
        {
            try
            {
                var s = Settings.Current;
                if (!s.BoosterEnabled) { RestoreBackground(); SetActive(null); return; }
                var games = new HashSet<string>(s.Games ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                string found = null;
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                    {
                        try
                        {
                            if (!games.Contains(p.ProcessName)) continue;
                            found ??= p.ProcessName;
                            if (p.PriorityClass != ProcessPriorityClass.High) p.PriorityClass = ProcessPriorityClass.High;
                        }
                        catch { /* processo protegido por anti-cheat: ignorar */ }
                    }
                }
                SetActive(found);

                if (found != null && s.BoosterThrottleBackground) LowerBackground();
                else RestoreBackground();
            }
            catch (Exception ex) { Sys.Log("booster: " + ex.Message); }
        }

        void LowerBackground()
        {
            foreach (var name in Background)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try
                        {
                            if (_lowered.ContainsKey(p.Id)) continue;
                            var prev = p.PriorityClass;
                            if (prev == ProcessPriorityClass.Idle || prev == ProcessPriorityClass.BelowNormal) continue;
                            p.PriorityClass = ProcessPriorityClass.BelowNormal;
                            _lowered[p.Id] = prev;
                        }
                        catch { }
                    }
                }
            }
        }

        void RestoreBackground()
        {
            if (_lowered.Count == 0) return;
            foreach (var kv in _lowered.ToList())
            {
                try { using var p = Process.GetProcessById(kv.Key); p.PriorityClass = kv.Value; } catch { }
            }
            _lowered.Clear();
        }

        public void Dispose() => Stop();
    }

    // ───────────────────────── Rede ─────────────────────────
    public static class NetTools
    {
        public static readonly (string Name, string Primary, string Secondary)[] DnsServers =
        {
            ("Cloudflare", "1.1.1.1", "1.0.0.1"),
            ("Google", "8.8.8.8", "8.8.4.4"),
            ("Quad9", "9.9.9.9", "149.112.112.112"),
            ("OpenDNS", "208.67.222.222", "208.67.220.220"),
            ("AdGuard", "94.140.14.14", "94.140.15.15"),
        };

        public static async Task<long> PingAsync(string host, int timeout = 1500)
        {
            try
            {
                using var p = new Ping();
                var r = await p.SendPingAsync(host, timeout);
                return r.Status == IPStatus.Success ? r.RoundtripTime : -1;
            }
            catch { return -1; }
        }

        /// <summary>Mede o tempo real de resposta DNS (média de várias consultas).</summary>
        public static async Task<double> DnsQueryMsAsync(string server, int tries = 4)
        {
            var times = new List<double>();
            string[] names = { "www.google.com", "www.microsoft.com", "discord.com", "store.steampowered.com", "www.youtube.com" };
            for (int i = 0; i < tries; i++)
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.ReceiveTimeout = 1500;
                    var q = BuildQuery(names[i % names.Length]);
                    var sw = Stopwatch.StartNew();
                    await udp.SendAsync(q, q.Length, new IPEndPoint(IPAddress.Parse(server), 53));
                    var recv = udp.ReceiveAsync();
                    if (await Task.WhenAny(recv, Task.Delay(1500)) != recv) continue;
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch { }
            }
            return times.Count == 0 ? -1 : times.Average();
        }

        static byte[] BuildQuery(string name)
        {
            var b = new List<byte>();
            var id = (ushort)Random.Shared.Next(ushort.MaxValue);
            b.AddRange(new byte[] { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 });
            foreach (var label in name.Split('.')) { b.Add((byte)label.Length); b.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
            b.AddRange(new byte[] { 0, 0, 1, 0, 1 });
            return b.ToArray();
        }

        public static List<string> ActiveAdapters() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                         && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                .Select(n => n.Name).ToList();

        public static int SetDns(string primary, string secondary)
        {
            int n = 0;
            foreach (var a in ActiveAdapters())
            {
                var r = Sys.Run("netsh.exe", $"interface ipv4 set dnsservers name=\"{a}\" static {primary} primary validate=no");
                if (r.Code == 0) { Sys.Run("netsh.exe", $"interface ipv4 add dnsservers name=\"{a}\" {secondary} index=2 validate=no"); n++; }
            }
            Sys.Run("ipconfig.exe", "/flushdns");
            return n;
        }

        public static void ResetDns()
        {
            foreach (var a in ActiveAdapters()) Sys.Run("netsh.exe", $"interface ipv4 set dnsservers name=\"{a}\" source=dhcp");
            Sys.Run("ipconfig.exe", "/flushdns");
        }
    }

    // ───────────────────────── Atualizações (Velopack, igual ao Discord/Squirrel) ─────────────────────────
    public static class Updater
    {
        static UpdateManager _mgr;
        static UpdateInfo _pending;

        public static string CurrentVersion
        {
            get
            {
                try { var m = Mgr(); if (m.IsInstalled) return m.CurrentVersion?.ToString(); } catch { }
                return typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            }
        }

        static UpdateManager Mgr()
        {
            if (_mgr != null) return _mgr;
            string src = Settings.Current.UpdateSource?.Trim() ?? "";
            IUpdateSource source = src.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(src, null, false)
                : new SimpleWebSource(src);
            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Directory.Exists(src))
                source = new SimpleFileSource(new DirectoryInfo(src));
            _mgr = new UpdateManager(source);
            return _mgr;
        }

        public static void ResetSource() { _mgr = null; _pending = null; }

        public static bool IsInstalled { get { try { return Mgr().IsInstalled; } catch { return false; } } }

        /// <returns>versão nova, ou null se não houver</returns>
        public static async Task<string> CheckAsync()
        {
            var m = Mgr();
            if (!m.IsInstalled) throw new InvalidOperationException("not-installed");
            _pending = await m.CheckForUpdatesAsync();
            return _pending?.TargetFullRelease?.Version?.ToString();
        }

        static bool _downloaded;
        public static bool Downloaded => _downloaded;

        /// <summary>Transfere a atualização em segundo plano (como o Discord). Fica pronta a instalar.</summary>
        public static async Task DownloadAsync(Action<int> progress = null)
        {
            if (_pending == null || _downloaded) return;
            await Mgr().DownloadUpdatesAsync(_pending, progress);
            _downloaded = true;
        }

        public static async Task DownloadAndRestartAsync(Action<int> progress)
        {
            if (_pending == null) return;
            await DownloadAsync(progress);
            Mgr().ApplyUpdatesAndRestart(_pending, new[] { "--updated" });
        }

        /// <summary>Se houver uma atualização transferida, instala-a quando a app fechar.</summary>
        public static void ApplyOnExit()
        {
            try { if (_downloaded && _pending != null) Mgr().WaitExitThenApplyUpdates(_pending, true, false, Array.Empty<string>()); }
            catch (Exception ex) { Sys.Log("apply on exit: " + ex.Message); }
        }
    }

    // ───────────────────────── Nativo ─────────────────────────
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        public static (uint load, double usedGb, double totalGb) Memory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
            return (m.dwMemoryLoad, (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0, m.ullTotalPhys / 1073741824.0);
        }

        public static long TotalRamKb()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref m) ? (long)(m.ullTotalPhys / 1024) : 3670016;
        }

        static long _pi, _pk, _pu;
        public static double CpuUsage()
        {
            if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
            long di = i - _pi, dk = k - _pk, du = u - _pu;
            _pi = i; _pk = k; _pu = u;
            long total = dk + du;
            return total <= 0 ? 0 : Math.Clamp((total - di) * 100.0 / total, 0, 100);
        }
    }
}
