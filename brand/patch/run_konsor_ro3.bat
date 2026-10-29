@echo off
rem Konsor RO3: connect to the Konsor server and start the game. Keep this window open while playing.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0konsor.ps1"
if errorlevel 1 pause
