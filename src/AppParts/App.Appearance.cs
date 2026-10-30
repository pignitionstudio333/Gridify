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
    /// <summary>全域外觀（圖示大小、外觀設定視窗）。</summary>
    public partial class App
    {
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
    }
}
