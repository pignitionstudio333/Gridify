using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Markup;

namespace GridifyApp
{
    /// <summary>
    /// 介面文字（英文 / 繁體中文）。語言設定為「自動」時跟著 Windows 的顯示語言；
    /// 找不到的字串會退回英文，再找不到就直接顯示 key，不會當機。
    /// </summary>
    public static class Loc
    {
        public const string AppName = "Gridify";

        static Dictionary<string, string> _cur;

        /// <summary>目前使用的語言：en 或 zh-TW。</summary>
        public static string Lang { get; private set; } = "en";

        /// <summary>pref：auto / en / zh-TW。必須在建立任何視窗之前呼叫。</summary>
        public static void Init(string pref)
        {
            string l = pref;
            if (string.IsNullOrEmpty(l) || l == "auto")
            {
                string n = CultureInfo.CurrentUICulture.Name;
                bool tw = n.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                          n.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                          n.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase) ||
                          n.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0;
                l = tw ? "zh-TW" : "en";
            }
            Lang = l == "zh-TW" ? "zh-TW" : "en";
            _cur = Lang == "zh-TW" ? Zh : En;
        }

        public static string T(string key)
        {
            var d = _cur ?? En;
            if (d.TryGetValue(key, out var v)) return v;
            if (En.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "app.name", "Gridify" },
            { "grid.default", "Desktop" },
            { "grid.defaultTitle", "Grid" },
            { "tab.defaultName", "Tab 1" },
            { "grid.new", "Grid {0}" },
            { "menu.open", "Open" },
            { "menu.openLocation", "Open file location" },
            { "menu.moveOut", "Move out of this grid (back to the Desktop grid)" },
            { "menu.renameFile", "Rename file" },
            { "menu.deleteFile", "Delete file (to Recycle Bin)" },
            { "menu.collapse", "Collapse / expand this grid" },
            { "menu.collapseHint", "Double-click title" },
            { "menu.renameGrid", "Rename this grid" },
            { "menu.newGrid", "New grid" },
            { "menu.deleteGrid", "Delete this grid" },
            { "menu.newTab", "New tab" },
            { "menu.renameTab", "Rename tab" },
            { "menu.deleteTab", "Delete tab" },
            { "menu.moveLeft", "Move left" },
            { "menu.moveRight", "Move right" },
            { "tab.newName", "Tab {0}" },
            { "drop.moveTab", "Move tab here" },
            { "menu.undo", "Undo" },
            { "menu.redo", "Redo" },
            { "tray.undo", "Undo last change" },
            { "tray.redo", "Redo" },
            { "tab.deleteConfirm", "Delete tab \"{0}\"?\n\nIts items move back to \"{1}\". Your files are not touched." },
            { "tab.deleteConfirm2", "Delete tab \"{0}\"?\n\nIts items are removed from this grid. Your files are not touched." },
            { "menu.iconSize", "Icon size (all grids)" },
            { "size.small", "Small" },
            { "size.medium", "Medium (default)" },
            { "size.large", "Large" },
            { "size.xl", "Extra large" },
            { "menu.appearance", "Appearance…" },
            { "menu.quit", "Quit Gridify (the desktop is restored)" },
            { "chevron.tip", "Collapse / expand (double-clicking the title works too)" },
            { "drop.add", "Drop to add" },
            { "drop.reorder", "Drop to reorder" },
            { "err.open", "Cannot open:\n{0}\n\n{1}" },
            { "rename.title", "Rename" },
            { "rename.prompt", "New name:" },
            { "rename.invalid", "The name contains characters that are not allowed." },
            { "rename.exists", "An item with that name already exists." },
            { "rename.fail", "Cannot rename:\n{0}" },
            { "delete.confirm", "Move these {0} item(s) to the Recycle Bin?\n\n{1}" },
            { "delete.fail", "Cannot delete \"{0}\":\n{1}" },
            { "btn.ok", "OK" },
            { "btn.cancel", "Cancel" },
            { "btn.close", "Close" },
            { "tray.newGrid", "New grid" },
            { "tray.toggle", "Show / hide all" },
            { "tray.takeover", "Take over desktop (hide the system desktop icons)" },
            { "tray.appearance", "Appearance…" },
            { "tray.categorize", "Organize by type…" },
            { "tray.refresh", "Refresh desktop" },
            { "tray.startup", "Start with Windows" },
            { "tray.hook", "Ctrl + right-click on the desktop creates a grid" },
            { "tray.language", "Language" },
            { "lang.auto", "Automatic" },
            { "lang.en", "English" },
            { "lang.zh", "繁體中文" },
            { "lang.restart", "Language changed. Restart Gridify to apply it." },
            { "tray.openData", "Open data folder" },
            { "tray.uninstall", "Uninstall…" },
            { "tray.quit", "Quit" },
            { "tray.tooltip", "Gridify" },
            { "tray.safeMode", "Safe mode: Shift was held, so Gridify did not take over the desktop this time." },
            { "grid.deleteConfirm", "Delete the grid \"{0}\"?\nDesktop items in it go back to the Desktop grid; other items are only removed from the list. Your files are never deleted." },
            { "cat.needTakeover", "Please turn on \"Take over desktop\" first." },
            { "cat.confirm", "Sort the items in the Desktop grid by type (Apps & shortcuts, Folders, Documents, Images & media)?\n\nYes = into tabs inside the Desktop grid (recommended)\nNo = into separate grids\nCancel = do nothing\n\nThis only changes which tab or grid an item belongs to; no files are touched. System icons and other types stay in \"Other\"." },
            { "cat.other", "Other" },
            { "cat.nothing", "There is nothing to sort in the current tab." },
            { "cat.apps", "Apps & shortcuts" },
            { "cat.folders", "Folders" },
            { "cat.docs", "Documents" },
            { "cat.media", "Images & media" },
            { "un.title", "Uninstall Gridify" },
            { "un.confirm", "Uninstalling will:\n  • Give the desktop icons back to Windows (stop taking over)\n  • Remove the start-with-Windows entry (one registry value)\n  • Delete all grid settings: {0}\n\nYour original files are never touched.\n\nUninstall now?" },
            { "un.devLayout", "Settings and the auto-start entry were removed and the desktop was restored.\n\nThe program is inside a source-code folder, so it is not deleted automatically. Please delete the folder yourself:\n{0}" },
            { "un.askDelete", "Settings and the auto-start entry were removed and the desktop was restored.\n\nAlso delete the program folder?\n{0}" },
            { "un.done", "Done. The program folder was kept; you can delete it later." },
            { "ap.title", "Appearance" },
            { "ap.size", "Size" },
            { "ap.iconSize", "Icon size" },
            { "ap.bg", "Background" },
            { "ap.opacity", "Opacity" },
            { "ap.custom", "Custom color…" },
            { "ap.fx", "Performance & effects" },
            { "ap.acrylic", "Acrylic blur background" },
            { "ap.acrylicHint", "Heavier on the GPU; may not work on old graphics cards or Remote Desktop. Turns the shadow off while enabled." },
            { "ap.shadow", "Grid shadow" },
            { "ap.shadowHint", "A soft shadow around each grid." },
            { "ap.anim", "Animations" },
            { "ap.animHint", "Collapse, icon fade-in and hover fades." },
            { "ap.reset", "Reset to defaults" },
            { "ap.n1", "Docs" },
            { "ap.n2", "Photos" },
            { "ap.n3", "Project" },
            { "ap.n4", "Tools" },
            { "ap.n5", "Notes" },
            { "ap.n6", "Music" },
            { "c.slate", "Slate" },
            { "c.black", "Black" },
            { "c.graphite", "Graphite" },
            { "c.navy", "Navy" },
            { "c.purple", "Purple" },
            { "c.green", "Green" },
            { "c.wine", "Wine" },
            { "c.lightgray", "Light gray" },
            { "c.white", "White" },
            { "w.title", "Welcome to Gridify" },
            { "w.heading", "Tidy your desktop into movable grids" },
            { "w.body", "Recommended: let Gridify take over the desktop. It hides Windows' own icon layer and shows every desktop item inside grids that you can move, resize and arrange freely.\n\nYour files are never moved, copied or deleted by this." },
            { "w.undoHead", "You can undo it at any time:" },
            { "w.undo1", "Tray icon → untick \"Take over desktop\"" },
            { "w.undo2", "Quit Gridify — the desktop icons come back automatically" },
            { "w.undo3", "Hold Shift while starting Gridify to start without taking over" },
            { "w.undo4", "If anything goes wrong, run  Gridify.exe --restore" },
            { "w.enable", "Take over my desktop (recommended)" },
            { "w.later", "Not now" },
        };

        static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            { "app.name", "Gridify" },
            { "grid.default", "桌面" },
            { "grid.defaultTitle", "收納區" },
            { "tab.defaultName", "分頁 1" },
            { "grid.new", "收納區 {0}" },
            { "menu.open", "開啟" },
            { "menu.openLocation", "開啟檔案所在位置" },
            { "menu.moveOut", "移出此收納區（回到「桌面」收納區）" },
            { "menu.renameFile", "重新命名檔案" },
            { "menu.deleteFile", "刪除檔案（移到資源回收筒）" },
            { "menu.collapse", "收合 / 展開這個收納區" },
            { "menu.collapseHint", "雙擊標題" },
            { "menu.renameGrid", "重新命名這個收納區" },
            { "menu.newGrid", "新增收納區" },
            { "menu.deleteGrid", "刪除這個收納區" },
            { "menu.newTab", "新增分頁" },
            { "menu.renameTab", "重新命名分頁" },
            { "menu.deleteTab", "刪除分頁" },
            { "menu.moveLeft", "向左移" },
            { "menu.moveRight", "向右移" },
            { "tab.newName", "分頁 {0}" },
            { "drop.moveTab", "把分頁移到這裡" },
            { "menu.undo", "復原" },
            { "menu.redo", "重做" },
            { "tray.undo", "復原上一個動作" },
            { "tray.redo", "重做" },
            { "tab.deleteConfirm", "刪除分頁「{0}」？\n\n分頁裡的項目會移回「{1}」，你的檔案不會被動到。" },
            { "tab.deleteConfirm2", "刪除分頁「{0}」？\n\n分頁裡的項目會從這個收納區移除，你的檔案不會被動到。" },
            { "menu.iconSize", "圖示大小（全部收納區）" },
            { "size.small", "小" },
            { "size.medium", "中（預設）" },
            { "size.large", "大" },
            { "size.xl", "特大" },
            { "menu.appearance", "外觀設定…" },
            { "menu.quit", "結束 Gridify（桌面會還原）" },
            { "chevron.tip", "收合 / 展開（雙擊標題列也可以）" },
            { "drop.add", "放開以加入" },
            { "drop.reorder", "放開以調整順序" },
            { "err.open", "無法開啟：\n{0}\n\n{1}" },
            { "rename.title", "重新命名" },
            { "rename.prompt", "新名稱：" },
            { "rename.invalid", "名稱含有不能使用的字元。" },
            { "rename.exists", "已經有同名的項目。" },
            { "rename.fail", "無法重新命名：\n{0}" },
            { "delete.confirm", "要把以下 {0} 個項目移到資源回收筒嗎？\n\n{1}" },
            { "delete.fail", "無法刪除「{0}」：\n{1}" },
            { "btn.ok", "確定" },
            { "btn.cancel", "取消" },
            { "btn.close", "關閉" },
            { "tray.newGrid", "新增收納區" },
            { "tray.toggle", "顯示 / 隱藏全部" },
            { "tray.takeover", "接管桌面（隱藏系統桌面 icon）" },
            { "tray.appearance", "外觀設定…" },
            { "tray.categorize", "依類型自動分類…" },
            { "tray.refresh", "重新整理桌面" },
            { "tray.startup", "開機自動啟動" },
            { "tray.hook", "在桌面按 Ctrl+右鍵新增收納區" },
            { "tray.language", "語言" },
            { "lang.auto", "自動" },
            { "lang.en", "English" },
            { "lang.zh", "繁體中文" },
            { "lang.restart", "語言已變更，重新啟動 Gridify 後生效。" },
            { "tray.openData", "開啟設定資料夾" },
            { "tray.uninstall", "解除安裝…" },
            { "tray.quit", "結束" },
            { "tray.tooltip", "Gridify 桌面收納區" },
            { "tray.safeMode", "安全模式：啟動時按住了 Shift，這次不接管桌面。" },
            { "grid.deleteConfirm", "刪除收納區「{0}」？\n裡面的桌面項目會回到「桌面」收納區；其他項目只移除記錄，原檔案不會被刪除。" },
            { "cat.needTakeover", "請先開啟「接管桌面」。" },
            { "cat.confirm", "把「桌面」收納區裡的項目依類型分類（應用程式與捷徑、資料夾、文件、圖片與影音）？\n\n是 = 在「桌面」收納區裡分成分頁（建議）\n否 = 分成多個收納區\n取消 = 不做\n\n只改變項目所屬的分頁或收納區，不會動任何檔案；系統圖示與其他類型留在「其他」。" },
            { "cat.other", "其他" },
            { "cat.nothing", "目前的分頁裡沒有可以分類的項目。" },
            { "cat.apps", "應用程式與捷徑" },
            { "cat.folders", "資料夾" },
            { "cat.docs", "文件" },
            { "cat.media", "圖片與影音" },
            { "un.title", "解除安裝 Gridify" },
            { "un.confirm", "解除安裝會：\n  • 把桌面圖示還給 Windows（取消接管）\n  • 移除開機自動啟動設定（登錄檔的一個值）\n  • 刪除所有收納區設定：{0}\n\n你的原始檔案完全不會被動到。\n\n確定要解除安裝？" },
            { "un.devLayout", "設定與自動啟動已移除，桌面已還原。\n\n偵測到程式位在專案原始碼資料夾內，為了安全不自動刪除程式本身，請自行刪除資料夾：\n{0}" },
            { "un.askDelete", "設定與自動啟動已移除，桌面已還原。\n\n要連程式所在的資料夾一起刪除嗎？\n{0}" },
            { "un.done", "已完成。程式資料夾仍保留，你可以之後自行刪除。" },
            { "ap.title", "外觀設定" },
            { "ap.size", "大小" },
            { "ap.iconSize", "圖示大小" },
            { "ap.bg", "背景" },
            { "ap.opacity", "透明度" },
            { "ap.custom", "自訂顏色…" },
            { "ap.fx", "效能與效果" },
            { "ap.acrylic", "毛玻璃背景" },
            { "ap.acrylicHint", "較耗效能；舊顯卡或遠端桌面可能無效。開啟時會停用陰影。" },
            { "ap.shadow", "收納區陰影" },
            { "ap.shadowHint", "收納區外圍的柔和陰影。" },
            { "ap.anim", "動畫效果" },
            { "ap.animHint", "收合、圖示淡入、懸停淡入淡出。" },
            { "ap.reset", "還原預設" },
            { "ap.n1", "文件" },
            { "ap.n2", "照片" },
            { "ap.n3", "專案" },
            { "ap.n4", "工具" },
            { "ap.n5", "筆記" },
            { "ap.n6", "音樂" },
            { "c.slate", "深藍灰" },
            { "c.black", "黑" },
            { "c.graphite", "石墨" },
            { "c.navy", "深藍" },
            { "c.purple", "深紫" },
            { "c.green", "深綠" },
            { "c.wine", "酒紅" },
            { "c.lightgray", "淺灰" },
            { "c.white", "白" },
            { "w.title", "歡迎使用 Gridify" },
            { "w.heading", "把桌面整理成可以自由移動的收納區" },
            { "w.body", "建議讓 Gridify 接管桌面：它會隱藏 Windows 原本的桌面圖示層，把所有桌面項目顯示在可以自由移動、縮放、排列的收納區裡。\n\n這個過程不會移動、複製或刪除你的任何檔案。" },
            { "w.undoHead", "隨時都可以還原：" },
            { "w.undo1", "系統匣圖示 → 取消勾選「接管桌面」" },
            { "w.undo2", "結束 Gridify — 桌面圖示會自動回來" },
            { "w.undo3", "啟動 Gridify 時按住 Shift，就不會接管桌面" },
            { "w.undo4", "萬一出問題，執行  Gridify.exe --restore" },
            { "w.enable", "接管我的桌面（建議）" },
            { "w.later", "先不要" },
        };
    }

    /// <summary>XAML 用法：Header="{l:T menu.open}"（xmlns:l="clr-namespace:GridifyApp"）。</summary>
    public class TExtension : MarkupExtension
    {
        public string Key { get; set; }

        public TExtension() { }
        public TExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key ?? "");
    }
}
