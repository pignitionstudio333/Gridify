# Gridify

[English](README.md) | [繁體中文](docs/README.zh-TW.md)

A lightweight desktop organizer for Windows 10/11. Group your desktop icons into tidy, movable grids — **without moving, copying or deleting a single file.**

<!-- Add screenshots to docs/images and reference them here -->

## Features
- **Reference mode** – grids only remember file paths. Your originals stay exactly where they are.
- **Desktop takeover (optional)** – hides the Windows icon layer and shows your desktop items in grids instead. Always restored on exit, logoff, crash or uninstall.
- **Drag & drop** – drop files in, drag items between grids, reorder inside a grid, collapse a grid by double-clicking its title.
- **Tabs** – every grid can have tabs; drag a tab to reorder it or move it (with its items) into another grid.
- **Undo / redo** – Ctrl+Z / Ctrl+Y for grid, tab and item changes (never touches your files).
- **Appearance** – icon size, opacity, color, acrylic blur, shadow, animations (all can be switched off for performance).
- **Auto-organize** – one click sorts the desktop into Apps, Folders, Documents and Media tabs (or grids).
- **English / 繁體中文** UI.
- **Version history & update check** – tray menu > "Version history / Check for updates...". Nothing is downloaded automatically.
- **No telemetry, no admin rights, no network by default** – the only connection is an update check that you trigger yourself (or turn on).

## Download
Get the latest build from the **Releases** page:

| File | For |
|---|---|
| `Gridify-x.y.z-win-x64-setup.exe` | Installer (per-user, no admin) |
| `Gridify-x.y.z-win-x64-portable.zip` | Portable: unzip anywhere, settings stay in the `data` folder next to the exe |
| `SHA256SUMS.txt` | Checksums |

Both versions are self-contained: **no .NET installation needed**.

### "Windows protected your PC" (SmartScreen)
Gridify is a free hobby project and is **not code-signed yet**, so Windows may show a warning the first time:
click **More info → Run anyway**. You can verify the download first:

    Get-FileHash .\Gridify-x.y.z-win-x64-portable.zip -Algorithm SHA256   # compare with SHA256SUMS.txt
    gh attestation verify .\Gridify-x.y.z-win-x64-portable.zip --repo pignitionstudio333/Gridify

The full source code is in this repository and the releases are built by GitHub Actions from tagged source.

## Usage
- Drag files/folders from the desktop or Explorer into a grid.
- Double-click an item to open it. Right-click an item for the normal Windows menu (Shift+right-click for more) plus Gridify entries (move out of the grid, open file location). F2 renames and Delete deletes, exactly like in Explorer.
- Drag empty space in the title bar to move a grid, the bottom-right corner to resize it.
- Click **+** in the title bar to add a tab. Double-click a tab to rename it, right-click for more, drag it to reorder or into another grid. Dropping items on a tab puts them there.
- **Ctrl + drag a grid's title** (when it has a single tab) moves the whole grid into another grid as a new tab.
- **Ctrl+Z** / **Ctrl+Y** undo and redo changes to grids, tabs and which items belong where (works while a grid window is focused).
- Tray icon: show/hide all, take over desktop, appearance, organize by type, language, start with Windows, how to use, version history / check for updates, uninstall, quit.
- Hold **Shift** while starting for **safe mode** (the desktop is not taken over).
- Language changes apply after restarting Gridify.

## If desktop icons stay hidden
Quit Gridify from the tray, or run `Gridify.exe --restore` (the portable zip includes `Restore-Desktop.bat`). Restarting Explorer or rebooting also brings them back.

## Updating
- Tray → **Version history / Check for updates...** tells you whether a newer version exists (only when you ask, or if you turn on the daily check).
- Installed version: download the new `...-setup.exe` and run it. It updates in place and keeps your settings (a backup of your settings is also made on the first start of the new version).
- Portable version: unzip the new version and copy your old `data` folder into it.

## Uninstall
- Installed version: Windows Settings → Apps, or tray → Uninstall. Gridify restores your icons first and asks whether to delete its settings.
- Portable version: tray → Uninstall (removes the Run value and the `data` folder), then delete the folder.

## Data & privacy
See [PRIVACY.md](docs/PRIVACY.md). Settings live in the `data` folder (portable) or `%LOCALAPPDATA%\Gridify` (installed); config saves are atomic with automatic backups, and crashes are logged to `crash.log`.

## Build from source
Windows + .NET 10 SDK. See [CONTRIBUTING.md](.github/CONTRIBUTING.md).

## License
[MIT](LICENSE) © Pignition Studio

Third-party credits: see [THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md).
