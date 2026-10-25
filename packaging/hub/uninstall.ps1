# SatraN for Windows uninstall:  irm https://107.189.24.195:2097/satra/dl/uninstall.ps1 -SkipCertificateCheck | iex
$u = Join-Path $env:ProgramFiles 'SatraN\Uninstall-SatraN.ps1'
if (Test-Path $u) { Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$u`""; 'SatraN removed.' }
else { 'SatraN is not installed.' }
