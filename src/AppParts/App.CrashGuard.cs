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
    /// <summary>當機防護：未處理例外、守護程序、緊急還原桌面。</summary>
    public partial class App
    {
        // ---------- 當機防護 ----------
        void InstallCrashHandlers()
        {
            // 介面執行緒的例外：記錄後繼續執行。短時間內連續出錯代表狀態壞了，才還原桌面並結束。
            DispatcherUnhandledException += (s, ev) =>
            {
                PerfLog.Crash("ui", ev.Exception);
                var now = DateTime.UtcNow;
                if ((now - _burstStart).TotalSeconds > 10) { _burstStart = now; _burstCount = 0; }
                if (++_burstCount > 8)
                {
                    EmergencyRestore();
                    Shutdown();
                    return;
                }
                ev.Handled = true;
            };

            // 其他執行緒的致命例外：程式即將結束，先存檔並把桌面還原
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                PerfLog.Crash("fatal", ev.ExceptionObject as Exception);
                EmergencyRestore();
            };

            TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                PerfLog.Crash("task", ev.Exception);
                ev.SetObserved();
            };

            try
            {
                System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
                System.Windows.Forms.Application.ThreadException += (s, ev) => PerfLog.Crash("winforms", ev.Exception);
            }
            catch { }
        }

        /// <summary>最後手段：存檔（寫入是原子替換，不會寫出半個檔）並把桌面圖示還給系統。</summary>
        void EmergencyRestore()
        {
            try { if (_cfg != null) SaveConfig(); } catch { }
            try { DesktopLayer.RestoreForce(); } catch { }
        }

        static void RunGuard(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid)) p.WaitForExit();
            }
            catch { }   // 主程式已經不在了
            Thread.Sleep(800);
            try
            {
                // 使用者很快重開了 Gridify 的話，不要去動桌面
                if (Mutex.TryOpenExisting("Local\\Gridify.SingleInstance", out var m)) { m.Dispose(); return; }
            }
            catch { }
            DesktopLayer.RestoreForce();
        }

        void StartGuard()
        {
            try
            {
                if (_guard != null && !_guard.HasExited) return;
            }
            catch { }
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !string.Equals(Path.GetFileName(exe), "Gridify.exe", StringComparison.OrdinalIgnoreCase)) return;
                _guard = Process.Start(new ProcessStartInfo(exe, "--guard " + Environment.ProcessId)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex) { PerfLog.Write("guard start failed: " + ex.Message); }
        }

        void StopGuard()
        {
            try { _guard?.Kill(); } catch { }
            _guard = null;
        }
    }
}
