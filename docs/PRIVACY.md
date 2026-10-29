# Privacy / 隱私

**English**

Gridify does not collect, transmit or share any data. It makes no network connections, has no telemetry, no analytics, no accounts and no auto-update check.

What it stores, and where:

| What | Where |
|---|---|
| Grid layout, appearance, language (`config.json`), backups, `crash.log`, `fence.log` | Portable build: the `data` folder next to `Gridify.exe`. Installed build: `%LOCALAPPDATA%\Gridify` |
| Optional "Start with Windows" entry (off by default) | One value named `Gridify` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| Native WPF libraries (single-file build only) | Extracted by the .NET runtime to a temporary folder under `%TEMP%\.net` |

Grids only store *paths* of your files. Your files are never moved, copied, uploaded or modified; "Delete" sends the item to the Recycle Bin and only when you ask for it.

Log files contain file paths and error messages and never leave your computer. Delete them any time.

**繁體中文**

Gridify 不會蒐集、傳送或分享任何資料：沒有網路連線、沒有遙測或分析、沒有帳號，也沒有自動檢查更新。

儲存內容：
- 收納區配置、外觀、語言（`config.json`）、備份與記錄檔：可攜版放在 `Gridify.exe` 旁的 `data` 資料夾；安裝版放在 `%LOCALAPPDATA%\Gridify`。
- 「開機自動啟動」（預設關閉）：登錄檔 `HKCU\...\Run` 底下一個名為 `Gridify` 的值。
- 單檔版的 WPF 原生元件：由 .NET 執行階段解壓到 `%TEMP%\.net`。

收納區只記錄檔案的「路徑」。原始檔案不會被搬移、複製、上傳或修改；只有你主動要求「刪除」時，才會送進資源回收筒。

記錄檔含檔案路徑與錯誤訊息，只存在你的電腦，可隨時刪除。
