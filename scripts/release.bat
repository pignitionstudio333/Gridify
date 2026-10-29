@echo off
setlocal EnableExtensions
chcp 65001 >nul
set "DOTNET_CLI_UI_LANGUAGE=en"
set "VSLANG=1033"
set "DOTNET_NOLOGO=1"
cd /d "%~dp0.."
set "ROOT=%CD%"
set "VER=1.0.0"
if not "%~1"=="" set "VER=%~1"
set "DIST=%ROOT%\dist"
set "BLD=%DIST%\build"
set "PKG=%DIST%\Gridify-%VER%-win-x64-portable"

echo === Gridify release %VER% ===
if exist "%ROOT%\publish\Gridify.exe" start /wait "" "%ROOT%\publish\Gridify.exe" --restore
taskkill /f /im Gridify.exe >nul 2>&1

if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%BLD%"
mkdir "%PKG%"
if not exist "%ROOT%\logs" mkdir "%ROOT%\logs"

echo [1/4] Publishing self-contained single file...
dotnet publish "%ROOT%\src\Gridify.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=%VER% -o "%BLD%" > "%ROOT%\logs\release.log" 2>&1
if errorlevel 1 (
  echo Publish FAILED. See logs\release.log
  pause
  exit /b 1
)

echo [2/4] Building portable package...
copy /y "%BLD%\Gridify.exe" "%PKG%\" >nul
type nul > "%PKG%\portable.flag"
copy /y "%ROOT%\scripts\portable\Restore-Desktop.bat" "%PKG%\" >nul
copy /y "%ROOT%\scripts\portable\README.txt" "%PKG%\" >nul
if exist "%ROOT%\LICENSE" copy /y "%ROOT%\LICENSE" "%PKG%\" >nul
if exist "%ROOT%\docs\PRIVACY.md" copy /y "%ROOT%\docs\PRIVACY.md" "%PKG%\" >nul
mkdir "%PKG%\data" >nul 2>&1
powershell -NoProfile -Command "Compress-Archive -Path '%PKG%\*' -DestinationPath '%DIST%\Gridify-%VER%-win-x64-portable.zip' -Force"

echo [3/4] Building installer (Inno Setup)...
set "ISCC="
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe") do if exist %%P set "ISCC=%%~P"
if defined ISCC (
  "%ISCC%" /Qp /DAppVersion=%VER% "%ROOT%\installer\Gridify.iss" >> "%ROOT%\logs\release.log" 2>&1
  if errorlevel 1 echo Installer build FAILED. See logs\release.log
) else (
  echo Inno Setup 6 not found - skipped. Install it from https://jrsoftware.org/isinfo.php
)

echo [4/4] Hashing...
powershell -NoProfile -Command "Get-ChildItem '%DIST%\*.zip','%DIST%\*.exe' -ErrorAction SilentlyContinue | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content -Encoding ascii '%DIST%\SHA256SUMS.txt'"
echo.
type "%DIST%\SHA256SUMS.txt"
echo.
echo Output: %DIST%
pause
