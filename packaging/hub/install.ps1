# SatraN for Windows (PowerShell as admin):  irm https://107.189.24.195:2097/satra/dl/install.ps1 -SkipCertificateCheck | iex   (PowerShell 7)
# Windows PowerShell 5:  [Net.ServicePointManager]::ServerCertificateValidationCallback={$true}; irm https://107.189.24.195:2097/satra/dl/install.ps1 | iex
$ErrorActionPreference = 'Stop'
# The Hub hostname is behind a CDN that does not carry port 2097 — talk to the origin IP. Accept only a valid
# certificate chain issued for the Satra Hub domain (name mismatch with the bare IP is the only thing tolerated).
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Net.ServicePointManager]::ServerCertificateValidationCallback = {
  param($s, $cert, $chain, $err)
  if ($err -eq 'None') { return $true }
  if ($err -ne 'RemoteCertificateNameMismatch') { return $false }
  $c2 = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $cert
  return ($c2.GetNameInfo('DnsName', $false) -match 'iivt\.ir$')
}
$B = 'https://107.189.24.195:2097/satra/dl'
$l = Invoke-RestMethod "$B/latest.json" -UseBasicParsing
$v = $l.version
$name = "SatraN-Setup-$v.exe"
$f = Join-Path $env:TEMP $name
Invoke-WebRequest "$B/$v/$name" -OutFile $f -UseBasicParsing
$want = $l.files.$name.sha256
if ($want -and ((Get-FileHash $f -Algorithm SHA256).Hash -ne $want.ToUpper())) { throw 'checksum mismatch' }
Start-Process $f -Verb RunAs -Wait
