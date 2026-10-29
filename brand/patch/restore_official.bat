@echo off
rem Undo the Konsor patch so the game talks to the official servers again.
cd /d "%~dp0"
RO3Emu.exe restore --yes
pause
