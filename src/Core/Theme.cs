using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GridifyApp
{
    /// <summary>
    /// 把外觀設定（圖示大小、背景透明度、背景顏色）轉成應用程式資源（Grid.*），
    /// XAML 用 DynamicResource 引用，所以改設定時所有收納區會立刻一起更新。
    /// </summary>
    public static class Theme
    {
        public const int MinIcon = 24, MaxIcon = 96;

        /// <summary>目前套用中的外觀設定（收納區視窗直接讀取）。</summary>
        public static AppearanceConfig Current { get; private set; } = new AppearanceConfig();

        /// <summary>外觀改變時觸發，收納區視窗據此更新陰影、毛玻璃等無法用資源表達的部分。</summary>
        public static event Action Changed;

        public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150;

        public static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
        }

        static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        public static void Apply(AppearanceConfig a)
        {
            var res = Application.Current.Resources;

            int icon = Math.Clamp(a.IconSize, MinIcon, MaxIcon);
            double opacity = Math.Clamp(a.BgOpacity, 0.1, 1.0);
            a.IconSize = icon;
            a.BgOpacity = opacity;

            Color bg = ParseColor(a.BgColor, Color.FromRgb(0x18, 0x20, 0x28));
            bool lightBg = IsLight(bg);
            Color text = lightBg ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;

            res["Grid.Bg"] = Frozen(WithAlpha(bg, (byte)Math.Round(opacity * 255)));
            res["Grid.Border"] = Frozen(WithAlpha(text, 0x26));
            res["Grid.Hover"] = Frozen(WithAlpha(text, 0x22));
            res["Grid.Select"] = Frozen(WithAlpha(text, 0x44));
            res["Grid.Grip"] = Frozen(WithAlpha(text, 0x66));
            res["Grid.Divider"] = Frozen(WithAlpha(text, 0x22));
            res["Grid.Text"] = Frozen(text);
            res["Grid.TitleText"] = Frozen(WithAlpha(text, 0xDD));

            var fx = new DropShadowEffect
            {
                Color = lightBg ? Colors.White : Colors.Black,
                ShadowDepth = 1,
                BlurRadius = 3,
                Opacity = 0.7
            };
            fx.Freeze();
            res["Grid.TextEffect"] = fx;

            double fontSize = Math.Round(Math.Clamp(icon * 0.34, 10.0, 14.0));
            res["Grid.IconSize"] = (double)icon;
            res["Grid.CellWidth"] = (double)(icon + 44);
            res["Grid.LabelFontSize"] = fontSize;
            res["Grid.LabelMaxHeight"] = fontSize * 2.9;

            Current = a;
            Changed?.Invoke();
        }
    }
}
