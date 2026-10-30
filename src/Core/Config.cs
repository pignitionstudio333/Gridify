using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GridifyApp
{
    /// <summary>收納區裡的一個分頁：有自己的項目清單。</summary>
    public class TabData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>空白代表「自動名稱」，顯示時依目前語言決定（分頁 1 / Tab 1）。</summary>
        public string Name { get; set; } = "";
        public List<string> Items { get; set; } = new List<string>();

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Loc.T("tab.defaultName") : Name;
    }

    public class GridData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "Grid";
        public bool IsDefault { get; set; }
        public double Left { get; set; } = 120;
        public double Top { get; set; } = 120;
        public double Width { get; set; } = 320;
        public double Height { get; set; } = 240;
        public bool Collapsed { get; set; }

        public List<TabData> Tabs { get; set; } = new List<TabData>();
        public int ActiveTab { get; set; }

        // 相容舊版：舊設定檔只有一份 "Items"（沒有分頁）。讀取時暫存起來，第一次用到分頁時轉成單一分頁；
        // 寫出時固定輸出「所有分頁項目的合併」，讓舊版程式讀到新設定檔也不會變空。
        List<string> _legacyItems;
        [JsonPropertyName("Items")]
        public List<string> LegacyItems
        {
            get
            {
                if (Tabs != null && Tabs.Count > 0)
                    return Tabs.Where(t => t != null && t.Items != null).SelectMany(t => t.Items).ToList();
                return _legacyItems;
            }
            set { _legacyItems = value; }
        }
        List<string> TakeLegacyItems() { var l = _legacyItems; _legacyItems = null; return l; }

        /// <summary>目前選取的分頁（一定存在；舊資料會在這裡自動轉成單一分頁）。</summary>
        [JsonIgnore]
        public TabData ActiveTabData
        {
            get
            {
                if (Tabs == null) Tabs = new List<TabData>();
                Tabs.RemoveAll(t => t == null);
                if (Tabs.Count == 0)
                    Tabs.Add(new TabData { Items = TakeLegacyItems() ?? new List<string>() });
                if (ActiveTab < 0 || ActiveTab >= Tabs.Count) ActiveTab = 0;
                return Tabs[ActiveTab];
            }
        }

        /// <summary>目前分頁的項目（舊程式碼沿用這個名稱）。</summary>
        [JsonIgnore]
        public List<string> Items
        {
            get { var t = ActiveTabData; if (t.Items == null) t.Items = new List<string>(); return t.Items; }
            set { ActiveTabData.Items = value ?? new List<string>(); }
        }

        /// <summary>所有分頁的項目。</summary>
        [JsonIgnore]
        public IEnumerable<string> AllItems
        {
            get
            {
                var _ = ActiveTabData;
                return Tabs.Where(t => t.Items != null).SelectMany(t => t.Items);
            }
        }

        /// <summary>
        /// 從「只有一個分頁」變成多個分頁之前呼叫：標題列會從收納區名稱改顯示分頁標籤，
        /// 所以第一個分頁（還是自動名稱的話）先沿用收納區原本的名稱，使用者看到的名字才不會變。
        /// </summary>
        public void InheritTitleForFirstTab()
        {
            var _t = ActiveTabData;
            if (Tabs.Count == 1 && string.IsNullOrWhiteSpace(Tabs[0].Name)) Tabs[0].Name = Title;
        }

        /// <summary>
        /// 回到「只有一個分頁」之後呼叫：標題列改顯示收納區名稱，這時收納區名稱沿用剩下那個分頁的名稱，
        /// 名字才會跟使用者剛剛看到的一致。「桌面」收納區和自動名稱的分頁不改。
        /// </summary>
        public void AdoptSingleTabName()
        {
            var _t = ActiveTabData;
            if (Tabs.Count == 1 && !IsDefault && !string.IsNullOrWhiteSpace(Tabs[0].Name)) Title = Tabs[0].Name;
        }

        public bool HasItem(string path)
        {
            return AllItems.Any(i => string.Equals(i, path, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>從所有分頁移除符合條件的項目，回傳移除數量。</summary>
        public int RemoveItems(Predicate<string> match)
        {
            var _ = ActiveTabData;
            int n = 0;
            foreach (var t in Tabs) if (t.Items != null) n += t.Items.RemoveAll(match);
            return n;
        }

        /// <summary>檔案改名後，把所有分頁裡的舊路徑換成新路徑。</summary>
        public void ReplaceItem(string oldPath, string newPath)
        {
            var _ = ActiveTabData;
            foreach (var t in Tabs)
                if (t.Items != null)
                    for (int i = 0; i < t.Items.Count; i++)
                        if (string.Equals(t.Items[i], oldPath, StringComparison.OrdinalIgnoreCase)) t.Items[i] = newPath;
        }
    }

    /// <summary>外觀設定（全域，所有收納區共用）。</summary>
    public class AppearanceConfig
    {
        public int IconSize { get; set; } = 32;
        public double BgOpacity { get; set; } = 0.6;
        public string BgColor { get; set; } = "#182028";
        public bool Acrylic { get; set; } = false;      // 毛玻璃背景（較耗效能）
        public bool Shadow { get; set; } = true;        // 收納區外圍陰影
        public bool Animations { get; set; } = true;    // 收合、淡入、懸停等動畫
    }

    public class AppConfig
    {
        [JsonPropertyName("Fences")]   // 保留舊設定檔的欄位名稱，既有資料才讀得到
        public List<GridData> Grids { get; set; } = new List<GridData>();
        public AppearanceConfig Appearance { get; set; } = new AppearanceConfig();

        /// <summary>第一次執行的歡迎畫面看過了嗎。</summary>
        public bool FirstRunDone { get; set; }

        /// <summary>桌面 Ctrl+右鍵新增收納區（需要全域滑鼠 hook，預設關閉，避免防毒誤判）。</summary>
        public bool DesktopHook { get; set; } = false;

        /// <summary>auto / en / zh-TW</summary>
        public string Language { get; set; } = "auto";
        /// <summary>接管桌面：隱藏 Windows 原本的桌面 icon 層，所有桌面項目改由收納區顯示。</summary>
        public bool TakeOver { get; set; } = true;

        /// <summary>每天自動檢查一次更新（會連到 GitHub，預設關閉）。</summary>
        public bool AutoCheckUpdate { get; set; } = false;
        public long LastUpdateCheckUtc { get; set; }
        /// <summary>已經用系統匣通知過的版本，同一個版本只通知一次。</summary>
        public string LastNotifiedVersion { get; set; } = "";
        /// <summary>上次執行的程式版本，用來在升級時先備份設定檔。</summary>
        public string LastRunVersion { get; set; } = "";
    }

    /// <summary>
    /// 資料位置有兩種模式：
    /// 便攜版：程式旁有 portable.flag 或 data 資料夾 → 資料放在程式旁的 data（整個資料夾搬走就帶著走）。
    /// 安裝版：其他情況 → 資料放在 %LOCALAPPDATA%\Gridify（程式裝在 Program Files 也不會因為沒有寫入權限而存不了檔）。
    /// </summary>
    public static class AppPaths
    {
        public static readonly string BaseDir = (Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory).TrimEnd('\\', '/');
        public static readonly bool Portable =
            File.Exists(Path.Combine(BaseDir, "portable.flag")) || Directory.Exists(Path.Combine(BaseDir, "data"));
        public static readonly string DataDir = Portable
            ? Path.Combine(BaseDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gridify");
        public static readonly string ConfigFile = Path.Combine(DataDir, "config.json");
    }

    /// <summary>
    /// 設定檔讀寫。防護重點：
    /// 1. 寫入先寫暫存檔再替換，並自動留 config.json.bak（上一版）。
    /// 2. 啟動時留 config.json.startup.bak，並每天一份快照放在 data\backups（保留最近 7 天）。
    /// 3. 主檔讀不出來時，原檔另存成 config.corrupt-時間.json（不覆蓋），改讀備份。
    /// 4. 載入後驗證修正：重複項目、壞掉的尺寸、多個「桌面」收納區等。
    /// </summary>
    public static class ConfigStore
    {
        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>解除安裝時設為 true，避免之後又把設定檔寫回去。</summary>
        public static bool Disabled;

        static string Bak => AppPaths.ConfigFile + ".bak";
        static string StartupBak => AppPaths.ConfigFile + ".startup.bak";
        static string BackupDir => Path.Combine(AppPaths.DataDir, "backups");

        static AppConfig TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                PerfLog.Write("config read failed (" + Path.GetFileName(path) + "): " + ex.Message);
                return null;
            }
        }

        public static AppConfig Load()
        {
            AppConfig cfg = null;
            bool fromMain = false;
            bool existed = File.Exists(AppPaths.ConfigFile);

            if (File.Exists(AppPaths.ConfigFile))
            {
                cfg = TryRead(AppPaths.ConfigFile);
                if (cfg != null) fromMain = true;
                else
                {
                    // 主檔壞了或讀不出來：先原樣另存一份，絕不直接覆蓋
                    try
                    {
                        File.Copy(AppPaths.ConfigFile,
                            Path.Combine(AppPaths.DataDir, "config.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                    }
                    catch { }
                    cfg = TryRead(Bak) ?? TryRead(StartupBak) ?? LatestSnapshot();
                    PerfLog.Write(cfg != null ? "config restored from backup" : "config unreadable, starting empty (corrupt copy kept)");
                }
            }

            if (cfg == null) cfg = new AppConfig();
            Sanitize(cfg);
            if (existed) cfg.FirstRunDone = true;   // 已經有設定檔的人（升級）不需要再看歡迎畫面

            if (fromMain && !Disabled)
            {
                try { File.Copy(AppPaths.ConfigFile, StartupBak, true); } catch { }
                DailySnapshot();
            }
            return cfg;
        }

        static AppConfig LatestSnapshot()
        {
            try
            {
                if (!Directory.Exists(BackupDir)) return null;
                foreach (var f in Directory.GetFiles(BackupDir, "config-*.json").OrderByDescending(x => x))
                {
                    var c = TryRead(f);
                    if (c != null) return c;
                }
            }
            catch { }
            return null;
        }

        /// <summary>版本改變（升級）後第一次啟動：先把升級前的設定檔另存一份，萬一新版轉換出錯可以手動還原。</summary>
        public static void BackupBeforeUpgrade(AppConfig cfg, string current)
        {
            if (cfg == null || cfg.LastRunVersion == current) return;
            try
            {
                if (cfg.FirstRunDone && File.Exists(AppPaths.ConfigFile))
                {
                    Directory.CreateDirectory(BackupDir);
                    File.Copy(AppPaths.ConfigFile, Path.Combine(BackupDir, "before-upgrade-" + current + ".json"), true);
                }
            }
            catch { }
            cfg.LastRunVersion = current;
        }

        static void DailySnapshot()
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                string today = Path.Combine(BackupDir, "config-" + DateTime.Now.ToString("yyyyMMdd") + ".json");
                if (!File.Exists(today)) File.Copy(AppPaths.ConfigFile, today, false);
                foreach (var old in Directory.GetFiles(BackupDir, "config-*.json").OrderByDescending(x => x).Skip(7))
                    try { File.Delete(old); } catch { }
            }
            catch { }
        }

        public static void Save(AppConfig cfg)
        {
            if (Disabled || cfg == null) return;
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                string json = JsonSerializer.Serialize(cfg, Opt);
                string tmp = AppPaths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(AppPaths.ConfigFile)) File.Replace(tmp, AppPaths.ConfigFile, Bak, true);
                else File.Move(tmp, AppPaths.ConfigFile);
            }
            catch (Exception ex) { PerfLog.Write("config save failed: " + ex.Message); }
        }

        /// <summary>載入後驗證修正，避免壞資料讓後面的程式碼出錯。</summary>
        public static void Sanitize(AppConfig cfg)
        {
            if (cfg.Grids == null) cfg.Grids = new List<GridData>();
            if (cfg.Appearance == null) cfg.Appearance = new AppearanceConfig();
            cfg.Grids.RemoveAll(f => f == null);

            var ids = new HashSet<string>();
            var desktopSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool defaultSeen = false;

            foreach (var f in cfg.Grids)
            {
                if (string.IsNullOrEmpty(f.Id) || !ids.Add(f.Id))
                {
                    f.Id = Guid.NewGuid().ToString("N");
                    ids.Add(f.Id);
                }
                if (string.IsNullOrWhiteSpace(f.Title)) f.Title = Loc.T("grid.defaultTitle");
                if (f.IsDefault)
                {
                    if (defaultSeen) f.IsDefault = false;
                    defaultSeen = true;
                }

                f.Left = Finite(f.Left, 120);
                f.Top = Finite(f.Top, 120);
                f.Width = Math.Min(5000, Math.Max(140, Finite(f.Width, 320)));
                f.Height = Math.Min(5000, Math.Max(90, Finite(f.Height, 240)));

                // 收合狀態下高度如果只剩最小值，多半是舊版把「收合高度」誤存成收納區高度，還原成預設高度
                if (f.Collapsed && f.Height <= 90.5) f.Height = 240;

                var _tab = f.ActiveTabData;   // 舊設定檔（只有 Items）在這裡自動轉成單一分頁
                if (f.Tabs.Count > 30) f.Tabs.RemoveRange(30, f.Tabs.Count - 30);

                var tabIds = new HashSet<string>();
                var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // 同一個收納區內不重複（跨分頁）
                foreach (var t in f.Tabs)
                {
                    if (string.IsNullOrEmpty(t.Id) || !tabIds.Add(t.Id))
                    {
                        t.Id = Guid.NewGuid().ToString("N");
                        tabIds.Add(t.Id);
                    }
                    if (t.Name == null) t.Name = "";   // 空白 = 自動名稱（顯示時依語言決定）

                    var clean = new List<string>();
                    foreach (var p in t.Items ?? new List<string>())
                    {
                        if (string.IsNullOrWhiteSpace(p) || !own.Add(p)) continue;
                        if (DesktopScanner.IsDesktopEntry(p) && !desktopSeen.Add(p)) continue;   // 桌面項目只能屬於一個收納區
                        clean.Add(p);
                    }
                    t.Items = clean;
                }
                if (f.ActiveTab < 0 || f.ActiveTab >= f.Tabs.Count) f.ActiveTab = 0;
            }
        }

        static double Finite(double v, double fallback) => (double.IsNaN(v) || double.IsInfinity(v)) ? fallback : v;
    }

    /// <summary>開機自動啟動：只寫 HKCU\...\Run 一個值，解除安裝時會移除。</summary>
    public static class StartupHelper
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Gridify";
        const string LegacyName = "PignitionFence";   // 改名前的舊值

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k?.GetValue(ValueName) != null;
        }

        public static void Set(bool enable)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k == null) return;
                k.DeleteValue(LegacyName, false);
                if (enable) k.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
                else k.DeleteValue(ValueName, false);
            }
        }

        /// <summary>改名前如果有開機自動啟動，改寫成新的值（指向目前的程式），舊值移除。</summary>
        public static void MigrateLegacy()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (k?.GetValue(LegacyName) == null) return;
                Set(true);
            }
            catch { }
        }
    }
}
