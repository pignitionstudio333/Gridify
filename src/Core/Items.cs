using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridifyApp
{
    /// <summary>收納區（grid）裡的一個項目：只記錄路徑，不會動到原檔案。路徑以 shell::: 開頭代表系統圖示（本機、資源回收筒…）。</summary>
    public class GridItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        ImageSource _icon;

        // 系統圖示（本機、資源回收筒…）才需要在圖示改變時重新載入；用弱參照，收納區關掉後不會被卡住
        static readonly List<WeakReference<GridItem>> LiveSpecial = new List<WeakReference<GridItem>>();

        public string FullPath { get; }
        public string Name { get; }
        public double ItemOpacity { get; }
        public bool IsSpecial { get; }
        public string Tip { get; }

        /// <summary>圖示在背景載入，載入完成前是 null（空白），完成後自動更新畫面。</summary>
        public ImageSource Icon
        {
            get => _icon;
            private set
            {
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public GridItem(string path)
        {
            FullPath = path;
            IsSpecial = path.StartsWith(DesktopScanner.Prefix, StringComparison.Ordinal);

            if (IsSpecial)
            {
                Name = IconHelper.SpecialName(path);
                ItemOpacity = 1.0;
                lock (LiveSpecial)
                {
                    LiveSpecial.RemoveAll(w => !w.TryGetTarget(out _));
                    LiveSpecial.Add(new WeakReference<GridItem>(this));
                }
            }
            else
            {
                bool isDir = Directory.Exists(path);
                bool exists = isDir || File.Exists(path);
                string n = isDir ? Path.GetFileName(path.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(path);
                Name = string.IsNullOrEmpty(n) ? path : n;
                ItemOpacity = exists ? 1.0 : 0.4;
            }

            Tip = IsSpecial ? Name : (Path.GetFileName(path.TrimEnd('\\', '/')) + "\n" + path);
            _icon = IconHelper.TryGetCached(path);
            if (_icon == null)
                IconHelper.LoadAsync(path, img => { if (img != null) Icon = img; });
        }

        /// <summary>讓所有顯示這個系統圖示的項目重新載入圖示（要先讓 IconHelper 丟掉快取）。需在畫面執行緒呼叫。</summary>
        public static void ReloadIcon(string path)
        {
            var hit = new List<GridItem>();
            lock (LiveSpecial)
            {
                LiveSpecial.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in LiveSpecial)
                    if (w.TryGetTarget(out var it) && string.Equals(it.FullPath, path, StringComparison.OrdinalIgnoreCase))
                        hit.Add(it);
            }
            foreach (var it in hit)
            {
                var item = it;
                IconHelper.LoadAsync(path, img => { if (img != null) item.Icon = img; });
            }
        }
    }

    /// <summary>
    /// 取得檔案／系統圖示：用一條專用的背景執行緒（STA）載入 128px 的高解析度圖示並快取，
    /// 載入完成再回到畫面執行緒更新，不會卡住畫面。
    /// </summary>
    public static class IconHelper
    {
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> NameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new object();
        static readonly BlockingCollection<(string Key, Action<ImageSource> Done)> Queue =
            new BlockingCollection<(string Key, Action<ImageSource> Done)>();

        // 效能統計（寫進 gridify.log）
        static int _loaded;
        static long _workMs;
        static readonly Stopwatch Wall = new Stopwatch();

        const int HiResSize = 128;

        static IconHelper()
        {
            var t = new Thread(Worker) { IsBackground = true, Name = "GridifyIconLoader" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public static ImageSource TryGetCached(string path)
        {
            lock (Gate) return Cache.TryGetValue(path, out var img) ? img : null;
        }

        /// <summary>
        /// 丟掉某個路徑的圖示快取，並讓畫面上用到它的項目重新載入。
        /// 給「圖示本身會變」的系統圖示用（資源回收筒空／滿）。需在畫面執行緒呼叫。
        /// </summary>
        public static void Invalidate(string path)
        {
            lock (Gate) Cache.Remove(path);
            GridItem.ReloadIcon(path);
        }

        public static void LoadAsync(string path, Action<ImageSource> done)
        {
            lock (Gate) { if (!Wall.IsRunning) Wall.Restart(); }
            Queue.Add((path, done));
        }

        static void Worker()
        {
            foreach (var job in Queue.GetConsumingEnumerable())
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    ImageSource img;
                    lock (Gate) Cache.TryGetValue(job.Key, out img);
                    if (img == null)
                    {
                        img = LoadImage(job.Key);
                        if (img != null) lock (Gate) Cache[job.Key] = img;
                    }

                    var done = job.Done;
                    var d = Application.Current?.Dispatcher;
                    if (d != null)
                        d.BeginInvoke(new Action(() =>
                        {
                            try { done(img); } catch { }
                        }));

                    lock (Gate)
                    {
                        _loaded++;
                        _workMs += sw.ElapsedMilliseconds;
                        if (Queue.Count == 0)
                        {
                            PerfLog.Write("icons: loaded " + _loaded + " (" + _workMs + "ms of work, " + Wall.ElapsedMilliseconds + "ms wall)");
                            _loaded = 0;
                            _workMs = 0;
                            Wall.Reset();
                        }
                    }
                }
                catch (Exception ex)
                {
                    PerfLog.Write("icon worker error: " + ex.Message);   // 單一圖示失敗不影響其他圖示，更不能讓程式結束
                }
            }
        }

        // ---------- Shell 介面 ----------
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint cb, uint flags);

        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfo", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attrs, ref SHFILEINFO info, uint cb, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr bindingCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        const uint SHGFI_ICON = 0x100, SHGFI_DISPLAYNAME = 0x200, SHGFI_PIDL = 0x8;

        static string ParseName(string path) =>
            path.StartsWith(DesktopScanner.Prefix, StringComparison.Ordinal) ? path.Substring("shell:".Length) : path;   // shell:::{GUID} -> ::{GUID}

        static ImageSource LoadImage(string path)
        {
            return ShellImage(ParseName(path), HiResSize) ?? LegacyIcon(path);
        }

        /// <summary>用 IShellItemImageFactory 取得高解析度、帶透明度的圖示。</summary>
        static ImageSource ShellImage(string parse, int size)
        {
            IShellItemImageFactory factory = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(parse, IntPtr.Zero, ref iid, out factory);
                int hr = factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x4, out hbm);   // BIGGERSIZEOK | ICONONLY
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return TrimSmallIcon(src, parse);
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        static int _trimLog;

        /// <summary>
        /// 有些程式的圖示本身只有 32～48px，Shell 會把它原樣放在 128px 畫布中央、四周留大片空白，
        /// 顯示出來就特別小。內容沒有填滿畫布時，裁掉空白邊，讓它和其他圖示一樣填滿格子。
        /// 有透明資訊就用透明判斷；沒有就用「和左上角同色」當作空白。
        /// </summary>
        static ImageSource TrimSmallIcon(BitmapSource src, string tag)
        {
            try
            {
                var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
                var px = new byte[stride * h];
                bgra.CopyPixels(px, stride, 0);

                bool hasAlpha = false;
                for (int k = 3; k < px.Length; k += 4)
                    if (px[k] < 250) { hasAlpha = true; break; }
                int bB = px[0], bG = px[1], bR = px[2];

                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * stride + x * 4;
                        bool content = hasAlpha
                            ? px[idx + 3] > 120   // 門檻要夠高，才不會把小圖示後面淡淡的底板算成內容
                            : (Math.Abs(px[idx] - bB) + Math.Abs(px[idx + 1] - bG) + Math.Abs(px[idx + 2] - bR)) > 24;
                        if (!content) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                if (maxX < minX || maxY < minY) return src;

                int cw = maxX - minX + 1, ch = maxY - minY + 1;
                if (_trimLog++ < 40)
                    PerfLog.Write("trim " + tag + " fmt=" + src.Format + " alpha=" + hasAlpha + " size=" + w + "x" + h + " content=" + cw + "x" + ch);

                if (Math.Max(cw, ch) >= w * 0.88) return src;   // 幾乎滿版就不動

                // 一律裁成正方形（以內容中心為準），圖示才不會變形
                int side = Math.Min(Math.Max(cw, ch), Math.Min(w, h));
                int x0 = Math.Max(0, Math.Min(w - side, minX + cw / 2 - side / 2));
                int y0 = Math.Max(0, Math.Min(h - side, minY + ch / 2 - side / 2));
                var cropped = new CroppedBitmap(src, new Int32Rect(x0, y0, side, side));
                cropped.Freeze();
                return cropped;
            }
            catch (Exception ex)
            {
                PerfLog.Write("trim failed: " + ex.Message);
                return src;
            }
        }

        /// <summary>備援：舊式 32px 圖示。</summary>
        static ImageSource LegacyIcon(string path)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                var info = new SHFILEINFO();
                if (path.StartsWith(DesktopScanner.Prefix, StringComparison.Ordinal))
                {
                    if (SHParseDisplayName(ParseName(path), IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return null;
                    SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_PIDL | SHGFI_ICON);
                }
                else
                {
                    SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON);
                }
                if (info.hIcon == IntPtr.Zero) return null;
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                DestroyIcon(info.hIcon);
                return src;
            }
            catch { return null; }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        /// <summary>系統圖示的顯示名稱（本機、資源回收筒…），由 Shell 提供，自動是目前語言。</summary>
        public static string SpecialName(string path)
        {
            lock (Gate) { if (NameCache.TryGetValue(path, out var cached)) return cached; }
            string name = path;
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(ParseName(path), IntPtr.Zero, out pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                {
                    var info = new SHFILEINFO();
                    SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_PIDL | SHGFI_DISPLAYNAME);
                    if (!string.IsNullOrEmpty(info.szDisplayName)) name = info.szDisplayName;
                }
            }
            catch { }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
            lock (Gate) NameCache[path] = name;
            return name;
        }
    }
}
