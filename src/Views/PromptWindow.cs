using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GridifyApp
{
    /// <summary>簡單的輸入對話框（重新命名用）。</summary>
    class PromptWindow : Window
    {
        readonly TextBox _box;
        public string Value => _box.Text;

        public PromptWindow(string title, string label, string initial)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;

            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
            _box = new TextBox { Text = initial };
            panel.Children.Add(_box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = Loc.T("btn.ok"), Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = Loc.T("btn.cancel"), Width = 72, IsCancel = true };
            ok.Click += (s, e) => DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }
    }
}
