using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GridifyApp
{
    /// <summary>
    /// 檔案刪除／改名交給 Windows 外殼（SHFileOperation）：確認視窗、資源回收筒、錯誤對話框都和檔案總管一樣，
    /// 也遵守使用者在 Windows 裡的設定（例如「刪除時顯示確認對話方塊」）。Gridify 不自己實作這些行為。
    /// </summary>
    public static class ShellFileOp
    {
        const uint FO_DELETE = 3, FO_RENAME = 4;
        const ushort FOF_ALLOWUNDO = 0x40;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        /// <summary>刪除。recycle = true 送進資源回收筒（Delete 鍵），false 是永久刪除（Shift+Delete，Windows 會自己再確認一次）。</summary>
        public static bool Delete(IntPtr owner, IEnumerable<string> paths, bool recycle)
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", paths) + "\0",   // 路徑以 \0 分隔，結尾要兩個 \0（Marshal 會補最後一個）
                pTo = null,
                fFlags = (ushort)(recycle ? FOF_ALLOWUNDO : 0)
            };
            int rc = SHFileOperation(ref op);
            return rc == 0 && !op.fAnyOperationsAborted;
        }

        /// <summary>改名（和檔案總管一樣可以在檔案總管按 Ctrl+Z 復原；失敗時由 Windows 顯示錯誤對話框）。</summary>
        public static bool Rename(IntPtr owner, string from, string to)
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = FO_RENAME,
                pFrom = from + "\0",
                pTo = to + "\0",
                fFlags = FOF_ALLOWUNDO
            };
            int rc = SHFileOperation(ref op);
            return rc == 0 && !op.fAnyOperationsAborted;
        }
    }
}
