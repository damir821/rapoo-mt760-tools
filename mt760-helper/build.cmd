@echo off
rem Builds MT760Helper.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set "NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%NET%\csc.exe" set "NET=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%NET%\csc.exe"
if not exist "%CSC%" (
  echo C# compiler not found. .NET Framework 4.x is required.
  exit /b 1
)
"%CSC%" -nologo -optimize+ -target:winexe -platform:anycpu -out:MT760Helper.exe ^
  -win32icon:src\app.ico -win32manifest:src\app.manifest -resource:src\app.ico,MT760Helper.app.ico ^
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Built MT760Helper.exe
