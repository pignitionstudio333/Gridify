# Privacy / 隱私

**English**

Gridify does not collect, transmit or share any data. It has no telemetry, no analytics and no accounts, and makes no network connections by default. The only connection is one HTTPS request to GitHub (`api.github.com`, asking for the latest release number) that happens only when you press "Check for updates" or turn on the optional daily check (off by default). GitHub sees your IP address and the user agent `Gridify/<version>`; nothing else is sent. Gridify never downloads or installs updates by itself.

What it stores, and where:

| What | Where |
|---|---|
| Grid layout, appearance, language (`config.json`), backups, `crash.log`, `gridify.log` | Portable build: the `data` folder next to `Gridify.exe`. Installed build: `%LOCALAPPDATA%\Gridify` |
| Optional "Start with Windows" entry (off by default) | One value named `Gridify` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| Native WPF libraries (single-file build only) | Extracted by the .NET runtime to a temporary folder under `%TEMP%\.net` |

Grids only store *paths* of your files. Your files are never moved, copied, uploaded or modified; "Delete" sends the item to the Recycle Bin and only when you ask for it.

Log files contain file paths and error messages and never leave your computer. Delete them any time.

**繁體中文**

Gridify 不會蒐集、傳送或分享任何資料：沒有遙測或分析、沒有帳號，預設也不連網。唯一的連線是對 GitHub（`api.github.com`）發出一個查詢最新版本號的 HTTPS 請求，只有在你按下「檢查更新」，或開啟「每天自動檢查」（預設關閉）時才會發生；GitHub 看得到你的 IP 位址與 `Gridify/<版本>` 這個識別字串，不會傳送其他任何資料。Gridify 不會自己下載或安裝更新。

儲存內容：
- 收納區配置、外觀、語言（`config.json`）、備份與記錄檔：可攜版放在 `Gridify.exe` 旁的 `data` 資料夾；安裝版放在 `%LOCALAPPDATA%\Gridify`。
- 「開機自動啟動」（預設關閉）：登錄檔 `HKCU\...\Run` 底下一個名為 `Gridify` 的值。
- 單檔版的 WPF 原生元件：由 .NET 執行階段解壓到 `%TEMP%\.net`。

收納區只記錄檔案的「路徑」。原始檔案不會被搬移、複製、上傳或修改；只有你主動要求「刪除」時，才會送進資源回收筒。

記錄檔含檔案路徑與錯誤訊息，只存在你的電腦，可隨時刪除。
