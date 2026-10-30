using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GridifyApp
{
    /// <summary>收納區的建立、刪除、分頁搬移，以及項目在收納區之間移動。</summary>
    public partial class App
    {
        // ---------- 收納區 ----------
        void OpenGrid(GridData d)
        {
            var w = new GridWindow(d, _host);
            _wins.Add(w);
            w.Show();
        }

        void AddGrid()
        {
            int n = _cfg.Grids.Count;
            AddGridAt(120 + (n % 8) * 30, 120 + (n % 8) * 30, false);
        }

        /// <summary>在指定位置（WPF 單位）新增收納區；startRename 為 true 時直接進入改名。</summary>
        void AddGridAt(double x, double y, bool startRename)
        {
            int n = _cfg.Grids.Count;
            var d = new GridData { Title = Loc.F("grid.new", n + 1), Left = x, Top = y };
            _cfg.Grids.Add(d);
            OpenGrid(d);
            SaveConfig();
            if (startRename)
            {
                var w = _wins[_wins.Count - 1];
                w.Activate();
                w.BeginRename();
            }
        }

        void DeleteGrid(GridWindow w)
        {
            if (w.Data.IsDefault) return;
            var r = MessageBox.Show(Loc.F("grid.deleteConfirm", w.Data.Title),
                Loc.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            var items = w.Data.AllItems.ToList();
            _cfg.Grids.Remove(w.Data);
            _wins.Remove(w);
            w.Close();

            if (_cfg.TakeOver)
            {
                var def = DefaultGrid();
                foreach (var p in items)
                    if (DesktopScanner.IsDesktopEntry(p) && !def.HasItem(p)) def.Items.Add(p);   // 全部回到「桌面」收納區目前的分頁
            }
            ReloadAll();
            SaveConfig();
        }

        /// <summary>刪除分頁：項目全部回到「桌面」收納區目前的分頁（只改項目屬於哪裡，不動任何檔案）。</summary>
        void DeleteTab(GridWindow w, int index)
        {
            var d = w.Data;
            var _active = d.ActiveTabData;
            if (d.Tabs.Count <= 1 || index < 0 || index >= d.Tabs.Count) return;
            var tab = d.Tabs[index];

            // 「桌面」收納區：接管桌面時一定有；沒接管時只有它已經存在才有
            GridData def = _cfg.TakeOver ? null : _cfg.Grids.FirstOrDefault(f => f.IsDefault);
            if (_cfg.TakeOver) def = DefaultGrid();

            string msg = def != null
                ? Loc.F("tab.deleteConfirm", tab.DisplayName, def.Title)
                : Loc.F("tab.deleteConfirm2", tab.DisplayName);
            if (MessageBox.Show(msg, Loc.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var items = (tab.Items ?? new List<string>()).ToList();
            d.Tabs.RemoveAt(index);
            if (index < d.ActiveTab) d.ActiveTab--;
            if (d.ActiveTab >= d.Tabs.Count) d.ActiveTab = d.Tabs.Count - 1;
            if (d.ActiveTab < 0) d.ActiveTab = 0;
            d.AdoptSingleTabName();   // 只剩一個分頁：收納區名稱改成剩下那個分頁的名稱

            if (def != null)
                foreach (var p in items)
                    if (!def.HasItem(p)) def.Items.Add(p);

            ReloadAll();
            SaveConfig();
        }

        /// <summary>
        /// 把分頁搬到另一個收納區（或同一個收納區內重新排序）。項目跟著分頁走，只改變「屬於哪裡」，不動任何檔案。
        /// 來源收納區少了最後一個分頁：一般收納區會消失（項目都在分頁裡，沒有東西遺失）；「桌面」收納區會留下一個空分頁。
        /// </summary>
        void MoveTabBetween(GridWindow src, int srcIndex, GridWindow dst, int dstIndex)
        {
            try
            {
                var sd = src.Data; var dd = dst.Data;
                var _a = sd.ActiveTabData; var _b = dd.ActiveTabData;
                if (srcIndex < 0 || srcIndex >= sd.Tabs.Count) return;
                if (!ReferenceEquals(sd, dd) && sd.Tabs.Count == 1) sd.InheritTitleForFirstTab();   // 單一分頁整個拖走：分頁沿用收納區的名稱
                var tab = sd.Tabs[srcIndex];

                if (ReferenceEquals(sd, dd))
                {
                    // 同一收納區：重新排序
                    int pos = Math.Max(0, Math.Min(sd.Tabs.Count, dstIndex));
                    sd.Tabs.RemoveAt(srcIndex);
                    if (pos > srcIndex) pos--;
                    sd.Tabs.Insert(pos, tab);
                    sd.ActiveTab = pos;
                }
                else
                {
                    if (dd.Tabs.Count >= 30) return;
                    dd.InheritTitleForFirstTab();   // 目標收納區若原本只有一個分頁，先讓它保住原本的名稱

                    // 目標收納區已經有的項目就不重複放
                    tab.Items = (tab.Items ?? new List<string>()).Where(p => !dd.HasItem(p)).ToList();

                    // 名稱和目標收納區的分頁重複時，加上編號
                    string name = tab.DisplayName;
                    if (dd.Tabs.Any(t => string.Equals(t.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        int n = 2;
                        string cand;
                        do { cand = name + " (" + n++ + ")"; }
                        while (dd.Tabs.Any(t => string.Equals(t.DisplayName, cand, StringComparison.OrdinalIgnoreCase)));
                        tab.Name = cand;
                    }

                    sd.Tabs.RemoveAt(srcIndex);
                    bool closeSource = false;
                    if (sd.Tabs.Count == 0)
                    {
                        if (sd.IsDefault) sd.Tabs.Add(new TabData());   // 「桌面」收納區不能消失
                        else closeSource = true;
                    }
                    else
                    {
                        if (srcIndex < sd.ActiveTab) sd.ActiveTab--;
                        if (sd.ActiveTab >= sd.Tabs.Count) sd.ActiveTab = sd.Tabs.Count - 1;
                        if (sd.ActiveTab < 0) sd.ActiveTab = 0;
                        sd.AdoptSingleTabName();   // 來源只剩一個分頁：收納區名稱改成剩下那個分頁的名稱
                    }

                    int pos = Math.Max(0, Math.Min(dd.Tabs.Count, dstIndex));
                    dd.Tabs.Insert(pos, tab);
                    dd.ActiveTab = pos;

                    if (closeSource)
                    {
                        _cfg.Grids.Remove(sd);
                        _wins.Remove(src);
                        src.Close();
                    }
                }
                ReloadAll();
                SaveConfig();
            }
            catch (Exception ex) { PerfLog.Crash("movetab", ex); }
        }

        GridData DefaultGrid()
        {
            var d = _cfg.Grids.FirstOrDefault(f => f.IsDefault);
            if (d != null) return d;
            var wa = SystemParameters.WorkArea;
            d = new GridData
            {
                Title = Loc.T("grid.default"),
                IsDefault = true,
                Left = wa.Left + 20,
                Top = wa.Top + 20,
                Width = 640,
                Height = Math.Max(300, wa.Height - 80)
            };
            _cfg.Grids.Add(d);
            OpenGrid(d);
            return d;
        }

        // ---------- 項目在收納區之間移動 ----------
        void OnDropped(GridWindow target, string[] paths)
        {
            foreach (var p in paths)
            {
                if (target.Data.Items.Any(i => Same(i, p))) continue;   // 已經在目前分頁
                target.Data.RemoveItems(i => Same(i, p));               // 在同一收納區的其他分頁 → 視為移到目前分頁
                if (DesktopScanner.IsDesktopEntry(p))
                    foreach (var f in _cfg.Grids)
                        if (f != target.Data) f.RemoveItems(i => Same(i, p));   // 桌面項目只屬於一個收納區
                target.Data.Items.Add(p);
            }
            ReloadAll();
            SaveConfig();
        }

        void OnMovedOut(GridWindow src, string[] paths)
        {
            foreach (var p in paths)
            {
                src.Data.RemoveItems(i => Same(i, p));
                if (_cfg.TakeOver && DesktopScanner.IsDesktopEntry(p))
                {
                    var def = DefaultGrid();
                    if (!def.HasItem(p)) def.Items.Add(p);
                }
            }
            ReloadAll();
            SaveConfig();
        }

        void OnRenamed(string oldPath, string newPath)
        {
            ClearUndo();   // 檔案改名後，舊路徑已不存在，往回復原會弄壞參照
            foreach (var f in _cfg.Grids) f.ReplaceItem(oldPath, newPath);
            ReloadAll();
            SaveConfig();
            SyncDesktop();
        }
    }
}
