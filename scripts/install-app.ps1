<#
.SYNOPSIS
    Compila, verifica e installa (o aggiorna) Local Code Intelligence Analyzer per l'utente corrente.

.DESCRIPTION
    1. Compilazione, test automatici e prova della CLI (scripts\build.ps1).
    2. Versione self-contained per Windows x64 (scripts\publish-win-x64.ps1).
    3. Installazione in %LOCALAPPDATA%\Programs\LocalCodeIntelligenceAnalyzer: la versione precedente
       viene sostituita solo a copia completata e ripristinata se qualcosa va storto.
    4. Collegamento nel menu Start, sul desktop (facoltativo), comando "lcia" nel PATH dell'utente
       (facoltativo), voce in Impostazioni > App > App installate (con disinstallazione).
    Non servono privilegi di amministratore. Indice, log e impostazioni non vengono toccati.

.PARAMETER SkipTests
    Salta compilazione di verifica, test e prova della CLI (sconsigliato).

.PARAMETER NoLaunch
    Non propone di avviare l'applicazione al termine.

.EXAMPLE
    Installa-o-aggiorna-app.bat
    powershell -ExecutionPolicy Bypass -File .\scripts\install-app.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LciaGitTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\LciaInstallTools.psm1') -Force

Write-LciaTitle 'Local Code Intelligence Analyzer - Installazione / aggiornamento'

$root = Get-LciaProjectRoot -ScriptsDirectory $PSScriptRoot
$version = Get-LciaProjectVersion -Root $root
$installDir = Get-LciaInstallDirectory
$previous = Get-LciaInstallInfo -InstallDirectory $installDir
$previousVersion = [string](Get-LciaInfoValue $previous 'Version' '')

try {
    Assert-LciaInstallDirectory -InstallDirectory $installDir
} catch {
    Write-LciaErr $_.Exception.Message
    exit 1
}

Write-LciaInfo "Versione da installare   : $version"
Write-LciaInfo ("Versione installata      : " + $(if ($previousVersion) { $previousVersion } else { 'nessuna' }))
Write-LciaInfo "Cartella del programma   : $installDir"
Write-LciaInfo "Dati (indice, log, impostazioni): $(Get-LciaDataDirectory) - non vengono modificati"

if ($previousVersion -and (Compare-LciaVersion -Left $version -Right $previousVersion) -lt 0) {
    Write-LciaWarn "La versione da installare ($version) è PRECEDENTE a quella installata ($previousVersion)."
    if (-not (Read-LciaYesNo 'Installare comunque la versione precedente?')) { Write-LciaInfo 'Annullato.'; exit 0 }
}

# ------------------------------------------------------------------ scelte (chieste solo alla prima installazione)
$shortcuts = Get-LciaShortcutPaths
if ($null -ne $previous -and $null -ne $previous.PSObject.Properties['DesktopShortcut']) {
    $wantDesktop = [bool]$previous.DesktopShortcut
} else {
    $wantDesktop = Read-LciaYesNo 'Creare un collegamento sul desktop?' -DefaultYes
}
if ($null -ne $previous -and $null -ne $previous.PSObject.Properties['AddedToPath']) {
    $wantPath = [bool]$previous.AddedToPath
} else {
    Write-LciaInfo 'Il comando "lcia" (riga di comando) può essere usato da qualsiasi terminale se la cartella'
    Write-LciaInfo 'del programma viene aggiunta al PATH del tuo utente (reversibile con la disinstallazione).'
    $wantPath = Read-LciaYesNo 'Aggiungere "lcia" al PATH dell''utente?' -DefaultYes
}

if (-not (Read-LciaYesNo "Procedere con compilazione$(if ($SkipTests) { '' } else { ', test' }) e installazione?" -DefaultYes)) {
    Write-LciaInfo 'Annullato: nessuna modifica effettuata.'
    exit 0
}

# ------------------------------------------------------------------ 1. build e test
if (-not $SkipTests) {
    Write-LciaTitle '1/4  Compilazione, test e prova della CLI'
    $global:LASTEXITCODE = 0
    try {
        & (Join-Path $PSScriptRoot 'build.ps1')
    } catch {
        Write-LciaErr $_.Exception.Message
        $global:LASTEXITCODE = 1
    }
    if ($LASTEXITCODE -ne 0) {
        Write-LciaErr 'Compilazione o test non riusciti: installazione annullata. La versione installata non è stata toccata.'
        exit 1
    }
} else {
    Write-LciaWarn 'Test saltati su richiesta (-SkipTests).'
}

# ------------------------------------------------------------------ 2. publish
Write-LciaTitle '2/4  Versione self-contained Windows x64'
$global:LASTEXITCODE = 0
try {
    & (Join-Path $PSScriptRoot 'publish-win-x64.ps1') -NoZip
} catch {
    Write-LciaErr $_.Exception.Message
    $global:LASTEXITCODE = 1
}
if ($LASTEXITCODE -ne 0) {
    Write-LciaErr 'Creazione della versione da installare non riuscita: installazione annullata.'
    exit 1
}
$publishDir = Join-Path $root "artifacts\publish\LocalCodeIntelligenceAnalyzer-$version-win-x64"
foreach ($required in @('LocalCodeIntelligenceAnalyzer.exe', 'lcia.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) {
        Write-LciaErr "File mancante nella versione compilata: $required ($publishDir)"
        exit 1
    }
}

# ------------------------------------------------------------------ 3. installazione
Write-LciaTitle '3/4  Installazione'
if ((Get-LciaRunningInstances -InstallDirectory $installDir).Count -gt 0) {
    Write-LciaWarn 'Local Code Intelligence Analyzer è aperto: va chiuso per sostituire i file.'
    if (-not (Read-LciaYesNo 'Chiuderlo ora? (un''indicizzazione in corso verrà interrotta e ripresa al prossimo avvio)' -DefaultYes)) {
        Write-LciaInfo 'Chiudi l''applicazione e rilancia l''installazione.'
        exit 1
    }
    if (-not (Close-LciaRunningInstances -InstallDirectory $installDir)) {
        Write-LciaErr 'L''applicazione (o lcia.exe) è ancora in esecuzione: chiudila e rilancia l''installazione.'
        exit 1
    }
    Write-LciaOk 'Applicazione chiusa.'
}

$infoFile = Join-Path ([System.IO.Path]::GetTempPath()) ('lcia-install-info-' + [guid]::NewGuid().ToString('N') + '.json')
$info = [ordered]@{
    Product         = 'Local Code Intelligence Analyzer'
    Version         = $version
    InstalledUtc    = [DateTime]::UtcNow.ToString('o')
    SourceDirectory = $root
    DesktopShortcut = $wantDesktop
    AddedToPath     = $wantPath
}
[System.IO.File]::WriteAllText($infoFile, ($info | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))

try {
    Install-LciaDirectory -SourceDirectory $publishDir -TargetDirectory $installDir -ExtraFiles @{
        'uninstall.ps1'     = (Join-Path $PSScriptRoot 'uninstall-app.ps1')
        'install-info.json' = $infoFile
    }
} catch {
    Write-LciaErr $_.Exception.Message
    exit 1
} finally {
    Remove-Item -LiteralPath $infoFile -Force -ErrorAction SilentlyContinue
}
Write-LciaOk "File copiati in $installDir"

$exe = Join-Path $installDir 'LocalCodeIntelligenceAnalyzer.exe'
$description = 'Analisi locale di repository e Full Project Context Export'
New-LciaShortcut -Path $shortcuts.StartMenu -Target $exe -WorkingDirectory $installDir -Description $description
Write-LciaOk 'Collegamento nel menu Start.'
if ($wantDesktop) {
    New-LciaShortcut -Path $shortcuts.Desktop -Target $exe -WorkingDirectory $installDir -Description $description
    Write-LciaOk 'Collegamento sul desktop.'
} elseif (Remove-LciaShortcut -Path $shortcuts.Desktop -InstallDirectory $installDir) {
    Write-LciaInfo 'Collegamento sul desktop rimosso (non richiesto).'
}

if ($wantPath) {
    $currentPath = Get-LciaUserPath
    $newPath = Add-LciaPathEntry -PathValue $currentPath -Entry $installDir
    if ($newPath -ne $currentPath) {
        Set-LciaUserPath -Value $newPath
        Write-LciaOk 'Cartella aggiunta al PATH dell''utente: "lcia" funziona nei terminali aperti da ora in poi.'
    } else {
        Write-LciaOk 'Comando "lcia" già disponibile nel PATH.'
    }
}

Register-LciaUninstallEntry -InstallDirectory $installDir -Version $version
Write-LciaOk 'Registrato in Impostazioni > App > App installate.'

# La cartella di publish serviva solo per l'installazione: la rimuovo per non occupare spazio.
Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------ 4. fine
Write-LciaTitle '4/4  Completato'
if ($previousVersion -and $previousVersion -ne $version) {
    Write-LciaOk "Aggiornato da $previousVersion a $version."
} else {
    Write-LciaOk "Local Code Intelligence Analyzer $version installato."
}
Write-LciaInfo 'Avvio: menu Start > "Local Code Intelligence Analyzer"'
Write-LciaInfo 'Riga di comando: lcia --help (in un nuovo terminale)'
Write-LciaInfo 'Disinstallazione: Disinstalla-app.bat oppure Impostazioni > App > App installate.'

if (-not $NoLaunch -and (Read-LciaYesNo 'Avviare l''applicazione ora?' -DefaultYes)) {
    Start-Process -FilePath $exe -WorkingDirectory $installDir
}
exit 0
