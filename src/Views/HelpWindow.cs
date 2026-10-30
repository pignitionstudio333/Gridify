using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridifyApp
{
    /// <summary>使用說明：內建在程式裡（離線可看），文字放在 Loc，會跟著介面語言切換。</summary>
    class HelpWindow : Window
    {
        const int SectionCount = 4;

        public HelpWindow()
        {
            Title = Loc.T("help.title");
            Width = 580;
            Height = 640;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI");
            FontSize = 13;

            var gray = new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66));
            var root = new Grid { Margin = new Thickness(22, 18, 22, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            top.Children.Add(new TextBlock { Text = Loc.AppName, FontSize = 24, FontWeight = FontWeights.Bold });
            top.Children.Add(new TextBlock { Text = Loc.T("help.sub"), Foreground = gray, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            var list = new StackPanel { Margin = new Thickness(14, 4, 14, 14) };
            for (int i = 1; i <= SectionCount; i++)
            {
                list.Children.Add(new TextBlock
                {
                    Text = Loc.T("help." + i + ".h"),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, i == 1 ? 6 : 16, 0, 4)
                });
                foreach (var line in Loc.T("help." + i + ".b").Split('\n'))
                {
                    if (line.Trim().Length == 0) continue;
                    list.Children.Add(new TextBlock
                    {
                        Text = "•  " + line,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 2, 0, 2),
                        LineHeight = 19
                    });
                }
            }

            var frame = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE1)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }
            };
            Grid.SetRow(frame, 1);
            root.Children.Add(frame);

            Content = root;
        }
    }
}
