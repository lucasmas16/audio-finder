$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Localization.cs', 'Models.cs', 'Audio.cs', 'Hotkeys.cs', 'Appearance.cs') | ForEach-Object { Join-Path (Join-Path $project 'src') $_ }
$output = Join-Path $project 'dist\DeviceIdentityTests.exe'
New-Item -ItemType Directory -Force (Split-Path $output -Parent) | Out-Null
& $compiler /nologo /codepage:65001 /platform:x64 /target:exe /main:AudioFinder.DeviceIdentityTests "/out:$output" /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources (Join-Path $PSScriptRoot 'DeviceIdentityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'A compilação dos testes de identidade falhou.' }
& $output (Join-Path $project 'artifacts\identity-tests')
if ($LASTEXITCODE -ne 0) { throw 'Os testes de identidade falharam.' }
