using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using VeloxBoost.Core;

namespace VeloxBoost.UI
{
    /// <summary>Required-update screen shown at startup: Update or Exit.</summary>
    public sealed class UpdateGate : Window
    {
        readonly TextBlock _status;
        readonly Border _fill;
        readonly StackPanel _buttons;

        public UpdateGate(string version)
        {
            Title = "VeloxBoost";
            Width = 440; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x1C));
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            Topmost = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/icon.ico")); } catch { }

            var accent = new LinearGradientBrush(Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x22, 0xD3, 0xEE), 0);
            var root = new StackPanel { Margin = new Thickness(32, 32, 32, 28) };

            var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = accent, HorizontalAlignment = HorizontalAlignment.Left };
            icon.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(icon);
            root.Children.Add(new TextBlock { Text = "Update required", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 20, 0, 6) });
            root.Children.Add(new TextBlock
            {
                Text = $"VeloxBoost {version} is available. You need to update to keep using the app.",
                FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xC0)), TextWrapping = TextWrapping.Wrap
            });

            var track = new Grid { Height = 4, Margin = new Thickness(0, 22, 0, 0), Visibility = Visibility.Collapsed };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)) });
            _fill = new Border { CornerRadius = new CornerRadius(2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Children.Add(_fill);
            root.Children.Add(track);
            _status = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x70, 0x95)), Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(_status);

            _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            var exit = MakeButton("Exit", false);
            var update = MakeButton("Update", true);
            exit.Click += (_, _) => { DialogResult = false; };
            update.Click += async (_, _) =>
            {
                _buttons.IsEnabled = false;
                track.Visibility = Visibility.Visible;
                try
                {
                    await Updater.DownloadAndRestartAsync(p => Dispatcher.BeginInvoke(() =>
                    {
                        _status.Text = $"Downloading… {p}%";
                        _fill.Width = track.ActualWidth * p / 100.0;
                    }));
                }
                catch (Exception ex)
                {
                    Sys.Log("gate update: " + ex);
                    _status.Text = "Update failed. Check your connection and try again.";
                    _buttons.IsEnabled = true;
                }
            };
            _buttons.Children.Add(exit);
            _buttons.Children.Add(update);
            root.Children.Add(_buttons);

            Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x28, 0x40)), BorderThickness = new Thickness(1), Child = root };
        }

        static Button MakeButton(string text, bool primary)
        {
            var bg = primary ? (Brush)new LinearGradientBrush(Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x22, 0xD3, 0xEE), 0) : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
            var tpl = new ControlTemplate(typeof(Button));
            var b = new FrameworkElementFactory(typeof(Border));
            b.SetValue(Border.BackgroundProperty, bg);
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            b.SetValue(Border.PaddingProperty, new Thickness(22, 10, 22, 10));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            b.AppendChild(cp);
            tpl.VisualTree = b;
            return new Button
            {
                Content = text, Template = tpl, Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        /// <summary>Returns false if the user chose to exit (or an update is being installed).</summary>
        public static bool Run()
        {
            if (!Updater.IsInstalled) return true;
            string v = null;
            try
            {
                var t = Task.Run(Updater.CheckAsync);
                if (t.Wait(TimeSpan.FromSeconds(8))) v = t.Result;
            }
            catch (Exception ex) { Sys.Log("gate check: " + ex.Message); }
            if (v == null) return true;
            var gate = new UpdateGate(v);
            gate.ShowDialog();
            return false;
        }
    }
}
