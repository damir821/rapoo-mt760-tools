@echo off
rem Runs the mouse-button test of MT760 Helper, saves result.txt and trace.txt next to this file,
rem then starts the helper again. Build MT760Helper.exe first (..\build.cmd).
cd /d "%~dp0"
set "NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist ClickTest.exe "%NET%\csc.exe" -nologo -out:ClickTest.exe -lib:"%NET%\WPF" -r:UIAutomationClient.dll -r:UIAutomationTypes.dll -r:WindowsBase.dll ClickTest.cs
if not exist "%APPDATA%\MT760Helper" mkdir "%APPDATA%\MT760Helper"
del /q "%APPDATA%\MT760Helper\trace.log" 2>nul
type nul > "%APPDATA%\MT760Helper\trace.on"
echo.
echo   The test takes about 1 minute and moves the mouse by itself.
echo   Do not touch the mouse or the keyboard until it says DONE.
echo.
ClickTest.exe > result.txt 2>&1
type result.txt
del /q "%APPDATA%\MT760Helper\trace.on" 2>nul
copy /y "%APPDATA%\MT760Helper\trace.log" trace.txt >nul 2>&1
start "" "%~dp0..\MT760Helper.exe" --tray
echo.
echo   DONE. MT760 Helper is running again. You can close this window.
pause >nul
