using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridifyApp
{
    /// <summary>第一次執行的歡迎畫面：說明「接管桌面」做什麼，以及所有還原方式，讓使用者自己決定。</summary>
    class WelcomeWindow : Window
    {
        /// <summary>使用者是否選擇接管桌面（直接關掉視窗視為「先不要」，最安全）。</summary>
        public bool TakeOver { get; private set; }

        /// <summary>「登入 Windows 時自動啟動」的選擇；AutoStartChosen 為 false（直接關掉視窗）代表不更動目前的設定。</summary>
        public bool AutoStart { get; private set; }
        public bool AutoStartChosen { get; private set; }

        public WelcomeWindow()
        {
            Title = Loc.T("w.title");
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI");
            FontSize = 13;

            var gray = new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66));
            var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };

            root.Children.Add(new TextBlock { Text = Loc.AppName, FontSize = 28, FontWeight = FontWeights.Bold });
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("w.heading"),
                FontSize = 15,
                Foreground = gray,
                Margin = new Thickness(0, 2, 0, 16),
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(new TextBlock { Text = Loc.T("w.body"), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("w.undoHead"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 16, 0, 4)
            });
            foreach (var key in new[] { "w.undo1", "w.undo2", "w.undo3", "w.undo4" })
                root.Children.Add(new TextBlock
                {
                    Text = "•  " + Loc.T(key),
                    Foreground = gray,
                    Margin = new Thickness(8, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });

            var auto = new CheckBox
            {
                Content = Loc.T("w.autostart"),
                IsChecked = StartupHelper.IsEnabled(),
                Margin = new Thickness(0, 18, 0, 0)
            };
            root.Children.Add(auto);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            var later = new Button { Content = Loc.T("w.later"), Height = 34, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var enable = new Button { Content = Loc.T("w.enable"), Height = 34, Padding = new Thickness(18, 0, 18, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
            later.Click += (s, e) => { TakeOver = false; AutoStart = auto.IsChecked == true; AutoStartChosen = true; DialogResult = false; };
            enable.Click += (s, e) => { TakeOver = true; AutoStart = auto.IsChecked == true; AutoStartChosen = true; DialogResult = true; };
            buttons.Children.Add(later);
            buttons.Children.Add(enable);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
