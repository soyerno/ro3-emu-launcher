@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo        RO3 Launcher - Compilacion Rapida
echo ========================================================
echo.
dotnet build Ro3Launcher.csproj -c Release
if %ERRORLEVEL% equ 0 (
    echo.
    echo [OK] Compilacion completada con exito sin errores.
) else (
    echo.
    echo [ERROR] La compilacion fallo. Revisa los mensajes arriba.
)
echo.
pause
