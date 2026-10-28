@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ========================================================
echo        RO3 Launcher - Deploy a Carpeta de Juego
echo ========================================================
echo.

set "TARGET_DIR=E:\Genesis Launcher - DEV"

:: 1. Verificar si existe la carpeta de destino
if not exist "%TARGET_DIR%" (
    echo [ERROR] No se encontro la carpeta de destino: "%TARGET_DIR%"
    pause
    exit /b 1
)

:: 2. Cerrar procesos de Ro3Launcher en ejecucion
tasklist /fi "imagename eq Ro3Launcher.exe" 2>nul | find /i "Ro3Launcher.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [INFO] Cerrando Ro3Launcher.exe en ejecucion...
    taskkill /f /im Ro3Launcher.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

:: 3. Compilar Release si dist\Ro3Launcher.exe no existe o si se solicita
echo [1/3] Compilando y empaquetando Release...
dotnet publish Ro3Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Fallo al compilar Ro3Launcher.csproj.
    pause
    exit /b 1
)

:: 4. Copiar a la carpeta de juego
echo [2/3] Copiando ejecutable a "%TARGET_DIR%"...
copy /y "dist\Ro3Launcher.exe" "%TARGET_DIR%\Ro3Launcher.exe" >nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] No se pudo copiar a "%TARGET_DIR%\Ro3Launcher.exe". Asegurate de que no este bloqueado.
    pause
    exit /b 1
)

echo [3/3] Despliegue completado con exito.
echo.
echo ========================================================
echo  [EXITO] Launcher desplegado en:
echo  "%TARGET_DIR%\Ro3Launcher.exe"
echo ========================================================
echo.

if "%1"=="--no-run" goto :done
if "%1"=="-n" goto :done
set /p RUN_NOW="Deseas iniciar Ro3Launcher ahora mismo? (S/N): "
if /i "%RUN_NOW%"=="S" (
    echo Iniciando "%TARGET_DIR%\Ro3Launcher.exe"...
    start "" "%TARGET_DIR%\Ro3Launcher.exe"
)
:done

