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

        void SaveConfig()
        {
            TrackUndo();
            ConfigStore.Save(_cfg);
        }

        // ---------- 復原 / 重做 ----------
        // 每次存檔時比對「結構」（收納區、分頁、項目的歸屬與名稱；不含位置、大小、折疊）。
        // 結構有變，就把變動「之前」的狀態推進復原堆疊。桌面自動同步造成的變動不記錄；
        // 檔案改名後會清空（舊路徑已不存在，還原會弄壞參照）。復原只動 Gridify 的設定，不動任何檔案。
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();
        string _lastKey, _lastFull;
        bool _inSync, _restoring;
        const int UndoMax = 30;

        string StructKey()
        {
            var sb = new StringBuilder();
            foreach (var g in _cfg.Grids)
            {
                var _x = g.ActiveTabData;
                sb.Append(g.Id).Append('|').Append(g.Title).Append('|').Append(g.IsDefault ? '1' : '0').Append('\n');
                foreach (var t in g.Tabs)
                {
                    sb.Append(' ').Append(t.Id).Append('|').Append(t.Name).Append('\n');
                    if (t.Items != null) foreach (var p in t.Items) sb.Append("  ").Append(p).Append('\n');
                }
            }
            return sb.ToString();
        }

        string Snapshot()
        {
            try { return JsonSerializer.Serialize(_cfg.Grids); } catch { return null; }
        }

        void TrackUndo()
        {
            if (_restoring) return;
            try
            {
                string key = StructKey();
                if (_lastKey == null) { _lastKey = key; _lastFull = Snapshot(); return; }
                if (key == _lastKey) return;
                if (!_inSync && _lastFull != null)
                {
                    _undo.Add(_lastFull);
                    if (_undo.Count > UndoMax) _undo.RemoveAt(0);
                    _redo.Clear();
                }
                _lastKey = key;
                _lastFull = Snapshot();
            }
            catch (Exception ex) { PerfLog.Write("undo track failed: " + ex.Message); }
        }

        void ClearUndo()
        {
            _undo.Clear();
            _redo.Clear();
            _lastKey = null;
            _lastFull = null;
        }

        void DoUndo()
        {
            if (_undo.Count == 0) return;
            string cur = Snapshot();
            string target = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            if (RestoreSnapshot(target) && cur != null) _redo.Add(cur);
        }

        void DoRedo()
        {
            if (_redo.Count == 0) return;
            string cur = Snapshot();
            string target = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            if (RestoreSnapshot(target) && cur != null)
            {
                _undo.Add(cur);
                if (_undo.Count > UndoMax) _undo.RemoveAt(0);
            }
        }

        /// <summary>把收納區 / 分頁 / 項目的歸屬還原成快照；現有收納區保留目前的位置、大小與折疊狀態，只有被刪掉的收納區會用快照裡的位置重建。</summary>
        bool RestoreSnapshot(string json)
        {
            List<GridData> snap;
            try { snap = JsonSerializer.Deserialize<List<GridData>>(json); }
            catch (Exception ex) { PerfLog.Crash("undo-parse", ex); return false; }
            if (snap == null) return false;

            _restoring = true;
            try
            {
                var keep = new List<GridData>();
                var created = new List<GridData>();
                foreach (var sg in snap)
                {
                    if (sg == null) continue;
                    var _s = sg.ActiveTabData;
                    var cg = _cfg.Grids.FirstOrDefault(g => g.Id == sg.Id);
                    if (cg == null) { keep.Add(sg); created.Add(sg); continue; }

                    string activeId = cg.ActiveTabData.Id;
                    cg.Title = sg.Title;
                    cg.IsDefault = sg.IsDefault;
                    cg.Tabs = sg.Tabs;
                    int ai = cg.Tabs.FindIndex(t => t.Id == activeId);   // 盡量停在原本選取的分頁
                    cg.ActiveTab = ai >= 0 ? ai : sg.ActiveTab;
                    var _c = cg.ActiveTabData;
                    keep.Add(cg);
                }

                // 快照之後才新增的收納區：關閉
                foreach (var cg in _cfg.Grids.ToList())
                {
                    if (snap.Any(s => s != null && s.Id == cg.Id)) continue;
                    var w = _wins.FirstOrDefault(x => ReferenceEquals(x.Data, cg));
                    if (w != null) { _wins.Remove(w); w.Close(); }
                }

                _cfg.Grids.Clear();
                _cfg.Grids.AddRange(keep);
                foreach (var g in created) OpenGrid(g);

                ReloadAll();
                ConfigStore.Save(_cfg);
            }
            catch (Exception ex) { PerfLog.Crash("undo-restore", ex); return false; }
            finally
            {
                _restoring = false;
                _lastKey = StructKey();
                _lastFull = Snapshot();
            }

            if (_takeOverActive) SyncDesktop();   // 還原期間桌面若有新檔案或檔案消失，重新對齊
            return true;
        }

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

            var undoItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.undo"));
            undoItem.Click += (s, a) => DoUndo();
            var redoItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.redo"));
            redoItem.Click += (s, a) => DoRedo();
            menu.Items.Add(undoItem);
            menu.Items.Add(redoItem);
            menu.Opening += (s, a) => { undoItem.Enabled = _undo.Count > 0; redoItem.Enabled = _redo.Count > 0; };
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
