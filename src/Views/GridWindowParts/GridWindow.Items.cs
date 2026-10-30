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
    /// <summary>項目：選取／開啟、檔案操作、懸停效果、右鍵選單。</summary>
    public partial class GridWindow
    {
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
        void RenameSelectedFile() => BeginRenameItem();

        string _editPath;
        bool _editDone = true;

        static double ResD(string key, double def)
        {
            try { return Convert.ToDouble(Application.Current.TryFindResource(key) ?? def); }
            catch { return def; }
        }

        /// <summary>F2：像檔案總管一樣，直接在圖示下方編輯檔名（只選取主檔名，不選副檔名）。</summary>
        void BeginRenameItem()
        {
            var sel = SelectedItems();
            if (sel.Count != 1 || sel[0].IsSpecial) return;
            var item = sel[0];
            if (!(List.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement c)) return;
            try
            {
                Activate();
                c.BringIntoView();
                List.UpdateLayout();
                Rect r = c.TransformToAncestor(List).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
                double icon = ResD("Grid.IconSize", 32), cell = Math.Max(ResD("Grid.CellWidth", 80), 70);

                _editPath = item.FullPath;
                _editDone = false;
                string name = Path.GetFileName(_editPath.TrimEnd('\\', '/'));
                ItemEditBox.Text = name;
                ItemEditBox.Width = cell;
                ItemEditBox.Margin = new Thickness(6 + r.Left + (r.Width - cell) / 2, 4 + r.Top + 6 + icon + 2, 0, 0);
                ItemEditBox.Visibility = Visibility.Visible;
                bool isDir = Directory.Exists(_editPath);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ItemEditBox.Focus();
                    ItemEditBox.Select(0, isDir ? name.Length : Path.GetFileNameWithoutExtension(name).Length);
                }), DispatcherPriority.Input);
            }
            catch (Exception ex) { PerfLog.Write("rename edit: " + ex.Message); _editDone = true; ItemEditBox.Visibility = Visibility.Collapsed; }
        }

        void ItemEdit_LostFocus(object sender, RoutedEventArgs e) => EndItemEdit(true);

        void ItemEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; EndItemEdit(true); }
            else if (e.Key == Key.Escape) { e.Handled = true; EndItemEdit(false); }
        }

        void EndItemEdit(bool commit)
        {
            if (_editDone || ItemEditBox.Visibility != Visibility.Visible) return;
            _editDone = true;
            string old = _editPath;
            string name = ItemEditBox.Text.Trim();
            ItemEditBox.Visibility = Visibility.Collapsed;
            Dispatcher.BeginInvoke(new Action(() => List.Focus()), DispatcherPriority.Input);
            if (!commit || string.IsNullOrEmpty(old) || name.Length == 0 || name == Path.GetFileName(old.TrimEnd('\\', '/'))) return;

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(Loc.T("rename.invalid"), Loc.AppName);
                return;
            }
            bool isDir = Directory.Exists(old);
            if (!isDir && !string.Equals(Path.GetExtension(name), Path.GetExtension(old), StringComparison.OrdinalIgnoreCase))
            {
                // 和檔案總管一樣：改副檔名前先警告
                if (MessageBox.Show(Loc.T("rename.extWarn"), Loc.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            string dest = Path.Combine(Path.GetDirectoryName(old.TrimEnd('\\', '/')), name);
            bool caseOnly = string.Equals(old, dest, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (File.Exists(dest) || Directory.Exists(dest)))
            {
                MessageBox.Show(Loc.T("rename.exists"), Loc.AppName);
                return;
            }
            ShellFileOp.Rename(new WindowInteropHelper(this).Handle, old, dest);   // 錯誤由 Windows 自己顯示
            if (File.Exists(dest) || Directory.Exists(dest)) _host.Renamed(old, dest);
        }

        /// <summary>Delete：交給 Windows 刪除（移到資源回收筒，確認視窗依你的 Windows 設定）；Shift+Delete 永久刪除。</summary>
        void DeleteSelectedFiles()
        {
            var sel = SelectedItems().Where(i => !i.IsSpecial).ToList();
            if (sel.Count == 0) return;
            bool permanent = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var existing = sel.Select(i => i.FullPath).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (existing.Count > 0)
                ShellFileOp.Delete(new WindowInteropHelper(this).Handle, existing, !permanent);
            if (!permanent) RecycleBinMonitor.Poke();   // 檔案進了資源回收筒，圖示要從空的換成滿的

            // 檔案真的不在了（刪掉了，或本來就找不到）才從收納區拿掉；使用者在 Windows 的對話框按了取消就什麼都不變
            var gone = sel.Where(i => !File.Exists(i.FullPath) && !Directory.Exists(i.FullPath)).Select(i => i.FullPath).ToList();
            if (gone.Count == 0) return;
            foreach (var i in Items.Where(x => gone.Contains(x.FullPath)).ToList()) Items.Remove(i);
            PersistItems();
            _host.Changed();
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

            // 在圖示上按右鍵：顯示 Windows 原生選單（開啟、內容、清空資源回收筒…），最下面附上 Gridify 的項目
            try
            {
                var li = FindListBoxItem(e.OriginalSource as DependencyObject);
                if (li != null && li.DataContext is GridItem clicked && ShowNativeMenu(li, clicked, e))
                    e.Handled = true;
            }
            catch (Exception ex) { PerfLog.Write("native menu: " + ex.Message); }
        }

        static ListBoxItem FindListBoxItem(DependencyObject d)
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

        bool ShowNativeMenu(ListBoxItem li, GridItem clicked, ContextMenuEventArgs e)
        {
            string Key(GridItem i) => i.IsSpecial ? "shell:" : (Path.GetDirectoryName(i.FullPath.TrimEnd('\\', '/')) ?? "");
            var sel = SelectedItems();
            if (!sel.Contains(clicked)) sel = new System.Collections.Generic.List<GridItem> { clicked };

            // 外殼選單一次只能處理同一個資料夾裡的項目：取和被點項目同資料夾的那些
            string k = Key(clicked);
            var group = sel.Where(i => string.Equals(Key(i), k, StringComparison.OrdinalIgnoreCase)).ToList();
            var paths = group.Select(i => i.IsSpecial ? i.FullPath.Substring("shell:".Length) : i.FullPath).ToList();

            bool anySpecial = sel.Any(i => i.IsSpecial);
            bool allDesktopInDefault = _data.IsDefault && sel.All(i => DesktopScanner.IsDesktopEntry(i.FullPath));
            var extras = new System.Collections.Generic.List<ShellMenuExtra>
            {
                new ShellMenuExtra { Text = Loc.T("menu.moveOut"), Enabled = !allDesktopInDefault, Run = () => Menu_MoveOut(null, null) },
                new ShellMenuExtra { Text = Loc.T("menu.openLocation"), Enabled = !anySpecial, Run = () => Menu_OpenLocation(null, null) },
                new ShellMenuExtra { Text = Loc.T("menu.moreGridify"), Run = ShowGridMenu },
            };

            int x, y;
            if (e.CursorLeft >= 0)
            {
                var cp = System.Windows.Forms.Cursor.Position;
                x = cp.X; y = cp.Y;
            }
            else
            {
                var p = li.PointToScreen(new Point(li.ActualWidth / 2, li.ActualHeight / 2));   // 用鍵盤（選單鍵）開啟時
                x = (int)p.X; y = (int)p.Y;
            }

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;   // Shift+右鍵：顯示更多選項（和檔案總管一樣）
            return ShellMenu.Show(new WindowInteropHelper(this).Handle, paths, extras, x, y, shift);
        }

        /// <summary>從原生選單的「更多 Gridify 選項」打開 Gridify 自己的收納區選單。</summary>
        void ShowGridMenu()
        {
            var cm = Root.ContextMenu;
            if (cm == null) return;
            try
            {
                Activate();
                Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            }
            catch { }
            cm.PlacementTarget = Root;
            cm.Placement = PlacementMode.MousePoint;
            cm.IsOpen = true;
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
                    case "griddelete": mi.IsEnabled = !_data.IsDefault; break;
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
    }
}
