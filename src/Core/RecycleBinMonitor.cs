using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace GridifyApp
{
    /// <summary>
    /// 資源回收筒的圖示會隨「有沒有東西」在空／滿之間切換，但收納區裡的圖示只載入一次就永久快取（IconHelper.Cache）。
    /// Windows 桌面本身是靠 Shell 的變更通知自己更新圖示，Gridify 隱藏了那一層，所以要自己做：
    /// 定時問 Shell 回收筒是否有內容，空／滿狀態一改變，就丟掉快取並讓畫面上的項目重新載入圖示。
    /// 只讀取回收筒的筆數，不碰任何檔案。
    /// </summary>
    public static class RecycleBinMonitor
    {
        /// <summary>收納區裡資源回收筒項目的路徑（shell::: + 回收筒的 CLSID）。</summary>
        public const string ItemPath = DesktopScanner.Prefix + "{645FF040-5081-101B-9F08-00AA002F954E}";

        // SHQUERYRBINFO { DWORD cbSize; __int64 i64Size; __int64 i64NumItems; }
        // 64 位元 Windows 上是 8 位元組對齊（cbSize 24，筆數在位移 16）；保險起見，不行就退回緊密排列（cbSize 20，位移 12）。
        // 用原始緩衝區呼叫，避免結構對齊猜錯。
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBinW(string rootPath, IntPtr info);

        static System.Threading.Timer _timer;
        static int _state = -1;      // -1 還不知道，0 空的，1 有東西
        static int _checking;        // 避免兩次檢查重疊
        static int _layout;          // 0 還不知道，1 = 24 位元組版，2 = 20 位元組版
        static int _failures;

        /// <summary>在建立收納區（開始載入圖示）之前呼叫，才能先記住目前是空還是滿。</summary>
        public static void Start()
        {
            if (_timer != null) return;
            Check(false);
            _timer = new System.Threading.Timer(_ => Check(true), null, 2000, 2000);
        }

        public static void Stop()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
        }

        /// <summary>剛做完可能改變回收筒的動作（例如刪除檔案）之後呼叫，不必等下一次計時。</summary>
        public static void Poke()
        {
            ThreadPool.QueueUserWorkItem(_ => Check(true));
        }

        static bool TryQuery(int cbSize, int numItemsOffset, out long items, out int hr)
        {
            items = 0;
            IntPtr buf = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(buf, i, 0);
                Marshal.WriteInt32(buf, 0, cbSize);
                hr = SHQueryRecycleBinW(null, buf);   // null = 所有磁碟機
                if (hr != 0) return false;
                items = Marshal.ReadInt64(buf, numItemsOffset);
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static bool Query(out long items)
        {
            int hr;
            if (_layout != 2)
            {
                if (TryQuery(24, 16, out items, out hr)) { _layout = 1; return true; }
                if (_layout == 1) { LogFailure(hr); return false; }
            }
            if (TryQuery(20, 12, out items, out hr)) { _layout = 2; return true; }
            LogFailure(hr);
            return false;
        }

        static void LogFailure(int hr)
        {
            if (_failures++ < 3) PerfLog.Write("recycle bin query failed: 0x" + hr.ToString("X8"));
        }

        static void Check(bool notify)
        {
            if (Interlocked.Exchange(ref _checking, 1) == 1) return;
            try
            {
                if (!Query(out long items)) return;

                int now = items > 0 ? 1 : 0;
                int prev = Interlocked.Exchange(ref _state, now);
                if (prev == -1) PerfLog.Write("recycle bin monitor: " + (now == 1 ? "not empty" : "empty") + " (layout " + _layout + ")");
                if (notify && prev != -1 && prev != now)
                {
                    PerfLog.Write("recycle bin " + (now == 1 ? "empty -> not empty" : "not empty -> empty") + ", refreshing icon");
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => IconHelper.Invalidate(ItemPath)));
                }
            }
            catch (Exception ex)
            {
                if (_failures++ < 3) PerfLog.Write("recycle bin monitor error: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref _checking, 0); }
        }
    }
}
