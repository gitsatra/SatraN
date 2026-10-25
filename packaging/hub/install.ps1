# SatraN for Windows:  irm https://p.iivt.ir:2097/satra/dl/install.ps1 | iex
$ErrorActionPreference = 'Stop'
$B = 'https://p.iivt.ir:2097/satra/dl'
$v = (Invoke-RestMethod "$B/latest.json").version
$f = Join-Path $env:TEMP "SatraN-Setup-$v.exe"
Invoke-WebRequest "$B/$v/SatraN-Setup-$v.exe" -OutFile $f -UseBasicParsing
Start-Process $f -Verb RunAs -Wait
