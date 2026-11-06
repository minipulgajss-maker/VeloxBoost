using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using VeloxBoost.Core;

namespace VeloxBoost.UI
{
    /// <summary>{ui:Tr chave} → texto traduzido que muda ao trocar de língua.</summary>
    public sealed class Tr : MarkupExtension
    {
        public string Key { get; set; }
        public Tr() { }
        public Tr(string key) { Key = key; }
        public override object ProvideValue(IServiceProvider sp) =>
            new Binding($"[{Key}]") { Source = L.I, Mode = BindingMode.OneWay }.ProvideValue(sp);
    }

    public abstract class Bindable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        protected bool Set<T>(ref T f, T v, [CallerMemberName] string n = null)
        {
            if (EqualityComparer<T>.Default.Equals(f, v)) return false;
            f = v; Raise(n); return true;
        }
    }

    /// <summary>Cache de estado (aplicada / disponível) calculado em segundo plano.</summary>
    public static class StateCache
    {
        public static readonly ConcurrentDictionary<string, (bool applicable, bool applied)> S = new();
        public static void Update(Tweak t)
        {
            bool ap = false, on = false;
            try { ap = t.Applicable; on = ap && t.IsApplied(); } catch { }
            S[t.Id] = (ap, on);
        }
        public static bool Applied(Tweak t) => S.TryGetValue(t.Id, out var s) && s.applied;
        public static bool Applicable(Tweak t) => !S.TryGetValue(t.Id, out var s) || s.applicable;
    }

    public sealed class TweakVM : Bindable
    {
        static readonly Brush SafeB = Frozen("#2234D399"), ModB = Frozen("#22FBBF24"), AdvB = Frozen("#22F87171");
        static readonly Brush SafeF = Frozen("#34D399"), ModF = Frozen("#FBBF24"), AdvF = Frozen("#F87171");
        static Brush Frozen(string c) { var b = (Brush)new BrushConverter().ConvertFromString(c); b.Freeze(); return b; }

        public Tweak T { get; }
        public TweakVM(Tweak t) { T = t; Sync(); }

        bool _isOn, _busy, _applicable = true;
        public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }
        public bool Busy { get => _busy; set { if (Set(ref _busy, value)) Raise(nameof(Idle)); } }
        public bool Idle => !_busy;
        public bool Applicable { get => _applicable; set { if (Set(ref _applicable, value)) { Raise(nameof(Opacity)); Raise(nameof(Details)); } } }
        public double Opacity => _applicable ? 1 : 0.42;

        public string Name => T.Name;
        public string Details => _applicable ? T.Details : L.T("tw.na") + "  •  " + T.Details;
        public string RiskText => L.T(T.Risk switch { Risk.Safe => "risk.safe", Risk.Moderate => "risk.moderate", _ => "risk.advanced" });
        public Brush RiskBg => T.Risk switch { Risk.Safe => SafeB, Risk.Moderate => ModB, _ => AdvB };
        public Brush RiskFg => T.Risk switch { Risk.Safe => SafeF, Risk.Moderate => ModF, _ => AdvF };
        public Visibility ToggleVis => T.IsAction ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ActionVis => T.IsAction ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RebootVis => T.NeedsReboot ? Visibility.Visible : Visibility.Collapsed;

        public void Sync()
        {
            Applicable = StateCache.Applicable(T);
            IsOn = StateCache.Applied(T);
        }

        public void RefreshTexts() { Raise(nameof(Name)); Raise(nameof(Details)); Raise(nameof(RiskText)); }
    }

    public sealed class DnsResultVM : Bindable
    {
        public string Name { get; set; }
        public string Ip { get; set; }
        double _ms = -2; bool _best;
        public double Ms { get => _ms; set { Set(ref _ms, value); Raise(nameof(MsText)); Raise(nameof(BarWidth)); } }
        public bool Best { get => _best; set { Set(ref _best, value); Raise(nameof(BestVis)); } }
        public Visibility BestVis => _best ? Visibility.Visible : Visibility.Collapsed;
        public string MsText => _ms == -2 ? "…" : _ms < 0 ? "timeout" : $"{_ms:0} ms";
        public double BarWidth => _ms <= 0 ? 4 : Math.Max(6, Math.Min(220, 220 * (1 - Math.Min(_ms, 200) / 220)));
    }

    public sealed class LogVM
    {
        public string Time { get; set; }
        public string Text { get; set; }
        public Brush Dot { get; set; }
    }
}
