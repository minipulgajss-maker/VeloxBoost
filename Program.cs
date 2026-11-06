using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using Velopack;
using VeloxBoost.Core;

namespace VeloxBoost
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Hooks do instalador/atualizador (tem de ser a primeira coisa a correr)
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => { try { Engine.SetStartup(false); } catch { } })
                .Run();

            if (args.Contains("--count"))
            {
                Catalog.Build();
                Console.WriteLine(Catalog.All.Count);
                foreach (var g in Catalog.All.GroupBy(t => t.Category)) Console.WriteLine($"{g.Key}: {g.Count()}");
                return;
            }

            // modo de desenvolvimento: gera capturas das páginas (só leitura, sem administrador)
            int snap = Array.IndexOf(args, "--snapshot");
            if (snap >= 0 && snap + 1 < args.Length)
            {
                Settings.Load(); L.I.SetLang(args.Length > snap + 2 ? args[snap + 2] : Settings.Current.Language); Catalog.Build();
                var sapp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var w = new UI.MainWindow(false, snapshotMode: true);
                w.Loaded += async (_, _) => { await w.SnapshotAsync(args[snap + 1]); sapp.Shutdown(); };
                sapp.Run();
                return;
            }

            if (!IsAdmin())
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath, string.Join(" ", args.Select(a => "\"" + a + "\"")))
                    { UseShellExecute = true, Verb = "runas" });
                }
                catch
                {
                    Settings.Load(); L.I.SetLang(Settings.Current.Language);
                    MessageBox.Show(L.T("msg.admin"), "VeloxBoost", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            using var mutex = new Mutex(true, @"Global\VeloxBoost_SingleInstance", out bool first);
            if (!first)
            {
                // já está aberto: pede à instância existente para aparecer
                try { using var ev = EventWaitHandle.OpenExisting("VeloxBoost_Show"); ev.Set(); } catch { }
                return;
            }

            Settings.Load();
            L.I.SetLang(Settings.Current.Language);
            Catalog.Build();

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Sys.Log("UI: " + e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Sys.Log("FATAL: " + e.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Sys.Log("Task: " + e.Exception); e.SetObserved(); };

            var win = new UI.MainWindow(args.Contains("--minimized"));
            app.Run();
        }

        static bool IsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
