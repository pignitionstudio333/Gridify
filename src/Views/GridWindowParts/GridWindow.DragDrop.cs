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
    /// <summary>拖放：拖進來、拖出去。</summary>
    public partial class GridWindow
    {
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

        /// <summary>
        /// 點到收納區外面（桌面、工作列、其他程式、另一個收納區）就取消選取。
        /// 右鍵選單、對話框、設定視窗都是 Gridify 自己的視窗，不算外面，所以選單操作不會失去選取。
        /// 稍等一下再看前景視窗是誰，避免切換瞬間誤判。
        /// </summary>
        void Window_Deactivated(object sender, EventArgs e)
        {
            if (List.SelectedItems.Count == 0) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            t.Tick += (s, a) =>
            {
                t.Stop();
                try { if (!IsActive && ForegroundIsOutside()) List.UnselectAll(); } catch { }
            };
            t.Start();
        }

        bool ForegroundIsOutside()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;   // 視窗正在切換，先不動
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (pid != (uint)Environment.ProcessId) return true;   // 其他程式（含桌面與工作列）
            foreach (var w in Application.Current.Windows.OfType<GridWindow>())
                if (!ReferenceEquals(w, this) && new WindowInteropHelper(w).Handle == fg) return true;   // 另一個收納區
            return false;   // 自己的選單、對話框、設定視窗
        }

        void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragArmed = FindItem(e.OriginalSource as DependencyObject) != null;

            // 點在空白處：取消選取（和檔案總管一樣；按住 Ctrl / Shift 時不動，點捲軸也不動）
            if (!_dragArmed && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                bool onScrollBar = false;
                for (var d = e.OriginalSource as DependencyObject; d != null && !ReferenceEquals(d, List);
                     d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                    if (d is ScrollBar) { onScrollBar = true; break; }
                if (!onScrollBar) List.UnselectAll();
            }
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
    }
}
