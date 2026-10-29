using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GridifyApp
{
    /// <summary>掃描桌面（個人桌面 + 公共桌面 + 系統圖示），並判斷路徑是否屬於桌面。</summary>
    public static class DesktopScanner
    {
        public const string Prefix = "shell:::";

        static readonly (string Guid, bool DefShown)[] SystemIcons =
        {
            ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false),   // 本機
            ("{645FF040-5081-101B-9F08-00AA002F954E}", true),    // 資源回收筒
            ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false),   // 網路
            ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", false),   // 使用者資料夾
            ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false),   // 控制台
        };

        static readonly string[] DesktopDirs =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        static string Norm(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return p; }
        }

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static IEnumerable<string> Dirs =>
            DesktopDirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Select(Norm).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public static bool IsFromDesktop(string path)
        {
            string dir = Path.GetDirectoryName(Norm(path));
            if (string.IsNullOrEmpty(dir)) return false;
            return DesktopDirs.Any(d => !string.IsNullOrEmpty(d) && Same(Norm(d), dir));
        }

        public static bool IsDesktopEntry(string path) =>
            path.StartsWith(Prefix, StringComparison.Ordinal) || IsFromDesktop(path);

        static bool Shown(string guid, bool defShown)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel"))
                {
                    var v = k?.GetValue(guid);
                    if (v is int i) return i == 0;
                }
            }
            catch { }
            return defShown;
        }

        /// <summary>
        /// 掃描桌面。complete 為 false 代表有目錄讀取失敗（例如暫時讀不到、權限問題）：
        /// 呼叫端不可以因為「這次沒掃到」就把項目從收納區移除。
        /// </summary>
        public static List<string> Scan(out bool complete)
        {
            complete = true;
            var list = new List<string>();
            foreach (var s in SystemIcons)
                if (Shown(s.Guid, s.DefShown)) list.Add(Prefix + s.Guid);

            var dirs = Dirs.ToList();
            if (dirs.Count == 0) complete = false;
            foreach (var dir in dirs)
            {
                try
                {
                    var entries = new DirectoryInfo(dir).EnumerateFileSystemInfos()
                        .Where(i => (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        .OrderBy(i => (i.Attributes & FileAttributes.Directory) == 0)
                        .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
                    foreach (var i in entries) list.Add(i.FullName);
                }
                catch (Exception ex)
                {
                    complete = false;
                    PerfLog.Write("scan failed for " + dir + ": " + ex.Message);
                }
            }
            return list;
        }
    }

    /// <summary>
    /// 隱藏／還原 Windows 原本的桌面 icon 層（只是 ShowWindow，不改任何檔案或設定，重開機或重啟 Explorer 也會恢復）。
    /// </summary>
    public static class DesktopLayer
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

        static bool _want;          // 使用者要求隱藏（Explorer 還沒就緒時，之後由 Keep 補做）
        static bool _hidden;        // 已經實際隱藏過
        static bool _wasVisible = true;

        /// <summary>看 Windows 的設定（檔案總管「顯示桌面圖示」）：使用者本來就把桌面圖示關掉的話，才不要幫他顯示。</summary>
        static bool UserWantsIcons()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                    if (k?.GetValue("HideIcons") is int v && v == 1) return false;
            }
            catch { }
            return true;
        }

        static IntPtr Find()
        {
            IntPtr def = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def == IntPtr.Zero)
            {
                IntPtr w = IntPtr.Zero;
                while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                {
                    def = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (def != IntPtr.Zero) break;
                }
            }
            return def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
        }

        public static void Hide()
        {
            _want = true;
            Keep();
        }

        /// <summary>
        /// 定時呼叫：Explorer 還沒啟動好（開機自動啟動時常見）、重新啟動，或自己把圖示層再顯示出來時，補做隱藏。
        /// </summary>
        public static void Keep()
        {
            try
            {
                if (!_want) return;
                IntPtr lv = Find();
                if (lv == IntPtr.Zero) return;
                if (!_hidden)
                {
                    _wasVisible = UserWantsIcons();   // 不看當下是否可見（可能是上次被強制結束留下的隱藏狀態）
                    _hidden = true;
                }
                if (_wasVisible && IsWindowVisible(lv)) ShowWindow(lv, 0);
            }
            catch { }
        }

        public static void Restore()
        {
            _want = false;
            try
            {
                if (!_hidden) return;
                _hidden = false;
                IntPtr lv = Find();
                if (lv != IntPtr.Zero && _wasVisible && !IsWindowVisible(lv)) ShowWindow(lv, 5);
            }
            catch { }
        }

        /// <summary>給 Gridify.exe --restore 用：不管之前狀態，圖示層沒顯示就顯示。</summary>
        public static void RestoreForce()
        {
            _want = false;
            try
            {
                IntPtr lv = Find();
                if (lv != IntPtr.Zero && UserWantsIcons() && !IsWindowVisible(lv)) ShowWindow(lv, 5);
            }
            catch { }
        }
    }
}
