# SatraN for Windows (PowerShell):  irm https://raw.githubusercontent.com/gitsatra/SatraN/main/install.ps1 | iex
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'gitsatra/SatraN'
$r = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -UseBasicParsing
$a = $r.assets | Where-Object { $_.name -like 'SatraN-Setup-*.exe' } | Select-Object -First 1
if (-not $a) { throw 'No Windows setup in the latest release' }
$f = Join-Path $env:TEMP $a.name
Invoke-WebRequest $a.browser_download_url -OutFile $f -UseBasicParsing
if ($a.digest -and $a.digest.StartsWith('sha256:')) {
  if ((Get-FileHash $f -Algorithm SHA256).Hash -ne $a.digest.Substring(7).ToUpper()) { throw 'checksum mismatch' }
}
Start-Process $f -Verb RunAs -Wait
