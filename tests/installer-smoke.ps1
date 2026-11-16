# Installs the generated setup into a temporary folder, reinstalls, uninstalls and checks safety refusals.
# -Integration also verifies the Start Menu shortcut and the Apps entry (use on disposable machines, e.g. CI).
param([switch]$Integration)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$version = (Get-Content -LiteralPath (Join-Path $project 'VERSION') -Raw).Trim()
$setup = Join-Path $project "release\AudioFinder-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw 'Gere o pacote antes: build.ps1 -Release' }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioFinder'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Audio Finder.lnk'
if ($Integration -and ((Test-Path $uninstallKey) -or (Test-Path $shortcut))) { throw 'Já existe uma instalação do Audio Finder neste usuário; o teste de integração não a sobrescreve.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('af-installer-' + [guid]::NewGuid().ToString('N'))
$dir = Join-Path $root 'Audio Finder'
$script:count = 0
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw "FAIL: $Message" }; $script:count++; Write-Output "PASS: $Message" }
function Invoke-Setup([string]$Exe, [string[]]$Arguments) { (Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru).ExitCode }
function DirArg([string]$Path) { '"/dir=' + $Path + '"' }
try {
    $install = @('/silent', (DirArg $dir)); if (-not $Integration) { $install += '/nointegration' }
    Check ((Invoke-Setup $setup $install) -eq 0) 'Instalação silenciosa conclui'
    Check ((Test-Path "$dir\AudioFinder.exe") -and (Test-Path "$dir\AudioFinder.exe.config") -and (Test-Path "$dir\Uninstall.exe") -and (Test-Path "$dir\AudioFinder.install") -and (Test-Path "$dir\LICENSE.txt")) 'Arquivos do aplicativo, licença, marcador e desinstalador instalados'
    Check (@(Get-ChildItem "$dir\assets\devices" -Filter '*.png').Count -eq 9) 'Ícones de dispositivos instalados'
    Check ((Get-Item "$dir\AudioFinder.exe").VersionInfo.ProductVersion -eq $version) 'Versão instalada corresponde a VERSION'
    Set-Content -LiteralPath "$dir\stale.txt" -Value 'antigo'
    Check (((Invoke-Setup $setup $install) -eq 0) -and -not (Test-Path "$dir\stale.txt") -and (Test-Path "$dir\AudioFinder.exe")) 'Reinstalação troca a pasta inteira'
    $foreign = Join-Path $root 'pasta-do-usuario'; New-Item -ItemType Directory -Path $foreign | Out-Null; Set-Content -LiteralPath (Join-Path $foreign 'manter.txt') -Value 'dados'
    $code = Invoke-Setup $setup @('/silent', '/nointegration', (DirArg $foreign))
    Check (($code -ne 0) -and (@(Get-ChildItem $foreign).Count -eq 1)) 'Instalação recusa pasta com arquivos que não são do Audio Finder'
    $code = Invoke-Setup $setup @('/uninstall', '/silent', '/nointegration', (DirArg $foreign))
    Check (($code -ne 0) -and (Test-Path (Join-Path $foreign 'manter.txt'))) 'Desinstalação recusa pasta sem marcador de instalação'
    if ($Integration) {
        $entry = Get-ItemProperty -LiteralPath $uninstallKey
        Check (($entry.DisplayVersion -eq $version) -and ($entry.InstallLocation -eq $dir) -and ($entry.UninstallString -like "*Uninstall.exe*")) 'Entrada em Aplicativos instalados registrada'
        Check (Test-Path $shortcut) 'Atalho no Menu Iniciar criado'
    }
    $remove = @('/uninstall', '/silent'); if (-not $Integration) { $remove += '/nointegration' }
    Check ((Invoke-Setup "$dir\Uninstall.exe" $remove) -eq 0) 'Desinstalador da pasta instalada inicia'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path $dir) -and $watch.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 250 }
    Check (-not (Test-Path $dir)) 'Desinstalação remove a pasta do aplicativo'
    if ($Integration) { Check ((-not (Test-Path $uninstallKey)) -and (-not (Test-Path $shortcut))) 'Desinstalação remove atalho e entrada em Aplicativos instalados' }
    Write-Output "$($script:count) verificações do instalador concluídas."
}
finally { if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue } }
