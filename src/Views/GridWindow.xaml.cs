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
    /// <summary>收納區視窗呼叫 App 的所有回呼。</summary>
    public class GridHost
    {
        public Action Save;
        public Action Exit;
        public Action NewGrid;
        public Action<GridWindow> DeleteGrid;
        public Action<GridWindow, string[]> Dropped;    // 有項目被拖進這個收納區
        public Action<GridWindow, string[]> MovedOut;   // 項目要移出這個收納區
        public Action<string, string> Renamed;           // 檔案被改名（舊路徑、新路徑）
        public Action Changed;                           // 檔案被刪除等，需要重新同步桌面
        public Action<int> IconStep;                     // Ctrl+滾輪：圖示大小增減（像素，正負）
        public Action<int> IconSet;                      // 圖示大小設為指定值
        public Action Appearance;                        // 開啟外觀設定視窗
        public Action<GridWindow, int> DeleteTab;        // 刪除分頁（確認、項目回到「桌面」收納區）
        public Action Undo;                              // 復原 / 重做（只動 Gridify 的設定，不動任何檔案）
        public Action Redo;
        public Func<bool> CanUndo;
        public Func<bool> CanRedo;
        public Action<GridWindow, int, GridWindow, int> MoveTabTo;   // 把分頁（來源視窗, 索引）搬到（目標視窗, 插入位置）；同一視窗 = 重新排序
    }

    public partial class GridWindow : Window
    {
        static class Native
        {
            public const int GWL_EXSTYLE = -20;
            public const int WS_EX_TOOLWINDOW = 0x80;   // 不出現在 Alt+Tab
            public const int WM_WINDOWPOSCHANGING = 0x0046;
            public const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
            public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

            [StructLayout(LayoutKind.Sequential)]
            public struct WINDOWPOS
            {
                public IntPtr hwnd, hwndInsertAfter;
                public int x, y, cx, cy;
                public uint flags;
            }

            [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
            [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
            [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

            [StructLayout(LayoutKind.Sequential)]
            public struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

            [StructLayout(LayoutKind.Sequential)]
            public struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }

            [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr h, ref WinCompAttrData data);
            [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        }

        const string GridFormat = "GridifyApp.Paths";
        const string TabFormat = "GridifyApp.Tab";
        static GridWindow _dragTabSource;   // 正在被拖曳的分頁來自哪個收納區
        static int _dragTabIndex = -1;

        readonly GridData _data;
        readonly GridHost _host;
        readonly DispatcherTimer _saveTimer;
        Point _dragStart;
        bool _dragArmed;
        bool _animating;
        bool _ready;
        int _renameTab = -1;            // 正在改名的分頁索引（-1 = 改的是收納區名稱）
        string _tabSig;                 // 分頁列目前的結構（名稱清單），沒變就只更新外觀、不重建
        Point _tabPress;
        bool _tabPressed;
        DispatcherTimer _hoverTimer;
        int _hoverIndex = -1;
        static GridWindow _dragSource;   // 目前正在被拖曳的項目來自哪個收納區（用來分辨「調整順序」與「從別處拖進來」）

        static readonly DropShadowEffect ShadowFx = MakeShadow();

        static DropShadowEffect MakeShadow()
        {
            var e = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.45 };
            e.Freeze();
            return e;
        }

        public GridData Data => _data;
        public ObservableCollection<GridItem> Items { get; } = new ObservableCollection<GridItem>();

        public GridWindow(GridData data, GridHost host)
        {
            InitializeComponent();
            _data = data;
            _host = host;

            Left = data.Left; Top = data.Top; Width = data.Width; Height = data.Height;
            EnsureVisible();
            TitleText.Text = data.Title;
            Reload();
            DataContext = this;

            ApplyLook();
            if (_data.Collapsed)
            {
                MinHeight = 0;
                Height = CollapsedHeight();
                List.Visibility = Visibility.Collapsed;
            }
            UpdateChevron();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _host.Save(); };
            PreviewKeyDown += Window_PreviewKeyDown;
            LocationChanged += (s, e) => Touch();
            SizeChanged += (s, e) => Touch();

            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => _ready = true), DispatcherPriority.ApplicationIdle);
            Theme.Changed += OnThemeChanged;
            Closed += (s, e) => Theme.Changed -= OnThemeChanged;
        }

        /// <summary>依 Data.Items 重建顯示的項目。</summary>
        public void Reload()
        {
            RefreshTabs();
            // 內容沒變就什麼都不做，避免每次同步都重建所有圖示
            if (Items.Count == _data.Items.Count)
            {
                bool same = true;
                for (int i = 0; i < Items.Count; i++)
                    if (!string.Equals(Items[i].FullPath, _data.Items[i], StringComparison.Ordinal)) { same = false; break; }
                if (same) return;
            }
            Items.Clear();
            foreach (var p in _data.Items) Items.Add(new GridItem(p));
        }

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

        // ---------- 儲存 ----------
        void Touch()
        {
            if (!IsLoaded || _animating) return;
            // 高度不在這裡存：只有使用者拖曳調整大小時才更新 _data.Height（收合 / 展開動畫、陰影變化都不會污染它）
            _data.Left = Left; _data.Top = Top; _data.Width = Width;
            _saveTimer.Stop(); _saveTimer.Start();
        }

        void PersistItems()
        {
            _data.Items = Items.Select(i => i.FullPath).ToList();
            _host.Save();
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
            try { DragMove(); } catch { }
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

        // ---------- 拖放進來（桌面項目在收納區之間是「移動」，其他檔案只記路徑） ----------
        static string[] ReadPaths(IDataObject d)
        {
            if (d.GetDataPresent(GridFormat)) return d.GetData(GridFormat) as string[];
            if (d.GetDataPresent(DataFormats.FileDrop)) return d.GetData(DataFormats.FileDrop) as string[];
            return null;
        }

        void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(TabFormat))
            {
                // 其他收納區的分頁可以放進來；自己的分頁只有放在標籤列上才有意義（排序），放在內容區沒有作用
                bool other = _dragTabSource != null && _dragTabSource != this;
                e.Effects = (other || IsInside(e.OriginalSource as DependencyObject, TabStrip)) ? DragDropEffects.Move : DragDropEffects.None;
                ShowTabHint();
                e.Handled = true;
                return;
            }
            var paths = ReadPaths(e.Data);
            bool ok = paths != null;
            e.Effects = ok ? DragDropEffects.Link : DragDropEffects.None;
            ShowHint(ok, paths);
            e.Handled = true;
        }

        /// <summary>拖曳中的項目是不是「從這個收納區目前顯示的分頁」拖出來的（是的話放下去只是調整順序）。</summary>
        bool DraggedFromThisView(string[] paths)
        {
            if (_dragSource != this || paths == null) return false;
            return Items.Any(i => paths.Any(p => string.Equals(p, i.FullPath, StringComparison.OrdinalIgnoreCase)));
        }

        void ShowHint(bool ok, string[] paths)
        {
            if (ok) DropHintText.Text = DraggedFromThisView(paths) ? Loc.T("drop.reorder") : Loc.T("drop.add");
            DropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        }

        void ShowTabHint()
        {
            if (_dragTabSource != null && _dragTabSource != this)
            {
                DropHintText.Text = Loc.T("drop.moveTab");
                DropHint.Visibility = Visibility.Visible;
            }
        }

        void Grid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(TabFormat)) { ShowTabHint(); return; }
            var p = ReadPaths(e.Data);
            ShowHint(p != null, p);
        }

        void Grid_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

        void Grid_Drop(object sender, DragEventArgs e)
        {
            DropHint.Visibility = Visibility.Collapsed;
            if (e.Data.GetDataPresent(TabFormat))
            {
                if (_dragTabSource == this && !IsInside(e.OriginalSource as DependencyObject, TabStrip)) { e.Handled = true; return; }
                HandleTabDrop(e, _data.Tabs.Count);   // 放在其他收納區的內容區 / 標籤列空白處 = 排到最後
                return;
            }
            var paths = ReadPaths(e.Data);
            if (paths != null && paths.Length > 0)
            {
                Point pt = e.GetPosition(List);
                if (!DraggedFromThisView(paths)) _host.Dropped(this, paths);   // 從別處（或同一收納區的其他分頁）拖進來：先加入
                ReorderTo(paths, pt);                                  // 再放到滑鼠所在的位置（同一個收納區內就是調整順序）
            }
            e.Handled = true;
        }

        /// <summary>找出滑鼠位置對應的插入點（回傳的是「插在第幾個項目之前」，Items.Count 代表放到最後）。</summary>
        int FindInsertIndex(Point p)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (!(List.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement el) || el.ActualWidth <= 0) continue;
                Rect r = el.TransformToAncestor(List).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                if (p.Y < r.Top) return i;                                  // 在這一列上方
                if (p.Y < r.Bottom && p.X < r.Left + r.Width / 2) return i;  // 同一列、在這個項目的左半邊
            }
            return Items.Count;
        }

        void ReorderTo(string[] paths, Point pt)
        {
            if (_data.Collapsed) return;
            var set = new System.Collections.Generic.HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            var moving = Items.Where(i => set.Contains(i.FullPath)).ToList();
            if (moving.Count == 0) return;

            int target = FindInsertIndex(pt);
            int before = Items.Take(Math.Min(target, Items.Count)).Count(i => moving.Contains(i));
            foreach (var m in moving) Items.Remove(m);
            target = Math.Max(0, Math.Min(Items.Count, target - before));
            foreach (var m in moving) Items.Insert(target++, m);
            PersistItems();
        }

        // ---------- 從收納區拖出去（只允許 Link，拖到 Explorer 只會建立捷徑，不會搬動原檔） ----------
        static ListBoxItem FindItem(DependencyObject d)
        {
            while (d != null)
            {
                if (d is ListBoxItem li) return li;
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragArmed = FindItem(e.OriginalSource as DependencyObject) != null;
        }

        void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) { _dragArmed = false; return; }
            Vector d = e.GetPosition(null) - _dragStart;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragArmed = false;

            var pressed = FindItem(e.OriginalSource as DependencyObject)?.DataContext as GridItem;
            string[] paths = (pressed != null && !List.SelectedItems.Contains(pressed))
                ? new[] { pressed.FullPath }
                : SelectedItems().Select(i => i.FullPath).ToArray();
            if (paths.Length == 0) return;

            var data = new DataObject();
            data.SetData(GridFormat, paths);
            if (paths.All(p => !p.StartsWith(DesktopScanner.Prefix, StringComparison.Ordinal)))
                data.SetData(DataFormats.FileDrop, paths);
            _dragSource = this;
            try { DragDrop.DoDragDrop(List, data, DragDropEffects.Link); }
            finally { _dragSource = null; }
        }

        // ---------- 選取 / 開啟 ----------
        System.Collections.Generic.List<GridItem> SelectedItems() => List.SelectedItems.Cast<GridItem>().ToList();

        void Item_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem li && li.DataContext is GridItem item) OpenPath(item.FullPath);
            e.Handled = true;
        }

        void Item_RightDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem li && !li.IsSelected)
            {
                List.UnselectAll();
                li.IsSelected = true;
            }
        }

        void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) DeleteSelectedFiles();
            else if (e.Key == Key.F2) RenameSelectedFile();
            else if (e.Key == Key.Enter) OpenSelected();
        }

        static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(Loc.F("err.open", path, ex.Message), Loc.AppName); }
        }

        void OpenSelected()
        {
            foreach (var i in SelectedItems()) OpenPath(i.FullPath);
        }

        // ---------- 檔案操作 ----------
        void RenameSelectedFile()
        {
            var sel = SelectedItems();
            if (sel.Count != 1 || sel[0].IsSpecial) return;
            string old = sel[0].FullPath;
            bool isDir = Directory.Exists(old);
            var dlg = new PromptWindow(Loc.T("rename.title"), Loc.T("rename.prompt"), Path.GetFileName(old));
            if (dlg.ShowDialog() != true) return;
            string name = dlg.Value.Trim();
            if (name.Length == 0 || name == Path.GetFileName(old)) return;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(Loc.T("rename.invalid"), Loc.AppName);
                return;
            }
            string dest = Path.Combine(Path.GetDirectoryName(old), name);
            try
            {
                if (File.Exists(dest) || Directory.Exists(dest)) { MessageBox.Show(Loc.T("rename.exists"), Loc.AppName); return; }
                if (isDir) Directory.Move(old, dest); else File.Move(old, dest);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("rename.fail", ex.Message), Loc.AppName);
                return;
            }
            _host.Renamed(old, dest);
        }

        void DeleteSelectedFiles()
        {
            var sel = SelectedItems().Where(i => !i.IsSpecial).ToList();
            if (sel.Count == 0) return;
            string names = string.Join("\n", sel.Take(8).Select(i => "  • " + i.Name)) + (sel.Count > 8 ? "\n  …" : "");
            if (MessageBox.Show(Loc.F("delete.confirm", sel.Count, names), Loc.AppName,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            var gone = new System.Collections.Generic.List<string>();
            foreach (var i in sel)
            {
                try
                {
                    if (Directory.Exists(i.FullPath))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(i.FullPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(i.FullPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    gone.Add(i.FullPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.F("delete.fail", i.Name, ex.Message), Loc.AppName);
                }
            }
            if (gone.Count == 0) return;
            foreach (var i in Items.Where(x => gone.Contains(x.FullPath)).ToList()) Items.Remove(i);
            PersistItems();
            _host.Changed();
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

        // ---------- 項目：懸停淡入淡出 / 新增淡入 ----------
        void Item_MouseEnter(object sender, MouseEventArgs e) => FadeHighlight(sender, 1);
        void Item_MouseLeave(object sender, MouseEventArgs e) => FadeHighlight(sender, 0);

        static void FadeHighlight(object sender, double to)
        {
            if (!(sender is ListBoxItem li) || !(li.Template?.FindName("hl", li) is UIElement hl)) return;
            if (Theme.Current.Animations)
                hl.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
            else
            {
                hl.BeginAnimation(OpacityProperty, null);
                hl.Opacity = to;
            }
        }

        void Item_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_ready || !Theme.Current.Animations) return;   // 啟動時一次載入大量項目就不播動畫
            if (sender is FrameworkElement fe && fe.DataContext is GridItem it)
                fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, it.ItemOpacity, TimeSpan.FromMilliseconds(180))
                {
                    FillBehavior = FillBehavior.Stop
                });
        }

        // ---------- 右鍵選單 ----------
        /// <summary>收納區在最底層、平常不是作用中視窗，選單要先讓它取得焦點，點旁邊或按 Esc 才關得掉。</summary>
        void Root_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                Activate();
                Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            }
            catch { }
        }

        void Menu_Opened(object sender, RoutedEventArgs e)
        {
            var sel = SelectedItems();
            bool has = sel.Count > 0;
            bool anySpecial = sel.Any(i => i.IsSpecial);
            bool allDesktopInDefault = _data.IsDefault && sel.All(i => DesktopScanner.IsDesktopEntry(i.FullPath));

            foreach (var mi in ((ContextMenu)sender).Items.OfType<MenuItem>())
            {
                switch (mi.Tag as string)
                {
                    case "item": mi.IsEnabled = has; break;
                    case "moveout": mi.IsEnabled = has && !allDesktopInDefault; break;
                    case "rename": mi.IsEnabled = sel.Count == 1 && !anySpecial; break;
                    case "delete": mi.IsEnabled = has && !anySpecial; break;
                    case "fencedelete": mi.IsEnabled = !_data.IsDefault; break;
                    case "undo": mi.IsEnabled = _host.CanUndo != null && _host.CanUndo(); break;
                    case "redo": mi.IsEnabled = _host.CanRedo != null && _host.CanRedo(); break;
                    case "newtab": mi.IsEnabled = _data.Tabs.Count < 30; break;
                    case "tabmulti": mi.IsEnabled = _data.Tabs.Count > 1; break;
                }
            }
        }

        void Menu_Open(object sender, RoutedEventArgs e) => OpenSelected();

        void Menu_OpenLocation(object sender, RoutedEventArgs e)
        {
            var i = SelectedItems().FirstOrDefault(x => !x.IsSpecial);
            if (i != null)
                try { Process.Start("explorer.exe", "/select,\"" + i.FullPath + "\""); } catch { }
        }

        void Menu_MoveOut(object sender, RoutedEventArgs e)
        {
            var paths = SelectedItems().Select(i => i.FullPath).ToArray();
            if (paths.Length > 0) _host.MovedOut(this, paths);
        }

        void Menu_RenameFile(object sender, RoutedEventArgs e) => RenameSelectedFile();
        void Menu_DeleteFile(object sender, RoutedEventArgs e) => DeleteSelectedFiles();
        void Menu_Rename(object sender, RoutedEventArgs e) => BeginRename();
        void Menu_New(object sender, RoutedEventArgs e) => _host.NewGrid();
        void Menu_Delete(object sender, RoutedEventArgs e) => _host.DeleteGrid(this);
        void Menu_Exit(object sender, RoutedEventArgs e) => _host.Exit();
        void Menu_Appearance(object sender, RoutedEventArgs e) => _host.Appearance();
        void Menu_ToggleCollapse(object sender, RoutedEventArgs e) => ToggleCollapse();
        void Menu_NewTab(object sender, RoutedEventArgs e) => AddTab();
        void Menu_Undo(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => _host.Undo()));
        void Menu_Redo(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => _host.Redo()));

        /// <summary>Ctrl+Z 復原、Ctrl+Y（或 Ctrl+Shift+Z）重做。只在 Gridify 視窗有焦點時有效；改名輸入框裡的 Ctrl+Z 仍是文字的復原。</summary>
        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (Keyboard.FocusedElement is TextBox) return;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (e.Key == Key.Z && !shift) { e.Handled = true; Dispatcher.BeginInvoke(new Action(() => _host.Undo())); }
            else if (e.Key == Key.Y || (e.Key == Key.Z && shift)) { e.Handled = true; Dispatcher.BeginInvoke(new Action(() => _host.Redo())); }
        }
        void Menu_RenameTab(object sender, RoutedEventArgs e) => BeginRenameTab(_data.ActiveTab);
        void Menu_DeleteTab(object sender, RoutedEventArgs e) => _host.DeleteTab(this, _data.ActiveTab);

        // ---------- 分頁（併在標題列；只有一個分頁時維持原本的標題樣子） ----------
        void RefreshTabs()
        {
            var _active = _data.ActiveTabData;   // 確保至少有一個分頁
            bool multi = _data.Tabs.Count > 1;
            TabStrip.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            TitleText.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
            TitleText.Text = _data.Title;
            TabAddSingle.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
            TitleBar.ToolTip = multi ? _data.Title : null;
            if (!multi) { _tabSig = null; TabPanel.Children.Clear(); return; }

            string sig = string.Join("\u0001", _data.Tabs.Select(t => t.DisplayName));
            if (sig == _tabSig && TabPanel.Children.Count == _data.Tabs.Count)
            {
                for (int i = 0; i < TabPanel.Children.Count; i++) StyleTab((Border)TabPanel.Children[i], i);
            }
            else
            {
                _tabSig = sig;
                TabPanel.Children.Clear();
                for (int i = 0; i < _data.Tabs.Count; i++) TabPanel.Children.Add(MakeTab(i));
            }

            int act = _data.ActiveTab;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (act >= 0 && act < TabPanel.Children.Count) ((FrameworkElement)TabPanel.Children[act]).BringIntoView();
            }), DispatcherPriority.Loaded);
        }

        void StyleTab(Border b, int i)
        {
            bool active = i == _data.ActiveTab;
            b.Tag = i;
            b.Opacity = active ? 1 : 0.65;
            if (active) b.SetResourceReference(Border.BackgroundProperty, "Grid.Select");
            else b.Background = Brushes.Transparent;
            if (b.Child is TextBlock tb) tb.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        Border MakeTab(int i)
        {
            var tab = _data.Tabs[i];
            var text = new TextBlock
            {
                Text = tab.DisplayName, FontSize = 12, MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Grid.TitleText");
            var b = new Border
            {
                Child = text, CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 2, 9, 3),
                Margin = new Thickness(1, 3, 1, 3), Cursor = Cursors.Hand, AllowDrop = true, ToolTip = tab.DisplayName
            };
            StyleTab(b, i);

            b.MouseEnter += (s, e) => { if ((int)b.Tag != _data.ActiveTab) b.SetResourceReference(Border.BackgroundProperty, "Grid.Hover"); };
            b.MouseLeave += (s, e) => { if ((int)b.Tag != _data.ActiveTab) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                int idx = (int)b.Tag;
                if (e.ClickCount == 2) { BeginRenameTab(idx); return; }
                _tabPress = e.GetPosition(null);
                _tabPressed = true;
                SelectTab(idx);
            };
            b.MouseLeftButtonUp += (s, e) => _tabPressed = false;
            b.MouseMove += (s, e) =>
            {
                // 在分頁上按住拖曳 = 拖這個分頁（同一列內重新排序，或放到其他收納區）；要移動收納區請拖標題列空白處
                if (!_tabPressed || e.LeftButton != MouseButtonState.Pressed) { _tabPressed = false; return; }
                Vector d = e.GetPosition(null) - _tabPress;
                if (Math.Abs(d.X) < 4 && Math.Abs(d.Y) < 4) return;
                _tabPressed = false;
                StartTabDrag(b, (int)b.Tag);
            };

            // 把項目拖到分頁上：停留一下就切過去；放開 = 放進那個分頁
            b.DragOver += Tab_DragOver;
            b.DragLeave += (s, e) => { StopHover(); StyleTab(b, (int)b.Tag); };
            b.Drop += Tab_Drop;

            var menu = new ContextMenu();
            menu.Items.Add(TabMenuItem(Loc.T("menu.renameTab"), true, () => BeginRenameTab((int)b.Tag)));
            menu.Items.Add(TabMenuItem(Loc.T("menu.moveLeft"), i > 0, () => MoveTab((int)b.Tag, -1)));
            menu.Items.Add(TabMenuItem(Loc.T("menu.moveRight"), i < _data.Tabs.Count - 1, () => MoveTab((int)b.Tag, 1)));
            menu.Items.Add(new Separator());
            menu.Items.Add(TabMenuItem(Loc.T("menu.newTab"), _data.Tabs.Count < 30, AddTab));
            menu.Items.Add(TabMenuItem(Loc.T("menu.deleteTab"), _data.Tabs.Count > 1, () => _host.DeleteTab(this, (int)b.Tag)));
            b.ContextMenu = menu;
            b.ContextMenuOpening += Root_ContextMenuOpening;
            return b;
        }

        static MenuItem TabMenuItem(string header, bool enabled, Action click)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (s, e) => click();
            return mi;
        }

        void SelectTab(int i)
        {
            if (i == _data.ActiveTab || i < 0 || i >= _data.Tabs.Count) return;
            _data.ActiveTab = i;
            Reload();            // 立刻換成這個分頁的項目（在任何存檔動作之前）
            _host.Save();
        }

        void AddTab()
        {
            if (_data.Tabs.Count >= 30) return;
            var _active = _data.ActiveTabData;
            _data.InheritTitleForFirstTab();   // 從單一分頁變成多個：第一個分頁沿用收納區原本的名稱
            int n = _data.Tabs.Count + 1;
            string name;
            do { name = Loc.F("tab.newName", n++); } while (_data.Tabs.Any(t => t.DisplayName == name));
            _data.Tabs.Add(new TabData { Name = name });
            _data.ActiveTab = _data.Tabs.Count - 1;
            Reload();
            _host.Save();
            int idx = _data.ActiveTab;
            Dispatcher.BeginInvoke(new Action(() => BeginRenameTab(idx)), DispatcherPriority.Loaded);   // 新增後直接輸入名稱
        }

        void MoveTab(int i, int delta)
        {
            int j = i + delta;
            if (i < 0 || j < 0 || i >= _data.Tabs.Count || j >= _data.Tabs.Count) return;
            var t = _data.Tabs[i];
            _data.Tabs.RemoveAt(i);
            _data.Tabs.Insert(j, t);
            if (_data.ActiveTab == i) _data.ActiveTab = j;
            else if (_data.ActiveTab == j) _data.ActiveTab = i;
            RefreshTabs();
            _host.Save();
        }

        void BeginRenameTab(int i)
        {
            if (i < 0 || i >= _data.Tabs.Count) return;
            SelectTab(i);
            _renameTab = i;
            TitleBox.Text = _data.Tabs[i].DisplayName;
            TitleBox.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => { TitleBox.Focus(); TitleBox.SelectAll(); }), DispatcherPriority.Input);
        }

        void TabAdd_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AddTab();
        }

        void TabScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;   // Ctrl+滾輪仍是調整圖示大小
            TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        }

        void StartTabDrag(Border b, int idx)
        {
            var data = new DataObject();
            data.SetData(TabFormat, idx.ToString());
            _dragTabSource = this;
            _dragTabIndex = idx;
            try { DragDrop.DoDragDrop(b, data, DragDropEffects.Move); }
            catch { }
            finally
            {
                _dragTabSource = null;
                _dragTabIndex = -1;
                StopHover();
                DropHint.Visibility = Visibility.Collapsed;
                RefreshTabs();
            }
        }

        /// <summary>放下被拖曳的分頁：交給 App 處理（延後執行，避免在來源視窗的拖曳迴圈裡關閉它）。</summary>
        void HandleTabDrop(DragEventArgs e, int insertIndex)
        {
            e.Handled = true;
            StopHover();
            DropHint.Visibility = Visibility.Collapsed;
            var srcWin = _dragTabSource;
            int srcIdx = _dragTabIndex;
            if (srcWin == null || srcIdx < 0) return;
            Dispatcher.BeginInvoke(new Action(() => _host.MoveTabTo(srcWin, srcIdx, this, insertIndex)), DispatcherPriority.Background);
        }

        void Tab_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(TabFormat))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                if (sender is Border tb) tb.SetResourceReference(Border.BackgroundProperty, "Grid.Hover");
                return;
            }
            bool ok = ReadPaths(e.Data) != null;
            e.Effects = ok ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
            if (ok && sender is FrameworkElement fe && fe.Tag is int idx && idx != _data.ActiveTab && idx != _hoverIndex) StartHover(idx);
        }

        void StartHover(int idx)
        {
            StopHover();
            _hoverIndex = idx;
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _hoverTimer.Tick += (s, e) =>
            {
                StopHover();
                SelectTab(idx);
            };
            _hoverTimer.Start();
        }

        void StopHover()
        {
            if (_hoverTimer != null) { _hoverTimer.Stop(); _hoverTimer = null; }
            _hoverIndex = -1;
        }

        void Tab_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(TabFormat) && sender is Border tb && tb.Tag is int ti)
            {
                bool right = e.GetPosition(tb).X > tb.ActualWidth / 2;
                HandleTabDrop(e, ti + (right ? 1 : 0));
                return;
            }
            e.Handled = true;
            StopHover();
            DropHint.Visibility = Visibility.Collapsed;
            var paths = ReadPaths(e.Data);
            if (paths == null || paths.Length == 0 || !(((FrameworkElement)sender).Tag is int idx)) return;
            if (idx < 0 || idx >= _data.Tabs.Count) return;
            _data.ActiveTab = idx;
            _host.Dropped(this, paths);   // 加入這個分頁；原本在同一收納區其他分頁的項目視為移動（App.OnDropped 處理）
        }

        void Menu_IconSize(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && int.TryParse(mi.Tag as string, out int size)) _host.IconSet(size);
        }

        /// <summary>Ctrl + 滾輪：全部收納區一起調整圖示大小。</summary>
        void Root_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            _host.IconStep(e.Delta > 0 ? 4 : -4);
            e.Handled = true;
        }
    }

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
