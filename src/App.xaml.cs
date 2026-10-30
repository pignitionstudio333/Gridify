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
    public partial class App : Application
    {
        Mutex _mutex;
        AppConfig _cfg;
        GridHost _host;
        readonly List<GridWindow> _wins = new List<GridWindow>();
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        System.Windows.Forms.NotifyIcon _tray;
        DispatcherTimer _syncTimer, _keepTimer;
        bool _hidden;
        bool _takeOverActive;   // 這次執行是否真的在接管桌面（設定想接管，但按住 Shift 啟動時會是 false）
        Process _guard;
        DateTime _burstStart = DateTime.MinValue;
        int _burstCount;
        DispatcherTimer _appearanceTimer;
        AppearanceWindow _appearanceWin;
        readonly DesktopMouseHook _mouseHook = new DesktopMouseHook();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var startWatch = Stopwatch.StartNew();
            InstallCrashHandlers();

            // Gridify.exe --guard <pid>：守護程序。主程式不管怎麼消失（當機、被工作管理員結束），都會把桌面圖示還原。
            int gi = Array.IndexOf(e.Args, "--guard");
            if (gi >= 0 && gi + 1 < e.Args.Length && int.TryParse(e.Args[gi + 1], out int guardPid))
            {
                RunGuard(guardPid);
                Shutdown();
                return;
            }

            // Gridify.exe --restore：把被隱藏的桌面圖示層還原後離開（給 uninstall.bat 用）
            if (e.Args.Contains("--restore"))
            {
                DesktopLayer.RestoreForce();
                Shutdown();
                return;
            }

            _mutex = new Mutex(true, "Local\\Gridify.SingleInstance", out bool created);
            if (!created) { Shutdown(); return; }

            _host = new GridHost
            {
                Save = SaveConfig,
                Exit = ExitApp,
                NewGrid = AddGrid,
                DeleteGrid = DeleteGrid,
                Dropped = OnDropped,
                MovedOut = OnMovedOut,
                Renamed = OnRenamed,
                Changed = SyncDesktop,
                IconStep = StepIconSize,
                IconSet = SetIconSize,
                Appearance = ShowAppearance,
                DeleteTab = DeleteTab,
                Undo = DoUndo,
                Redo = DoRedo,
                CanUndo = () => _undo.Count > 0,
                CanRedo = () => _redo.Count > 0,
                MoveTabTo = MoveTabBetween
            };

            _cfg = ConfigStore.Load();
            Loc.Init(_cfg.Language);          // 必須在建立任何視窗之前
            ConfigStore.BackupBeforeUpgrade(_cfg, AppInfo.Version);
            if (_cfg.Appearance == null) _cfg.Appearance = new AppearanceConfig();
            Theme.Apply(_cfg.Appearance);   // 必須在建立任何收納區視窗之前
            _appearanceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _appearanceTimer.Tick += (s, a) => { _appearanceTimer.Stop(); SaveConfig(); };
            // 按住 Shift 啟動 = 安全模式：這次不接管桌面（不改變設定）
            bool safeMode = (MouseNative.GetAsyncKeyState(0x10) & 0x8000) != 0;

            if (!_cfg.FirstRunDone)
            {
                var welcome = new WelcomeWindow();
                welcome.ShowDialog();
                _cfg.TakeOver = welcome.TakeOver;
                if (welcome.AutoStartChosen)
                    try { StartupHelper.Set(welcome.AutoStart); } catch { }
                _cfg.FirstRunDone = true;
                SaveConfig();
            }
            _takeOverActive = _cfg.TakeOver && !safeMode;

            if (_cfg.Grids.Count == 0 && !_takeOverActive) _cfg.Grids.Add(new GridData { Title = Loc.F("grid.new", 1) });
            RecycleBinMonitor.Start();   // 要在建立收納區（載入圖示）之前，才能記住回收筒目前是空還是滿
            foreach (var f in _cfg.Grids) OpenGrid(f);

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _syncTimer.Tick += (s, a) => { _syncTimer.Stop(); SyncDesktop(); };
            _keepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _keepTimer.Tick += (s, a) => DesktopLayer.Keep();

            SessionEnding += (s, a) => DesktopLayer.Restore();   // 登出、關機前一定把桌面還原
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            BuildTray();
            if (_cfg.DesktopHook) InstallDesktopHook();
            if (_takeOverActive) EnableTakeOver();
            SaveConfig();

            if (_cfg.AutoCheckUpdate)
            {
                var updTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };   // 啟動 30 秒後才檢查，不拖慢開機
                updTimer.Tick += (s, a) => { updTimer.Stop(); AutoCheckUpdate(); };
                updTimer.Start();
            }

            if (safeMode && _cfg.TakeOver)
                _tray.ShowBalloonTip(6000, Loc.AppName, Loc.T("tray.safeMode"), System.Windows.Forms.ToolTipIcon.Info);

            startWatch.Stop();
            PerfLog.Write("startup: " + startWatch.ElapsedMilliseconds + "ms, grids " + _cfg.Grids.Count +
                          ", items " + _cfg.Grids.Sum(f => f.AllItems.Count()) + ", mem " + MemMb() + "MB");
            // 圖示在背景載入完之後再記一次記憶體
            var memTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            memTimer.Tick += (s, a) => { memTimer.Stop(); PerfLog.Write("mem after 10s: " + MemMb() + "MB"); };
            memTimer.Start();
        }

        static long MemMb()
        {
            try { return Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024); } catch { return -1; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { StopWatchers(); } catch { }
            DesktopLayer.Restore();   // 結束時一定把桌面圖示還給系統
            try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; } catch { }
            StopGuard();
            _mouseHook.Remove();
            RecycleBinMonitor.Stop();
            _tray?.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        void SaveConfig()
        {
            TrackUndo();
            ConfigStore.Save(_cfg);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var w in _wins.ToList()) w.EnsureVisible();   // 外接螢幕拔掉、解析度改變後，把跑到畫面外的收納區拉回來
                DesktopLayer.Keep();
            }));
        }

        void ExitApp()
        {
            SaveConfig();
            Shutdown();   // OnExit 會把桌面圖示還給系統
        }

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        void ReloadAll()
        {
            foreach (var w in _wins.ToList()) w.Reload();
        }

        // ---------- 桌面 Ctrl+右鍵 新增收納區（實際的 hook 在 Core/DesktopMouseHook.cs） ----------
        void InstallDesktopHook() => _mouseHook.Install((x, y) => AddGridAt(x, y, true));
        void RemoveDesktopHook() => _mouseHook.Remove();
    }
}
