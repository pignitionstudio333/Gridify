@echo off
rem Restores the Windows desktop icons if Gridify was closed unexpectedly.
cd /d "%~dp0"
if exist "Gridify.exe" (
  start /wait "" "Gridify.exe" --restore
  echo Desktop icons restored.
) else (
  echo Gridify.exe not found next to this file.
)
pause
