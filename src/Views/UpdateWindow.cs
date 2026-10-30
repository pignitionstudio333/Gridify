using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridifyApp
{
    /// <summary>版本紀錄 + 檢查更新。版本紀錄打包在程式裡，不用連網；只有按下「檢查更新」才會連到 GitHub。</summary>
    class UpdateWindow : Window
    {
        readonly Button _check;
        readonly Button _download;
        readonly TextBlock _status;
        bool _busy;

        public UpdateWindow(bool autoCheck, Action<bool> autoChanged)
        {
            Title = Loc.T("upd.title");
            Width = 560;
            Height = 620;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI");
            FontSize = 13;

            var gray = new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66));
            var grid = new Grid { Margin = new Thickness(22, 18, 22, 18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = Loc.AppName, FontSize = 24, FontWeight = FontWeights.Bold });
            top.Children.Add(new TextBlock { Text = Loc.F("upd.current", AppInfo.Version), Foreground = gray, Margin = new Thickness(0, 2, 0, 12) });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _check = new Button { Content = Loc.T("upd.check"), Height = 32, Padding = new Thickness(16, 0, 16, 0) };
            _download = new Button
            {
                Content = Loc.T("upd.download"),
                Height = 32,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(10, 0, 0, 0),
                FontWeight = FontWeights.SemiBold,
                Visibility = Visibility.Collapsed
            };
            _check.Click += async (s, e) => await DoCheck();
            _download.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl) { UseShellExecute = true }); } catch { }
            };
            row.Children.Add(_check);
            row.Children.Add(_download);
            top.Children.Add(row);

            _status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
            top.Children.Add(_status);

            var auto = new CheckBox
            {
                Content = Loc.T("upd.auto"),
                IsChecked = autoCheck,
                Margin = new Thickness(0, 12, 0, 0)
            };
            auto.Click += (s, e) => autoChanged?.Invoke(auto.IsChecked == true);
            top.Children.Add(auto);
            top.Children.Add(new TextBlock
            {
                Text = Loc.T("upd.note"),
                Foreground = gray,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 12)
            });
            top.Children.Add(new TextBlock { Text = Loc.T("upd.history"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
            Grid.SetRow(top, 0);
            grid.Children.Add(top);

            var list = new StackPanel { Margin = new Thickness(10, 6, 10, 10) };
            Fill(list, AppInfo.Changelog(), gray);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            };
            var frame = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE1)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = scroll
            };
            Grid.SetRow(frame, 1);
            grid.Children.Add(frame);

            Content = grid;
        }

        async System.Threading.Tasks.Task DoCheck()
        {
            if (_busy) return;
            _busy = true;
            _check.IsEnabled = false;
            _download.Visibility = Visibility.Collapsed;
            _status.Text = Loc.T("upd.checking");
            try { ShowResult(await UpdateChecker.CheckAsync()); }
            finally { _busy = false; _check.IsEnabled = true; }
        }

        /// <summary>顯示檢查結果（手動檢查，或由系統匣通知點進來）。</summary>
        public void ShowResult(UpdateResult r)
        {
            if (r == null) return;
            if (!r.Ok)
            {
                _status.Text = Loc.F("upd.error", r.Error);
                _status.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
                return;
            }
            if (r.Newer)
            {
                _status.Text = Loc.F("upd.newer", r.Latest);
                _status.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x6F, 0x2F));
                _download.Visibility = Visibility.Visible;
            }
            else
            {
                _status.Text = Loc.T("upd.latest");
                _status.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x6F, 0x2F));
            }
        }

        static string Clean(string t)
        {
            t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]*\)", "$1");   // [文字](網址) → 文字
            return t.Replace("**", "").Replace("`", "");
        }

        /// <summary>把 CHANGELOG 的 Markdown（標題、條列）簡單轉成畫面上的文字。</summary>
        static void Fill(StackPanel p, string md, Brush gray)
        {
            if (string.IsNullOrWhiteSpace(md)) return;
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith("# ")) continue;
                if (line.StartsWith("## "))
                    p.Children.Add(new TextBlock { Text = Clean(line.Substring(3)), FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 2) });
                else if (line.StartsWith("### "))
                    p.Children.Add(new TextBlock { Text = Clean(line.Substring(4)), FontWeight = FontWeights.SemiBold, Foreground = gray, Margin = new Thickness(0, 6, 0, 2) });
                else if (line.StartsWith("- "))
                    p.Children.Add(new TextBlock { Text = "•  " + Clean(line.Substring(2)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 0, 2) });
                else
                    p.Children.Add(new TextBlock { Text = Clean(line), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
            }
        }
    }
}
