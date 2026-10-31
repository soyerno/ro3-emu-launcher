@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
for /f "delims=" %%i in ('dotnet msbuild Ro3Launcher.csproj -getProperty:AssemblyName') do set "APP=%%i"

:: Optional version stamp: publish.cmd 1.2.0 -> the launcher reports 1.2.0 and can self-update to newer releases.
:: Without it the build is 0.0.0-dev, which never replaces itself (see docs\AUTO_UPDATE.md).
set "VERSION_ARG="
if not "%~1"=="" set "VERSION_ARG=-p:Version=%~1"

echo ========================================================
echo        RO3 Launcher - Publicacion Single-File
echo ========================================================
echo.

:: 1. Cerrar procesos bloqueantes del launcher si existen
tasklist /fi "imagename eq %APP%.exe" 2>nul | find /i "%APP%.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [AVISO] Cerrando instancia previa de %APP%.exe en ejecucion...
    taskkill /f /im %APP%.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

:: 2. Limpiar carpeta dist
if not exist "dist" mkdir "dist"

echo [1/2] Compilando y empaquetando ejecutable standalone...
echo       Configuracion: Release (win-x64, self-contained, single-file)
if defined VERSION_ARG (echo       Version: %~1) else (echo       Version: 0.0.0-dev ^(sin auto-actualizacion^))
echo.

dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true %VERSION_ARG% -o dist
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] La publicacion ha fallado. Revisa los mensajes de dotnet arriba.
    pause
    exit /b 1
)

echo.
echo [2/2] Verificando binario generado...
if exist "dist\%APP%.exe" (
    for %%F in ("dist\%APP%.exe") do set size=%%~zF
    set /a sizeMB=!size! / 1048576
    echo ========================================================
    echo  [EXITO] Ejecutable listo en:
    echo  %~dp0dist\%APP%.exe - Tamano: !sizeMB! MB
    echo ========================================================
) else (
    echo [ERROR] No se encontro dist\%APP%.exe
)

echo.
pause
