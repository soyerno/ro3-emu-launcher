@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo        RO3 Launcher - Modo Desarrollo UI en Vivo
echo ========================================================
echo.
echo En este modo, el launcher lee los archivos HTML, CSS y JS
echo directamente de la carpeta de este DevKit en tiempo real.
echo Puedes hacer cambios y verlos al instante sin recompilar.
echo.

:: 1. Verificar ejecutable
if not exist "dist\Ro3Launcher.exe" (
    echo Compilando ejecutable base por primera vez...
    dotnet build Ro3Launcher.csproj -c Release
    dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
)

:: 2. Configurar variable de entorno para sobreescribir la UI interna
set "RO3EMU_LAUNCHER_UI=%~dp0"

echo [OK] Variable RO3EMU_LAUNCHER_UI configurada a:
echo      "%RO3EMU_LAUNCHER_UI%"
echo.
echo Iniciando launcher en modo desarrollo...
start "" "dist\Ro3Launcher.exe"

echo.
echo Launcher iniciado. Puedes editar los archivos en:
echo - ui.index.html
echo - ui.app.css
echo - ui.app.js
echo.
