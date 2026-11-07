@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo csc.exe not found & exit /b 1 )
if not exist dist mkdir dist

"%CSC%" -nologo -codepage:65001 -target:winexe -out:dist\iKVMConnector.exe ^
  -r:System.dll -r:System.Security.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  src\Program.cs src\MainForm.cs src\Core.cs src\Bmc.cs src\InfoForm.cs
if errorlevel 1 exit /b 1

"%CSC%" -nologo -codepage:65001 -target:exe -out:dist\iKVM-diag.exe ^
  -r:System.dll src\Core.cs src\Diag.cs src\DiagMain.cs
if errorlevel 1 exit /b 1

echo.
echo Built: dist\iKVMConnector.exe  dist\iKVM-diag.exe
echo Copy them next to your own viewer\ folder (see README).
