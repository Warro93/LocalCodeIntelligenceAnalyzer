<#
.SYNOPSIS
    Crea la distribuzione Windows x64 self-contained (non richiede .NET installato sul PC di destinazione)
    e uno zip pronto da copiare o allegare a una release.

.PARAMETER NoZip
    Non crea lo zip (usato da install-app.ps1, che copia direttamente la cartella).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
    powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1 -ReadyToRun
#>
[CmdletBinding()]
param(
    [switch]$ReadyToRun,
    [switch]$FrameworkDependent,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $root

[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0' }

$name = "LocalCodeIntelligenceAnalyzer-$version-win-x64"
$output = Join-Path $root "artifacts\publish\$name"
$zip = Join-Path $root "artifacts\$name.zip"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$common = @('-c', 'Release', '-r', 'win-x64', "--self-contained", $selfContained, "-p:PublishReadyToRun=$($ReadyToRun.IsPresent.ToString().ToLowerInvariant())", '-o', $output)

foreach ($project in @('src\Lcia.App\Lcia.App.csproj', 'src\Lcia.Cli\Lcia.Cli.csproj')) {
    Write-Host "==> dotnet publish $project" -ForegroundColor Cyan
    & dotnet publish (Join-Path $root $project) @common
    if ($LASTEXITCODE -ne 0) { Write-Host 'ERRORE: publish non riuscito.' -ForegroundColor Red; exit $LASTEXITCODE }
}

foreach ($doc in @('README.md', 'CHANGELOG.md', 'docs\USER_GUIDE.md', 'docs\INSTALLATION.md', 'docs\THIRD_PARTY_LICENSES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $output -Force
}

$folderSize = (Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host ('Cartella: {0} ({1:N0} MB)' -f $output, ($folderSize / 1MB)) -ForegroundColor Green

if (-not $NoZip) {
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
    $zipSize = (Get-Item -LiteralPath $zip).Length
    Write-Host ('Zip:      {0} ({1:N0} MB)' -f $zip, ($zipSize / 1MB)) -ForegroundColor Green
    Write-Host 'Avvio: LocalCodeIntelligenceAnalyzer.exe   ·   riga di comando: lcia.exe --help'
}
exit 0
