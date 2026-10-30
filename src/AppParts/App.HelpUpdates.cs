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
    /// <summary>使用說明、版本紀錄與檢查更新視窗。</summary>
    public partial class App
    {
        // ---------- 使用說明 ----------
        HelpWindow _helpWin;

        void ShowHelp()
        {
            if (_helpWin == null)
            {
                _helpWin = new HelpWindow();
                _helpWin.Closed += (s, a) => _helpWin = null;
                _helpWin.Show();
            }
            _helpWin.Activate();
        }

        // ---------- 版本紀錄 / 檢查更新 ----------
        UpdateWindow _updateWin;
        UpdateResult _pendingUpdate;

        void ShowUpdates(UpdateResult result = null)
        {
            if (_updateWin == null)
            {
                _updateWin = new UpdateWindow(_cfg.AutoCheckUpdate, on => { _cfg.AutoCheckUpdate = on; SaveConfig(); });
                _updateWin.Closed += (s, a) => _updateWin = null;
                _updateWin.Show();
            }
            _updateWin.Activate();
            if (result != null) _updateWin.ShowResult(result);
        }

        /// <summary>每天最多檢查一次；有新版本就用系統匣通知一次（同一個版本不重複通知）。失敗就安靜略過。</summary>
        async void AutoCheckUpdate()
        {
            try
            {
                if (!_cfg.AutoCheckUpdate) return;
                var last = new DateTime(Math.Max(0, _cfg.LastUpdateCheckUtc), DateTimeKind.Utc);
                if ((DateTime.UtcNow - last).TotalHours < 24) return;
                var r = await UpdateChecker.CheckAsync();
                if (!r.Ok) return;
                _cfg.LastUpdateCheckUtc = DateTime.UtcNow.Ticks;
                if (r.Newer && _cfg.LastNotifiedVersion != r.Latest)
                {
                    _cfg.LastNotifiedVersion = r.Latest;
                    _pendingUpdate = r;
                    _tray?.ShowBalloonTip(8000, Loc.AppName, Loc.F("upd.balloon", r.Latest), System.Windows.Forms.ToolTipIcon.Info);
                }
                SaveConfig();
            }
            catch { }
        }
    }
}
