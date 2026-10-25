# SatraN for Windows uninstall:  irm https://107.189.24.195:2097/satra/dl/uninstall.ps1 -SkipCertificateCheck | iex
$d = Join-Path $env:ProgramFiles 'SatraN'
$exe = Join-Path $d 'Uninstall.exe'
$u = Join-Path $d 'Uninstall-SatraN.ps1'
if (Test-Path $exe) { Start-Process $exe -Verb RunAs -Wait; 'SatraN removed.' }
elseif (Test-Path $u) { Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$u`""; 'SatraN removed.' }
else { 'SatraN is not installed.' }
