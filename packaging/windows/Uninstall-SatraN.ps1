#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
Get-Process SatraN -ErrorAction SilentlyContinue | Stop-Process -Force
$Dest = Join-Path $env:ProgramFiles 'SatraN'
$Lnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\SatraN.lnk'
if (Test-Path $Lnk) { Remove-Item -Force $Lnk }
$Reg = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatraN'
if (Test-Path $Reg) { Remove-Item -Recurse -Force $Reg }
if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
Write-Host 'SatraN uninstalled.'
