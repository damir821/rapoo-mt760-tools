@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Apply.ps1" -Mode restore
timeout /t 3 >nul
