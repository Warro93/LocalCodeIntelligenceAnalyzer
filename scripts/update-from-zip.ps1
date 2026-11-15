<#
.SYNOPSIS
    Aggiorna i sorgenti del progetto con una nuova versione ricevuta come zip, poi (se vuoi)
    compila, installa e pubblica su GitHub.

.DESCRIPTION
    1. Trova lo zip (parametro -ZipPath, ultimo LocalCodeIntelligenceAnalyzer*.zip nei Download o scelta da finestra).
    2. Lo estrae in una cartella temporanea (con controllo dei percorsi) e lo confronta con il progetto:
       file nuovi, modificati, rimossi. Le sole differenze di fine riga (CRLF/LF) non contano.
    3. Mostra il riepilogo e, dopo conferma, applica le modifiche. Prima salva una copia dei file che
       verranno sovrascritti o eliminati in artifacts\source-backups\<data-ora> (esclusa da Git).
    4. Propone: compilazione + test + installazione (install-app.ps1), poi commit firmato e push (03-review-and-push.ps1).
    Non tocca .git, bin, obj, artifacts, .vs e i file *.user. I file non gestiti da Git non vengono mai eliminati.

.EXAMPLE
    Aggiorna-sorgenti-da-zip.bat
    powershell -ExecutionPolicy Bypass -File .\scripts\update-from-zip.ps1 -ZipPath "$HOME\Downloads\LocalCodeIntelligenceAnalyzer-0.3.0.zip"
#>
[CmdletBinding()]
param(
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\LciaInstallTools.psm1') -Force

function Show-List {
    param([string]$Title, [string[]]$Items, [int]$Max = 40)
    if ($Items.Count -eq 0) { return }
    Write-LciaInfo "$Title ($($Items.Count)):"
    $Items | Select-Object -First $Max | ForEach-Object { Write-LciaInfo "    $_" }
    if ($Items.Count -gt $Max) { Write-LciaInfo "    ... e altri $($Items.Count - $Max)" }
}

function Get-DownloadsFolder {
    try {
        $shell = New-Object -ComObject Shell.Application
        $path = $shell.NameSpace('shell:Downloads').Self.Path
        if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    } catch { }
    return (Join-Path $env:USERPROFILE 'Downloads')
}

function Select-ZipWithDialog {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = 'Scegli lo zip della nuova versione di Local Code Intelligence Analyzer'
        $dialog.Filter = 'Archivi zip (*.zip)|*.zip'
        $dialog.InitialDirectory = Get-DownloadsFolder
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $dialog.FileName }
        return $null
    } catch {
        return (Read-LciaText -Prompt 'Percorso completo dello zip')
    }
}

Write-LciaTitle 'Local Code Intelligence Analyzer - Aggiornamento dei sorgenti da zip'

$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
$currentVersion = Get-LciaProjectVersion -Root $root
Write-LciaInfo "Progetto         : $root"
Write-LciaInfo "Versione attuale : $currentVersion"
if ((Test-LciaGitRepository -Root $root) -and (Test-LciaCommand 'git')) {
    Push-Location -LiteralPath $root
    try {
        $currentBranch = Get-LciaGitValue -Arguments @('symbolic-ref', '--short', 'HEAD')
        $hasDevelop = (Invoke-LciaGit -Arguments @('rev-parse', '--verify', '--quiet', 'refs/heads/develop') -AllowFailure).ExitCode -eq 0
    } finally {
        Pop-Location
    }
    Write-LciaInfo "Branch           : $currentBranch"
    if ($hasDevelop -and (Get-LciaBranchKind $currentBranch) -eq 'main') {
        Write-LciaWarn 'Sei su main: con Git Flow gli aggiornamenti si applicano su develop (o su un branch feature/*).'
        if ((Get-LciaPendingFiles -Root $root).Count -eq 0 -and (Read-LciaYesNo 'Passare a develop prima di aggiornare?' -DefaultYes)) {
            Push-Location -LiteralPath $root
            try {
                Invoke-LciaGit -Arguments @('switch', 'develop') | Out-Null
            } finally {
                Pop-Location
            }
            Write-LciaOk 'Ora sei su develop.'
        }
    }
}

# ------------------------------------------------------------------ 1. scelta dello zip
if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $downloads = Get-DownloadsFolder
    $candidate = Get-ChildItem -LiteralPath $downloads -Filter 'LocalCodeIntelligenceAnalyzer*.zip' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*-win-x64.zip' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $candidate) {
        Write-LciaInfo "Zip più recente nei Download: $($candidate.Name) ($($candidate.LastWriteTime.ToString('g')))"
        if (Read-LciaYesNo 'Usare questo file?' -DefaultYes) { $ZipPath = $candidate.FullName }
    }
    if ([string]::IsNullOrWhiteSpace($ZipPath)) { $ZipPath = Select-ZipWithDialog }
}
if ([string]::IsNullOrWhiteSpace($ZipPath) -or -not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    Write-LciaErr 'Nessuno zip selezionato (o file inesistente).'
    exit 1
}
$ZipPath = (Resolve-Path -LiteralPath $ZipPath).Path
if ($ZipPath.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    Write-LciaErr 'Lo zip si trova dentro la cartella del progetto: spostalo (ad es. nei Download) e rilancia.'
    exit 1
}

# ------------------------------------------------------------------ 2. estrazione e confronto
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('lcia-update-' + [guid]::NewGuid().ToString('N'))
try {
    try { Unblock-File -LiteralPath $ZipPath -ErrorAction SilentlyContinue } catch { }
    Write-LciaInfo 'Estrazione in una cartella temporanea...'
    try {
        Expand-LciaZip -ZipPath $ZipPath -Destination $temp
    } catch {
        Write-LciaErr "Zip non valido: $($_.Exception.Message)"
        exit 1
    }
    $newRoot = Find-LciaProjectRoot -Directory $temp
    if ($null -eq $newRoot) {
        Write-LciaErr 'Lo zip non contiene il progetto (manca LocalCodeIntelligenceAnalyzer.slnx).'
        exit 1
    }
    $newVersion = Get-LciaProjectVersion -Root $newRoot
    Write-LciaInfo "Versione nello zip: $newVersion"
    if ((Compare-LciaVersion -Left $newVersion -Right $currentVersion) -lt 0) {
        Write-LciaWarn "Lo zip contiene una versione PRECEDENTE ($newVersion) a quella del progetto ($currentVersion)."
        if (-not (Read-LciaYesNo 'Continuare comunque?')) { Write-LciaInfo 'Annullato.'; exit 0 }
    }

    $diff = Compare-LciaSourceTrees -CurrentRoot $root -NewRoot $newRoot

    # Si eliminano solo file gestiti da Git (recuperabili); senza Git non si elimina nulla.
    $isGit = (Test-LciaGitRepository -Root $root) -and (Test-LciaCommand 'git')
    $removable = @()
    $keptUntracked = @()
    $locallyModified = @()
    if ($isGit) {
        Push-Location -LiteralPath $root
        try {
            $tracked = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList @(, [System.StringComparer]::OrdinalIgnoreCase)
            foreach ($path in (Invoke-LciaGit -Arguments @('-c', 'core.quotepath=false', 'ls-files')).Output) { if ($path) { [void]$tracked.Add($path) } }
        } finally {
            Pop-Location
        }
        foreach ($path in $diff.Removed) {
            if ($tracked.Contains($path)) { $removable += $path } else { $keptUntracked += $path }
        }
        # Senza alcun commit tutti i file risultano "in sospeso": l'avviso avrebbe senso solo dopo il primo commit.
        Push-Location -LiteralPath $root
        try {
            $hasCommits = (Invoke-LciaGit -Arguments @('rev-parse', '--verify', 'HEAD') -AllowFailure).ExitCode -eq 0
        } finally {
            Pop-Location
        }
        if ($hasCommits) {
            $pendingPaths = @(Get-LciaPendingFiles -Root $root | ForEach-Object { $_.Path })
            $touched = @($diff.Changed) + @($removable)
            $locallyModified = @($pendingPaths | Where-Object { $touched -contains $_ })
        }
    } else {
        $keptUntracked = @($diff.Removed)
    }

    Write-LciaTitle "Riepilogo: $currentVersion -> $newVersion"
    Write-LciaInfo "Invariati: $($diff.Unchanged.Count)   Nuovi: $($diff.Added.Count)   Modificati: $($diff.Changed.Count)   Da eliminare: $($removable.Count)"
    Show-List 'Nuovi' $diff.Added
    Show-List 'Modificati' $diff.Changed
    Show-List 'Da eliminare (gestiti da Git, recuperabili)' $removable
    if ($keptUntracked.Count -gt 0) {
        Show-List 'Assenti nella nuova versione ma NON eliminati (non gestiti da Git: decidi tu)' $keptUntracked 20
    }
    if ($locallyModified.Count -gt 0) {
        Write-LciaWarn 'Questi file hanno modifiche locali non ancora salvate in un commit e verranno sostituiti'
        Write-LciaWarn '(una copia resta comunque in artifacts\source-backups):'
        $locallyModified | ForEach-Object { Write-LciaWarn "    $_" }
    }

    $total = $diff.Added.Count + $diff.Changed.Count + $removable.Count
    if ($total -eq 0) {
        Write-LciaOk 'Il progetto è già allineato con lo zip: nessuna modifica necessaria.'
    } else {
        if (-not (Read-LciaYesNo "Applicare le $total modifiche?" -DefaultYes)) {
            Write-LciaInfo 'Annullato: nessun file modificato.'
            exit 0
        }

        # -------------------------------------------------------------- 3. copia di sicurezza e applicazione
        $backup = Join-Path $root ('artifacts\source-backups\' + (Get-Date).ToString('yyyyMMdd-HHmmss'))
        foreach ($path in @($diff.Changed) + @($removable)) {
            $source = Join-Path $root $path
            $target = Join-Path $backup $path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
        foreach ($path in @($diff.Added) + @($diff.Changed)) {
            $target = Join-Path $root $path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $newRoot $path) -Destination $target -Force
        }
        foreach ($path in $removable) {
            Remove-Item -LiteralPath (Join-Path $root $path) -Force
            # Elimina le cartelle rimaste vuote (mai la radice del progetto).
            $directory = Split-Path -Parent (Join-Path $root $path)
            while ($directory -and $directory.Length -gt $root.Length -and
                   (Test-Path -LiteralPath $directory) -and
                   @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
                Remove-Item -LiteralPath $directory -Force
                $directory = Split-Path -Parent $directory
            }
        }
        Write-LciaOk "Sorgenti aggiornati alla versione $newVersion."
        if ((Test-Path -LiteralPath $backup)) { Write-LciaInfo "Copia dei file sostituiti: $backup" }
    }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------ 4. passi successivi (facoltativi)
Write-Host ''
if (Read-LciaYesNo 'Compilare, testare e installare ora la nuova versione?' -DefaultYes) {
    $global:LASTEXITCODE = 0
    & (Join-Path $PSScriptRoot 'install-app.ps1') -NoLaunch
    if ($LASTEXITCODE -ne 0) {
        Write-LciaErr 'Installazione non completata: i sorgenti sono aggiornati, ma è meglio non pubblicarli finché la build non passa.'
        exit $LASTEXITCODE
    }
}

if ((Test-LciaGitRepository -Root $root) -and (Test-LciaCommand 'git')) {
    Push-Location -LiteralPath $root
    try { $origin = Get-LciaGitValue -Arguments @('remote', 'get-url', 'origin') } finally { Pop-Location }
    if ($origin -and (Read-LciaYesNo 'Creare il commit (firmato) e inviarlo su GitHub ora?')) {
        $global:LASTEXITCODE = 0
        & (Join-Path $PSScriptRoot '03-review-and-push.ps1') -Message "Aggiornamento alla versione $newVersion"
        exit $LASTEXITCODE
    }
}
exit 0
