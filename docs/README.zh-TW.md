# Gridify

[English](../README.md) | [繁體中文](README.zh-TW.md)

Windows 10/11 的輕量桌面收納工具。把桌面圖示整理成可移動的收納區，**而且不會搬移、複製或刪除任何一個檔案。**

<!-- 截圖放在 docs/images 後在此引用 -->

## 特色
- **參照模式**：收納區只記錄檔案路徑，原始檔案完全留在原處。
- **接管桌面（可選）**：隱藏 Windows 的桌面圖示層，改由收納區顯示桌面項目。結束、登出、當機或解除安裝時一定會還原。
- **拖放**：檔案拖進收納區、項目在收納區之間移動、區內拖曳排序、雙擊標題折疊。
- **分頁**：每個收納區都可以有分頁；拖曳分頁可以排序，或連同項目搬到其他收納區。
- **復原／重做**：Ctrl+Z / Ctrl+Y 可復原收納區、分頁與項目歸屬的變動（絕不動你的檔案）。
- **外觀**：圖示大小、透明度、顏色、壓克力模糊、陰影、動畫（都可關閉以節省效能）。
- **自動分類**：一鍵分成應用程式、資料夾、文件、媒體分頁（或收納區）。
- **English / 繁體中文** 介面。
- **無遙測、不連網、不需系統管理員權限。**

## 下載
到 **Releases** 頁面下載最新版：

| 檔案 | 用途 |
|---|---|
| `Gridify-x.y.z-win-x64-setup.exe` | 安裝版（僅目前使用者，免系統管理員） |
| `Gridify-x.y.z-win-x64-portable.zip` | 可攜版：解壓縮即用，設定存在 exe 旁的 `data` 資料夾 |
| `SHA256SUMS.txt` | 檔案雜湊值 |

兩種版本都已內含執行環境，**不需要另外安裝 .NET**。

### 「Windows 已保護您的電腦」（SmartScreen）
Gridify 是免費的個人專案，**目前尚未做程式碼簽章**，第一次執行時 Windows 可能出現警告：
按 **其他資訊 → 仍要執行**。下載後可先驗證：

    Get-FileHash .\Gridify-x.y.z-win-x64-portable.zip -Algorithm SHA256   # 與 SHA256SUMS.txt 比對
    gh attestation verify .\Gridify-x.y.z-win-x64-portable.zip --repo pignitionstudio333/Gridify

完整原始碼都在此儲存庫，發佈檔由 GitHub Actions 依標籤版本的原始碼建置。

## 使用方式
- 把檔案／資料夾從桌面或檔案總管拖進收納區。
- 雙擊項目開啟；右鍵可開啟所在位置、重新命名、移出收納區、刪除（移到資源回收筒）。
- 拖標題列空白處移動收納區，拖右下角調整大小。
- 按標題列的 **+** 新增分頁；雙擊分頁重新命名，右鍵有更多選項，拖曳分頁可排序或搬到其他收納區；把項目拖到分頁上就會放進那個分頁。
- **Ctrl+Z** / **Ctrl+Y** 復原／重做收納區、分頁與項目歸屬的變動（收納區視窗有焦點時有效）。
- 系統匣圖示：顯示／隱藏全部、接管桌面、外觀、依類型分類、語言、開機自動啟動、解除安裝、結束。
- 啟動時按住 **Shift** 進入**安全模式**（不接管桌面）。
- 語言變更需重新啟動 Gridify 才生效。

## 桌面圖示一直消失？
從系統匣結束 Gridify，或執行 `Gridify.exe --restore`（可攜版 zip 內附 `Restore-Desktop.bat`）。重啟檔案總管或重新開機也會恢復。

## 解除安裝
- 安裝版：Windows 設定 → 應用程式，或系統匣 → 解除安裝。會先還原桌面圖示，並詢問是否刪除設定。
- 可攜版：系統匣 → 解除安裝（移除開機啟動值與 `data` 資料夾），再刪除程式資料夾。

## 資料與隱私
詳見 [PRIVACY.md](PRIVACY.md)。設定存在 `data` 資料夾（可攜版）或 `%LOCALAPPDATA%\Gridify`（安裝版）；設定檔採原子寫入並自動備份，當機會記錄到 `crash.log`。

## 從原始碼建置
Windows + .NET 10 SDK，詳見 [CONTRIBUTING.md](../.github/CONTRIBUTING.md)。

## 授權
[MIT](../LICENSE) © Pignition Studio

第三方致謝請見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
