@echo off
setlocal enableextensions

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [EGT] ERROR: dotnet SDK was not found in PATH.
  echo [EGT] Please install .NET 8 SDK first.
  pause
  exit /b 1
)

echo [EGT] Building solution...
dotnet build easy_game_translator.sln -c Debug -nologo
if errorlevel 1 (
  echo [EGT] Build failed.
  pause
  exit /b 1
)

set "APP_EXE=src\EGT.App\bin\Debug\net8.0-windows\EGT.App.exe"
if not exist "%APP_EXE%" (
  echo [EGT] ERROR: App executable not found: %APP_EXE%
  pause
  exit /b 1
)

echo [EGT] Launching GUI...
start "" "%CD%\%APP_EXE%"
exit /b 0
