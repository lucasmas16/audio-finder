$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' | ForEach-Object FullName)
$output = Join-Path $project 'dist\ConnectionUiFlowTests.exe'
& $compiler /nologo /codepage:65001 /platform:x64 /target:exe /main:AudioFinder.ConnectionUiFlowTests "/out:$output" ('/win32manifest:' + (Join-Path $project 'app.manifest')) /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll @sources (Join-Path $PSScriptRoot 'ConnectionUiFlowTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'A compilação dos testes UI/core falhou.' }
Copy-Item -LiteralPath (Join-Path $project 'AudioFinder.exe.config') -Destination ($output + '.config') -Force
& $output (Join-Path $project 'artifacts\connection-ui-flow')
if ($LASTEXITCODE -ne 0) { throw 'Os testes UI/core falharam.' }
