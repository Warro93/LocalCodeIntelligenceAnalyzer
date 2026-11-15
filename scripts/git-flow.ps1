<#
.SYNOPSIS
    Gestione dei branch secondo Git Flow: develop, feature/*, release/*, hotfix/*.

.DESCRIPTION
    Modello (best practice Git Flow):
      main        solo versioni pubblicate; ogni merge in main diventa una release con tag vX.Y.Z
      develop     integrazione dello sviluppo quotidiano; nasce da main ed è permanente
      feature/*   una funzionalità; nasce da develop e torna in develop
      release/*   preparazione della versione X.Y.Z; nasce da develop, va in main (con tag) e torna in develop
      hotfix/*    correzione urgente di una versione pubblicata; nasce da main, va in main (con tag) e in develop
    main e develop sono permanenti; feature, release e hotfix sono temporanei e si creano quando servono,
    con un nome preciso (es. release/0.3.0), poi si chiudono e si eliminano.

    Ogni operazione chiede conferma. I merge usano --no-ff e sono firmati (-S) se la firma è configurata.
    Nessun push forzato, nessuna eliminazione senza conferma, nessun token: l'accesso a GitHub è quello di Git/gh.

.PARAMETER Action
    init | feature | release | hotfix | finish | status. Senza parametro viene mostrato un menu.

.PARAMETER Name
    Nome della feature oppure versione della release/hotfix (facoltativo: altrimenti viene chiesto).

.EXAMPLE
    GitHub-4-branch-git-flow.bat
    powershell -ExecutionPolicy Bypass -File .\scripts\git-flow.ps1 -Action feature -Name "ricerca sql"
#>
[CmdletBinding()]
param(
    [string]$Action = '',
    [string]$Name = ''
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force

Write-LciaTitle 'Local Code Intelligence Analyzer - Branch (Git Flow)'

if (-not (Test-LciaCommand 'git')) {
    Write-LciaErr 'Git non è installato: winget install --id Git.Git -e'
    exit 1
}
$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
Set-Location -LiteralPath $root
if (-not (Test-LciaGitRepository -Root $root)) {
    Write-LciaErr 'Il progetto non è ancora un repository Git: esegui prima GitHub-1-prima-pubblicazione.bat'
    exit 1
}
if ((Invoke-LciaGit -Arguments @('rev-parse', '--verify', 'HEAD') -AllowFailure).ExitCode -ne 0) {
    Write-LciaErr 'Il repository non ha ancora commit: completa prima GitHub-1-prima-pubblicazione.bat'
    exit 1
}
$origin = Get-LciaGitValue -Arguments @('remote', 'get-url', 'origin')
$signing = Get-LciaSigningStatus -Root $root
$sign = $signing.Ready
$script:signingConfirmed = $false

# ------------------------------------------------------------------ funzioni di supporto

function Get-CurrentBranch {
    return (Get-LciaGitValue -Arguments @('symbolic-ref', '--short', 'HEAD'))
}

function Test-LocalBranch {
    param([string]$Branch)
    return ((Invoke-LciaGit -Arguments @('rev-parse', '--verify', '--quiet', "refs/heads/$Branch") -AllowFailure).ExitCode -eq 0)
}

function Test-RemoteBranch {
    param([string]$Branch)
    if ([string]::IsNullOrEmpty($origin)) { return $false }
    $result = Invoke-LciaGit -Arguments @('ls-remote', '--heads', 'origin', $Branch) -AllowFailure
    return ($result.ExitCode -eq 0 -and @($result.Output | Where-Object { $_ }).Count -gt 0)
}

function Invoke-Step {
    param([string[]]$Arguments, [string]$FailMessage)
    $result = Invoke-LciaGit -Arguments $Arguments -AllowFailure
    $result.Output | ForEach-Object { Write-LciaInfo $_ }
    if ($result.ExitCode -ne 0) {
        Write-LciaErr $FailMessage
        exit 1
    }
}

function Assert-CleanTree {
    $pending = Get-LciaPendingFiles -Root $root
    if ($pending.Count -gt 0) {
        Write-LciaErr "Ci sono $($pending.Count) file modificati non ancora salvati in un commit."
        Write-LciaInfo 'Prima salvali con GitHub-2-invia-modifiche.bat (oppure mettili da parte con: git stash), poi rilancia.'
        exit 1
    }
}

function Confirm-Signing {
    if ($sign -or $script:signingConfirmed) { return }
    Write-LciaWarn ('Firma dei commit non configurata: ' + ($signing.Messages -join '; ') + '.')
    Write-LciaInfo 'I merge non risulterebbero "Verified". Configura la firma con GitHub-2-invia-modifiche.bat.'
    if (-not (Read-LciaYesNo 'Continuare comunque senza firma?')) {
        Write-LciaInfo 'Annullato: nessuna modifica effettuata.'
        exit 0
    }
    $script:signingConfirmed = $true
}

function Switch-ToBranch {
    param([string]$Branch)
    if ((Get-CurrentBranch) -eq $Branch) { return }
    Invoke-Step -Arguments @('switch', $Branch) -FailMessage "Impossibile passare al branch $Branch."
}

function Sync-FromGitHub {
    param([string]$Branch)
    if (-not (Test-RemoteBranch $Branch)) { return }
    Write-LciaInfo "Allineamento di $Branch con GitHub (git pull --ff-only)..."
    Invoke-Step -Arguments @('pull', '--ff-only', 'origin', $Branch) `
        -FailMessage "Il branch $Branch locale e quello su GitHub sono divergenti: allineali (git pull --rebase origin $Branch) e rilancia."
}

function Merge-Branch {
    param([string]$Source, [string]$Message)
    $arguments = @('merge', '--no-ff')
    if ($sign) { $arguments += '-S' }
    $arguments += @('-m', $Message, $Source)
    $result = Invoke-LciaGit -Arguments $arguments -AllowFailure
    $result.Output | ForEach-Object { Write-LciaInfo $_ }
    if ($result.ExitCode -ne 0) {
        Write-LciaErr "Merge di $Source non riuscito (probabili conflitti)."
        Write-LciaInfo 'Risolvi i conflitti (Visual Studio o VS Code), poi: git add . e git commit'
        Write-LciaInfo 'oppure annulla il merge con: git merge --abort. Poi rilancia questo script.'
        exit 1
    }
    Write-LciaOk "Merge completato: $Message"
}

function Publish-Branch {
    param([string]$Branch)
    if ([string]::IsNullOrEmpty($origin)) {
        Write-LciaInfo "Remote origin non configurato: $Branch resta solo in locale."
        return $false
    }
    if (-not (Read-LciaYesNo "Inviare $Branch su GitHub (git push -u origin $Branch)?" -DefaultYes)) {
        Write-LciaInfo "$Branch non inviato: potrai farlo più tardi con GitHub-2-invia-modifiche.bat."
        return $false
    }
    Invoke-Step -Arguments @('push', '-u', 'origin', $Branch) -FailMessage "Invio di $Branch non riuscito: verifica l'accesso (gh auth status)."
    Write-LciaOk "$Branch inviato su GitHub."
    return $true
}

function Remove-FinishedBranch {
    param([string]$Branch)
    if (Read-LciaYesNo "Eliminare il branch locale $Branch (già unito)?" -DefaultYes) {
        Invoke-Step -Arguments @('branch', '-d', $Branch) -FailMessage "Il branch $Branch non risulta unito: non eliminato."
        Write-LciaOk "Branch locale $Branch eliminato."
    }
    if ((Test-RemoteBranch $Branch) -and (Read-LciaYesNo "Eliminare anche $Branch su GitHub?" -DefaultYes)) {
        Invoke-Step -Arguments @('push', 'origin', '--delete', $Branch) -FailMessage "Eliminazione di $Branch su GitHub non riuscita."
        Write-LciaOk "Branch $Branch eliminato su GitHub."
    }
}

function Assert-Develop {
    if (Test-LocalBranch 'develop') { return }
    Write-LciaErr 'Il branch develop non esiste ancora: scegli prima "Inizializza Git Flow".'
    exit 1
}

function Set-VersionWithCommit {
    param([string]$Version)
    $current = Get-LciaProjectVersion -Root $root
    if ($current -eq $Version) {
        Write-LciaOk "Directory.Build.props è già alla versione $Version."
        return
    }
    if (-not (Read-LciaYesNo "Impostare la versione $Version in Directory.Build.props (ora $current) e creare il commit?" -DefaultYes)) {
        Write-LciaWarn "Versione non aggiornata: ricordati di impostarla a $Version prima di chiudere il branch."
        return
    }
    if (-not (Initialize-LciaGitIdentity -Root $root)) { exit 0 }
    [void](Set-LciaProjectVersion -Root $root -Version $Version)
    Invoke-Step -Arguments @('add', 'Directory.Build.props') -FailMessage 'git add non riuscito.'
    $message = "Versione $Version"
    if (Set-LciaChangelogRelease -Path (Join-Path $root 'CHANGELOG.md') -Version $Version) {
        Invoke-Step -Arguments @('add', 'CHANGELOG.md') -FailMessage 'git add non riuscito.'
        $message = "Versione $Version (CHANGELOG: note 'Non rilasciato' assegnate alla versione)"
        Write-LciaOk "CHANGELOG.md: la sezione 'Non rilasciato' è diventata '## [$Version]'."
    }
    $arguments = @('commit')
    if ($sign) { $arguments += '-S' }
    $arguments += @('-m', $message)
    Invoke-Step -Arguments $arguments -FailMessage 'Commit della nuova versione non riuscito.'
    Write-LciaOk "Versione impostata a $Version (commit creato)."
}

function Read-Version {
    param([string]$Suggested, [string]$Kind)
    $value = $Name
    while (-not (Test-LciaSemVer $value)) {
        if (-not [string]::IsNullOrWhiteSpace($value)) { Write-LciaWarn 'Formato non valido: usa X.Y.Z (es. 0.3.0).' }
        $value = Read-LciaText -Prompt "Versione della $Kind (X.Y.Z)" -Default $Suggested
        $value = $value.TrimStart('v', 'V')
    }
    if ((Invoke-LciaGit -Arguments @('rev-parse', '-q', '--verify', "refs/tags/v$value") -AllowFailure).ExitCode -eq 0) {
        Write-LciaErr "Il tag v$value esiste già: scegli una versione nuova."
        exit 1
    }
    return $value
}

function Show-Model {
    Write-LciaInfo 'main       versioni pubblicate (ogni merge = release con tag)'
    Write-LciaInfo 'develop    sviluppo quotidiano (nasce da main)'
    Write-LciaInfo 'feature/*  una funzionalità: da develop a develop'
    Write-LciaInfo 'release/*  preparazione di una versione: da develop a main (+ tag) e develop'
    Write-LciaInfo 'hotfix/*   correzione urgente: da main a main (+ tag) e develop'
}

# ------------------------------------------------------------------ azioni

function Invoke-Init {
    Assert-CleanTree
    if (-not (Test-LocalBranch 'main')) {
        Write-LciaErr 'Branch main non trovato: Git Flow richiede main come branch delle versioni pubblicate.'
        exit 1
    }
    if (Test-LocalBranch 'develop') {
        Write-LciaOk 'Il branch develop esiste già.'
    } elseif (Test-RemoteBranch 'develop') {
        Write-LciaInfo 'develop esiste su GitHub: lo scarico e lo collego.'
        Invoke-Step -Arguments @('fetch', 'origin', 'develop') -FailMessage 'Download di develop non riuscito.'
        Invoke-Step -Arguments @('branch', '--track', 'develop', 'origin/develop') -FailMessage 'Creazione di develop non riuscita.'
        Write-LciaOk 'develop creato da origin/develop.'
    } else {
        if (-not (Read-LciaYesNo 'Creare il branch develop a partire da main?' -DefaultYes)) {
            Write-LciaInfo 'Annullato: nessuna modifica effettuata.'
            exit 0
        }
        Invoke-Step -Arguments @('branch', 'develop', 'main') -FailMessage 'Creazione di develop non riuscita.'
        Write-LciaOk 'develop creato da main.'
    }
    if (-not (Test-RemoteBranch 'develop')) { [void](Publish-Branch 'develop') }
    if ((Get-CurrentBranch) -ne 'develop' -and (Read-LciaYesNo 'Passare ora a develop (dove si lavora ogni giorno)?' -DefaultYes)) {
        Switch-ToBranch 'develop'
    }
    Write-LciaTitle 'Git Flow pronto'
    Show-Model
    Write-LciaInfo 'Su GitHub il branch predefinito resta main. I branch release/* e hotfix/* si creano solo quando servono.'
}

function Invoke-Feature {
    Assert-CleanTree
    Assert-Develop
    $text = $Name
    $slug = ConvertTo-LciaBranchSlug $text
    while ([string]::IsNullOrEmpty($slug)) {
        $text = Read-LciaText -Prompt 'Nome della funzionalità (es. ricerca sql)'
        $slug = ConvertTo-LciaBranchSlug $text
    }
    $branch = "feature/$slug"
    if (Test-LocalBranch $branch) {
        Write-LciaErr "Il branch $branch esiste già."
        exit 1
    }
    if (-not (Read-LciaYesNo "Creare $branch a partire da develop?" -DefaultYes)) { Write-LciaInfo 'Annullato.'; exit 0 }
    Switch-ToBranch 'develop'
    Sync-FromGitHub 'develop'
    Invoke-Step -Arguments @('switch', '-c', $branch) -FailMessage "Creazione di $branch non riuscita."
    Write-LciaOk "Sei su $branch."
    Write-LciaInfo 'Lavora qui; salva e invia con GitHub-2-invia-modifiche.bat; alla fine scegli "Chiudi il branch corrente".'
}

function Invoke-Release {
    Assert-CleanTree
    Assert-Develop
    $existing = @((Invoke-LciaGit -Arguments @('for-each-ref', '--format=%(refname:short)', 'refs/heads/release')).Output | Where-Object { $_ })
    if ($existing.Count -gt 0) {
        Write-LciaWarn ('Esiste già un branch di release: ' + ($existing -join ', ') + '. Di norma se ne prepara una alla volta.')
        if (-not (Read-LciaYesNo 'Crearne comunque un altro?')) { exit 0 }
    }
    Switch-ToBranch 'develop'
    Sync-FromGitHub 'develop'
    $current = Get-LciaProjectVersion -Root $root
    $version = Read-Version -Suggested (Get-LciaNextVersion -Version $current -Part 'minor') -Kind 'release'
    $branch = "release/$version"
    if (Test-LocalBranch $branch) { Write-LciaErr "Il branch $branch esiste già."; exit 1 }
    if (-not (Read-LciaYesNo "Creare $branch a partire da develop?" -DefaultYes)) { Write-LciaInfo 'Annullato.'; exit 0 }
    Confirm-Signing
    Invoke-Step -Arguments @('switch', '-c', $branch) -FailMessage "Creazione di $branch non riuscita."
    Write-LciaOk "Sei su $branch."
    Set-VersionWithCommit -Version $version
    Write-LciaTitle "Prossimi passi per la release $version"
    Write-LciaInfo "1. In CHANGELOG.md aggiungi la sezione '## [$version] - <data>' con le novità (diventa il testo della release)."
    Write-LciaInfo '2. Solo correzioni e rifiniture su questo branch; salva con GitHub-2-invia-modifiche.bat.'
    Write-LciaInfo '3. Scegli "Chiudi il branch corrente": merge in main, tag firmato, release su GitHub, merge in develop.'
}

function Invoke-Hotfix {
    Assert-CleanTree
    if (-not (Read-LciaYesNo 'Un hotfix corregge la versione pubblicata e parte da main. Continuare?' -DefaultYes)) { exit 0 }
    Switch-ToBranch 'main'
    Sync-FromGitHub 'main'
    $current = Get-LciaProjectVersion -Root $root
    $version = Read-Version -Suggested (Get-LciaNextVersion -Version $current -Part 'patch') -Kind 'hotfix'
    $branch = "hotfix/$version"
    if (Test-LocalBranch $branch) { Write-LciaErr "Il branch $branch esiste già."; exit 1 }
    if (-not (Read-LciaYesNo "Creare $branch a partire da main?" -DefaultYes)) { Write-LciaInfo 'Annullato.'; exit 0 }
    Confirm-Signing
    Invoke-Step -Arguments @('switch', '-c', $branch) -FailMessage "Creazione di $branch non riuscita."
    Write-LciaOk "Sei su $branch."
    Set-VersionWithCommit -Version $version
    Write-LciaTitle "Prossimi passi per l'hotfix $version"
    Write-LciaInfo '1. Correggi il problema e salva con GitHub-2-invia-modifiche.bat.'
    Write-LciaInfo "2. In CHANGELOG.md aggiungi la sezione '## [$version] - <data>' con la correzione."
    Write-LciaInfo '3. Scegli "Chiudi il branch corrente": merge in main, tag firmato, release su GitHub, merge in develop.'
}

function Select-BranchToFinish {
    $current = Get-CurrentBranch
    if ((Get-LciaBranchKind $current) -in @('feature', 'release', 'hotfix')) { return $current }
    $candidates = @((Invoke-LciaGit -Arguments @('for-each-ref', '--format=%(refname:short)', 'refs/heads/feature', 'refs/heads/release', 'refs/heads/hotfix')).Output | Where-Object { $_ })
    if ($candidates.Count -eq 0) {
        Write-LciaInfo 'Nessun branch feature/release/hotfix da chiudere.'
        exit 0
    }
    Write-LciaInfo 'Branch da chiudere:'
    for ($i = 0; $i -lt $candidates.Count; $i++) { Write-LciaInfo ("  {0}. {1}" -f ($i + 1), $candidates[$i]) }
    $answer = Read-LciaText -Prompt 'Numero del branch (Invio per annullare)'
    $index = 0
    if (-not [int]::TryParse($answer, [ref]$index) -or $index -lt 1 -or $index -gt $candidates.Count) {
        Write-LciaInfo 'Annullato.'
        exit 0
    }
    return $candidates[$index - 1]
}

function Invoke-Finish {
    Assert-CleanTree
    $branch = Select-BranchToFinish
    $kind = Get-LciaBranchKind $branch
    Write-LciaInfo "Branch da chiudere: $branch ($kind)"

    if ($kind -eq 'feature') {
        Assert-Develop
        if (-not (Read-LciaYesNo "Unire $branch in develop (merge --no-ff)?" -DefaultYes)) { Write-LciaInfo 'Annullato.'; exit 0 }
        Confirm-Signing
        Switch-ToBranch 'develop'
        Sync-FromGitHub 'develop'
        Merge-Branch -Source $branch -Message "Merge $branch in develop"
        [void](Publish-Branch 'develop')
        Remove-FinishedBranch $branch
        Write-LciaOk "Feature chiusa: sei su develop."
        return
    }

    # release o hotfix: la versione è nel nome del branch
    $version = $branch.Substring($branch.IndexOf('/') + 1)
    if (-not (Test-LciaSemVer $version)) {
        Write-LciaErr "Il nome $branch non contiene una versione X.Y.Z."
        exit 1
    }
    Assert-Develop
    Switch-ToBranch $branch
    $changelog = Join-Path $root 'CHANGELOG.md'
    $notes = Get-LciaChangelogSection -Path $changelog -Version $version
    $unreleased = Get-LciaChangelogSection -Path $changelog -Version 'Non rilasciato'
    if ($notes.Count -eq 0 -and $unreleased.Count -gt 0 -and
        (Read-LciaYesNo "CHANGELOG.md non ha ancora la sezione ${version}: usare le note 'Non rilasciato'?" -DefaultYes)) {
        if (-not (Initialize-LciaGitIdentity -Root $root)) { exit 0 }
        [void](Set-LciaChangelogRelease -Path $changelog -Version $version)
        Invoke-Step -Arguments @('add', 'CHANGELOG.md') -FailMessage 'git add non riuscito.'
        $arguments = @('commit')
        if ($sign) { $arguments += '-S' }
        $arguments += @('-m', "CHANGELOG: note della versione $version")
        Invoke-Step -Arguments $arguments -FailMessage 'Commit del CHANGELOG non riuscito.'
        $notes = Get-LciaChangelogSection -Path $changelog -Version $version
    }
    if ($notes.Count -eq 0) {
        Write-LciaErr "CHANGELOG.md non contiene la sezione '## [$version]' con le novità."
        Write-LciaInfo 'Aggiungila su questo branch, salvala con GitHub-2-invia-modifiche.bat e rilancia.'
        exit 1
    }
    if ((Get-LciaProjectVersion -Root $root) -ne $version) {
        Set-VersionWithCommit -Version $version
        if ((Get-LciaProjectVersion -Root $root) -ne $version) { exit 1 }
    }
    $label = 'Release'
    if ($kind -eq 'hotfix') { $label = 'Hotfix' }
    Write-LciaTitle "$label $version"
    Write-LciaInfo 'Verranno eseguiti, ognuno con conferma:'
    Write-LciaInfo "  1. merge di $branch in main e invio su GitHub"
    Write-LciaInfo "  2. tag firmato v$version (GitHub Actions pubblica la release con lo zip win-x64)"
    Write-LciaInfo "  3. merge di $branch in develop e invio su GitHub"
    Write-LciaInfo "  4. eliminazione di $branch"
    if (-not (Read-LciaYesNo 'Procedere?' -DefaultYes)) { Write-LciaInfo 'Annullato.'; exit 0 }
    Confirm-Signing

    Switch-ToBranch 'main'
    Sync-FromGitHub 'main'
    Merge-Branch -Source $branch -Message "$label $version"
    $mainPublished = Publish-Branch 'main'

    if ($mainPublished) {
        Write-LciaTitle "Tag v$version"
        $global:LASTEXITCODE = 0
        & (Join-Path $PSScriptRoot 'create-release.ps1')
        if ($LASTEXITCODE -ne 0) {
            Write-LciaWarn 'Tag non creato: potrai crearlo più tardi da main con GitHub-3-crea-release.bat.'
        }
        Set-Location -LiteralPath $root
    } else {
        Write-LciaWarn 'main non inviato: il tag va creato dopo l''invio, con GitHub-3-crea-release.bat.'
    }

    Switch-ToBranch 'develop'
    Sync-FromGitHub 'develop'
    Merge-Branch -Source $branch -Message "Merge $branch in develop"
    [void](Publish-Branch 'develop')
    Remove-FinishedBranch $branch
    Write-LciaOk "$label $version chiuso: sei su develop."
}

function Invoke-Status {
    Write-LciaInfo ('Branch corrente: ' + (Get-CurrentBranch))
    Write-LciaTitle 'Branch locali (git branch -vv)'
    (Invoke-LciaGit -Arguments @('branch', '-vv')).Output | ForEach-Object { Write-LciaInfo $_ }
    if (-not [string]::IsNullOrEmpty($origin)) {
        Write-LciaTitle 'Branch su GitHub'
        $remote = Invoke-LciaGit -Arguments @('ls-remote', '--heads', 'origin') -AllowFailure
        if ($remote.ExitCode -eq 0) {
            $remote.Output | Where-Object { $_ } | ForEach-Object { Write-LciaInfo ($_ -replace '^\S+\s+refs/heads/', '') }
        } else {
            Write-LciaWarn 'GitHub non raggiungibile.'
        }
    }
    Write-LciaTitle 'Modello'
    Show-Model
}

# ------------------------------------------------------------------ menu

if ([string]::IsNullOrWhiteSpace($Action)) {
    Write-LciaInfo ('Branch corrente: ' + (Get-CurrentBranch))
    Write-Host ''
    Show-Model
    Write-Host ''
    Write-LciaInfo '1. Inizializza Git Flow (crea develop da main e lo invia su GitHub)'
    Write-LciaInfo '2. Nuova feature        (feature/<nome> da develop)'
    Write-LciaInfo '3. Nuova release        (release/<versione> da develop)'
    Write-LciaInfo '4. Nuovo hotfix         (hotfix/<versione> da main)'
    Write-LciaInfo '5. Chiudi il branch corrente (merge, tag se release/hotfix, pulizia)'
    Write-LciaInfo '6. Stato dei branch'
    Write-LciaInfo '0. Esci'
    $choice = Read-LciaText -Prompt 'Scelta' -Default '0'
    switch ($choice) {
        '1' { $Action = 'init' }
        '2' { $Action = 'feature' }
        '3' { $Action = 'release' }
        '4' { $Action = 'hotfix' }
        '5' { $Action = 'finish' }
        '6' { $Action = 'status' }
        default { exit 0 }
    }
}

switch ($Action.ToLowerInvariant()) {
    'init' { Invoke-Init }
    'feature' { Invoke-Feature }
    'release' { Invoke-Release }
    'hotfix' { Invoke-Hotfix }
    'finish' { Invoke-Finish }
    'status' { Invoke-Status }
    default {
        Write-LciaErr "Azione sconosciuta: $Action (usa init, feature, release, hotfix, finish, status)."
        exit 2
    }
}
exit 0
