@echo off
rem ==== AntigravityAutoApprove build script ====
rem 使用系统自带 .NET Framework 4.x 编译器, 无需安装任何 SDK
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
if not exist "%LOCALAPPDATA%\AntigravityAutoApprove" mkdir "%LOCALAPPDATA%\AntigravityAutoApprove"
"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:"%LOCALAPPDATA%\AntigravityAutoApprove\AntigravityAutoApprove.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" /r:"%WPF%\WindowsBase.dll" ^
  "%~dp0AutoApprover.cs"
if %errorlevel%==0 (echo BUILD OK: %LOCALAPPDATA%\AntigravityAutoApprove\AntigravityAutoApprove.exe) else (echo BUILD FAILED)
