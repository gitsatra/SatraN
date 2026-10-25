# SatraN for Windows uninstall:  irm https://raw.githubusercontent.com/gitsatra/SatraN/main/uninstall.ps1 | iex
$u = Join-Path $env:ProgramFiles 'SatraN\Uninstall-SatraN.ps1'
if (Test-Path $u) { Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$u`""; 'SatraN removed.' }
else { 'SatraN is not installed.' }
