<#
.SYNOPSIS
    Pubblica una release su GitHub: crea il tag firmato vX.Y.Z della versione corrente e lo invia.
    La compilazione, i test e lo zip win-x64 allegato alla release li esegue GitHub Actions (release.yml).

.DESCRIPTION
    Controlli prima del tag:
    - repository Git con remote "origin", nessuna modifica in sospeso, branch allineato con GitHub;
    - sezione "## [X.Y.Z]" presente in CHANGELOG.md (diventa il testo della release);
    - tag non ancora esistente né in locale né su GitHub.
    Chiede conferma prima di creare il tag e prima di inviarlo. Nessun token richiesto.

.EXAMPLE
    GitHub-3-crea-release.bat
    powershell -ExecutionPolicy Bypass -File .\scripts\create-release.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force

Write-LciaTitle 'Local Code Intelligence Analyzer - Release su GitHub'

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
$origin = Get-LciaGitValue -Arguments @('remote', 'get-url', 'origin')
if ([string]::IsNullOrEmpty($origin)) {
    Write-LciaErr 'Remote origin non configurato: esegui prima GitHub-1-prima-pubblicazione.bat'
    exit 1
}
$web = ConvertTo-LciaGitHubWebUrl -RemoteUrl $origin

$version = Get-LciaProjectVersion -Root $root
$tag = "v$version"
Write-LciaInfo "Versione : $version  (tag $tag)"
Write-LciaInfo "GitHub   : $(if ($web) { $web } else { $origin })"

# ------------------------------------------------------------------ controlli
$pending = Get-LciaPendingFiles -Root $root
if ($pending.Count -gt 0) {
    Write-LciaErr "Ci sono $($pending.Count) file modificati non ancora inviati: esegui prima GitHub-2-invia-modifiche.bat"
    exit 1
}

$notes = Get-LciaChangelogSection -Path (Join-Path $root 'CHANGELOG.md') -Version $version
if ($notes.Count -eq 0) {
    Write-LciaErr "CHANGELOG.md non contiene la sezione '## [$version]': aggiungila (diventa il testo della release)."
    exit 1
}

if ((Invoke-LciaGit -Arguments @('rev-parse', '-q', '--verify', "refs/tags/$tag") -AllowFailure).ExitCode -eq 0) {
    Write-LciaErr "Il tag $tag esiste già in locale. Per una nuova release aumenta la versione in Directory.Build.props."
    Write-LciaInfo "(Se il tag locale è un tentativo non inviato puoi eliminarlo con: git tag -d $tag)"
    exit 1
}

Write-LciaInfo 'Lettura dello stato su GitHub (git fetch, sola lettura)...'
$fetch = Invoke-LciaGit -Arguments @('fetch', '--quiet', 'origin') -AllowFailure
if ($fetch.ExitCode -ne 0) {
    Write-LciaErr ('Impossibile contattare GitHub: ' + ($fetch.Output -join ' '))
    Write-LciaInfo 'Verifica la connessione e l''accesso (gh auth status), poi rilancia.'
    exit 1
}
$remoteTag = Invoke-LciaGit -Arguments @('ls-remote', '--tags', 'origin', "refs/tags/$tag") -AllowFailure
if ($remoteTag.ExitCode -eq 0 -and (@($remoteTag.Output | Where-Object { $_ }).Count -gt 0)) {
    Write-LciaErr "La release $tag esiste già su GitHub. Aumenta la versione in Directory.Build.props e in CHANGELOG.md."
    exit 1
}

$branch = Get-LciaGitValue -Arguments @('symbolic-ref', '--short', 'HEAD')
if ($branch -ne 'main') { Write-LciaWarn "Sei sul branch '$branch' (di solito le release partono da main)." }
$upstream = Get-LciaGitValue -Arguments @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}')
if ([string]::IsNullOrEmpty($upstream)) {
    Write-LciaErr "Il branch '$branch' non è ancora stato inviato su GitHub: esegui prima GitHub-2-invia-modifiche.bat"
    exit 1
}
$ahead = Get-LciaGitValue -Arguments @('rev-list', '--count', "$upstream..HEAD")
$behind = Get-LciaGitValue -Arguments @('rev-list', '--count', "HEAD..$upstream")
if ($ahead -ne '0') {
    Write-LciaErr "Ci sono $ahead commit locali non ancora inviati: esegui prima GitHub-2-invia-modifiche.bat"
    exit 1
}
if ($behind -ne '0') {
    Write-LciaErr "GitHub ha $behind commit che non hai in locale: allinea con 'git pull --rebase' prima della release."
    exit 1
}
Write-LciaOk "Branch $branch allineato con GitHub."

# ------------------------------------------------------------------ firma
$signing = Get-LciaSigningStatus -Root $root
$tagArgs = @('tag', '-a', $tag, '-m', "Local Code Intelligence Analyzer $version")
if ($signing.Ready) {
    $tagArgs = @('tag', '-s', $tag, '-m', "Local Code Intelligence Analyzer $version")
    Write-LciaOk "Il tag verrà firmato ($($signing.Format))."
} else {
    Write-LciaWarn ('Firma non configurata: ' + ($signing.Messages -join '; ') + '.')
    Write-LciaInfo 'Il tag sarebbe NON firmato (i commit restano come sono). Per configurare la firma: GitHub-2-invia-modifiche.bat.'
    if (-not (Read-LciaYesNo 'Continuare con un tag non firmato?')) { Write-LciaInfo 'Annullato.'; exit 0 }
}

# ------------------------------------------------------------------ riepilogo e conferme
Write-LciaTitle "Release $tag"
Write-LciaInfo ('Commit: ' + (Get-LciaGitValue -Arguments @('log', '-1', '--format=%h %s')))
Write-LciaInfo 'Note di rilascio (da CHANGELOG.md):'
$notes | Select-Object -First 25 | ForEach-Object { Write-LciaInfo "    $_" }
if ($notes.Count -gt 25) { Write-LciaInfo "    ... ($($notes.Count - 25) righe in più)" }

if (-not (Read-LciaYesNo "Creare il tag ${tag}?")) { Write-LciaInfo 'Annullato: nessuna modifica effettuata.'; exit 0 }
$created = Invoke-LciaGit -Arguments $tagArgs -AllowFailure
if ($created.ExitCode -ne 0) {
    $created.Output | ForEach-Object { Write-LciaErr $_ }
    Write-LciaErr 'Creazione del tag non riuscita (se il problema è la firma, vedi docs/GITHUB_SETUP.md).'
    exit 1
}
Write-LciaOk "Tag $tag creato in locale."

if (-not (Read-LciaYesNo "Inviare il tag $tag su GitHub? Avvierà la compilazione e la pubblicazione della release.")) {
    Write-LciaInfo "Tag NON inviato. Per inviarlo più tardi rilancia questo script dopo averlo eliminato (git tag -d $tag),"
    Write-LciaInfo "oppure esegui: git push origin $tag"
    exit 0
}
$push = Invoke-LciaGit -Arguments @('push', 'origin', $tag) -AllowFailure
$push.Output | ForEach-Object { Write-LciaInfo $_ }
if ($push.ExitCode -ne 0) {
    Write-LciaErr 'Invio del tag non riuscito. Verifica l''accesso (gh auth status) e rilancia: il tag locale resta.'
    exit 1
}

Write-LciaOk "Tag $tag inviato. GitHub Actions ora compila, esegue i test e crea la release con lo zip win-x64."
if ($web) {
    Write-LciaInfo "Avanzamento : $web/actions"
    Write-LciaInfo "Release     : $web/releases/tag/$tag  (disponibile tra qualche minuto)"
    if (Read-LciaYesNo 'Aprire la pagina di avanzamento nel browser?' -DefaultYes) { Start-Process "$web/actions" }
}
exit 0
