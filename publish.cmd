@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ========================================================
echo        KonsorRO Launcher - Publicacion Single-File
echo ========================================================
echo.

:: 1. Cerrar procesos bloqueantes de KonsorRO si existen
tasklist /fi "imagename eq KonsorRO.exe" 2>nul | find /i "KonsorRO.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [AVISO] Cerrando instancia previa de KonsorRO.exe en ejecucion...
    taskkill /f /im KonsorRO.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

:: 2. Limpiar carpeta dist
if not exist "dist" mkdir "dist"

echo [1/2] Compilando y empaquetando ejecutable standalone...
echo       Configuracion: Release (win-x64, self-contained, single-file)
echo.

dotnet publish KonsorRO.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] La publicacion ha fallado. Revisa los mensajes de dotnet arriba.
    pause
    exit /b 1
)

echo.
echo [2/2] Verificando binario generado...
if exist "dist\KonsorRO.exe" (
    for %%F in ("dist\KonsorRO.exe") do set size=%%~zF
    set /a sizeMB=!size! / 1048576
    echo ========================================================
    echo  [EXITO] Ejecutable listo en:
    echo  %~dp0dist\KonsorRO.exe - Tamano: !sizeMB! MB
    echo ========================================================
) else (
    echo [ERROR] No se encontro dist\KonsorRO.exe
)

echo.
pause
