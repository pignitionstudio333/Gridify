using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GridifyApp
{
    /// <summary>外觀設定視窗（全域）：上方即時預覽，下方分成「大小」「背景」「效能與效果」三張卡片。</summary>
    class AppearanceWindow : Window
    {
        static readonly (string Name, string Hex)[] Presets =
        {
            ("c.slate", "#182028"), ("c.black", "#000000"), ("c.graphite", "#2B2B2B"),
            ("c.navy", "#1E3A5F"), ("c.purple", "#3B1F4E"), ("c.green", "#1F4D3A"),
            ("c.wine", "#5A2A2A"), ("c.lightgray", "#F2F2F2"), ("c.white", "#FFFFFF")
        };

        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));

        /// <summary>現代風格的滑桿：細圓角軌道、左側填色、圓形把手（懸停與拖曳時變色）。</summary>
        static readonly Style SliderStyle = (Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
  <Setter Property='Height' Value='24'/>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='Slider'>
        <Grid Background='Transparent' SnapsToDevicePixels='True'>
          <Border Height='4' CornerRadius='2' Background='#D9DCE1' VerticalAlignment='Center'/>
          <Track x:Name='PART_Track' VerticalAlignment='Center'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>
                <RepeatButton.Template>
                  <ControlTemplate TargetType='RepeatButton'>
                    <Grid Background='Transparent' Height='24'>
                      <Border Height='4' CornerRadius='2' Background='#4C9AFF' VerticalAlignment='Center'/>
                    </Grid>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>
                <RepeatButton.Template>
                  <ControlTemplate TargetType='RepeatButton'>
                    <Grid Background='Transparent' Height='24'/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb Width='18' Height='18' Focusable='False'>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Ellipse x:Name='dot' Fill='White' Stroke='#4C9AFF' StrokeThickness='2'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='dot' Property='Fill' Value='#EAF3FF'/>
                      </Trigger>
                      <Trigger Property='IsDragging' Value='True'>
                        <Setter TargetName='dot' Property='Fill' Value='#4C9AFF'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");

        readonly AppearanceConfig _cfg;
        readonly Action _changed;
        readonly Slider _size;
        readonly Slider _opacity;
        readonly TextBlock _sizeLabel;
        readonly TextBlock _opacityLabel;
        readonly CheckBox _acrylic;
        readonly CheckBox _shadow;
        readonly CheckBox _anim;
        readonly Border _pvGrid;
        readonly TextBlock _pvTitle;
        readonly WrapPanel _pvItems;
        readonly List<(Border Swatch, string Hex)> _swatches = new List<(Border, string)>();
        bool _init = true;

        public AppearanceWindow(AppearanceConfig cfg, Action changed)
        {
            _cfg = cfg;
            _changed = changed;

            Title = Loc.T("ap.title");
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI");
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(18) };

            // ---- 預覽 ----
            var wallpaper = new Border
            {
                Height = 170,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0x3A, 0x6E, 0xA5), 0),
                        new GradientStop(Color.FromRgb(0x8E, 0x5F, 0xA8), 0.55),
                        new GradientStop(Color.FromRgb(0xE5, 0x8F, 0x65), 1)
                    }, new Point(0, 0), new Point(1, 1))
            };
            _pvTitle = new TextBlock { Text = Loc.T("grid.default"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(10, 6, 10, 4) };
            _pvItems = new WrapPanel { Margin = new Thickness(6, 4, 6, 4) };
            var pvGrid = new Grid();
            pvGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pvGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pvGrid.Children.Add(_pvTitle);
            Grid.SetRow(_pvItems, 1);
            pvGrid.Children.Add(_pvItems);
            _pvGrid = new Border
            {
                Width = 300,
                Height = 130,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = pvGrid
            };
            wallpaper.Child = _pvGrid;
            root.Children.Add(wallpaper);

            // ---- 卡片：大小 ----
            _size = new Slider { Style = SliderStyle, Minimum = Theme.MinIcon, Maximum = Theme.MaxIcon, TickFrequency = 4, IsSnapToTickEnabled = true };
            _sizeLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(Card(Loc.T("ap.size"), SliderRow(Loc.T("ap.iconSize"), _size, _sizeLabel)));

            // ---- 卡片：背景 ----
            var bg = new StackPanel();
            _opacity = new Slider { Style = SliderStyle, Minimum = 10, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
            _opacityLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            bg.Children.Add(SliderRow(Loc.T("ap.opacity"), _opacity, _opacityLabel));

            var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var p in Presets)
            {
                var sw = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Margin = new Thickness(0, 0, 8, 6),
                    Cursor = Cursors.Hand,
                    ToolTip = Loc.T(p.Name),
                    Background = new SolidColorBrush(Theme.ParseColor(p.Hex, Colors.Black))
                };
                string hex = p.Hex;
                sw.MouseLeftButtonUp += (s, e) => { _cfg.BgColor = hex; Changed(); };
                _swatches.Add((sw, hex));
                wrap.Children.Add(sw);
            }
            var custom = new Button { Content = Loc.T("ap.custom"), Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 6) };
            custom.Click += (s, e) => PickCustomColor();
            wrap.Children.Add(custom);
            bg.Children.Add(wrap);
            root.Children.Add(Card(Loc.T("ap.bg"), bg));

            // ---- 卡片：效能與效果 ----
            var fx = new StackPanel();
            _acrylic = Check(Loc.T("ap.acrylic"), Loc.T("ap.acrylicHint"));
            _shadow = Check(Loc.T("ap.shadow"), Loc.T("ap.shadowHint"));
            _anim = Check(Loc.T("ap.anim"), Loc.T("ap.animHint"));
            fx.Children.Add(Wrap(_acrylic, Loc.T("ap.acrylicHint")));
            fx.Children.Add(Wrap(_shadow, Loc.T("ap.shadowHint")));
            fx.Children.Add(Wrap(_anim, Loc.T("ap.animHint")));
            root.Children.Add(Card(Loc.T("ap.fx"), fx));

            // ---- 底部按鈕 ----
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var reset = new Button { Content = Loc.T("ap.reset"), Width = 88, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = Loc.T("btn.close"), Width = 76, Height = 30, IsCancel = true, IsDefault = true };
            reset.Click += (s, e) => ResetDefaults();
            close.Click += (s, e) => Close();
            buttons.Children.Add(reset);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;

            _size.ValueChanged += (s, e) => { if (_init) return; _cfg.IconSize = (int)Math.Round(_size.Value); Changed(); };
            _opacity.ValueChanged += (s, e) => { if (_init) return; _cfg.BgOpacity = _opacity.Value / 100.0; Changed(); };
            _acrylic.Click += (s, e) => { _cfg.Acrylic = _acrylic.IsChecked == true; Changed(); };
            _shadow.Click += (s, e) => { _cfg.Shadow = _shadow.IsChecked == true; Changed(); };
            _anim.Click += (s, e) => { _cfg.Animations = _anim.IsChecked == true; Changed(); };

            Sync();
        }

        // ---------- 版面小工具 ----------
        static CheckBox Check(string text, string tip) => new CheckBox { Content = text, ToolTip = tip, VerticalContentAlignment = VerticalAlignment.Center };

        static UIElement Wrap(CheckBox box, string hint)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            p.Children.Add(box);
            p.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11.5,
                Margin = new Thickness(20, 1, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x76, 0x80)),
                TextWrapping = TextWrapping.Wrap
            });
            return p;
        }

        static Border Card(string title, UIElement content)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            p.Children.Add(content);
            return new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(14, 12, 14, 8),
                CornerRadius = new CornerRadius(8),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE5, 0xE8)),
                BorderThickness = new Thickness(1),
                Child = p
            };
        }

        static Grid SliderRow(string label, Slider slider, TextBlock value)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            slider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(value, 2);
            g.Children.Add(l);
            g.Children.Add(slider);
            g.Children.Add(value);
            return g;
        }

        // ---------- 狀態同步 ----------
        void Changed()
        {
            _changed();
            RefreshAll();
        }

        /// <summary>設定從別處改變（Ctrl+滾輪、右鍵選單、還原預設）時，把所有控制項同步成目前設定。</summary>
        public void Sync()
        {
            _init = true;
            _size.Value = _cfg.IconSize;
            _opacity.Value = Math.Round(_cfg.BgOpacity * 100);
            _acrylic.IsChecked = _cfg.Acrylic;
            _shadow.IsChecked = _cfg.Shadow;
            _anim.IsChecked = _cfg.Animations;
            _init = false;
            RefreshAll();
        }

        void RefreshAll()
        {
            _sizeLabel.Text = _cfg.IconSize + " px";
            _opacityLabel.Text = Math.Round(_cfg.BgOpacity * 100) + " %";

            foreach (var (sw, hex) in _swatches)
            {
                bool on = string.Equals(hex, _cfg.BgColor, StringComparison.OrdinalIgnoreCase);
                sw.BorderThickness = new Thickness(on ? 2.5 : 1);
                sw.BorderBrush = on ? Accent : new SolidColorBrush(Color.FromRgb(0xC8, 0xCB, 0xD0));
            }
            RefreshPreview();
        }

        void RefreshPreview()
        {
            Color bg = Theme.ParseColor(_cfg.BgColor, Color.FromRgb(0x18, 0x20, 0x28));
            bool light = Theme.IsLight(bg);
            Color text = light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;
            byte alpha = (byte)Math.Round(Math.Clamp(_cfg.BgOpacity, 0.1, 1.0) * 255);

            _pvGrid.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
            _pvGrid.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, text.R, text.G, text.B));
            _pvTitle.Foreground = new SolidColorBrush(Color.FromArgb(0xDD, text.R, text.G, text.B));

            int icon = Math.Clamp(_cfg.IconSize, Theme.MinIcon, Theme.MaxIcon);
            double font = Math.Round(Math.Clamp(icon * 0.34, 10.0, 14.0));
            string[] names = { Loc.T("ap.n1"), Loc.T("ap.n2"), Loc.T("ap.n3"), Loc.T("ap.n4"), Loc.T("ap.n5"), Loc.T("ap.n6") };
            Color[] tints =
            {
                Color.FromRgb(0x4C, 0x9A, 0xFF), Color.FromRgb(0xF0, 0x8A, 0x5D), Color.FromRgb(0x6B, 0xCB, 0x77),
                Color.FromRgb(0xB5, 0x83, 0xFF), Color.FromRgb(0xFF, 0xC8, 0x57), Color.FromRgb(0xFF, 0x6B, 0x8B)
            };

            _pvItems.Children.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                var cell = new StackPanel { Width = icon + 30, Margin = new Thickness(2) };
                cell.Children.Add(new Border
                {
                    Width = icon,
                    Height = icon,
                    CornerRadius = new CornerRadius(Math.Max(4, icon / 6.0)),
                    Background = new SolidColorBrush(tints[i]),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                cell.Children.Add(new TextBlock
                {
                    Text = names[i],
                    FontSize = font,
                    Foreground = new SolidColorBrush(text),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                _pvItems.Children.Add(cell);
            }
        }

        void PickCustomColor()
        {
            var cur = Theme.ParseColor(_cfg.BgColor, Colors.Black);
            using (var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B)
            })
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _cfg.BgColor = "#" + dlg.Color.R.ToString("X2") + dlg.Color.G.ToString("X2") + dlg.Color.B.ToString("X2");
                    Changed();
                }
            }
        }

        void ResetDefaults()
        {
            var d = new AppearanceConfig();
            _cfg.IconSize = d.IconSize;
            _cfg.BgOpacity = d.BgOpacity;
            _cfg.BgColor = d.BgColor;
            _cfg.Acrylic = d.Acrylic;
            _cfg.Shadow = d.Shadow;
            _cfg.Animations = d.Animations;
            _changed();
            Sync();
        }
    }
}
