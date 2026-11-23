@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Apply.ps1" -Mode install
timeout /t 3 >nul
