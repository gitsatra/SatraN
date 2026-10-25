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
$Version = '__VERSION__'
Write-Host "Installing SatraN $Version to $Dest ..."

# 1) wait for the running app (it quits itself after starting this update) and its cores to exit — files must not be locked
$deadline = (Get-Date).AddSeconds(20)
do {
  $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Dest, [StringComparison]::OrdinalIgnoreCase) }
  if (-not $running) { break }
  Start-Sleep -Milliseconds 400
} while ((Get-Date) -lt $deadline)
$running | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2) stage the new files next to the old ones, carry user data over (servers, subscriptions, settings, logs, rollbacks)
$New = "$Dest.new"; $Old = "$Dest.old"
if (Test-Path $New) { Remove-Item -Recurse -Force $New }
Copy-Item -Recurse -Force $Src $New
if (Test-Path $Dest) {
  foreach ($d in 'guiConfigs','guiBackups','guiLogs','binConfigs','rollback') {
    $from = Join-Path $Dest $d
    if (Test-Path $from) { Copy-Item -Recurse -Force $from (Join-Path $New $d) }
  }
}

# 3) atomic-ish swap with rollback if anything fails
if (Test-Path $Old) { Remove-Item -Recurse -Force $Old -ErrorAction SilentlyContinue }
try {
  if (Test-Path $Dest) { Rename-Item $Dest $Old }
  Rename-Item $New $Dest
} catch {
  Write-Warning "Install failed ($_) - restoring the previous version"
  if ((-not (Test-Path $Dest)) -and (Test-Path $Old)) { Rename-Item $Old $Dest }
  throw
}
Remove-Item -Recurse -Force $Old -ErrorAction SilentlyContinue

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
Set-ItemProperty $Reg DisplayVersion $Version
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
