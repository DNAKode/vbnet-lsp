@echo off
REM VB.NET Language Support - Install Script Wrapper
REM Double-click this file or run from command prompt

powershell -ExecutionPolicy Bypass -File "%~dp0install-extension.ps1" %*

if errorlevel 1 (
    echo.
    echo Installation failed. Press any key to exit...
    pause > nul
)
