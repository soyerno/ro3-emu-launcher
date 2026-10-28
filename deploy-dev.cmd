@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ========================================================
echo        KonsorRO Launcher - Deploy a Carpeta de Juego
echo ========================================================
echo.

set "TARGET_DIR=E:\Genesis Launcher - DEV"

:: 1. Verificar si existe la carpeta de destino
if not exist "%TARGET_DIR%" (
    echo [ERROR] No se encontro la carpeta de destino: "%TARGET_DIR%"
    pause
    exit /b 1
)

:: 2. Cerrar procesos de KonsorRO en ejecucion
tasklist /fi "imagename eq KonsorRO.exe" 2>nul | find /i "KonsorRO.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [INFO] Cerrando KonsorRO.exe en ejecucion...
    taskkill /f /im KonsorRO.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

:: 3. Compilar Release si dist\KonsorRO.exe no existe o si se solicita
echo [1/3] Compilando y empaquetando Release...
dotnet publish KonsorRO.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Fallo al compilar KonsorRO.csproj.
    pause
    exit /b 1
)

:: 4. Copiar a la carpeta de juego
echo [2/3] Copiando ejecutable a "%TARGET_DIR%"...
copy /y "dist\KonsorRO.exe" "%TARGET_DIR%\KonsorRO.exe" >nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] No se pudo copiar a "%TARGET_DIR%\KonsorRO.exe". Asegurate de que no este bloqueado.
    pause
    exit /b 1
)

echo [3/3] Despliegue completado con exito.
echo.
echo ========================================================
echo  [EXITO] Launcher desplegado en:
echo  "%TARGET_DIR%\KonsorRO.exe"
echo ========================================================
echo.

if "%1"=="--no-run" goto :done
if "%1"=="-n" goto :done
set /p RUN_NOW="Deseas iniciar KonsorRO ahora mismo? (S/N): "
if /i "%RUN_NOW%"=="S" (
    echo Iniciando "%TARGET_DIR%\KonsorRO.exe"...
    start "" "%TARGET_DIR%\KonsorRO.exe"
)
:done

