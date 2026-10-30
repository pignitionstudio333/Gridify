using System;
using System.Diagnostics;
using System.Windows;

namespace GridifyApp
{
    internal static class MouseNative
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

    /// <summary>
    /// 桌面 Ctrl+右鍵 新增收納區：不寫登錄檔、不注入系統選單，用低階滑鼠 hook 偵測「按住 Ctrl 在桌面按右鍵」。
    /// 一般右鍵仍是 Windows 原本的選單。回呼裡不可以做重工作，也不可以讓例外丟出去。
    /// </summary>
    public sealed class DesktopMouseHook
    {
        IntPtr _hook;
        Action<double, double> _onCtrlRightClick;   // 按住 Ctrl 在桌面按右鍵時呼叫（螢幕座標已換算成 WPF 單位）

        public bool IsInstalled => _hook != IntPtr.Zero;
        MouseNative.HookProc _hookProc;   // 要保留參考，避免被 GC 回收

        public void Remove()
        {
            if (_hook != IntPtr.Zero) MouseNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        public void Install(Action<double, double> onCtrlRightClick)
        {
            if (_hook != IntPtr.Zero) return;
            _onCtrlRightClick = onCtrlRightClick;
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
                            var cb = _onCtrlRightClick;
                            if (cb != null) Application.Current?.Dispatcher.BeginInvoke(new Action(() => cb(x, y)));
                        }
                        return (IntPtr)1;   // 吃掉這次點擊，不讓桌面跳出選單
                    }
                }
            }
            }
            catch { }   // 全域 hook 裡的例外不可以丟出去
            return MouseNative.CallNextHookEx(_hook, code, wParam, lParam);
        }
    }
}
