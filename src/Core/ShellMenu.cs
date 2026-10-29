using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WF = System.Windows.Forms;

namespace GridifyApp
{
    /// <summary>附加在 Windows 原生選單最下面的 Gridify 自己的項目。</summary>
    public sealed class ShellMenuExtra
    {
        public string Text;
        public bool Enabled = true;
        public Action Run;
    }

    /// <summary>
    /// 顯示 Windows 原生的檔案右鍵選單（和桌面、檔案總管一樣：開啟、以系統管理員身分執行、
    /// 傳送到、內容、清空資源回收筒、7-Zip…），這樣收納區裡的圖示按右鍵不會少掉系統功能。
    /// 只讀取選單、使用者選了才執行對應動作；不寫登錄檔。
    /// </summary>
    public static class ShellMenu
    {
        const uint ID_FIRST = 1, ID_LAST = 0x6FFF, EXTRA_BASE = 0x7000;
        const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800;
        const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
        const uint CMF_EXTENDEDVERBS = 0x100;

        // ---------- COM 介面 ----------
        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint attrs);
            [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr strret);
            [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
        }

        [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
        }

        [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
            [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu3
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
            [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
            [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public IntPtr lpParameters;
            public IntPtr lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr lpTitle;
            public IntPtr lpVerbW;
            public IntPtr lpParametersW;
            public IntPtr lpDirectoryW;
            public IntPtr lpTitleW;
            public POINT ptInvoke;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll")]
        static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool AppendMenu(IntPtr h, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr h, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        /// <summary>收「傳送到」「開啟方式」子選單需要的訊息，轉給外殼處理。</summary>
        sealed class MenuWnd : WF.NativeWindow
        {
            public IContextMenu2 Cm2;
            public IContextMenu3 Cm3;

            protected override void WndProc(ref WF.Message m)
            {
                const int WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C, WM_INITMENUPOPUP = 0x117, WM_MENUCHAR = 0x120;
                if (m.Msg == WM_INITMENUPOPUP || m.Msg == WM_DRAWITEM || m.Msg == WM_MEASUREITEM || m.Msg == WM_MENUCHAR)
                {
                    try
                    {
                        if (Cm3 != null)
                        {
                            Cm3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out IntPtr r);
                            m.Result = r;
                            return;
                        }
                        if (Cm2 != null && m.Msg != WM_MENUCHAR)
                        {
                            Cm2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam);
                            m.Result = m.Msg == WM_INITMENUPOPUP ? IntPtr.Zero : (IntPtr)1;
                            return;
                        }
                    }
                    catch { }
                }
                base.WndProc(ref m);
            }
        }

        /// <summary>
        /// 在螢幕座標 (x, y) 顯示 paths（必須在同一個資料夾，或都是系統項目）的原生選單。
        /// 成功顯示（不論使用者有沒有選）回傳 true；任何失敗回傳 false，呼叫端改用 Gridify 自己的選單。
        /// </summary>
        public static bool Show(IntPtr owner, IList<string> paths, IList<ShellMenuExtra> extras, int x, int y, bool extended)
        {
            Action after = null;
            bool ok;
            try { ok = ShowCore(owner, paths, extras, x, y, extended, out after); }
            catch (Exception ex) { PerfLog.Write("ShellMenu: " + ex.Message); return false; }
            try { after?.Invoke(); } catch { }
            return ok;
        }

        static bool ShowCore(IntPtr owner, IList<string> paths, IList<ShellMenuExtra> extras, int x, int y, bool extended, out Action after)
        {
            after = null;
            var pidls = new List<IntPtr>();
            var rels = new List<IntPtr>();
            IShellFolder folder = null;
            IContextMenu cm = null;
            IntPtr hmenu = IntPtr.Zero;
            MenuWnd wnd = null;
            try
            {
                foreach (var p in paths)
                {
                    if (SHParseDisplayName(p, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return false;
                    pidls.Add(pidl);
                }
                if (pidls.Count == 0) return false;

                Guid iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
                for (int i = 0; i < pidls.Count; i++)
                {
                    if (SHBindToParent(pidls[i], ref iidFolder, out IntPtr pf, out IntPtr last) != 0 || pf == IntPtr.Zero) return false;
                    if (i == 0) folder = (IShellFolder)Marshal.GetObjectForIUnknown(pf);
                    Marshal.Release(pf);
                    rels.Add(last);
                }

                Guid iidCm = new Guid("000214E4-0000-0000-C000-000000000046");
                if (folder.GetUIObjectOf(owner, (uint)rels.Count, rels.ToArray(), ref iidCm, IntPtr.Zero, out IntPtr pcm) != 0 || pcm == IntPtr.Zero)
                    return false;
                cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
                Marshal.Release(pcm);

                hmenu = CreatePopupMenu();
                if (hmenu == IntPtr.Zero) return false;
                if (cm.QueryContextMenu(hmenu, 0, ID_FIRST, ID_LAST, extended ? CMF_EXTENDEDVERBS : 0) < 0) return false;

                if (extras != null && extras.Count > 0)
                {
                    AppendMenu(hmenu, MF_SEPARATOR, UIntPtr.Zero, null);
                    for (int i = 0; i < extras.Count; i++)
                        AppendMenu(hmenu, MF_STRING | (extras[i].Enabled ? 0u : MF_GRAYED), (UIntPtr)(EXTRA_BASE + (uint)i), extras[i].Text);
                }

                wnd = new MenuWnd();
                wnd.CreateHandle(new WF.CreateParams());
                wnd.Cm2 = cm as IContextMenu2;
                wnd.Cm3 = cm as IContextMenu3;
                SetForegroundWindow(wnd.Handle);

                uint cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, wnd.Handle, IntPtr.Zero);
                if (cmd >= EXTRA_BASE && extras != null && cmd - EXTRA_BASE < extras.Count)
                {
                    after = extras[(int)(cmd - EXTRA_BASE)].Run;
                }
                else if (cmd >= ID_FIRST && cmd <= ID_LAST)
                {
                    var ci = new CMINVOKECOMMANDINFOEX();
                    ci.cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>();
                    ci.fMask = 0x4000 | 0x20000000;   // CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE
                    ci.hwnd = owner;
                    ci.lpVerb = (IntPtr)(cmd - ID_FIRST);
                    ci.lpVerbW = (IntPtr)(cmd - ID_FIRST);
                    ci.nShow = 1;
                    ci.ptInvoke = new POINT { x = x, y = y };
                    cm.InvokeCommand(ref ci);
                }
                return true;
            }
            finally
            {
                if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
                if (wnd != null) { wnd.Cm2 = null; wnd.Cm3 = null; wnd.DestroyHandle(); }
                if (cm != null) Marshal.ReleaseComObject(cm);
                if (folder != null) Marshal.ReleaseComObject(folder);
                foreach (var p in pidls) Marshal.FreeCoTaskMem(p);
            }
        }
    }
}
