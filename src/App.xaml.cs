using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                Appearance = ShowAppearance
            };

            _cfg = ConfigStore.Load();
            Loc.Init(_cfg.Language);          // 必須在建立任何視窗之前
            StartupHelper.MigrateLegacy();
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
                _cfg.FirstRunDone = true;
                SaveConfig();
            }
            _takeOverActive = _cfg.TakeOver && !safeMode;

            if (_cfg.Grids.Count == 0 && !_takeOverActive) _cfg.Grids.Add(new GridData { Title = Loc.F("grid.new", 1) });
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

            if (safeMode && _cfg.TakeOver)
                _tray.ShowBalloonTip(6000, Loc.AppName, Loc.T("tray.safeMode"), System.Windows.Forms.ToolTipIcon.Info);

            startWatch.Stop();
            PerfLog.Write("startup: " + startWatch.ElapsedMilliseconds + "ms, fences " + _cfg.Grids.Count +
                          ", items " + _cfg.Grids.Sum(f => f.Items.Count) + ", mem " + MemMb() + "MB");
            // 圖示在背景載入完之後再記一次記憶體
            var memTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            memTimer.Tick += (s, a) => { memTimer.Stop(); PerfLog.Write("mem after 10s: " + MemMb() + "MB"); };
            memTimer.Start();
        }

        static long MemMb()
        {
            try { return Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024); } catch { return -1; }
        }

        // ---------- 外觀（全域） ----------
        void ApplyAppearance()
        {
            Theme.Apply(_cfg.Appearance);
            _appearanceTimer.Stop();
            _appearanceTimer.Start();   // 拖動滑桿時不要每一格都寫檔
        }

        void SetIconSize(int size)
        {
            _cfg.Appearance.IconSize = size;
            ApplyAppearance();
            _appearanceWin?.Sync();
        }

        void StepIconSize(int delta) => SetIconSize(_cfg.Appearance.IconSize + delta);

        void ShowAppearance()
        {
            if (_appearanceWin != null) { _appearanceWin.Activate(); return; }
            _appearanceWin = new AppearanceWindow(_cfg.Appearance, ApplyAppearance);
            _appearanceWin.Closed += (s, a) => { _appearanceWin = null; SaveConfig(); };
            _appearanceWin.Show();
            _appearanceWin.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { StopWatchers(); } catch { }
            DesktopLayer.Restore();   // 結束時一定把桌面圖示還給系統
            try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; } catch { }
            StopGuard();
            if (_hook != IntPtr.Zero) MouseNative.UnhookWindowsHookEx(_hook);
            _tray?.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        void SaveConfig() => ConfigStore.Save(_cfg);

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

            var items = w.Data.Items.ToList();
            _cfg.Grids.Remove(w.Data);
            _wins.Remove(w);
            w.Close();

            if (_cfg.TakeOver)
            {
                var def = DefaultGrid();
                foreach (var p in items)
                    if (DesktopScanner.IsDesktopEntry(p) && !def.Items.Any(i => Same(i, p))) def.Items.Add(p);
            }
            ReloadAll();
            SaveConfig();
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
                if (target.Data.Items.Any(i => Same(i, p))) continue;
                if (DesktopScanner.IsDesktopEntry(p))
                    foreach (var f in _cfg.Grids)
                        if (f != target.Data) f.Items.RemoveAll(i => Same(i, p));   // 桌面項目只屬於一個收納區
                target.Data.Items.Add(p);
            }
            ReloadAll();
            SaveConfig();
        }

        void OnMovedOut(GridWindow src, string[] paths)
        {
            foreach (var p in paths)
            {
                src.Data.Items.RemoveAll(i => Same(i, p));
                if (_cfg.TakeOver && DesktopScanner.IsDesktopEntry(p))
                {
                    var def = DefaultGrid();
                    if (!def.Items.Any(i => Same(i, p))) def.Items.Add(p);
                }
            }
            ReloadAll();
            SaveConfig();
        }

        void OnRenamed(string oldPath, string newPath)
        {
            foreach (var f in _cfg.Grids)
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], oldPath)) f.Items[i] = newPath;
            ReloadAll();
            SaveConfig();
            SyncDesktop();
        }

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
            if (!_takeOverActive) return;

            var syncWatch = Stopwatch.StartNew();
            var scan = DesktopScanner.Scan(out bool complete);
            var scanSet = new HashSet<string>(scan, StringComparer.OrdinalIgnoreCase);
            var def = DefaultGrid();

            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _cfg.Grids) foreach (var p in f.Items) assigned.Add(p);

            bool changed = false;
            foreach (var p in scan)
                if (!assigned.Contains(p)) { def.Items.Add(p); changed = true; }

            // 只有「這次掃描完整」才會移除項目；而且檔案還在磁碟上的（例如被設成隱藏）一律保留，
            // 避免暫時讀不到桌面就把使用者排好的收納區清空。
            if (complete)
                foreach (var f in _cfg.Grids)
                    if (f.Items.RemoveAll(p => DesktopScanner.IsDesktopEntry(p) && !scanSet.Contains(p) && !StillExists(p)) > 0) changed = true;

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
            var ask = MessageBox.Show(Loc.T("cat.confirm"),
                Loc.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

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

        // ---------- 桌面 Ctrl+右鍵 新增收納區 ----------
        // 不寫登錄檔、不注入系統選單：用滑鼠 hook 偵測「按住 Ctrl 在桌面按右鍵」，
        // 直接在滑鼠位置建立新收納區並進入改名。一般右鍵仍是 Windows 原本的選單。
        static class MouseNative
        {
            public const int WH_MOUSE_LL = 14, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, VK_CONTROL = 0x11;
            public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct POINT { public int x, y; }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
            [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
            [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
            [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
            [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
            [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
            [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetDpiForSystem();
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
            [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern IntPtr GetModuleHandle(string name);
        }

        IntPtr _hook;
        MouseNative.HookProc _hookProc;   // 要保留參考，避免被 GC 回收

        void RemoveDesktopHook()
        {
            if (_hook != IntPtr.Zero) MouseNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        void InstallDesktopHook()
        {
            if (_hook != IntPtr.Zero) return;
            _hookProc = MouseHookCallback;
            string mod = Process.GetCurrentProcess().MainModule.ModuleName;
            _hook = MouseNative.SetWindowsHookEx(MouseNative.WH_MOUSE_LL, _hookProc, MouseNative.GetModuleHandle(mod), 0);
        }

        static string ClassOf(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(64);
            MouseNative.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        static bool IsDesktop(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            string c = ClassOf(h);
            if (c == "SysListView32") return ClassOf(MouseNative.GetParent(h)) == "SHELLDLL_DefView";
            return c == "SHELLDLL_DefView" || c == "Progman" || c == "WorkerW";
        }

        IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
            if (code >= 0)
            {
                int msg = wParam.ToInt32();
                if ((msg == MouseNative.WM_RBUTTONDOWN || msg == MouseNative.WM_RBUTTONUP) &&
                    (MouseNative.GetAsyncKeyState(MouseNative.VK_CONTROL) & 0x8000) != 0)
                {
                    var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MouseNative.MSLLHOOKSTRUCT>(lParam);
                    if (IsDesktop(MouseNative.WindowFromPoint(info.pt)))
                    {
                        if (msg == MouseNative.WM_RBUTTONUP)
                        {
                            double scale = MouseNative.GetDpiForSystem() / 96.0;
                            double x = info.pt.x / scale, y = info.pt.y / scale;
                            Dispatcher.BeginInvoke(new Action(() => AddGridAt(x, y, true)));
                        }
                        return (IntPtr)1;   // 吃掉這次點擊，不讓桌面跳出選單
                    }
                }
            }
            }
            catch { }   // 全域 hook 裡的例外不可以丟出去
            return MouseNative.CallNextHookEx(_hook, code, wParam, lParam);
        }

        // ---------- 系統匣 ----------
        void BuildTray()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(Loc.T("tray.newGrid"), null, (s, a) => AddGrid());
            menu.Items.Add(Loc.T("tray.toggle"), null, (s, a) => ToggleAll());

            var take = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.takeover"))
            {
                CheckOnClick = true,
                Checked = _takeOverActive
            };
            take.Click += (s, a) =>
            {
                _cfg.TakeOver = take.Checked;
                if (take.Checked) EnableTakeOver(); else DisableTakeOver();
                SaveConfig();
            };
            menu.Items.Add(take);

            menu.Items.Add(Loc.T("tray.appearance"), null, (s, a) => ShowAppearance());
            menu.Items.Add(Loc.T("tray.categorize"), null, (s, a) => AutoCategorize());
            menu.Items.Add(Loc.T("tray.refresh"), null, (s, a) => SyncDesktop());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var startup = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.startup"))
            {
                CheckOnClick = true,
                Checked = StartupHelper.IsEnabled()
            };
            startup.Click += (s, a) => StartupHelper.Set(startup.Checked);
            menu.Items.Add(startup);

            var hook = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.hook"))
            {
                CheckOnClick = true,
                Checked = _cfg.DesktopHook
            };
            hook.Click += (s, a) =>
            {
                _cfg.DesktopHook = hook.Checked;
                if (hook.Checked) InstallDesktopHook(); else RemoveDesktopHook();
                SaveConfig();
            };
            menu.Items.Add(hook);

            var lang = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.language"));
            foreach (var opt in new[] { ("auto", "lang.auto"), ("en", "lang.en"), ("zh-TW", "lang.zh") })
            {
                string code = opt.Item1;
                var item = new System.Windows.Forms.ToolStripMenuItem(Loc.T(opt.Item2))
                {
                    Checked = (_cfg.Language ?? "auto") == code
                };
                item.Click += (s, a) =>
                {
                    _cfg.Language = code;
                    SaveConfig();
                    MessageBox.Show(Loc.T("lang.restart"), Loc.AppName);   // 語言在下次啟動時套用
                };
                lang.DropDownItems.Add(item);
            }
            menu.Items.Add(lang);

            menu.Items.Add(Loc.T("tray.openData"), null, (s, a) =>
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
            });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(Loc.T("tray.uninstall"), null, (s, a) => Uninstall());
            menu.Items.Add(Loc.T("tray.quit"), null, (s, a) => ExitApp());

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = MakeTrayIcon(),
                Text = Loc.T("tray.tooltip"),
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += (s, a) => ToggleAll();
        }

        /// <summary>程式內自己畫的系統匣圖示（藍色圓角方塊加四格），不需要額外的圖檔。</summary>
        static System.Drawing.Icon MakeTrayIcon()
        {
            try
            {
                using (var bmp = new System.Drawing.Bitmap(32, 32))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    using (var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 0x3B, 0x82, 0xF6)))
                    {
                        const float r = 7f, x = 1f, y = 1f, w = 30f, h = 30f;
                        path.AddArc(x, y, r * 2, r * 2, 180, 90);
                        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                        path.CloseFigure();
                        g.FillPath(bg, path);
                    }
                    int[] alphas = { 255, 200, 200, 255 };
                    int k = 0;
                    foreach (int py in new[] { 6, 17 })
                        foreach (int px in new[] { 6, 17 })
                            using (var wb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(alphas[k++], 255, 255, 255)))
                                g.FillRectangle(wb, px, py, 9, 9);
                    return System.Drawing.Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch { return System.Drawing.SystemIcons.Application; }
        }

        void ToggleAll()
        {
            _hidden = !_hidden;
            foreach (var w in _wins)
            {
                if (_hidden) w.Hide(); else w.Show();
            }
        }

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
            try { if (Directory.Exists(AppPaths.DataDir)) Directory.Delete(AppPaths.DataDir, true); } catch { }

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
                                n.Equals("data", StringComparison.OrdinalIgnoreCase) ||
                                n.Equals("portable.flag", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("PRIVACY", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("Restore-Desktop", StringComparison.OrdinalIgnoreCase) ||
                                n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                                n.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
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
