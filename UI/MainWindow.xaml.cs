using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VeloxBoost.Core;
using WinForms = System.Windows.Forms;

namespace VeloxBoost.UI
{
    public partial class MainWindow : Window
    {
        readonly GameBooster _booster = new();
        readonly Dictionary<string, List<TweakVM>> _vms = new();
        readonly ObservableCollection<LogVM> _log = new();
        readonly ObservableCollection<string> _games = new();
        readonly ObservableCollection<DnsResultVM> _dns = new();
        readonly Dictionary<string, (TextBlock count, Border bar, Grid track)> _catCards = new();
        readonly Dictionary<string, TextBlock> _navBadges = new();
        readonly Dictionary<string, RadioButton> _nav = new();
        readonly DispatcherTimer _stats = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        WinForms.NotifyIcon _tray;
        WinForms.ToolStripMenuItem _trayOpen, _trayOpt, _trayExit;
        string _cat;
        bool _reallyClose, _busy, _trayHintShown, _statesReady;
        int _tick;

        static readonly Brush DotGood = MakeBrush("#34D399"), DotWarn = MakeBrush("#FBBF24"), DotBad = MakeBrush("#F87171"), DotInfo = MakeBrush("#A78BFA");
        static Brush MakeBrush(string c) { var b = (Brush)new BrushConverter().ConvertFromString(c); b.Freeze(); return b; }

        readonly bool _snapshot;
        public MainWindow(bool startMinimized, bool snapshotMode = false)
        {
            _snapshot = snapshotMode;
            InitializeComponent();
            SourceInitialized += (_, _) =>
            {
                int round = 2; // DWMWCP_ROUND – cantos arredondados nativos do Windows 11
                try { Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
            };
            StateChanged += (_, _) =>
            {
                Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
            };
            Closing += OnClosing;

            foreach (var t in Catalog.All)
            {
                if (!_vms.TryGetValue(t.Category, out var l)) _vms[t.Category] = l = new List<TweakVM>();
                l.Add(new TweakVM(t));
            }

            LogList.ItemsSource = _log;
            GameList.ItemsSource = _games;
            DnsList.ItemsSource = _dns;
            foreach (var g in Settings.Current.Games) _games.Add(g);
            foreach (var d in NetTools.DnsServers) _dns.Add(new DnsResultVM { Name = d.Name, Ip = d.Primary });

            BuildNav();
            BuildCategoryCards();
            BuildSettings();
            if (!_snapshot) BuildTray();

            (Settings.Current.Profile switch { 0 => ProfSafe, 2 => ProfExt, _ => ProfBal }).IsChecked = true;
            BoostAuto.IsChecked = Settings.Current.BoosterEnabled;
            BoostBg.IsChecked = Settings.Current.BoosterThrottleBackground;

            BackupPath.Text = Sys.DataDir;

            L.I.LanguageChanged += OnLanguageChanged;
            OnLanguageChanged();

            _booster.ActiveGameChanged += g => Dispatcher.BeginInvoke(() => UpdateBoostStatus(g));
            if (!_snapshot) _booster.Start();
            UpdateBoostStatus(null);

            _stats.Tick += Stats_Tick;
            _stats.Start();

            // procura atualizações a cada 3 horas enquanto a app está aberta (também na bandeja)
            var updTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(3) };
            updTimer.Tick += async (_, _) => { if (Settings.Current.CheckUpdates && !_snapshot && !Updater.Downloaded) await CheckUpdatesAsync(silent: true); };
            updTimer.Start();

            if (!_snapshot) ListenForShowRequests();
            StartAmbientAnimations();
            ShowPage("dashboard");
            AddLog($"VeloxBoost {Updater.CurrentVersion} — {Catalog.All.Count} " + L.T("dash.loaded"), DotInfo);

            if (!startMinimized) Show();
            _ = InitAsync();
        }

        // ═════════════════════════ Arranque ═════════════════════════
        async Task InitAsync()
        {
            await RefreshStatesAsync(Catalog.All);
            _statesReady = true;

            if (Settings.Current.ReapplyOnStartup && Settings.Current.AppliedIds.Count > 0)
            {
                var r = await Engine.ReapplyAsync();
                if (r.Ok > 0) AddLog(L.F("dash.done", r.Ok, r.Failed, 0), DotGood);
                await RefreshStatesAsync(Catalog.All.Where(t => Settings.Current.AppliedIds.Contains(t.Id)));
            }

            if (!_snapshot) await CheckUpdatesAsync(silent: true);
        }

        async Task RefreshStatesAsync(IEnumerable<Tweak> tweaks)
        {
            var list = tweaks.ToList();
            await Task.Run(() => Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = 6 }, StateCache.Update));
            var ids = new HashSet<string>(list.Select(t => t.Id));
            foreach (var vm in _vms.Values.SelectMany(v => v)) if (ids.Contains(vm.T.Id)) vm.Sync();
            if (PageTweaks.Visibility == Visibility.Visible) ApplyFilter();
            RefreshCounts();
        }

        // ═════════════════════════ Navegação ═════════════════════════
        void BuildNav()
        {
            AddNav("dashboard", "", "nav.dashboard");
            AddSection("nav.section.opt");
            foreach (var c in Catalog.Categories) AddNav("cat:" + c, Catalog.Glyphs[c], "cat." + c, badge: true);
            AddSection("nav.section.tools");
            AddNav("booster", "", "nav.booster");
            AddNav("backup", "", "nav.backup");
            AddNav("settings", "", "nav.settings");
        }

        void AddSection(string key)
        {
            var t = new TextBlock { Style = (Style)FindResource("Caps"), Margin = new Thickness(12, 16, 0, 6) };
            t.SetBinding(TextBlock.TextProperty, new Binding($"[{key}]") { Source = L.I });
            NavPanel.Children.Add(t);
        }

        void AddNav(string key, string glyph, string titleKey, bool badge = false)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 15, Margin = new Thickness(0, 0, 12, 0) };
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetBinding(TextBlock.TextProperty, new Binding($"[{titleKey}]") { Source = L.I });
            Grid.SetColumn(title, 1);
            g.Children.Add(icon); g.Children.Add(title);
            if (badge)
            {
                var b = new TextBlock { FontSize = 10.5, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
                _navBadges[key.Substring(4)] = b;
            }
            var rb = new RadioButton { GroupName = "nav", Style = (Style)FindResource("NavBtn"), Content = g, Tag = key };
            rb.Checked += (_, _) => Navigate(key);
            _nav[key] = rb;
            NavPanel.Children.Add(rb);
        }

        void Navigate(string key)
        {
            if (key.StartsWith("cat:")) ShowPage("tweaks", key.Substring(4));
            else ShowPage(key);
        }

        void SelectNav(string key) { if (_nav.TryGetValue(key, out var rb) && rb.IsChecked != true) rb.IsChecked = true; }

        void ShowPage(string page, string cat = null)
        {
            FrameworkElement target = page switch
            {
                "tweaks" => PageTweaks,
                "booster" => PageBooster,
                "backup" => PageBackup,
                "settings" => PageSettings,
                _ => PageDashboard
            };
            foreach (FrameworkElement p in PageHost.Children) if (p != target) p.Visibility = Visibility.Collapsed;

            if (page == "tweaks")
            {
                _cat = cat;
                SearchBox.Text = "";
                LoadCategory();
            }
            if (page == "backup") BackupCount.Text = L.F("bk.entries", BackupStore.Count);
            SelectNav(page == "tweaks" ? "cat:" + cat : page);
            RefreshCounts();

            target.Visibility = Visibility.Visible;
            if (_snapshot) { target.BeginAnimation(OpacityProperty, null); target.Opacity = 1; target.RenderTransform = null; return; }
            var tt = new TranslateTransform(0, 16);
            target.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        }

        void LoadCategory()
        {
            if (_cat == null) return;
            CatGlyph.Text = Catalog.Glyphs[_cat];
            CatTitle.Text = L.T("cat." + _cat);
            CatDesc.Text = L.T("catd." + _cat);
            bool cleanup = _cat == "cleanup";
            BtnApplyAll.Visibility = cleanup ? Visibility.Collapsed : Visibility.Visible;
            BtnRevertAll.Visibility = cleanup ? Visibility.Collapsed : Visibility.Visible;
            BtnApplyRecText.Text = cleanup ? L.T("tw.runAll") : L.T("tw.applyRec");
            ApplyFilter();
            UpdateCatHeader();
        }

        void ApplyFilter()
        {
            if (_cat == null) return;
            string q = SearchBox.Text?.Trim() ?? "";
            IEnumerable<TweakVM> items = _vms[_cat].Where(v => v.Applicable);
            if (q.Length > 0)
                items = Catalog.All.Where(t => t.NamePt.Contains(q, StringComparison.OrdinalIgnoreCase) || t.NameEn.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Details.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Select(t => _vms[t.Category].First(v => v.T == t)).Where(v => v.Applicable && _vms.ContainsKey(v.T.Category));
            TweakList.ItemsSource = items.ToList();
        }

        void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

        // ═════════════════════════ Cartões de categoria ═════════════════════════
        void BuildCategoryCards()
        {
            foreach (var c in Catalog.Categories)
            {
                var card = new Border { Style = (Style)FindResource("HoverCard"), Margin = new Thickness(6), Padding = new Thickness(16, 14, 16, 14), Cursor = Cursors.Hand };
                var sp = new StackPanel();
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.ColumnDefinitions.Add(new ColumnDefinition());
                var ic = new Grid { Width = 34, Height = 34 };
                ic.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = (Brush)FindResource("AccentGrad"), Opacity = 0.9 });
                ic.Children.Add(new TextBlock { Text = Catalog.Glyphs[c], Style = (Style)FindResource("Icon"), Foreground = Brushes.White, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center });
                var name = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                name.SetBinding(TextBlock.TextProperty, new Binding($"[cat.{c}]") { Source = L.I });
                Grid.SetColumn(name, 1);
                top.Children.Add(ic); top.Children.Add(name);
                var count = new TextBlock { FontSize = 11.5, Foreground = (Brush)FindResource("Sub"), Margin = new Thickness(0, 12, 0, 6) };
                var track = new Grid { Height = 4 };
                track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255)) });
                var bar = new Border { CornerRadius = new CornerRadius(2), Background = (Brush)FindResource("AccentGrad"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                track.Children.Add(bar);
                sp.Children.Add(top); sp.Children.Add(count); sp.Children.Add(track);
                card.Child = sp;
                string key = "cat:" + c;
                card.MouseLeftButtonUp += (_, _) => SelectNav(key);
                CatGrid.Children.Add(card);
                _catCards[c] = (count, bar, track);
                track.SizeChanged += (_, _) => RefreshCounts();
            }
        }

        void RefreshCounts()
        {
            int totalApplicable = 0, totalApplied = 0;
            foreach (var c in Catalog.Categories)
            {
                var list = _vms[c];
                int applicable, applied;
                if (c == "cleanup")
                {
                    applicable = list.Count(v => v.Applicable);
                    applied = applicable;
                    if (_navBadges.TryGetValue(c, out var nb)) nb.Text = applicable.ToString();
                    if (_catCards.TryGetValue(c, out var cc)) { cc.count.Text = $"{applicable} • {L.T("tw.run")}"; SetBar(cc.bar, cc.track.ActualWidth, 1); }
                    continue;
                }
                applicable = list.Count(v => v.Applicable);
                applied = list.Count(v => v.IsOn);
                totalApplicable += applicable; totalApplied += applied;
                if (_navBadges.TryGetValue(c, out var b)) b.Text = $"{applied}/{applicable}";
                if (_catCards.TryGetValue(c, out var card))
                {
                    card.count.Text = L.F("tw.count", applied, applicable);
                    SetBar(card.bar, card.track.ActualWidth, applicable == 0 ? 0 : applied / (double)applicable);
                }
            }

            int total = Catalog.All.Count;
            AppliedText.Text = $"{totalApplied}";
            AppliedText.ToolTip = $"{totalApplied} / {totalApplicable}  ({total} {L.T("dash.total").ToLowerInvariant()})";
            SetBar(AppliedBar, ((FrameworkElement)AppliedBar.Parent).ActualWidth, totalApplicable == 0 ? 0 : totalApplied / (double)totalApplicable);
            if (_statesReady || totalApplied > 0) AnimateScore(totalApplicable == 0 ? 0 : totalApplied * 100.0 / totalApplicable);
            if (PageTweaks.Visibility == Visibility.Visible) UpdateCatHeader();
        }

        void UpdateCatHeader()
        {
            if (_cat == null) return;
            var list = _vms[_cat];
            if (_cat == "cleanup")
            {
                CatCount.Text = $"{list.Count(v => v.Applicable)} • {L.T("tw.run")}";
                SetBar(CatBar, 160, 1);
                return;
            }
            int a = list.Count(v => v.Applicable), on = list.Count(v => v.IsOn);
            CatCount.Text = L.F("tw.count", on, a);
            SetBar(CatBar, 160, a == 0 ? 0 : on / (double)a);
        }

        static void SetBar(Border bar, double full, double pct)
        {
            if (full <= 0 || double.IsNaN(full)) return;
            double to = Math.Max(0, Math.Min(1, pct)) * full;
            double from = double.IsNaN(bar.Width) ? 0 : bar.Width;
            bar.BeginAnimation(WidthProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ═════════════════════════ Anel de pontuação ═════════════════════════
        public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(nameof(Score), typeof(double), typeof(MainWindow),
            new PropertyMetadata(0.0, (d, e) => ((MainWindow)d).DrawRing((double)e.NewValue)));
        public double Score { get => (double)GetValue(ScoreProperty); set => SetValue(ScoreProperty, value); }

        void AnimateScore(double to) =>
            BeginAnimation(ScoreProperty, new DoubleAnimation(Score, to, TimeSpan.FromMilliseconds(1100)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });

        void DrawRing(double pct)
        {
            ScoreText.Text = ((int)Math.Round(pct)).ToString();
            if (pct <= 0.05) { RingPath.Data = null; return; }
            const double c = 95, r = 88;
            double a = Math.Min(pct, 99.99) / 100 * 2 * Math.PI;
            var start = new Point(c, c - r);
            var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            RingPath.Data = new PathGeometry(new[] { fig });
        }

        // ═════════════════════════ Otimizar ═════════════════════════
        void Profile_Checked(object sender, RoutedEventArgs e)
        {
            int p = int.Parse((string)((FrameworkElement)sender).Tag);
            Settings.Current.Profile = p;
            Settings.Save();
            ProfileDesc.Text = L.T(p switch { 0 => "prof.safe.d", 2 => "prof.extreme.d", _ => "prof.balanced.d" });
        }

        async void Optimize_Click(object sender, RoutedEventArgs e) => await OptimizeAsync();

        async Task OptimizeAsync()
        {
            if (_busy) return;
            int p = Settings.Current.Profile;
            if (p == 2 && !Confirm(L.T("msg.extreme"))) return;
            _busy = true;
            OptimizeBtn.IsEnabled = false;
            OptimizeText.Text = L.T("dash.optimizing");
            OptProgress.Visibility = Visibility.Visible;
            SetProgress(0, L.T("msg.rp.creating"));
            try
            {
                if (Settings.Current.RestorePointFirst)
                {
                    bool ok = await Task.Run(() => Engine.CreateRestorePoint("VeloxBoost"));
                    AddLog(L.T(ok ? "msg.rp.ok" : "msg.rp.fail"), ok ? DotGood : DotWarn);
                }
                var prog = new Progress<(int done, int total, string name)>(x => SetProgress(x.total == 0 ? 1 : x.done / (double)x.total, x.name));
                var r = await Engine.ApplyAsync(Catalog.ForProfile(p), prog);
                long mb = r.Freed / 1048576;
                string msg = L.F("dash.done", r.Ok, r.Failed, mb);
                AddLog(msg, r.Failed == 0 ? DotGood : DotWarn);
                foreach (var err in r.Errors.Take(5)) AddLog(err, DotBad);
                if (r.NeedsReboot) AddLog(L.T("dash.reboot"), DotInfo);
                ShowToast(msg);
                await RefreshStatesAsync(Catalog.All);
            }
            catch (Exception ex) { Sys.Log("optimize: " + ex); AddLog(ex.Message, DotBad); }
            finally
            {
                _busy = false;
                OptimizeBtn.IsEnabled = true;
                OptimizeText.Text = L.T("dash.optimize");
                await Task.Delay(1500);
                OptProgress.Visibility = Visibility.Collapsed;
            }
        }

        void SetProgress(double pct, string text)
        {
            ProgressText.Text = text;
            ProgressPct.Text = $"{pct * 100:0}%";
            var parent = (FrameworkElement)ProgressFill.Parent;
            ProgressFill.Width = Math.Max(0, parent.ActualWidth * pct);
        }

        // ═════════════════════════ Otimizações individuais ═════════════════════════
        async void Tweak_Toggle(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not TweakVM vm) return;
            bool want = cb.IsChecked == true;
            vm.Busy = true;
            try
            {
                var r = want ? await Engine.ApplyAsync(new[] { vm.T }) : await Engine.RevertAsync(new[] { vm.T });
                await Task.Run(() => StateCache.Update(vm.T));
                vm.Sync();
                if (r.Failed > 0) { AddLog(L.F("msg.failed", vm.Name) + " — " + r.Errors.FirstOrDefault(), DotBad); ShowToast(L.F("msg.failed", vm.Name)); }
                else
                {
                    AddLog(L.F(want ? "msg.applied" : "msg.revertedOne", vm.Name), want ? DotGood : DotInfo);
                    if (want && vm.T.NeedsReboot) ShowToast(L.F("msg.applied", vm.Name) + " — " + L.T("dash.reboot"));
                }
            }
            finally { vm.Busy = false; RefreshCounts(); }
        }

        async void Tweak_Run(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not TweakVM vm) return;
            vm.Busy = true;
            try
            {
                var r = await Engine.ApplyAsync(new[] { vm.T });
                string msg = vm.T.Category == "cleanup" ? $"{vm.Name}: {L.F("tw.freed", r.Freed / 1048576)}" : L.F(r.Failed == 0 ? "msg.applied" : "msg.failed", vm.Name);
                AddLog(msg, r.Failed == 0 ? DotGood : DotBad);
                ShowToast(msg);
            }
            finally { vm.Busy = false; }
        }

        async Task RunBulk(IEnumerable<TweakVM> items, bool apply)
        {
            if (_busy) return;
            _busy = true;
            var list = items.Where(v => v.Applicable).ToList();
            foreach (var v in list) v.Busy = true;
            try
            {
                var r = apply ? await Engine.ApplyAsync(list.Select(v => v.T)) : await Engine.RevertAsync(list.Select(v => v.T));
                await RefreshStatesAsync(list.Select(v => v.T));
                string msg = apply ? L.F("dash.done", r.Ok, r.Failed, r.Freed / 1048576) : L.F("msg.reverted", r.Ok);
                AddLog(msg, r.Failed == 0 ? DotGood : DotWarn);
                foreach (var err in r.Errors.Take(3)) AddLog(err, DotBad);
                if (apply && r.NeedsReboot) AddLog(L.T("dash.reboot"), DotInfo);
                ShowToast(msg);
            }
            finally { foreach (var v in list) v.Busy = false; _busy = false; RefreshCounts(); }
        }

        async void ApplyRec_Click(object sender, RoutedEventArgs e)
        {
            if (_cat == null) return;
            int p = Math.Min(Settings.Current.Profile, 1);
            await RunBulk(_vms[_cat].Where(v => (int)v.T.Risk <= p && !v.IsOn), true);
        }

        async void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_cat == null) return;
            var items = _vms[_cat].Where(v => !v.IsOn).ToList();
            if (items.Any(v => v.T.Risk == Risk.Advanced) && !Confirm(L.T("msg.extreme"))) return;
            await RunBulk(items, true);
        }

        async void RevertCat_Click(object sender, RoutedEventArgs e)
        {
            if (_cat == null || !Confirm(L.T("msg.revertall"))) return;
            await RunBulk(_vms[_cat].Where(v => !v.T.IsAction), false);
        }

        // ═════════════════════════ Game Booster ═════════════════════════
        void BoostSetting_Click(object sender, RoutedEventArgs e)
        {
            Settings.Current.BoosterEnabled = BoostAuto.IsChecked == true;
            Settings.Current.BoosterThrottleBackground = BoostBg.IsChecked == true;
            Settings.Save();
            UpdateBoostStatus(_booster.ActiveGame);
        }

        void UpdateBoostStatus(string game)
        {
            BoostStatusText.Text = game != null ? L.F("boost.detected", game) : L.T("boost.none");
            GamePill.Visibility = game != null ? Visibility.Visible : Visibility.Collapsed;
            GamePillText.Text = game != null ? L.F("boost.detected", game) : "";
            if (game != null) AddLog(L.F("boost.detected", game), DotGood);
        }

        void AddGame_Click(object sender, RoutedEventArgs e) => AddGame();
        void GameInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddGame(); }

        void AddGame()
        {
            string n = (GameInput.Text ?? "").Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            if (n.Length == 0 || Settings.Current.Games.Contains(n, StringComparer.OrdinalIgnoreCase)) { GameInput.Text = ""; return; }
            Settings.Current.Games.Insert(0, n);
            _games.Insert(0, n);
            Settings.Save();
            GameInput.Text = "";
        }

        void RemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string n)
            {
                Settings.Current.Games.RemoveAll(g => string.Equals(g, n, StringComparison.OrdinalIgnoreCase));
                _games.Remove(n);
                Settings.Save();
            }
        }

        // ═════════════════════════ Rede ═════════════════════════
        async void DnsTest_Click(object sender, RoutedEventArgs e)
        {
            foreach (var d in _dns) { d.Ms = -2; d.Best = false; }
            ShowToast(L.T("net.testing"));
            await Task.WhenAll(_dns.Select(async d => { var ms = await NetTools.DnsQueryMsAsync(d.Ip); await Dispatcher.InvokeAsync(() => d.Ms = ms); }));
            var best = _dns.Where(d => d.Ms > 0).OrderBy(d => d.Ms).FirstOrDefault();
            if (best != null) { best.Best = true; BestDnsBtn.IsEnabled = true; AddLog($"DNS: {best.Name} {best.Ms:0} ms", DotGood); }
        }

        async void BestDns_Click(object sender, RoutedEventArgs e)
        {
            var best = _dns.FirstOrDefault(d => d.Best);
            if (best == null) return;
            var srv = NetTools.DnsServers.First(s => s.Name == best.Name);
            int n = await Task.Run(() => NetTools.SetDns(srv.Primary, srv.Secondary));
            string msg = L.F("net.applied", best.Name, n);
            AddLog(msg, DotGood); ShowToast(msg);
        }

        async void ResetDns_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(NetTools.ResetDns);
            AddLog(L.T("net.reset"), DotInfo); ShowToast(L.T("net.reset"));
        }

        async void FlushDns_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => Sys.Run("ipconfig.exe", "/flushdns"));
            AddLog(L.T("net.flushed"), DotGood); ShowToast(L.T("net.flushed"));
        }

        // ═════════════════════════ Backup ═════════════════════════
        async void RestorePoint_Click(object sender, RoutedEventArgs e)
        {
            ShowToast(L.T("msg.rp.creating"));
            bool ok = await Task.Run(() => Engine.CreateRestorePoint("VeloxBoost"));
            string m = L.T(ok ? "msg.rp.ok" : "msg.rp.fail");
            AddLog(m, ok ? DotGood : DotWarn); ShowToast(m);
        }

        async void RevertEverything_Click(object sender, RoutedEventArgs e)
        {
            if (!Confirm(L.T("msg.revertall"))) return;
            await RunBulk(_vms.Values.SelectMany(v => v).Where(v => !v.T.IsAction), false);
            BackupCount.Text = L.F("bk.entries", BackupStore.Count);
        }

        void OpenData_Click(object sender, RoutedEventArgs e) => Open(Sys.DataDir);
        void Rstrui_Click(object sender, RoutedEventArgs e) => Open(System.IO.Path.Combine(Environment.SystemDirectory, "rstrui.exe"));

        static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Sys.Log("open: " + ex.Message); }
        }

        // ═════════════════════════ Definições ═════════════════════════
        void BuildSettings()
        {
            foreach (var (code, name) in L.Languages)
            {
                var rb = new RadioButton { GroupName = "lang", Style = (Style)FindResource("DarkChip"), Content = new TextBlock { Text = name, Foreground = (Brush)FindResource("Text") }, Margin = new Thickness(0, 0, 8, 8) };
                rb.IsChecked = Settings.Current.Language == code;
                rb.Checked += (_, _) => { Settings.Current.Language = code; Settings.Save(); L.I.SetLang(code); };
                LangPanel.Children.Add(rb);
            }

            AddSettingRow("set.startup", "set.startup.d", () => Settings.Current.StartWithWindows, v => { Settings.Current.StartWithWindows = v; Engine.SetStartup(v); });
            AddSettingRow("set.reapply", "set.reapply.d", () => Settings.Current.ReapplyOnStartup, v => Settings.Current.ReapplyOnStartup = v);
            AddSettingRow("set.restorepoint", "set.restorepoint.d", () => Settings.Current.RestorePointFirst, v => Settings.Current.RestorePointFirst = v);
            AddSettingRow("set.tray", "set.tray.d", () => Settings.Current.CloseToTray, v => Settings.Current.CloseToTray = v, last: true);
        }

        void AddSettingRow(string titleKey, string descKey, Func<bool> get, Action<bool> set, bool last = false)
        {
            var row = new Border { Padding = new Thickness(20, 16, 20, 16), BorderBrush = (Brush)FindResource("Stroke"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var t = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold };
            t.SetBinding(TextBlock.TextProperty, new Binding($"[{titleKey}]") { Source = L.I });
            var d = new TextBlock { Style = (Style)FindResource("SubT"), Margin = new Thickness(0, 3, 0, 0), FontSize = 12 };
            d.SetBinding(TextBlock.TextProperty, new Binding($"[{descKey}]") { Source = L.I });
            sp.Children.Add(t); sp.Children.Add(d);
            var cb = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
            cb.Click += (_, _) => { set(cb.IsChecked == true); Settings.Save(); };
            Grid.SetColumn(cb, 1);
            g.Children.Add(sp); g.Children.Add(cb);
            row.Child = g;
            SettingsRows.Children.Add(row);
        }

        void OnLanguageChanged()
        {
            foreach (var vm in _vms.Values.SelectMany(v => v)) vm.RefreshTexts();
            ProfileDesc.Text = L.T(Settings.Current.Profile switch { 0 => "prof.safe.d", 2 => "prof.extreme.d", _ => "prof.balanced.d" });
            VersionText.Text = "VeloxBoost — " + L.F("set.version", Updater.CurrentVersion);
            SideVersion.Text = "VeloxBoost " + Updater.CurrentVersion;
            if (string.IsNullOrEmpty(SideUpdate.Text) || !_updateAvailable) SideUpdate.Text = L.T("app.tagline");
            if (!_optimizing()) OptimizeText.Text = L.T("dash.optimize");
            if (_trayOpen != null) { _trayOpen.Text = L.T("tray.open"); _trayOpt.Text = L.T("tray.optimize"); _trayExit.Text = L.T("tray.exit"); }
            UpdateBoostStatus(_booster.ActiveGame);
            BackupCount.Text = L.F("bk.entries", BackupStore.Count);
            if (_cat != null) LoadCategory();
            RefreshCounts();
        }
        bool _optimizing() => _busy && !OptimizeBtn.IsEnabled;

        // ═════════════════════════ Atualizações ═════════════════════════
        bool _updateAvailable;

        async Task CheckUpdatesAsync(bool silent)
        {
            CheckUpdBtn.IsEnabled = false;
            UpdateStatus.Text = L.T("set.upd.checking");
            try
            {
                var v = await Updater.CheckAsync();
                if (v == null) { UpdateStatus.Text = L.T("set.upd.none"); InstallUpdBtn.Visibility = Visibility.Collapsed; }
                else
                {
                    _updateAvailable = true;
                    UpdateStatus.Text = L.F("set.upd.available", v);
                    SideUpdate.Text = L.F("set.upd.available", v);
                    UpdDot.Fill = (Brush)FindResource("AccentGrad3");
                    InstallUpdBtn.Visibility = Visibility.Visible;
                    AddLog(L.F("set.upd.available", v), DotInfo);
                    ShowToast(L.F("set.upd.available", v));
                    // como o Discord: transfere já em segundo plano; instala ao reiniciar ou ao sair
                    try
                    {
                        await Updater.DownloadAsync(p => Dispatcher.BeginInvoke(() => UpdateStatus.Text = L.F("set.upd.downloading", p)));
                        UpdateStatus.Text = L.F("set.upd.available", v) + " ✓";
                        SideUpdate.Text = L.T("set.upd.install");
                        if (silent && !IsVisible)
                            _tray?.ShowBalloonTip(4000, "VeloxBoost", L.F("set.upd.available", v), WinForms.ToolTipIcon.Info);
                    }
                    catch (Exception ex) { Sys.Log("bg download: " + ex.Message); }
                }
            }
            catch (InvalidOperationException) { UpdateStatus.Text = L.T("set.upd.notinstalled"); }
            catch (Exception ex)
            {
                UpdateStatus.Text = L.F("set.upd.error", Sys.Trim(ex.Message));
                Sys.Log("update check: " + ex);
            }
            finally { CheckUpdBtn.IsEnabled = true; }
        }

        async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(false);

        async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            InstallUpdBtn.IsEnabled = false;
            UpdProgress.Visibility = Visibility.Visible;
            try
            {
                _reallyClose = true;
                await Updater.DownloadAndRestartAsync(p => Dispatcher.BeginInvoke(() =>
                {
                    UpdateStatus.Text = L.F("set.upd.downloading", p);
                    UpdFill.Width = ((FrameworkElement)UpdFill.Parent).ActualWidth * p / 100.0;
                }));
            }
            catch (Exception ex)
            {
                _reallyClose = false;
                UpdateStatus.Text = L.F("set.upd.error", Sys.Trim(ex.Message));
                InstallUpdBtn.IsEnabled = true;
                Sys.Log("update install: " + ex);
            }
        }

        void UpdateBadge_Click(object sender, MouseButtonEventArgs e) => SelectNav("settings");

        // ═════════════════════════ Estatísticas ao vivo ═════════════════════════
        async void Stats_Tick(object sender, EventArgs e)
        {
            if (!IsVisible) return;
            double cpu = Native.CpuUsage();
            var (load, used, total) = Native.Memory();
            CpuText.Text = $"{cpu:0}%";
            RamText.Text = $"{used:0.0} / {total:0} GB";
            SetBar(CpuBar, ((FrameworkElement)CpuBar.Parent).ActualWidth, cpu / 100);
            SetBar(RamBar, ((FrameworkElement)RamBar.Parent).ActualWidth, load / 100.0);
            if (_tick++ % 2 == 0)
            {
                long ms = await NetTools.PingAsync("1.1.1.1");
                PingText.Text = ms < 0 ? "—" : $"{ms} ms";
                SetBar(PingBar, ((FrameworkElement)PingBar.Parent).ActualWidth, ms < 0 ? 0 : Math.Max(0.05, 1 - ms / 150.0));
            }
        }

        // ═════════════════════════ Registo e toasts ═════════════════════════
        void AddLog(string text, Brush dot)
        {
            _log.Insert(0, new LogVM { Time = DateTime.Now.ToString("HH:mm"), Text = text, Dot = dot });
            while (_log.Count > 40) _log.RemoveAt(_log.Count - 1);
            Sys.Log(text);
        }

        DispatcherTimer _toastTimer;
        void ShowToast(string text)
        {
            ToastText.Text = text;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
            ToastShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
                ToastShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, TimeSpan.FromMilliseconds(300)));
            };
            _toastTimer.Start();
        }

        bool Confirm(string text) =>
            MessageBox.Show(this, text, "VeloxBoost — " + L.T("msg.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        // ═════════════════════════ Animações ambiente ═════════════════════════
        void StartAmbientAnimations()
        {
            void Drift(UIElement el, double dx, double dy, double secs)
            {
                var tt = (TranslateTransform)el.RenderTransform;
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(secs)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromSeconds(secs * 1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            }

            var e2 = new SineEase { EasingMode = EasingMode.EaseInOut };


            var st = (ScaleTransform)PulseRing.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1.8, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1.8, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
            PulseRing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
            GameDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        // ═════════════════════════ Janela e bandeja ═════════════════════════
        void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void Close_Click(object sender, RoutedEventArgs e) => Close();

        void BuildTray()
        {
            System.Drawing.Icon icon;
            try
            {
                using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico")).Stream;
                icon = new System.Drawing.Icon(s);
            }
            catch { icon = System.Drawing.SystemIcons.Application; }

            var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
            _trayOpen = new WinForms.ToolStripMenuItem(L.T("tray.open"), null, (_, _) => ShowFromTray());
            _trayOpt = new WinForms.ToolStripMenuItem(L.T("tray.optimize"), null, async (_, _) => { ShowFromTray(); SelectNav("dashboard"); await OptimizeAsync(); });
            _trayExit = new WinForms.ToolStripMenuItem(L.T("tray.exit"), null, (_, _) => { _reallyClose = true; Close(); });
            menu.Items.AddRange(new WinForms.ToolStripItem[] { _trayOpen, _trayOpt, new WinForms.ToolStripSeparator(), _trayExit });
            _tray = new WinForms.NotifyIcon { Icon = icon, Text = "VeloxBoost", Visible = true, ContextMenuStrip = menu };
            _tray.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) ShowFromTray(); };
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false;
        }

        void ListenForShowRequests()
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "VeloxBoost_Show");
            var th = new Thread(() => { while (true) { ev.WaitOne(); Dispatcher.BeginInvoke(ShowFromTray); } }) { IsBackground = true };
            th.Start();
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClose && Settings.Current.CloseToTray && _tray != null)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown) { _trayHintShown = true; _tray.ShowBalloonTip(3000, "VeloxBoost", L.T("tray.running"), WinForms.ToolTipIcon.Info); }
                return;
            }
            _stats.Stop();
            _booster.Stop();
            if (!_snapshot) Updater.ApplyOnExit();
            Settings.Save();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (!_snapshot) Application.Current.Shutdown();
        }

        // ═════════════════════════ Capturas (desenvolvimento) ═════════════════════════
        public async Task SnapshotAsync(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            await Task.Delay(400);
            for (int i = 0; i < 100 && !_statesReady; i++) await Task.Delay(200);
            var pages = new (string nav, string file)[] { ("dashboard", "1-dashboard"), ("cat:gaming", "2-gaming"), ("cat:network", "3-network"), ("cat:cleanup", "4-cleanup"), ("booster", "5-booster"), ("backup", "6-backup"), ("settings", "7-settings") };
            foreach (var (nav, file) in pages)
            {
                SelectNav(nav);
                if (nav == "dashboard") ShowToast("Done: 214 applied, 0 failed, 1834 MB freed.");
                await Task.Delay(1600);
                Root.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(this);
                var rtb = new RenderTargetBitmap((int)(Root.ActualWidth * dpi.DpiScaleX), (int)(Root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x16)), null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));

                }
                rtb.Render(dv); rtb.Render(Root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(System.IO.Path.Combine(folder, file + ".png"));
                enc.Save(fs);
            }
            _reallyClose = true;
            Close();
        }
    }
}
