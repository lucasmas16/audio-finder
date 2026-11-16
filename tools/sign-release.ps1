# Local signing for SIGNING_MODE=local: rebuilds the tagged commit, signs app and installer with
# tools/sign.ps1, tests the installer and attaches the files to the draft release created by CI.
# Requires gh authenticated and the signing environment described in tools/sign.ps1.
param([Parameter(Mandatory = $true)][string]$Tag)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
Push-Location $project
try {
    if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "Tag inválida: $Tag" }
    $version = $Matches[1]
    if ((Get-Content VERSION -Raw).Trim() -ne $version) { throw "VERSION não corresponde à tag $Tag." }
    if (git status --porcelain) { throw 'Há alterações não commitadas; assine a partir de um checkout limpo.' }
    $head = git rev-parse HEAD; $tagged = git rev-parse "$Tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or $head -ne $tagged) { throw "O checkout atual não é o commit da tag $Tag (use git checkout $Tag)." }
    $draft = gh release view $Tag --json isDraft --jq .isDraft
    if ($LASTEXITCODE -ne 0 -or $draft -ne 'true') { throw "Não há rascunho de release para $Tag. Envie a tag e aguarde a pipeline." }

    & (Join-Path $project 'build.ps1') -Release -Version $version -SignScript (Join-Path $PSScriptRoot 'sign.ps1') -RequireSigned
    & (Join-Path $project 'tests\installer-smoke.ps1')
    $files = Get-ChildItem (Join-Path $project 'release') -File | ForEach-Object FullName
    gh release upload $Tag @files --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao anexar os arquivos ao rascunho.' }
    Write-Output "Arquivos assinados anexados ao rascunho $Tag. Revise e publique no GitHub."
}
finally { Pop-Location }
