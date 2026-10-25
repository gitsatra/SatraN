# SatraN for Windows uninstall:  irm https://p.iivt.ir:2097/satra/dl/uninstall.ps1 | iex
$u = Join-Path $env:ProgramFiles 'SatraN\Uninstall-SatraN.ps1'
if (Test-Path $u) { Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$u`""; 'SatraN removed.' }
else { 'SatraN is not installed.' }
