using System;
using System.IO;

namespace GridifyApp
{
    /// <summary>效能與除錯紀錄：寫在 data\fence.log（超過 256KB 就重新開始）。</summary>
    public static class PerfLog
    {
        static readonly object Gate = new object();

        public static void Write(string message) => WriteTo("fence.log", message);

        /// <summary>例外紀錄寫在 data\crash.log，方便回報問題；程式會繼續執行，不會因為單一錯誤結束。</summary>
        public static void Crash(string where, Exception ex)
        {
            WriteTo("crash.log", "[" + where + "] " + (ex?.ToString() ?? "unknown error"));
        }

        static void WriteTo(string file, string message)
        {
            if (ConfigStore.Disabled) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                    string f = Path.Combine(AppPaths.DataDir, file);
                    var fi = new FileInfo(f);
                    if (fi.Exists && fi.Length > 256 * 1024) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("MM-dd HH:mm:ss ") + message + "\n");
                }
            }
            catch { }
        }
    }
}
