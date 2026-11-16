param([switch]$Probe)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Localization.cs', 'Models.cs', 'Audio.cs', 'Hotkeys.cs', 'Appearance.cs', 'BluetoothConnection.cs') | ForEach-Object { Join-Path (Join-Path $project 'src') $_ }
$output = Join-Path $project 'dist\BluetoothConnectionTests.exe'
& $compiler /nologo /codepage:65001 /platform:x64 /target:exe /main:AudioFinder.BluetoothConnectionTests "/out:$output" /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources (Join-Path $PSScriptRoot 'BluetoothConnectionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'A compilação dos testes Bluetooth falhou.' }
if ($Probe) { & $output --probe } else { & $output }
if ($LASTEXITCODE -ne 0) { throw 'Os testes Bluetooth falharam.' }
