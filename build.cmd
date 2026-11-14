@echo off
rem Builds dist\SurrounDeadModLauncher.exe (.NET Framework 4.8, C# 5) and tests\SelfTest.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
set REFS=/reference:"%FW%\System.IO.Compression.dll" /reference:"%FW%\System.IO.Compression.FileSystem.dll" /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll
cd /d "%~dp0"
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /warn:4 /out:dist\SurrounDeadModLauncher.exe /win32manifest:app.manifest /win32icon:assets\launcher.ico %REFS% src\Core.cs src\Mods.cs src\Pixel.cs src\Ui.cs src\Program.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /out:tests\SelfTest.exe %REFS% src\Core.cs src\Mods.cs tests\SelfTest.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /out:tests\UiShot.exe %REFS% src\Core.cs src\Mods.cs src\Pixel.cs src\Ui.cs tests\UiShot.cs
if errorlevel 1 exit /b 1
echo OK
