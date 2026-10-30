using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GridifyApp
{
    /// <summary>視窗外觀與行為：置底、標題列、縮放、位置保護、陰影／毛玻璃、收合。</summary>
    public partial class GridWindow
    {
        // ---------- 置底 + 不進 Alt+Tab ----------
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (HwndSource)PresentationSource.FromVisual(this);
            IntPtr h = src.Handle;
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
            src.AddHook(WndProc);
            ApplyLook();
            Native.SetWindowPos(h, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                // 任何想改變 Z 順序的請求（例如點擊視窗）一律改成沉到最底
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((pos.flags & Native.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        // ---------- 標題列：拖曳 / 雙擊改名 ----------
        static bool IsInside(DependencyObject d, DependencyObject ancestor)
        {
            while (d != null)
            {
                if (ReferenceEquals(d, ancestor)) return true;
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>
        /// 標題列：空白處按住拖曳 = 移動收納區，雙擊 = 收合。
        /// 用 Preview（先於子元素處理）判斷，分頁、＋、箭頭、改名框自己處理，其餘一律算空白處。
        /// </summary>
        void TitleBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (IsInside(src, TitleBox) || IsInside(src, TabPanel) || IsInside(src, TabAdd) ||
                IsInside(src, TabAddSingle) || IsInside(src, Chevron)) return;
            e.Handled = true;
            if (e.ClickCount == 2) { ToggleCollapse(); return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && _data.Tabs.Count == 1)
            {
                // 只有一個分頁時：Ctrl+拖曳 = 把整個收納區當成一個分頁，拖到別的收納區去合併
                _singleDrag = e.GetPosition(TitleBar);
                TitleBar.CaptureMouse();
                return;
            }
            try { DragMove(); } catch { }
        }

        Point? _singleDrag;

        void TitleBar_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_singleDrag == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _singleDrag = null; TitleBar.ReleaseMouseCapture(); return; }
            var p = e.GetPosition(TitleBar);
            if (Math.Abs(p.X - _singleDrag.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _singleDrag.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _singleDrag = null;
            TitleBar.ReleaseMouseCapture();
            StartTabDrag(TitleBar, 0);
        }

        void TitleBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_singleDrag == null) return;
            _singleDrag = null;
            TitleBar.ReleaseMouseCapture();
        }

        public void BeginRename()
        {
            _renameTab = -1;
            TitleBox.Text = _data.Title;
            TitleBox.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => { TitleBox.Focus(); TitleBox.SelectAll(); }), DispatcherPriority.Input);
        }

        void CommitRename()
        {
            if (TitleBox.Visibility != Visibility.Visible) return;
            TitleBox.Visibility = Visibility.Collapsed;
            string t = TitleBox.Text.Trim();
            if (_renameTab >= 0)
            {
                int idx = _renameTab;
                _renameTab = -1;
                if (idx < _data.Tabs.Count && t != _data.Tabs[idx].DisplayName)
                {
                    _data.Tabs[idx].Name = t;   // 清空 = 回到自動名稱
                    RefreshTabs();
                    _host.Save();
                }
                return;
            }
            if (t.Length > 0) { _data.Title = t; TitleText.Text = t; if (_data.Tabs.Count > 1) TitleBar.ToolTip = t; _host.Save(); }
        }

        void TitleBox_LostFocus(object sender, RoutedEventArgs e) => CommitRename();

        void TitleBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) CommitRename();
            else if (e.Key == Key.Escape) { _renameTab = -1; TitleBox.Visibility = Visibility.Collapsed; }
        }

        // ---------- 4 邊 + 4 角縮放 ----------
        // 用螢幕上的滑鼠位置計算，拖左邊／上邊時視窗本身會移動，不能用 Thumb 的相對位移。
        Point _rzStart;
        Rect _rzRect;

        Point CursorDip()
        {
            Point screen = PointToScreen(Mouse.GetPosition(this));
            var src = PresentationSource.FromVisual(this);
            return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(screen) : screen;
        }

        void Resize_Started(object sender, DragStartedEventArgs e)
        {
            _rzStart = CursorDip();
            _rzRect = new Rect(Left, Top, Width, Height);
        }

        void Resize_Delta(object sender, DragDeltaEventArgs e)
        {
            string t = (string)((Thumb)sender).Tag;
            if (_data.Collapsed) { t = t.Replace("T", "").Replace("B", ""); if (t.Length == 0) return; }   // 收合時只能左右調整
            Point cur = CursorDip();
            double dx = cur.X - _rzStart.X, dy = cur.Y - _rzStart.Y;

            double left = _rzRect.Left, top = _rzRect.Top, right = _rzRect.Right, bottom = _rzRect.Bottom;
            if (t.Contains("L")) left = Math.Min(_rzRect.Left + dx, right - MinWidth);
            if (t.Contains("R")) right = Math.Max(_rzRect.Right + dx, left + MinWidth);
            if (t.Contains("T")) top = Math.Min(_rzRect.Top + dy, bottom - MinHeight);
            if (t.Contains("B")) bottom = Math.Max(_rzRect.Bottom + dy, top + MinHeight);

            Left = left;
            Top = top;
            Width = right - left;
            Height = bottom - top;
            if (!_data.Collapsed) _data.Height = Height;   // 展開狀態下的高度，收合後再展開要回到這個大小
        }

        // ---------- 位置保護 ----------
        /// <summary>
        /// 收納區不能跑到看不見的地方（拔掉外接螢幕、改解析度、設定檔被改壞之後常見）：
        /// 至少要有一塊標題列留在所有螢幕的範圍內，否則拉回來。
        /// </summary>
        public void EnsureVisible()
        {
            try
            {
                double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
                if (vw <= 0 || vh <= 0) return;

                if (double.IsNaN(Left) || double.IsInfinity(Left)) Left = vl + 20;
                if (double.IsNaN(Top) || double.IsInfinity(Top)) Top = vt + 20;
                if (Width > vw) Width = vw;
                if (!_data.Collapsed && Height > vh) { Height = vh; _data.Height = vh; }

                double left = Math.Max(vl - Width + 80, Math.Min(Left, vl + vw - 80));
                double top = Math.Max(vt, Math.Min(Top, vt + vh - 40));
                if (Math.Abs(left - Left) > 0.5) Left = left;
                if (Math.Abs(top - Top) > 0.5) Top = top;
            }
            catch { }
        }

        // ---------- 外觀：陰影 / 毛玻璃 ----------
        void OnThemeChanged() => ApplyLook();

        double ShadowPad => (Theme.Current.Shadow && !Theme.Current.Acrylic) ? 10 : 0;

        void ApplyLook()
        {
            var a = Theme.Current;
            double pad = ShadowPad;
            Root.Margin = new Thickness(pad);
            Root.Effect = pad > 0 ? ShadowFx : null;

            if (a.Acrylic) Root.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));   // 幾乎透明，但仍可點擊
            else Root.SetResourceReference(Border.BackgroundProperty, "Grid.Bg");

            if (_data.Collapsed && !_animating) Height = CollapsedHeight();

            IntPtr h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetAcrylic(h, a);
        }

        static void SetAcrylic(IntPtr h, AppearanceConfig a)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                Color c = Theme.ParseColor(a.BgColor, Color.FromRgb(0x18, 0x20, 0x28));
                int alpha = (int)Math.Round(Math.Clamp(a.BgOpacity, 0.1, 1.0) * 255);
                var accent = new Native.AccentPolicy
                {
                    AccentState = a.Acrylic ? 4 : 0,   // 4 = ACCENT_ENABLE_ACRYLICBLURBEHIND, 0 = 關閉
                    AccentFlags = 2,
                    GradientColor = (alpha << 24) | (c.B << 16) | (c.G << 8) | c.R
                };
                int size = Marshal.SizeOf(accent);
                p = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, p, false);
                var data = new Native.WinCompAttrData { Attribute = 19, Data = p, SizeOfData = size };   // 19 = WCA_ACCENT_POLICY
                Native.SetWindowCompositionAttribute(h, ref data);

                if (a.Acrylic)
                {
                    int round = 2;   // DWMWCP_ROUND：讓毛玻璃的四個角也是圓角（Windows 11）
                    Native.DwmSetWindowAttribute(h, 33, ref round, sizeof(int));
                }
            }
            catch { }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
        }

        // ---------- 收合 / 展開 ----------
        double CollapsedHeight() => 30 + ShadowPad * 2;

        void ToggleCollapse() => SetCollapsed(!_data.Collapsed, true);

        void SetCollapsed(bool collapse, bool animate)
        {
            if (_animating) return;
            _data.Collapsed = collapse;
            UpdateChevron();

            double target = collapse ? CollapsedHeight() : Math.Max(90, _data.Height);
            if (collapse) MinHeight = 0;
            else { MinHeight = 90; List.Visibility = Visibility.Visible; }

            if (!animate || !Theme.Current.Animations || !IsLoaded)
            {
                Height = target;
                FinishCollapse(collapse);
                return;
            }

            _animating = true;
            var an = new DoubleAnimation(ActualHeight, target, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            an.Completed += (s, e) =>
            {
                BeginAnimation(HeightProperty, null);
                Height = target;
                _animating = false;
                FinishCollapse(collapse);
            };
            BeginAnimation(HeightProperty, an);
        }

        void FinishCollapse(bool collapse)
        {
            List.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            _host.Save();
        }

        void UpdateChevron()
        {
            Chevron.Text = _data.Collapsed ? "\u25B8" : "\u25BE";
            if (!TitleBar.IsMouseOver) FadeChevron(_data.Collapsed ? 0.6 : 0);
        }

        void FadeChevron(double to)
        {
            if (Theme.Current.Animations && IsLoaded)
                Chevron.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
            else
            {
                Chevron.BeginAnimation(OpacityProperty, null);
                Chevron.Opacity = to;
            }
        }

        void TitleBar_MouseEnter(object sender, MouseEventArgs e) { FadeChevron(0.85); TabAddSingle.Opacity = 0.9; }
        void TitleBar_MouseLeave(object sender, MouseEventArgs e) { FadeChevron(_data.Collapsed ? 0.6 : 0); TabAddSingle.Opacity = 0.4; }

        void Chevron_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleCollapse();
            e.Handled = true;
        }
    }
}
