@echo off
setlocal
cd /d "%~dp0\.."

echo ========================================================
echo        RO3 Launcher DevKit - Patch ZIP packager
echo ========================================================
echo.

for /f "delims=" %%i in ('dotnet msbuild Ro3Launcher.csproj -getProperty:AssemblyName') do set "APP=%%i"
python tools\package-patch.py --exe %APP%.exe %*
if errorlevel 1 (
    pause
    exit /b 1
)

echo.
timeout /t 3 >nul
