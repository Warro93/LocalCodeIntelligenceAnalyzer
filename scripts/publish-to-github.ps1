<#
.SYNOPSIS
    Procedura guidata completa: inizializzazione Git, creazione del repository GitHub e pubblicazione.

.DESCRIPTION
    Esegue in sequenza 01-init-git.ps1, 02-create-github-repo.ps1 (solo se "origin" non esiste)
    e 03-review-and-push.ps1. Si ferma e chiede conferma prima di ogni operazione remota o
    potenzialmente distruttiva. È sicuro da rilanciare: non duplica repository né remote.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\publish-to-github.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force

function Invoke-Step {
    param([string]$Script)
    & (Join-Path $PSScriptRoot $Script)
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        Write-LciaErr "Il passaggio $Script si è fermato (codice $LASTEXITCODE). Correggi e rilancia: gli step già completati verranno riconosciuti."
        exit $LASTEXITCODE
    }
}

Write-LciaTitle 'Local Code Intelligence Analyzer - Pubblicazione guidata su GitHub'
Write-LciaInfo 'Passi: 1) Git locale  2) repository GitHub  3) controllo, commit firmato e push.'
Write-LciaInfo 'Ogni operazione remota richiede una tua conferma esplicita.'

$global:LASTEXITCODE = 0
Invoke-Step '01-init-git.ps1'

$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
Push-Location -LiteralPath $root
try {
    $origin = Get-LciaGitValue -Arguments @('remote', 'get-url', 'origin')
} finally {
    Pop-Location
}

if ([string]::IsNullOrEmpty($origin)) {
    if (-not (Read-LciaYesNo 'Procedere con la creazione del repository su GitHub?')) { exit 0 }
    $global:LASTEXITCODE = 0
    Invoke-Step '02-create-github-repo.ps1'
} else {
    Write-LciaOk "Remote origin già configurato ($origin): creazione del repository saltata."
}

if (-not (Read-LciaYesNo 'Procedere con revisione, commit e push?')) { exit 0 }
$global:LASTEXITCODE = 0
Invoke-Step '03-review-and-push.ps1'
