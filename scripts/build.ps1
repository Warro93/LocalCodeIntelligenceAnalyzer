<#
.SYNOPSIS
    Compila la solution, esegue i test automatici e una prova end-to-end della CLI sul progetto DemoShop.

.DESCRIPTION
    Richiede .NET SDK 10 (https://dotnet.microsoft.com/download/dotnet/10.0).
    La prova della CLI usa una cartella dati temporanea: non tocca l'indice personale né il repository.

.PARAMETER Configuration
    Debug o Release (predefinito Release).

.PARAMETER SkipTests
    Salta i test automatici (sconsigliato).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'LocalCodeIntelligenceAnalyzer.slnx'
Set-Location -LiteralPath $root

function Invoke-Checked {
    param([string]$Title, [string[]]$Arguments)
    Write-Host ''
    Write-Host "==> $Title" -ForegroundColor Cyan
    Write-Host "    dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERRORE: '$Title' non riuscito (codice $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'ERRORE: .NET SDK non trovato. Installa .NET 10 SDK: winget install --id Microsoft.DotNet.SDK.10 -e' -ForegroundColor Red
    exit 1
}
$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    Write-Host 'ERRORE: serve .NET SDK 10.x. SDK installati:' -ForegroundColor Red
    $sdks | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Invoke-Checked 'Ripristino pacchetti NuGet' @('restore', $solution)
Invoke-Checked 'Compilazione' @('build', $solution, '-c', $Configuration, '--no-restore')

if (-not $SkipTests) {
    Invoke-Checked 'Test automatici' @('test', $solution, '-c', $Configuration, '--no-build', '--logger', 'console;verbosity=normal')
}

if (-not $SkipSmokeTest) {
    $data = Join-Path ([System.IO.Path]::GetTempPath()) ("lcia-smoke-" + [guid]::NewGuid().ToString('N'))
    $exports = Join-Path $data 'exports'
    $demo = Join-Path $root 'samples\DemoShop'
    $cli = @('run', '--project', (Join-Path $root 'src\Lcia.Cli\Lcia.Cli.csproj'), '-c', $Configuration, '--no-build', '--')
    try {
        Invoke-Checked 'CLI: indicizzazione DemoShop' ($cli + @('index', $demo, '--data-dir', $data))
        Invoke-Checked 'CLI: ricerca' ($cli + @('search', $demo, 'CreateOrderAsync', '--data-dir', $data))
        Invoke-Checked 'CLI: riferimenti' ($cli + @('search', $demo, 'IOrderRepository', '--mode', 'refs', '--data-dir', $data))
        Invoke-Checked 'CLI: esportazione' ($cli + @('export', $demo, '--out', $exports, '--format', 'md', '--data-dir', $data))
        $files = @(Get-ChildItem -LiteralPath $exports -File)
        if ($files.Count -eq 0) { throw 'Nessun file esportato.' }
        $content = [System.IO.File]::ReadAllText($files[0].FullName)
        if ($content.Contains('Demo-Only-Not-A-Real-Password-123')) { throw 'La password fittizia NON è stata oscurata.' }
        if (-not $content.Contains('## FILE: `Services/OrderService.cs`')) { throw 'Il dossier non contiene OrderService.cs.' }
        Write-Host '    Esportazione verificata: delimitatori presenti, segreti oscurati.' -ForegroundColor Green
    } finally {
        Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host 'Build completata con successo.' -ForegroundColor Green
Write-Host "Avvio dai sorgenti: dotnet run --project src\Lcia.App -c $Configuration"
Write-Host 'Per installarla (menu Start, comando lcia): Installa-o-aggiorna-app.bat'
exit 0
