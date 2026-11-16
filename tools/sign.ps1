# Signs one file in place with signtool. Used by build.ps1 -SignScript.
# The private key never enters this repository: the certificate comes from the current user's store
# (Certum SimplySign Desktop, a hardware token or another OV certificate) or from a signtool plug-in.
#
#   AUDIOFINDER_SIGN_THUMBPRINT  SHA-1 thumbprint of the certificate in Cert:\CurrentUser\My
#   AUDIOFINDER_SIGN_TIMESTAMP   RFC 3161 timestamp URL (default: Certum)
#   AUDIOFINDER_SIGN_ARGS        extra signtool arguments, e.g. "/dlib <dll> /dmdf <metadata.json>" for Azure
#   AUDIOFINDER_SIGNTOOL         full path to signtool.exe (default: newest Windows SDK x64)
param([Parameter(Mandatory = $true)][string]$Path)
$ErrorActionPreference = 'Stop'
$thumbprint = ($env:AUDIOFINDER_SIGN_THUMBPRINT -replace '\s', '')
$extra = if ($env:AUDIOFINDER_SIGN_ARGS) { @($env:AUDIOFINDER_SIGN_ARGS -split '\s+' | Where-Object { $_ }) } else { @() }
if (-not $thumbprint -and $extra.Count -eq 0) { throw 'Defina AUDIOFINDER_SIGN_THUMBPRINT (ou AUDIOFINDER_SIGN_ARGS) antes de assinar.' }
$timestamp = if ($env:AUDIOFINDER_SIGN_TIMESTAMP) { $env:AUDIOFINDER_SIGN_TIMESTAMP } else { 'http://time.certum.pl' }
$signtool = $env:AUDIOFINDER_SIGNTOOL
if (-not $signtool) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signtool = Get-ChildItem -LiteralPath $kits -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool -or -not (Test-Path -LiteralPath $signtool)) { throw 'signtool.exe não encontrado. Instale o Windows SDK ou defina AUDIOFINDER_SIGNTOOL.' }
$arguments = @('sign', '/fd', 'sha256', '/tr', $timestamp, '/td', 'sha256', '/d', 'Audio Finder', '/du', 'https://github.com/lucasmas16/audio-finder')
if ($thumbprint) { $arguments += @('/sha1', $thumbprint) }
$arguments += $extra
& $signtool @arguments $Path
if ($LASTEXITCODE -ne 0) { throw "signtool sign falhou ($LASTEXITCODE): $Path" }
& $signtool verify /pa /q $Path
if ($LASTEXITCODE -ne 0) { throw "signtool verify falhou ($LASTEXITCODE): $Path" }
