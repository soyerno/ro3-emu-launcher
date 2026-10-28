@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ========================================================
echo        RO3 Launcher - Publicacion Single-File
echo ========================================================
echo.

:: 1. Cerrar procesos bloqueantes de Ro3Launcher si existen
tasklist /fi "imagename eq Ro3Launcher.exe" 2>nul | find /i "Ro3Launcher.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [AVISO] Cerrando instancia previa de Ro3Launcher.exe en ejecucion...
    taskkill /f /im Ro3Launcher.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

:: 2. Limpiar carpeta dist
if not exist "dist" mkdir "dist"

echo [1/2] Compilando y empaquetando ejecutable standalone...
echo       Configuracion: Release (win-x64, self-contained, single-file)
echo.

dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] La publicacion ha fallado. Revisa los mensajes de dotnet arriba.
    pause
    exit /b 1
)

echo.
echo [2/2] Verificando binario generado...
if exist "dist\Ro3Launcher.exe" (
    for %%F in ("dist\Ro3Launcher.exe") do set size=%%~zF
    set /a sizeMB=!size! / 1048576
    echo ========================================================
    echo  [EXITO] Ejecutable listo en:
    echo  %~dp0dist\Ro3Launcher.exe - Tamano: !sizeMB! MB
    echo ========================================================
) else (
    echo [ERROR] No se encontro dist\Ro3Launcher.exe
)

echo.
pause
