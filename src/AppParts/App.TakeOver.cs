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
    /// <summary>接管桌面：隱藏／還原系統圖示層、監看桌面資料夾並同步。</summary>
    public partial class App
    {
        // ---------- 接管桌面 ----------
        void EnableTakeOver()
        {
            _takeOverActive = true;
            DefaultGrid();
            SyncDesktop();
            DesktopLayer.Hide();
            StartGuard();
            StartWatchers();
            _keepTimer.Start();
        }

        void DisableTakeOver()
        {
            _takeOverActive = false;
            _keepTimer.Stop();
            StopWatchers();
            DesktopLayer.Restore();
            StopGuard();
        }

        /// <summary>把桌面（個人 + 公共 + 系統圖示）與收納區對齊：新項目進「桌面」收納區、消失的項目從收納區移除。</summary>
        void SyncDesktop()
        {
            _inSync = true;   // 桌面自動同步造成的變動不算使用者的動作，不進復原堆疊
            try { SyncDesktopCore(); }
            finally { _inSync = false; }
        }

        void SyncDesktopCore()
        {
            if (!_takeOverActive) return;

            var syncWatch = Stopwatch.StartNew();
            var scan = DesktopScanner.Scan(out bool complete);
            var scanSet = new HashSet<string>(scan, StringComparer.OrdinalIgnoreCase);
            var def = DefaultGrid();

            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _cfg.Grids) foreach (var p in f.AllItems) assigned.Add(p);

            bool changed = false;
            foreach (var p in scan)
                if (!assigned.Contains(p)) { def.Items.Add(p); changed = true; }

            // 只有「這次掃描完整」才會移除項目；而且檔案還在磁碟上的（例如被設成隱藏）一律保留，
            // 避免暫時讀不到桌面就把使用者排好的收納區清空。
            if (complete)
                foreach (var f in _cfg.Grids)
                    if (f.RemoveItems(p => DesktopScanner.IsDesktopEntry(p) && !scanSet.Contains(p) && !StillExists(p)) > 0) changed = true;

            if (changed)
            {
                ReloadAll();
                SaveConfig();
            }
            PerfLog.Write("sync: " + syncWatch.ElapsedMilliseconds + "ms, scanned " + scan.Count + ", changed " + changed);
        }

        static bool StillExists(string p)
        {
            if (p.StartsWith(DesktopScanner.Prefix, StringComparison.Ordinal)) return false;
            try { return File.Exists(p) || Directory.Exists(p); } catch { return true; }
        }

        void StartWatchers()
        {
            StopWatchers();
            foreach (var dir in DesktopScanner.Dirs)
            {
                try
                {
                    var w = new FileSystemWatcher(dir)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                        EnableRaisingEvents = true
                    };
                    FileSystemEventHandler h = (s, a) => Dispatcher.BeginInvoke(new Action(() => { _syncTimer.Stop(); _syncTimer.Start(); }));
                    w.Created += h;
                    w.Deleted += h;
                    w.Changed += h;
                    w.Renamed += (s, a) => Dispatcher.BeginInvoke(new Action(() => { _syncTimer.Stop(); _syncTimer.Start(); }));
                    w.Error += (s, a) => Dispatcher.BeginInvoke(new Action(() => { _syncTimer.Stop(); _syncTimer.Start(); }));   // 事件太多緩衝區滿了：重新完整同步一次
                    _watchers.Add(w);
                }
                catch { }
            }
        }

        void StopWatchers()
        {
            foreach (var w in _watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
            _watchers.Clear();
        }
    }
}
