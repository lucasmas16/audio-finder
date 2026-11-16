# Build stages (all optional except the compile):
#   build.ps1                         compile dist\AudioFinder.exe
#   build.ps1 -Release                compile + installer + portable zip + SHA256SUMS
#   -SignScript tools\sign.ps1        sign the app before packaging and the installer after it
#   -PackageOnly                      package an existing (e.g. externally signed) dist\AudioFinder.exe
#   -ChecksumsOnly                    rewrite SHA256SUMS.txt after release files were replaced
#   -RequireSigned                    fail unless the app (and, when this run signs it, the installer) carries a valid Authenticode signature
param([switch]$RunTests, [switch]$SafeTests, [switch]$Release, [string]$Version, [string]$SignScript, [switch]$PackageOnly, [switch]$ChecksumsOnly, [switch]$RequireSigned)
$ErrorActionPreference = 'Stop'
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida '$Version'. Use MAJOR.MINOR.PATCH no arquivo VERSION." }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outDir = Join-Path $PSScriptRoot 'dist'
$objDir = Join-Path $PSScriptRoot 'obj'
$releaseDir = Join-Path $PSScriptRoot 'release'
$appPath = Join-Path $outDir 'AudioFinder.exe'
$setupPath = Join-Path $releaseDir "AudioFinder-Setup-$Version.exe"
$icon = Join-Path $PSScriptRoot 'AudioFinder.ico'
New-Item -ItemType Directory -Force -Path $outDir, $objDir | Out-Null
if ($SignScript) { $SignScript = (Resolve-Path -LiteralPath $SignScript).Path }

function Invoke-Sign([string]$Path) {
    if (-not $SignScript) { return }
    & $SignScript -Path $Path
    if (-not $?) { throw "A assinatura falhou: $Path" }
}
function Assert-Signed([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') { throw "Assinatura ausente ou inválida ($($signature.Status)): $Path" }
    Write-Output "Assinado: $(Split-Path $Path -Leaf) por $($signature.SignerCertificate.Subject)"
}
function Write-Checksums {
    $sums = Get-ChildItem -LiteralPath $releaseDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    [System.IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), [string[]]$sums, (New-Object System.Text.UTF8Encoding($false)))
}
# Version metadata is generated from VERSION so the executable, installer and release tag stay aligned.
function Write-AssemblyInfo([string]$Path, [string]$Title) {
    $lines = @(
        'using System.Reflection;',
        "[assembly: AssemblyTitle(""$Title"")]",
        '[assembly: AssemblyProduct("Audio Finder")]',
        '[assembly: AssemblyCopyright("Copyright (c) 2026 Audio Finder contributors. MIT License.")]',
        "[assembly: AssemblyVersion(""$Version.0"")]",
        "[assembly: AssemblyFileVersion(""$Version.0"")]",
        "[assembly: AssemblyInformationalVersion(""$Version"")]"
    )
    [System.IO.File]::WriteAllLines($Path, $lines, (New-Object System.Text.UTF8Encoding($false)))
}

if ($ChecksumsOnly) {
    if ($RequireSigned) { Assert-Signed $setupPath }
    Write-Checksums
    Write-Output "SHA256SUMS.txt atualizado em $releaseDir"
    return
}

$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$options = @('/nologo', '/optimize+', '/platform:x64', '/codepage:65001', '/warn:4', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Xml.dll', ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')))
if (Test-Path $icon) { $options += ('/win32icon:' + $icon) }
if ($PackageOnly) {
    $Release = $true
    if (-not (Test-Path $appPath)) { throw 'Rode build.ps1 antes de -PackageOnly.' }
    $built = (Get-Item -LiteralPath $appPath).VersionInfo.ProductVersion
    if ($built -ne $Version) { throw "dist\AudioFinder.exe é da versão $built, esperada $Version." }
}
else {
    $appInfo = Join-Path $objDir 'AppInfo.g.cs'
    Write-AssemblyInfo $appInfo 'Audio Finder'
    & $compiler @options /target:winexe /main:AudioFinder.Program "/out:$appPath" @sources $appInfo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
    Invoke-Sign $appPath
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AudioFinder.exe.config') -Destination $outDir -Force
    if (Test-Path (Join-Path $PSScriptRoot 'assets')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $outDir -Recurse -Force }
}
if ($RunTests) {
    $testsPath = Join-Path $outDir 'AudioFinder.Tests.exe'
    & $compiler @options /target:exe /main:AudioFinder.Tests "/out:$testsPath" @sources (Join-Path $PSScriptRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'A compilação dos testes falhou.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AudioFinder.exe.config') -Destination ($testsPath + '.config') -Force
    $testArgs = @((Join-Path $PSScriptRoot 'artifacts'))
    if ($SafeTests) { $testArgs += '--safe' }
    & $testsPath @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
}
if ($Release) {
    if ($RequireSigned) { Assert-Signed $appPath }
    # Only an explicit allowlist enters the package: no settings, logs, test output or local artifacts.
    $stage = Join-Path $objDir 'payload'
    foreach ($path in @($releaseDir, $stage)) { if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
    New-Item -ItemType Directory -Force -Path $releaseDir, (Join-Path $stage 'assets\devices') | Out-Null
    Copy-Item -LiteralPath $appPath, (Join-Path $PSScriptRoot 'AudioFinder.exe.config') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets\devices') -Filter '*.png' | Copy-Item -Destination (Join-Path $stage 'assets\devices')

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payload = Join-Path $objDir 'payload.zip'
    if (Test-Path $payload) { Remove-Item -LiteralPath $payload -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $setupInfo = Join-Path $objDir 'SetupInfo.g.cs'
    Write-AssemblyInfo $setupInfo 'Audio Finder Setup'
    $setupOptions = @('/nologo', '/optimize+', '/platform:anycpu', '/codepage:65001', '/warn:4', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll', ('/win32manifest:' + (Join-Path $PSScriptRoot 'installer\Setup.manifest')), ("/resource:$payload,AudioFinder.Payload.zip"))
    if (Test-Path $icon) { $setupOptions += ('/win32icon:' + $icon) }
    & $compiler @setupOptions /target:winexe /main:AudioFinderSetup.Program "/out:$setupPath" (Join-Path $PSScriptRoot 'installer\Setup.cs') $setupInfo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação do instalador falhou.' }
    Invoke-Sign $setupPath
    # Without a sign hook the installer is signed by an external step, then checked by -ChecksumsOnly -RequireSigned.
    if ($RequireSigned -and $SignScript) { Assert-Signed $setupPath }

    $portable = Join-Path $releaseDir "AudioFinder-$Version-win-x64.zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $portable, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Checksums
    Write-Output "Release $Version em $releaseDir"
}
Write-Output "Pronto: $appPath"
