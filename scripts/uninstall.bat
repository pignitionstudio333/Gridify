@echo off
setlocal
for %%I in ("%~dp0..") do set "ROOT=%%~fI"

echo ==============================================
echo   Gridify uninstall (development folder)
echo ==============================================
echo This will:
echo   1. Stop the running Gridify (and the old Fence.exe, if any)
echo   2. Restore desktop icons hidden by Gridify
echo   3. Remove the auto-start entries (registry values Gridify / PignitionFence)
echo   4. Delete the publish, dist, logs, bin, obj and data folders
echo Your original files are never touched.
echo.
set /p OK=Uninstall? (Y/N): 
if /i not "%OK%"=="Y" (
  echo Cancelled.
  pause
  exit /b
)

taskkill /f /im Gridify.exe >nul 2>&1
taskkill /f /im Fence.exe >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Gridify /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PignitionFence /f >nul 2>&1
timeout /t 1 /nobreak >nul

rem Restore hidden desktop icons first (must happen before data is deleted)
for %%E in ("%ROOT%\publish\Gridify.exe" "%ROOT%\src\bin\Release\net10.0-windows\win-x64\Gridify.exe" "%ROOT%\publish\Fence.exe" "%ROOT%\src\bin\Release\net10.0-windows\win-x64\Fence.exe") do (
  if exist %%E (
    start /wait "" %%E --restore
    goto :restored
  )
)
echo [WARNING] Gridify.exe not found, hidden desktop icons could not be restored automatically.
echo If an icon is missing from the desktop, drag it back or restart Explorer.
pause
:restored

for %%D in (publish dist logs src\bin src\obj data) do (
  if exist "%ROOT%\%%D" rmdir /s /q "%ROOT%\%%D"
)

echo.
echo Removed: auto-start entries, build output and settings.
echo.
set /p ALL=Also delete the whole folder "%ROOT%" including source code? (Y/N): 
if /i "%ALL%"=="Y" (
  > "%TEMP%\gridify_rm.bat" echo @echo off
  >> "%TEMP%\gridify_rm.bat" echo ping 127.0.0.1 -n 3 ^>nul
  >> "%TEMP%\gridify_rm.bat" echo rmdir /s /q "%ROOT%"
  >> "%TEMP%\gridify_rm.bat" echo del "%%~f0"
  cd /d "%TEMP%"
  start "" /min "%TEMP%\gridify_rm.bat"
  exit
)

echo Done. Source code is still in %ROOT%
pause
