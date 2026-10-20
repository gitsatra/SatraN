#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Fully remove SatraN: Program Files, Start Menu, Apps & Features entry, and user data.
#>
$ErrorActionPreference = 'Continue'
Write-Host 'Uninstalling SatraN...'

# Stop app + cores that may lock files under Program Files
foreach ($n in @('SatraN', 'xray', 'sing-box', 'singbox')) {
  Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500

$Dest = Join-Path $env:ProgramFiles 'SatraN'
$Programs = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
foreach ($lnk in @('SatraN.lnk', 'Uninstall SatraN.lnk')) {
  $p = Join-Path $Programs $lnk
  if (Test-Path $p) { Remove-Item -Force $p -ErrorAction SilentlyContinue }
}

$Reg = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatraN'
if (Test-Path $Reg) { Remove-Item -Recurse -Force $Reg -ErrorAction SilentlyContinue }

# Program Files install tree (may include guiConfigs written next to the exe)
if (Test-Path $Dest) {
  # Retry a few times if AV / explorer still holds a handle
  for ($i = 0; $i -lt 5; $i++) {
    try {
      Remove-Item -LiteralPath $Dest -Recurse -Force -ErrorAction Stop
      break
    } catch {
      Start-Sleep -Seconds 1
      Get-Process -Name SatraN,xray,sing-box -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
  }
  if (Test-Path $Dest) {
    # Last resort: cmd rmdir
    cmd /c "rmdir /s /q `"$Dest`""
  }
}

# User data (%LOCALAPPDATA%\SatraN) — full uninstall removes this too
$Data = Join-Path $env:LOCALAPPDATA 'SatraN'
if (Test-Path $Data) {
  Remove-Item -LiteralPath $Data -Recurse -Force -ErrorAction SilentlyContinue
}

# Leftover Setup extracts in Temp
Get-ChildItem $env:TEMP -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -like 'SatraN-Setup-*' } |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

$left = @(
  (Test-Path $Dest),
  (Test-Path $Reg),
  (Test-Path (Join-Path $Programs 'SatraN.lnk')),
  (Test-Path $Data)
) -contains $true

if ($left) {
  Write-Host 'SatraN uninstall finished with some leftovers still locked. Reboot and run Uninstall again if needed.'
  exit 1
}

Write-Host 'SatraN fully uninstalled.'
exit 0
