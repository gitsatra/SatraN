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
$Uninstall = Join-Path $Dest 'Uninstall-SatraN.ps1'
Copy-Item -Force (Join-Path $Root 'Uninstall-SatraN.ps1') $Uninstall -ErrorAction SilentlyContinue
if (-not (Test-Path $Uninstall)) {
  Copy-Item -Force (Join-Path $PSScriptRoot 'Uninstall-SatraN.ps1') $Uninstall -ErrorAction SilentlyContinue
}

# Start Menu shortcut (app already requireAdministrator via manifest)
$Wsh = New-Object -ComObject WScript.Shell
$Programs = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$Lnk = Join-Path $Programs 'SatraN.lnk'
$S = $Wsh.CreateShortcut($Lnk)
$S.TargetPath = $Exe
$S.WorkingDirectory = $Dest
$S.Description = 'SatraN'
$S.Save()

# Add/Remove Programs entry
$Reg = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatraN'
New-Item -Path $Reg -Force | Out-Null
Set-ItemProperty $Reg DisplayName 'SatraN'
Set-ItemProperty $Reg Publisher 'Satra'
Set-ItemProperty $Reg DisplayIcon $Exe
Set-ItemProperty $Reg InstallLocation $Dest
Set-ItemProperty $Reg UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Uninstall`""
Set-ItemProperty $Reg NoModify 1
Set-ItemProperty $Reg NoRepair 1

Write-Host "Done. Launch SatraN from the Start Menu (UAC once per launch - then VPN/TUN works)."
Start-Process $Exe
