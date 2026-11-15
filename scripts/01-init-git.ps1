<#
.SYNOPSIS
    Inizializza (se necessario) il repository Git locale del progetto, sul branch "main".

.DESCRIPTION
    - Verifica che Git sia installato e che la cartella sia quella del progetto.
    - Esegue "git init" solo se il repository non esiste (non sovrascrive mai repository esistenti).
    - Imposta il branch principale "main".
    - Verifica .gitignore, identità Git e firma dei commit (Verified su GitHub).
    - Mostra lo stato del repository.
    Nessuna operazione remota: non contatta GitHub.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\01-init-git.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force

Write-LciaTitle 'Local Code Intelligence Analyzer - 1. Inizializzazione Git locale'

if (-not (Test-LciaCommand 'git')) {
    Write-LciaErr 'Git non è installato o non è nel PATH.'
    Write-LciaInfo 'Installa Git per Windows con uno di questi metodi, poi riapri il terminale:'
    Write-LciaInfo '  winget install --id Git.Git -e --source winget'
    Write-LciaInfo '  oppure scarica l''installer da https://git-scm.com/download/win'
    exit 1
}
Write-LciaOk ((Get-LciaGitValue -Arguments @('--version')))

$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
Write-LciaOk "Cartella di progetto: $root"
Set-Location -LiteralPath $root

# ------------------------------------------------------------------ git init
if (Test-LciaGitRepository -Root $root) {
    Write-LciaOk 'Repository Git già presente: nessuna reinizializzazione.'
} else {
    $init = Invoke-LciaGit -Arguments @('init', '-b', 'main') -AllowFailure
    if ($init.ExitCode -ne 0) {
        # Git più vecchio di 2.28: init classico e poi branch main.
        Invoke-LciaGit -Arguments @('init') | Out-Null
        Invoke-LciaGit -Arguments @('symbolic-ref', 'HEAD', 'refs/heads/main') | Out-Null
    }
    Write-LciaOk 'Repository Git inizializzato sul branch main.'
}

# ------------------------------------------------------------------ branch main
$branch = Get-LciaGitValue -Arguments @('symbolic-ref', '--short', 'HEAD')
$hasCommits = (Invoke-LciaGit -Arguments @('rev-parse', '--verify', 'HEAD') -AllowFailure).ExitCode -eq 0
if ($branch -ne 'main') {
    if (-not $hasCommits) {
        Invoke-LciaGit -Arguments @('symbolic-ref', 'HEAD', 'refs/heads/main') | Out-Null
        Write-LciaOk 'Branch principale impostato su main.'
    } elseif (Read-LciaYesNo "Il branch corrente è '$branch'. Rinominarlo in 'main' (git branch -M main)?") {
        Invoke-LciaGit -Arguments @('branch', '-M', 'main') | Out-Null
        Write-LciaOk 'Branch rinominato in main.'
    } else {
        Write-LciaWarn "Il branch resta '$branch'. Gli script successivi pubblicano il branch corrente."
    }
} else {
    Write-LciaOk 'Branch corrente: main.'
}

# ------------------------------------------------------------------ .gitignore
$gitignore = Join-Path $root '.gitignore'
if (-not (Test-Path -LiteralPath $gitignore)) {
    Write-LciaErr '.gitignore mancante: non pubblicare finché non è stato ripristinato (vedi il pacchetto originale del progetto).'
    exit 1
}
$content = [System.IO.File]::ReadAllText($gitignore)
$required = @('[Bb]in/', '[Oo]bj/', '.vs/', '*.db', '*.sqlite', '.env', '*.pfx', 'secrets.json', 'exports/', 'TestResults/')
$missing = @($required | Where-Object { -not $content.Contains($_) })
if ($missing.Count -gt 0) {
    Write-LciaWarn ('.gitignore non contiene: ' + ($missing -join ', '))
} else {
    Write-LciaOk '.gitignore presente e completo (build, database locali, segreti, esportazioni).'
}
$check = Invoke-LciaGit -Arguments @('check-ignore', '-q', 'src/Lcia.App/bin/x.dll') -AllowFailure
if ($check.ExitCode -eq 0) { Write-LciaOk 'Verifica: le cartelle bin/ sono ignorate.' } else { Write-LciaWarn 'Le cartelle bin/ non risultano ignorate.' }

# ------------------------------------------------------------------ identità e firma
if (-not (Initialize-LciaGitIdentity -Root $root)) {
    Write-LciaWarn 'Identità Git non impostata: verrà richiesta di nuovo prima del commit (script 03).'
}

$signing = Get-LciaSigningStatus -Root $root
if ($signing.Ready) {
    Write-LciaOk "Firma dei commit attiva ($($signing.Format)): i commit risulteranno Verified su GitHub se la chiave è registrata come Signing key."
} else {
    Write-LciaWarn ('Firma dei commit non pronta: ' + ($signing.Messages -join '; ') + '.')
    Write-LciaInfo 'Senza firma i commit NON risulteranno "Verified" su GitHub.'
    Write-LciaInfo 'Lo script 03-review-and-push.ps1 può configurare la firma SSH; dettagli in docs/GITHUB_SETUP.md.'
}

# ------------------------------------------------------------------ stato
Write-LciaTitle 'Stato del repository'
(Invoke-LciaGit -Arguments @('status', '--short', '--branch')).Output | ForEach-Object { Write-LciaInfo $_ }
Write-Host ''
Write-LciaInfo 'Passo successivo: .\scripts\02-create-github-repo.ps1 (crea il repository su GitHub, senza inviare file).'
exit 0
