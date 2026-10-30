@echo off
setlocal
chcp 65001 >nul
set "DOTNET_CLI_UI_LANGUAGE=en"
set "VSLANG=1033"
set "DOTNET_NOLOGO=1"
cd /d "%~dp0.."
set "ROOT=%CD%"

echo [1/3] Stopping running Gridify...
taskkill /f /im Gridify.exe >nul 2>&1
timeout /t 1 /nobreak >nul
rem Restore the desktop icon layer in case the app was force-killed while hiding it
if exist "%ROOT%\publish\Gridify.exe" start /wait "" "%ROOT%\publish\Gridify.exe" --restore

echo [2/3] Removing old program files (data folder is kept)...
if exist "%ROOT%\publish" (
  for %%F in ("%ROOT%\publish\Gridify.*") do del /f /q "%%~F" >nul 2>&1
)

echo [3/3] Building and publishing...
if exist "%ROOT%\src\obj\Release" rmdir /s /q "%ROOT%\src\obj\Release"
if exist "%ROOT%\src\bin\Release" rmdir /s /q "%ROOT%\src\bin\Release"
if not exist "%ROOT%\logs" mkdir "%ROOT%\logs"
rem Old restore files from before the rename must not be reused
del /f /q "%ROOT%\src\obj\*.json" >nul 2>&1
del /f /q "%ROOT%\src\obj\*.props" >nul 2>&1
del /f /q "%ROOT%\src\obj\*.targets" >nul 2>&1
dotnet publish "%ROOT%\src\Gridify.csproj" -c Release -r win-x64 --self-contained false -o "%ROOT%\publish" > "%ROOT%\logs\build.log" 2>&1
set RESULT=%errorlevel%
type "%ROOT%\logs\build.log"

if not "%RESULT%"=="0" (
  echo.
  echo *** PUBLISH FAILED. Please send the messages above to Claude. ***
  pause
  exit /b 1
)

if not exist "%ROOT%\publish\Gridify.exe" (
  echo.
  echo *** Build reported success but publish\Gridify.exe is missing. ***
  pause
  exit /b 1
)

echo.
echo Done: %ROOT%\publish\Gridify.exe
set /p RUN=Start it now? (Y/N): 
if /i "%RUN%"=="Y" start "" "%ROOT%\publish\Gridify.exe"
