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
    /// <summary>依檔案類型自動分類。</summary>
    public partial class App
    {
        // ---------- 依類型自動分類 ----------
        static readonly string[] AppExt = { ".lnk", ".exe", ".url", ".bat", ".cmd", ".msi" };
        static readonly string[] DocExt = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv", ".rtf", ".odt", ".json" };
        static readonly string[] MediaExt = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".psd", ".mp4", ".mov", ".mkv", ".avi", ".mp3", ".wav", ".flac" };

        static string Categorize(string p)
        {
            if (p.StartsWith(DesktopScanner.Prefix, StringComparison.Ordinal)) return null;
            if (Directory.Exists(p)) return Loc.T("cat.folders");
            string ext = Path.GetExtension(p).ToLowerInvariant();
            if (Array.IndexOf(AppExt, ext) >= 0) return Loc.T("cat.apps");
            if (Array.IndexOf(DocExt, ext) >= 0) return Loc.T("cat.docs");
            if (Array.IndexOf(MediaExt, ext) >= 0) return Loc.T("cat.media");
            return null;
        }

        void AutoCategorize()
        {
            if (!_takeOverActive)
            {
                MessageBox.Show(Loc.T("cat.needTakeover"), Loc.AppName);
                return;
            }
            // 是 = 在「桌面」收納區裡分成分頁（建議）；否 = 分成多個收納區（舊做法）；取消 = 不做
            var ask = MessageBox.Show(Loc.T("cat.confirm"),
                Loc.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ask == MessageBoxResult.Cancel) return;
            if (ask == MessageBoxResult.Yes) { CategorizeIntoTabs(DefaultGrid()); return; }

            var def = DefaultGrid();
            var wa = SystemParameters.WorkArea;
            int slot = _cfg.Grids.Count(x => !x.IsDefault);
            foreach (var p in def.Items.ToList())
            {
                string cat = Categorize(p);
                if (cat == null) continue;
                var cf = _cfg.Grids.FirstOrDefault(x => !x.IsDefault && x.Title == cat);
                if (cf == null)
                {
                    cf = new GridData
                    {
                        Title = cat,
                        Left = def.Left + def.Width + 20 + (slot % 3) * 340,
                        Top = wa.Top + 20 + (slot / 3) * 270,
                        Width = 320,
                        Height = 250
                    };
                    slot++;
                    _cfg.Grids.Add(cf);
                    OpenGrid(cf);
                }
                def.Items.RemoveAll(i => Same(i, p));
                cf.Items.Add(p);
            }
            ReloadAll();
            SaveConfig();
        }

        /// <summary>
        /// 把「桌面」收納區目前分頁裡的項目，依類型分到同一個收納區的分頁（應用程式與捷徑、資料夾、文件、圖片與影音）。
        /// 只改變項目屬於哪個分頁；分不出類型的項目（系統圖示、其他檔案）留在原本的分頁，並命名為「其他」。
        /// </summary>
        void CategorizeIntoTabs(GridData def)
        {
            var src = def.ActiveTabData;
            var cats = new[] { Loc.T("cat.apps"), Loc.T("cat.folders"), Loc.T("cat.docs"), Loc.T("cat.media") };
            var buckets = new Dictionary<string, List<string>>();
            foreach (var c in cats) buckets[c] = new List<string>();
            foreach (var p in src.Items.ToList())
            {
                string cat = Categorize(p);
                if (cat != null && buckets.ContainsKey(cat)) buckets[cat].Add(p);
            }
            if (buckets.Values.All(b => b.Count == 0))
            {
                MessageBox.Show(Loc.T("cat.nothing"), Loc.AppName);
                return;
            }

            foreach (var c in cats)
            {
                var list = buckets[c];
                if (list.Count == 0) continue;
                var tab = def.Tabs.FirstOrDefault(t => !ReferenceEquals(t, src) &&
                                                       string.Equals(t.DisplayName, c, StringComparison.OrdinalIgnoreCase));
                if (tab == null)
                {
                    if (def.Tabs.Count >= 30) continue;
                    tab = new TabData { Name = c };
                    def.Tabs.Insert(def.Tabs.IndexOf(src), tab);   // 新分頁都排在原本那個分頁前面
                }
                foreach (var p in list)
                {
                    src.Items.RemoveAll(i => Same(i, p));
                    if (!tab.Items.Any(i => Same(i, p))) tab.Items.Add(p);
                }
            }

            if (string.IsNullOrWhiteSpace(src.Name)) src.Name = Loc.T("cat.other");
            def.ActiveTab = def.Tabs.IndexOf(src);   // 之後桌面新增的檔案會進到這個分頁
            ReloadAll();
            SaveConfig();
        }
    }
}
