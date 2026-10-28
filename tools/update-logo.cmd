@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo        KonsorRO DevKit - Actualizador de Logo
echo ========================================================
echo.
if "%~1"=="" (
    echo Arrastra una imagen sobre este archivo .cmd, o escribe su ruta:
    set /p "IMG_PATH=Ruta de la imagen: "
) else (
    set "IMG_PATH=%~1"
)

if not exist "%IMG_PATH%" (
    echo [ERROR] No existe el archivo especificado.
    pause
    exit /b 1
)

python process_logo.py "%IMG_PATH%"
echo.
pause
