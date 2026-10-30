# Changelog

## 1.2.0 - 2026-10-30
### Added
- **Version history / Check for updates**: tray menu > "Version history / Check for updates...". The version history is built into the app (works offline); pressing "Check for updates" contacts GitHub once to ask for the latest version and offers a link to the download page. An optional "check once a day" setting (off by default) can notify you from the tray. Gridify never downloads or installs anything by itself.
- **Automatic settings backup before an upgrade**: when the version changes, the settings file is copied to the backups folder (`before-upgrade-<version>.json`).
- **Ctrl + drag the title of a single-tab grid** onto another grid to move the whole grid there as a new tab (the tab keeps the grid's name). The source grid disappears; the Desktop grid is never removed - it keeps an empty tab so new desktop items always have a place. Items are only references, so no shortcut is lost; items already in the target grid are not duplicated.
- The first-run welcome window has a **"Start Gridify when I sign in to Windows"** checkbox (off by default, reflects the current setting).

### Changed
- The installer now gives the desktop icons back and stops a running Gridify before upgrading in place.
- Privacy: still no network access by default. A single HTTPS request to GitHub (asking for the latest release number, nothing about you is sent) happens only when you press "Check for updates" or turn on the daily check.

## 1.1.1 - 2026-10-29
### Fixed (critical)
- **Critical bug in 1.1.0 and earlier: right-clicking an icon inside a grid showed only Gridify's own menu, so Windows functions were missing** (Empty Recycle Bin, Properties, Run as administrator, Send to, 7-Zip, ...). Icons now show the normal Windows context menu with a few Gridify entries at the bottom (Shift+right-click for the extended menu). Right-clicking empty space in a grid still shows the Gridify menu. **Everyone on 1.0.0 / 1.1.0 should update.**

## 1.1.0 - 2026-10-29
### Added
- **Tabs inside grids.** Click the `+` in a grid's title bar (or right-click > New tab). With two or more tabs the title bar shows the tab labels; with one tab it looks as before. Double-click a tab to rename it, right-click for move left/right and delete, scroll the mouse wheel over the tabs when there are many.
- **Move tabs between grids.** Drag a tab onto another grid's tab bar or body to move it (and its items); drag within the tab bar to reorder. Dropping the last tab of a normal grid removes that grid; the Desktop grid always keeps one tab.
- **Drop items onto a tab** to put them there; hovering over a tab while dragging switches to it.
- **Organize by type into tabs.** "Organize by type" can now create tabs inside the Desktop grid (recommended) or separate grids as before.
- **Undo / redo** (Ctrl+Z / Ctrl+Y, also in the right-click and tray menus) for changes to grids, tabs and which items belong where. Up to 30 steps, never touches your files, cleared after a file rename.

### Changed
- Settings files now store tabs; old files are converted automatically. The merged item list is still written, so going back to 1.0.0 keeps your items.
- When a grid gains its second tab the first tab keeps the grid's name; when it drops back to one tab the grid takes that tab's name (the Desktop grid keeps its name).
- Dragging empty space in the title bar always moves the grid.

### Fixed
- A collapsed grid could expand to the wrong height. The expanded height is now only changed when you resize the grid.

## 1.0.0 - 2026-10-29
First public release (renamed from the private "Fence" tool).

- Reference-mode desktop grids: drag files in, nothing is moved or copied.
- Optional desktop takeover with guaranteed restore (exit, crash guard, uninstall, `--restore`).
- Free movement of items between grids, drag to reorder, collapse/expand.
- Appearance settings: icon size, background opacity/color, acrylic, shadow, animations.
- English and Traditional Chinese UI.
- Portable (`data` folder next to the exe) and installed (`%LOCALAPPDATA%\Gridify`) modes.
- First-run welcome and Shift safe mode.
- Atomic config saves, automatic backups, crash log.
