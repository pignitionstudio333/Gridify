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
    /// <summary>解除安裝流程。</summary>
    public partial class App
    {
        // ---------- 解除安裝 ----------
        void Uninstall()
        {
            // 安裝版：交給安裝程式附的解除安裝程式處理（它會還原桌面、移除開機啟動、刪除程式檔案）
            if (!AppPaths.Portable)
            {
                string unins = Path.Combine(AppPaths.BaseDir, "unins000.exe");
                if (File.Exists(unins))
                {
                    try { Process.Start(new ProcessStartInfo(unins) { UseShellExecute = true }); } catch { }
                    return;
                }
            }

            var r = MessageBox.Show(Loc.F("un.confirm", AppPaths.DataDir),
                Loc.T("un.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try { StopWatchers(); } catch { }
            DesktopLayer.Restore();   // 先把桌面圖示還給系統
            ConfigStore.Disabled = true;
            try { StartupHelper.Set(false); } catch { }
            ConfigStore.RemoveAllData();   // 只刪 Gridify 自己的檔案，不遞迴刪整個資料夾

            string msg;
            if (IsDevLayout() || !CanDeleteProgramFolder())
            {
                msg = Loc.F("un.devLayout", AppPaths.BaseDir);
            }
            else if (MessageBox.Show(Loc.F("un.askDelete", AppPaths.BaseDir),
                         Loc.T("un.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    // 等本程式結束後再刪除自己所在的資料夾
                    Process.Start(new ProcessStartInfo("cmd.exe",
                        "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + AppPaths.BaseDir + "\"")
                    { CreateNoWindow = true, UseShellExecute = false });
                }
                catch { }
                msg = null;
            }
            else
            {
                msg = Loc.T("un.done");
            }

            if (msg != null) MessageBox.Show(msg, Loc.AppName);
            Shutdown();
        }

        /// <summary>
        /// 只有「資料夾裡全部都是 Gridify 自己的檔案」才允許自動刪除程式資料夾。
        /// 避免使用者把 zip 直接解壓在下載資料夾、桌面等地方時，連其他檔案一起刪掉。
        /// </summary>
        static bool CanDeleteProgramFolder()
        {
            try
            {
                if (!File.Exists(Path.Combine(AppPaths.BaseDir, "Gridify.exe"))) return false;
                foreach (var e in new DirectoryInfo(AppPaths.BaseDir).EnumerateFileSystemInfos())
                {
                    string n = e.Name;
                    bool ours = n.StartsWith("Gridify", StringComparison.OrdinalIgnoreCase) ||
                                (n.Equals("data", StringComparison.OrdinalIgnoreCase) && new DirectoryInfo(e.FullName).GetFileSystemInfos().Length == 0) ||
                                n.Equals("portable.flag", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("PRIVACY", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("Restore-Desktop", StringComparison.OrdinalIgnoreCase);
                    if (!ours) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>往上 4 層內若有 .csproj / .sln，代表是在開發資料夾裡跑，不自動刪除。</summary>
        static bool IsDevLayout()
        {
            try
            {
                var dir = new DirectoryInfo(AppPaths.BaseDir);
                if (dir.Parent == null) return true;                 // 磁碟根目錄
                for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
                    if (dir.GetFiles("*.csproj").Length > 0 || dir.GetFiles("*.sln").Length > 0 ||
                        Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                        (Directory.Exists(Path.Combine(dir.FullName, "src")) && new DirectoryInfo(Path.Combine(dir.FullName, "src")).GetFiles("*.csproj").Length > 0)) return true;
                return false;
            }
            catch { return true; }
        }
    }
}
