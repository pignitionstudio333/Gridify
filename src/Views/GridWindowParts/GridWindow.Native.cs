using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GridifyApp
{
    /// <summary>Win32 呼叫（置底、工具視窗樣式、毛玻璃）。</summary>
    public partial class GridWindow
    {
        static class Native
        {
            public const int GWL_EXSTYLE = -20;
            public const int WS_EX_TOOLWINDOW = 0x80;   // 不出現在 Alt+Tab
            public const int WM_WINDOWPOSCHANGING = 0x0046;
            public const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
            public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

            [StructLayout(LayoutKind.Sequential)]
            public struct WINDOWPOS
            {
                public IntPtr hwnd, hwndInsertAfter;
                public int x, y, cx, cy;
                public uint flags;
            }

            [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
            [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
            [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

            [StructLayout(LayoutKind.Sequential)]
            public struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

            [StructLayout(LayoutKind.Sequential)]
            public struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }

            [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr h, ref WinCompAttrData data);
            [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        }
    }
}
