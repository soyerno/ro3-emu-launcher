@echo off
setlocal
cd /d "%~dp0\.."

echo ========================================================
echo        RO3 Launcher DevKit - Patch ZIP packager
echo ========================================================
echo.

python tools\package-patch.py %*
if errorlevel 1 (
    pause
    exit /b 1
)

echo.
timeout /t 3 >nul
