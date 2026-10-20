#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Install SatraN into Program Files (admin). TUN works because the app manifest requests elevation at launch.
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Src = Join-Path $Root 'SatraN'
if (-not (Test-Path (Join-Path $Src 'SatraN.exe'))) {
  # Running from inside the extracted zip where this script sits next to SatraN.exe
  if (Test-Path (Join-Path $Root 'SatraN.exe')) { $Src = $Root }
  else { throw "SatraN.exe not found next to this script." }
}
$Dest = Join-Path $env:ProgramFiles 'SatraN'
Write-Host "Installing SatraN to $Dest ..."
if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
Copy-Item -Recurse -Force $Src $Dest

$Exe = Join-Path $Dest 'SatraN.exe'
$UninstallPs1 = Join-Path $Dest 'Uninstall-SatraN.ps1'
Copy-Item -Force (Join-Path $Src 'Uninstall-SatraN.ps1') $UninstallPs1 -ErrorAction SilentlyContinue
if (-not (Test-Path $UninstallPs1)) {
  Copy-Item -Force (Join-Path $PSScriptRoot 'Uninstall-SatraN.ps1') $UninstallPs1 -ErrorAction SilentlyContinue
}
if (-not (Test-Path $UninstallPs1)) {
  throw 'Uninstall-SatraN.ps1 missing from package.'
}

# Wrapper so Apps & Features / double-click elevates reliably on Windows PowerShell 5
$UninstallCmd = Join-Path $Dest 'Uninstall-SatraN.cmd'
@"
@echo off
:: Elevate and run full uninstall
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0Uninstall-SatraN.ps1\"'"
"@ | Set-Content -Path $UninstallCmd -Encoding ASCII

$Programs = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$Wsh = New-Object -ComObject WScript.Shell

$Lnk = Join-Path $Programs 'SatraN.lnk'
$S = $Wsh.CreateShortcut($Lnk)
$S.TargetPath = $Exe
$S.WorkingDirectory = $Dest
$S.Description = 'SatraN'
$S.Save()

$ULnk = Join-Path $Programs 'Uninstall SatraN.lnk'
$U = $Wsh.CreateShortcut($ULnk)
$U.TargetPath = $UninstallCmd
$U.WorkingDirectory = $Dest
$U.Description = 'Uninstall SatraN'
$U.Save()

# Apps & Features (Settings -> Apps)
$Reg = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatraN'
New-Item -Path $Reg -Force | Out-Null
Set-ItemProperty $Reg DisplayName 'SatraN'
Set-ItemProperty $Reg DisplayVersion '1.1.9'
Set-ItemProperty $Reg Publisher 'Satra'
Set-ItemProperty $Reg DisplayIcon $Exe
Set-ItemProperty $Reg InstallLocation $Dest
Set-ItemProperty $Reg UninstallString "`"$UninstallCmd`""
Set-ItemProperty $Reg QuietUninstallString "`"$UninstallCmd`""
Set-ItemProperty $Reg NoModify 1
Set-ItemProperty $Reg NoRepair 1
try {
  $sizeKb = [int]((Get-ChildItem $Dest -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1KB)
  Set-ItemProperty $Reg EstimatedSize $sizeKb
} catch {}

Write-Host "Done. Launch SatraN from the Start Menu (UAC once per launch - then VPN/TUN works)."
Write-Host "To remove: Start Menu -> Uninstall SatraN, or Settings -> Apps -> SatraN -> Uninstall."
Start-Process $Exe
