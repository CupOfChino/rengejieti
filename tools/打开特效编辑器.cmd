@echo off
setlocal
where node.exe >nul 2>&1
if errorlevel 1 (
  echo Node.js 20 or newer is required. Add node.exe to PATH and try again.
  pause
  exit /b 1
)
node "%~dp0vfx-editor\server.mjs"
if errorlevel 1 pause
