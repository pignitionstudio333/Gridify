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
    /// <summary>系統匣圖示與選單。</summary>
    public partial class App
    {
        // ---------- 系統匣 ----------
        void BuildTray()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var Sep = new Func<System.Windows.Forms.ToolStripSeparator>(() => new System.Windows.Forms.ToolStripSeparator());

            // 1. 常用
            menu.Items.Add(Loc.T("tray.newGrid"), null, (s, a) => AddGrid());
            menu.Items.Add(Loc.T("tray.toggle"), null, (s, a) => ToggleAll());
            menu.Items.Add(Sep());

            // 2. 整理桌面
            menu.Items.Add(Loc.T("tray.categorize"), null, (s, a) => AutoCategorize());
            menu.Items.Add(Loc.T("tray.refresh"), null, (s, a) => SyncDesktop());
            var undoItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.undo")) { ShortcutKeyDisplayString = "Ctrl+Z" };
            undoItem.Click += (s, a) => DoUndo();
            var redoItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.redo")) { ShortcutKeyDisplayString = "Ctrl+Y" };
            redoItem.Click += (s, a) => DoRedo();
            menu.Items.Add(undoItem);
            menu.Items.Add(redoItem);
            menu.Opening += (s, a) => { undoItem.Enabled = _undo.Count > 0; redoItem.Enabled = _redo.Count > 0; };
            menu.Items.Add(Sep());

            // 3. 設定
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
            menu.Items.Add(Sep());

            // 4. 說明
            menu.Items.Add(Loc.T("tray.help"), null, (s, a) => ShowHelp());
            menu.Items.Add(Loc.T("tray.updates"), null, (s, a) => ShowUpdates());
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
            _tray.BalloonTipClicked += (s, a) =>
            {
                if (_pendingUpdate == null) return;
                var r = _pendingUpdate; _pendingUpdate = null;
                ShowUpdates(r);
            };
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
    }
}
