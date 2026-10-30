using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace GridifyApp
{
    /// <summary>程式版本與固定的下載網址。</summary>
    public static class AppInfo
    {
        public const string ReleasesUrl = "https://github.com/pignitionstudio333/Gridify/releases/latest";
        const string ApiUrl = "https://api.github.com/repos/pignitionstudio333/Gridify/releases/latest";

        public static string Version
        {
            get
            {
                try
                {
                    var a = typeof(AppInfo).Assembly;
                    var v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                    if (!string.IsNullOrEmpty(v)) return v.Split('+')[0];
                    return a.GetName().Version?.ToString(3) ?? "0.0.0";
                }
                catch { return "0.0.0"; }
            }
        }

        public static string ApiEndpoint => ApiUrl;

        /// <summary>讀取打包在程式裡的版本紀錄（依介面語言；找不到中文就用英文）。</summary>
        public static string Changelog()
        {
            string[] names = Loc.Lang == "zh-TW" ? new[] { "Changelog.zh-TW", "Changelog.en" } : new[] { "Changelog.en" };
            foreach (var n in names)
            {
                try
                {
                    using (var s = typeof(AppInfo).Assembly.GetManifestResourceStream(n))
                    {
                        if (s == null) continue;
                        using (var r = new StreamReader(s)) return r.ReadToEnd();
                    }
                }
                catch { }
            }
            return "";
        }
    }

    public class UpdateResult
    {
        public bool Ok;          // 有成功問到 GitHub
        public bool Newer;       // 有比目前更新的版本
        public string Latest;    // 最新版本號（不含 v）
        public string Error;     // 失敗原因（給使用者看的簡短說明）
    }

    /// <summary>
    /// 檢查更新：只對 GitHub 的公開 API 發出一個 HTTPS GET，沒有送出任何你的資料。
    /// 只有使用者按下「檢查更新」，或開啟了「每天自動檢查」才會執行。不下載、不執行任何東西。
    /// </summary>
    public static class UpdateChecker
    {
        static readonly HttpClient Http = Create();

        static HttpClient Create()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd("Gridify/" + AppInfo.Version);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            }
            catch { }   // 標頭格式有問題也不能讓檢查更新當掉
            return c;
        }

        public static bool TryParse(string tag, out Version v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;
            tag = tag.Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
            int cut = tag.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) tag = tag.Substring(0, cut);
            if (!Version.TryParse(tag, out var p)) return false;
            v = new Version(p.Major, p.Minor, Math.Max(p.Build, 0));   // 1.2 與 1.2.0 視為相同
            return true;
        }

        public static async Task<UpdateResult> CheckAsync()
        {
            var res = new UpdateResult();
            try
            {
                using (var resp = await Http.GetAsync(AppInfo.ApiEndpoint).ConfigureAwait(true))
                {
                    if (resp.StatusCode == HttpStatusCode.NotFound) { res.Error = "no release"; return res; }
                    if (!resp.IsSuccessStatusCode) { res.Error = "HTTP " + (int)resp.StatusCode; return res; }
                    string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        string tag = doc.RootElement.GetProperty("tag_name").GetString();
                        if (!TryParse(tag, out Version latest)) { res.Error = "bad version"; return res; }
                        TryParse(AppInfo.Version, out Version cur);
                        res.Latest = latest.ToString(3);
                        res.Newer = cur != null && latest > cur;
                        res.Ok = true;
                    }
                }
            }
            catch (TaskCanceledException) { res.Error = "timeout"; }
            catch (HttpRequestException) { res.Error = "network"; }
            catch (Exception ex) { res.Error = ex.GetType().Name; }
            if (!res.Ok) PerfLog.Write("update check failed: " + res.Error);
            return res;
        }
    }
}
