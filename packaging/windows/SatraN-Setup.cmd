@echo off
:: Elevate and run Install-SatraN.ps1 (UAC prompt once for install)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0Install-SatraN.ps1\"'"
