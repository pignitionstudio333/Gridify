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
    /// <summary>分頁（併在標題列）。</summary>
    public partial class GridWindow
    {
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

        void StartTabDrag(DependencyObject b, int idx)
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
}
