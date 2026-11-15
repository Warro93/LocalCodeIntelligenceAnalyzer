<#
.SYNOPSIS
    Controlla i file da pubblicare, crea un commit (firmato se configurato) e lo invia a GitHub,
    chiedendo conferma esplicita prima di ogni passaggio.

.DESCRIPTION
    1. Mostra git status e l'elenco dei file nuovi/modificati/eliminati.
    2. BLOCCA la pubblicazione se trova output di build, database locali, log, esportazioni,
       chiavi/certificati, file troppo grandi o possibili segreti (token, chiavi private,
       password in connection string). I dati fittizi di test sono elencati in
       scripts\secret-scan-allowlist.txt.
    3. Verifica la firma dei commit (necessaria per lo stato "Verified" su GitHub) e, se vuoi,
       configura la firma SSH solo per questo repository.
    4. Chiede conferma per: git add, git commit, git push. Nessun push senza conferma.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\03-review-and-push.ps1
#>
[CmdletBinding()]
param(
    [string]$Message
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force

Write-LciaTitle 'Local Code Intelligence Analyzer - 3. Revisione, commit e push'

$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
Set-Location -LiteralPath $root
if (-not (Test-LciaGitRepository -Root $root)) {
    Write-LciaErr 'Repository Git locale non inizializzato: esegui prima .\scripts\01-init-git.ps1'
    exit 1
}

$origin = Get-LciaGitValue -Arguments @('remote', 'get-url', 'origin')
if ([string]::IsNullOrEmpty($origin)) {
    Write-LciaErr 'Remote origin non configurato: esegui prima .\scripts\02-create-github-repo.ps1'
    exit 1
}
$branch = Get-LciaGitValue -Arguments @('symbolic-ref', '--short', 'HEAD')
Write-LciaOk "Branch: $branch  ->  $origin"

# ------------------------------------------------------------------ 1. stato
Write-LciaTitle 'git status'
(Invoke-LciaGit -Arguments @('status', '--short', '--branch')).Output | ForEach-Object { Write-LciaInfo $_ }

$pending = Get-LciaPendingFiles -Root $root
$hasCommits = (Invoke-LciaGit -Arguments @('rev-parse', '--verify', 'HEAD') -AllowFailure).ExitCode -eq 0
if ($pending.Count -eq 0) {
    Write-LciaOk 'Nessun file da aggiungere al commit.'
}

# ------------------------------------------------------------------ 2. controlli
if ($pending.Count -gt 0) {
    Write-LciaTitle "File che verranno inclusi nel commit ($($pending.Count))"
    foreach ($entry in $pending) {
        $label = switch -Regex ($entry.Status) {
            '\?\?' { 'nuovo     '; break }
            'D' { 'eliminato '; break }
            'R' { 'rinominato'; break }
            default { 'modificato' }
        }
        Write-LciaInfo "$label  $($entry.Path)"
    }

    $existingPaths = @($pending | Where-Object { $_.Status -notmatch 'D' } | ForEach-Object { $_.Path })

    Write-LciaTitle 'Controllo file da non pubblicare'
    $forbidden = Find-LciaForbiddenFiles -RelativePaths $existingPaths -Root $root
    if ($forbidden.Count -gt 0) {
        foreach ($item in $forbidden) { Write-LciaErr "$($item.Path): $($item.Reason)" }
        Write-LciaErr 'Pubblicazione BLOCCATA: rimuovi questi file o aggiungili a .gitignore, poi rilancia lo script.'
        exit 2
    }
    Write-LciaOk 'Nessun output di build, database, log, esportazione, chiave o file troppo grande.'

    Write-LciaTitle 'Controllo segreti'
    $secrets = Find-LciaSecrets -RelativePaths $existingPaths -Root $root
    if ($secrets.Count -gt 0) {
        foreach ($item in $secrets) { Write-LciaErr "$($item.Path):$($item.Line)  possibile $($item.Rule)" }
        Write-LciaErr 'Pubblicazione BLOCCATA: rimuovi i segreti (usa variabili d''ambiente o User Secrets).'
        Write-LciaInfo 'Solo se si tratta di dati FITTIZI di test, aggiungi il percorso a scripts\secret-scan-allowlist.txt.'
        exit 3
    }
    Write-LciaOk 'Nessun possibile segreto trovato.'
}

# ------------------------------------------------------------------ 3. identità e firma
if ($pending.Count -gt 0) {
    Write-LciaTitle 'Autore dei commit (identità Git)'
    if (-not (Initialize-LciaGitIdentity -Root $root)) {
        Write-LciaInfo 'Annullato: senza nome ed email Git non è possibile creare il commit. Nessuna modifica inviata.'
        exit 0
    }
}

Write-LciaTitle 'Firma dei commit (Verified su GitHub)'
$signing = Get-LciaSigningStatus -Root $root
$sign = $signing.Ready
if ($sign) {
    Write-LciaOk "Firma attiva ($($signing.Format)): chiave $($signing.SigningKey)"
} else {
    Write-LciaWarn ('Firma non pronta: ' + ($signing.Messages -join '; ') + '.')
    $defaultKey = Join-Path $HOME '.ssh\id_ed25519.pub'
    if (Read-LciaYesNo 'Configurare ora la firma SSH SOLO per questo repository?') {
        $keyPath = Read-LciaText -Prompt 'Percorso della chiave pubblica SSH' -Default $defaultKey
        if (-not (Test-Path -LiteralPath $keyPath)) {
            Write-LciaErr "Chiave non trovata: $keyPath"
            Write-LciaInfo 'Creane una con:  ssh-keygen -t ed25519 -C "tua-email-github"'
            Write-LciaInfo 'Poi registrala su GitHub come "Signing key" (vedi docs/GITHUB_SETUP.md) e rilancia lo script.'
            exit 4
        }
        Set-LciaSshSigning -Root $root -PublicKeyPath $keyPath
        Write-LciaOk 'Firma SSH configurata (git config --local).'
        Write-LciaInfo 'Ricorda: la stessa chiave deve essere registrata su GitHub come "Signing key":'
        Write-LciaInfo "  gh ssh-key add `"$keyPath`" --type signing --title `"LCIA signing key`""
        Write-LciaInfo '  (se richiesto: gh auth refresh -h github.com -s admin:ssh_signing_key)'
        $sign = $true
    } elseif (-not (Read-LciaYesNo 'Continuare SENZA firma? I commit non risulteranno Verified.')) {
        Write-LciaInfo 'Annullato: nessuna modifica effettuata.'
        exit 0
    }
}

# ------------------------------------------------------------------ 4. add / commit
if ($pending.Count -gt 0) {
    if (-not (Read-LciaYesNo "Eseguire git add per i $($pending.Count) file elencati?")) {
        Write-LciaInfo 'Annullato: nessuna modifica effettuata.'
        exit 0
    }
    Invoke-LciaGit -Arguments @('add', '--all') | Out-Null
    Write-LciaTitle 'Riepilogo delle modifiche in stage'
    (Invoke-LciaGit -Arguments @('diff', '--cached', '--stat')).Output | Select-Object -Last 25 | ForEach-Object { Write-LciaInfo $_ }

    if ([string]::IsNullOrWhiteSpace($Message)) {
        $default = if ($hasCommits) { '' } else { 'Fase 1-2: architettura, documentazione, script GitHub e MVP (scanner, analisi C#, indice, ricerca, export)' }
        $Message = ''
        while ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = Read-LciaText -Prompt 'Messaggio del commit' -Default $default
        }
    }

    if (-not (Read-LciaYesNo "Creare il commit $(if ($sign) { 'FIRMATO' } else { 'NON firmato' }) con messaggio: `"$Message`"?")) {
        Write-LciaWarn 'Commit annullato: i file restano in stage (git restore --staged . per annullare).'
        exit 0
    }
    $commitArgs = @('commit', '-m', $Message)
    if ($sign) { $commitArgs = @('commit', '-S', '-m', $Message) }
    $commit = Invoke-LciaGit -Arguments $commitArgs -AllowFailure
    $commit.Output | ForEach-Object { Write-LciaInfo $_ }
    if ($commit.ExitCode -ne 0) {
        Write-LciaErr 'Commit non riuscito (vedi messaggi sopra). Se il problema è la firma, controlla docs/GITHUB_SETUP.md.'
        exit 5
    }
    if (Test-LciaHeadSigned -Root $root) { Write-LciaOk 'Commit firmato.' } else { Write-LciaWarn 'Il commit NON è firmato.' }
    $hasCommits = $true
}

if (-not $hasCommits) {
    Write-LciaInfo 'Nessun commit da pubblicare.'
    exit 0
}

# ------------------------------------------------------------------ 5. push
$upstream = Get-LciaGitValue -Arguments @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}')
if (-not [string]::IsNullOrEmpty($upstream)) {
    $ahead = Get-LciaGitValue -Arguments @('rev-list', '--count', "$upstream..HEAD")
    if ($ahead -eq '0') {
        Write-LciaOk "Il branch è già allineato con ${upstream}: niente da inviare."
        exit 0
    }
    Write-LciaInfo "Commit da inviare: $ahead"
}

Write-LciaTitle 'Commit da inviare'
(Invoke-LciaGit -Arguments @('log', '--oneline', '-n', '10')).Output | ForEach-Object { Write-LciaInfo $_ }
if (-not (Read-LciaYesNo "Eseguire git push verso origin/${branch}?")) {
    Write-LciaInfo 'Push annullato: i commit restano solo in locale.'
    exit 0
}

$push = Invoke-LciaGit -Arguments @('push', '-u', 'origin', $branch) -AllowFailure
$push.Output | ForEach-Object { Write-LciaInfo $_ }
if ($push.ExitCode -ne 0) {
    Write-LciaErr 'Push non riuscito. Verifica autenticazione (gh auth status) e permessi sul repository.'
    exit 6
}

$webUrl = ConvertTo-LciaGitHubWebUrl -RemoteUrl $origin
if (-not $webUrl) { $webUrl = $origin -replace '\.git$', '' }
Write-LciaOk "Pubblicazione completata: $webUrl"
Write-LciaInfo 'Su GitHub, nella pagina dei commit, verifica la dicitura "Verified".'
exit 0
