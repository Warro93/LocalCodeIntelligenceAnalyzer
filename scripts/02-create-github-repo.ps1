<#
.SYNOPSIS
    Crea il repository su GitHub (vuoto) e collega il remote "origin". Non invia file.

.DESCRIPTION
    - Verifica GitHub CLI (gh) e l'autenticazione (gh auth status).
    - Se non sei autenticato mostra le istruzioni per "gh auth login" (da eseguire tu, a mano).
    - Chiede nome e visibilità (pubblico/privato) e crea il repository SOLO dopo conferma esplicita.
    - Collega "origin" senza duplicare remote o repository esistenti (sicuro da rilanciare).
    - Mostra quali file verrebbero pubblicati: l'invio vero avviene solo con 03-review-and-push.ps1.
    Nessun token viene richiesto, salvato o scritto nel repository.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\02-create-github-repo.ps1
#>
[CmdletBinding()]
param(
    [string]$Description = 'Local Code Intelligence Analyzer: analisi locale di repository .NET e SQL Server con export del contesto per qualsiasi AI.'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force

Write-LciaTitle 'Local Code Intelligence Analyzer - 2. Creazione del repository GitHub'

$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
Set-Location -LiteralPath $root
if (-not (Test-LciaGitRepository -Root $root)) {
    Write-LciaErr 'Repository Git locale non inizializzato: esegui prima .\scripts\01-init-git.ps1'
    exit 1
}

# ------------------------------------------------------------------ remote già presente?
$existingOrigin = Get-LciaGitValue -Arguments @('remote', 'get-url', 'origin')
if (-not [string]::IsNullOrEmpty($existingOrigin)) {
    Write-LciaOk "Il remote origin è già configurato: $existingOrigin"
    Write-LciaInfo 'Nessuna azione necessaria. Per pubblicare: .\scripts\03-review-and-push.ps1'
    exit 0
}

# ------------------------------------------------------------------ GitHub CLI
if (-not (Test-LciaCommand 'gh')) {
    Write-LciaErr 'GitHub CLI (gh) non è installato.'
    Write-LciaInfo 'Installa con:  winget install --id GitHub.cli -e --source winget'
    Write-LciaInfo 'Poi riapri il terminale ed esegui:  gh auth login'
    Write-LciaInfo 'In alternativa crea il repository dal sito https://github.com/new (senza README, .gitignore o licenza) ed esegui:'
    Write-LciaInfo '  git remote add origin https://github.com/<tuo-utente>/<nome-repository>.git'
    exit 1
}

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& gh auth status 2>&1 | Out-Null
$authenticated = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = $previous
if (-not $authenticated) {
    Write-LciaErr 'GitHub CLI non è autenticato.'
    Write-LciaInfo 'Esegui tu, in questo terminale:  gh auth login'
    Write-LciaInfo 'Scegli: GitHub.com -> HTTPS -> "Login with a web browser". Poi rilancia questo script.'
    Write-LciaInfo 'Non incollare mai token in chat, negli script o nei file del progetto.'
    exit 1
}

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$owner = (& gh api user --jq '.login' 2>$null) | Select-Object -First 1
$ErrorActionPreference = $previous
if ([string]::IsNullOrWhiteSpace($owner)) {
    Write-LciaErr 'Impossibile leggere il nome utente GitHub (gh api user).'
    exit 1
}
$owner = $owner.Trim()
Write-LciaOk "Autenticato su GitHub come: $owner"

# ------------------------------------------------------------------ nome e visibilità
$name = ''
while ($true) {
    $name = Read-LciaText -Prompt 'Nome del repository GitHub' -Default (Split-Path -Leaf $root)
    if ($name -match '^[A-Za-z0-9._-]{1,100}$') { break }
    Write-LciaWarn 'Nome non valido: usa lettere, numeri, punto, trattino o underscore.'
}

$visibility = ''
while ($visibility -eq '') {
    $answer = (Read-LciaText -Prompt 'Visibilità: scrivi "privato" oppure "pubblico"').ToLowerInvariant()
    switch ($answer) {
        'privato' { $visibility = 'private' }
        'private' { $visibility = 'private' }
        'pubblico' { $visibility = 'public' }
        'public' { $visibility = 'public' }
        default { Write-LciaWarn 'Scelta obbligatoria: privato o pubblico.' }
    }
}

# ------------------------------------------------------------------ cosa verrà pubblicato (anteprima)
$pending = Get-LciaPendingFiles -Root $root
$tracked = @((Invoke-LciaGit -Arguments @('ls-files')).Output | Where-Object { $_ })
Write-LciaTitle 'Anteprima'
Write-LciaInfo "Repository da creare : $owner/$name ($visibility), VUOTO"
Write-LciaInfo "File già nei commit  : $($tracked.Count)"
Write-LciaInfo "File nuovi/modificati: $($pending.Count) (verranno controllati e confermati con lo script 03)"
$topLevel = @($pending | ForEach-Object { ($_.Path -split '/')[0] } | Group-Object | Sort-Object Name)
foreach ($group in $topLevel) { Write-LciaInfo ("  {0,-40} {1,5} file" -f $group.Name, $group.Count) }
Write-LciaInfo 'In questo passaggio NON viene inviato alcun file.'

$existing = $false
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& gh repo view "$owner/$name" --json name 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { $existing = $true }
$ErrorActionPreference = $previous

$url = "https://github.com/$owner/$name.git"
if ($existing) {
    Write-LciaWarn "Il repository $owner/$name esiste già su GitHub: non verrà ricreato."
    if (-not (Read-LciaYesNo "Collegarlo come remote origin ($url)?")) { Write-LciaInfo 'Nessuna modifica effettuata.'; exit 0 }
} else {
    if (-not (Read-LciaYesNo "Creare ora il repository $owner/$name ($visibility) su GitHub?")) { Write-LciaInfo 'Annullato: nessuna modifica effettuata.'; exit 0 }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & gh repo create "$owner/$name" "--$visibility" --description $Description 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous
    if ($code -ne 0) {
        Write-LciaErr ('Creazione non riuscita: ' + (($output | ForEach-Object { $_.ToString() }) -join ' '))
        exit 1
    }
    Write-LciaOk "Repository creato: https://github.com/$owner/$name"
}

Invoke-LciaGit -Arguments @('remote', 'add', 'origin', $url) | Out-Null
Write-LciaOk "Remote origin collegato: $url"
(Invoke-LciaGit -Arguments @('remote', '-v')).Output | ForEach-Object { Write-LciaInfo $_ }
Write-Host ''
Write-LciaInfo 'Passo successivo: .\scripts\03-review-and-push.ps1 (controllo, commit firmato e push con conferma).'
exit 0
