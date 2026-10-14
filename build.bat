@echo off
cd /d "%~dp0"
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /codepage:65001 /resource:logo.png,logo.png /out:FixedClipboard.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll FixedClipboard.cs
if errorlevel 1 exit /b 1
if not "%CI%"=="true" pause
