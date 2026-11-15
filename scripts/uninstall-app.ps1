<#
.SYNOPSIS
    Disinstalla Local Code Intelligence Analyzer per l'utente corrente.

.DESCRIPTION
    Rimuove la cartella del programma, i collegamenti (menu Start e desktop), la voce nel PATH
    dell'utente e quella in Impostazioni > App > App installate.
    I dati (indice, log, impostazioni) vengono eliminati SOLO se lo confermi esplicitamente.
    I repository analizzati e i dossier esportati non vengono mai toccati.

    Lo script è autonomo: una copia viene installata come "uninstall.ps1" nella cartella del programma
    ed è quella usata da Impostazioni > App.

.PARAMETER NoPause
    Non attende Invio alla fine (usato da Disinstalla-app.bat, che ha già la sua pausa).

.EXAMPLE
    Disinstalla-app.bat
    powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-app.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$productName = 'Local Code Intelligence Analyzer'
$productFolder = 'LocalCodeIntelligenceAnalyzer'
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCodeIntelligenceAnalyzer'

function Write-Info { param([string]$Text) Write-Host "  $Text" }
function Write-Ok { param([string]$Text) Write-Host "  [OK] $Text" -ForegroundColor Green }
function Write-Warn { param([string]$Text) Write-Host "  [ATTENZIONE] $Text" -ForegroundColor Yellow }
function Write-Err { param([string]$Text) Write-Host "  [ERRORE] $Text" -ForegroundColor Red }

function Confirm-Choice {
    param([string]$Question, [switch]$DefaultYes)
    $suffix = if ($DefaultYes) { '[S/n]' } else { '[s/N]' }
    $answer = Read-Host "  $Question $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return [bool]$DefaultYes }
    return @('s', 'si', 'sì', 'y', 'yes') -contains $answer.Trim().ToLowerInvariant()
}

function Stop-Uninstall {
    param([int]$Code)
    if (-not $NoPause) {
        Write-Host ''
        [void](Read-Host '  Premi Invio per chiudere')
    }
    exit $Code
}

function Get-RunningInstances {
    param([string]$Directory)
    $prefix = $Directory.TrimEnd('\') + '\'
    $found = @()
    foreach ($process in @(Get-Process -Name 'LocalCodeIntelligenceAnalyzer', 'lcia' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $process.Path } catch { $path = $null }
        if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $found += $process }
    }
    return ,$found
}

function Remove-ShortcutIfOurs {
    param([string]$Path, [string]$Directory)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $shell = New-Object -ComObject WScript.Shell
    try { $target = $shell.CreateShortcut($Path).TargetPath } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    if ($target -and $target.StartsWith($Directory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $Path -Force
        Write-Ok "Collegamento rimosso: $Path"
    }
}

function Remove-FromUserPath {
    param([string]$Directory)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    if ($null -eq $key) { return }
    try {
        $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $entry = $Directory.TrimEnd('\')
        $parts = @($raw.Split(';') | Where-Object { $_ -ne '' -and $_.Trim().TrimEnd('\') -ine $entry })
        $updated = $parts -join ';'
        if ($updated -eq $raw) { return }
        $key.SetValue('Path', $updated, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    } finally {
        $key.Close()
    }
    try {
        if (-not ('LciaNative.Environment' -as [type])) {
            Add-Type -Namespace LciaNative -Name Environment -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
'@
        }
        $result = [UIntPtr]::Zero
        [void][LciaNative.Environment]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result)
    } catch { }
    Write-Ok 'Cartella rimossa dal PATH dell''utente.'
}

# ------------------------------------------------------------------ dove si trova l'installazione
Write-Host ''
Write-Host ('=' * 70) -ForegroundColor DarkCyan
Write-Host " $productName - Disinstallazione" -ForegroundColor Cyan
Write-Host ('=' * 70) -ForegroundColor DarkCyan

$installDir = $null
if (Test-Path -LiteralPath $registryPath) {
    $installDir = (Get-ItemProperty -LiteralPath $registryPath).InstallLocation
}
if ([string]::IsNullOrWhiteSpace($installDir)) {
    $installDir = Join-Path $env:LOCALAPPDATA "Programs\$productFolder"
}
$installDir = $installDir.TrimEnd('\')

$isOurFolder = (Test-Path -LiteralPath $installDir) -and
    ((Split-Path -Leaf $installDir) -eq $productFolder) -and
    ((Test-Path -LiteralPath (Join-Path $installDir 'install-info.json')) -or
     (Test-Path -LiteralPath (Join-Path $installDir 'LocalCodeIntelligenceAnalyzer.exe')))

$dataDir = if ([string]::IsNullOrWhiteSpace($env:LCIA_DATA_DIR)) { Join-Path $env:LOCALAPPDATA $productFolder } else { $env:LCIA_DATA_DIR }
$links = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) "$productName.lnk"),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) "$productName.lnk")
)

if (-not $isOurFolder -and -not (Test-Path -LiteralPath $registryPath)) {
    Write-Info "Nessuna installazione trovata in $installDir."
    Stop-Uninstall 0
}

Write-Info 'Verranno rimossi:'
if ($isOurFolder) { Write-Info "  - cartella del programma: $installDir" }
Write-Info '  - collegamenti nel menu Start e sul desktop'
Write-Info '  - voce nel PATH dell''utente (se presente)'
Write-Info '  - voce in Impostazioni > App > App installate'
Write-Info "I dati in $dataDir (indice, log, impostazioni) restano, salvo tua conferma più avanti."
if (-not (Confirm-Choice 'Disinstallare?')) {
    Write-Info 'Annullato: nessuna modifica effettuata.'
    Stop-Uninstall 0
}

# ------------------------------------------------------------------ applicazione aperta?
if ((Get-RunningInstances -Directory $installDir).Count -gt 0) {
    Write-Warn 'L''applicazione è aperta.'
    if (-not (Confirm-Choice 'Chiuderla ora?' -DefaultYes)) { Write-Info 'Chiudila e rilancia la disinstallazione.'; Stop-Uninstall 1 }
    foreach ($process in (Get-RunningInstances -Directory $installDir)) { try { [void]$process.CloseMainWindow() } catch { } }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Get-RunningInstances -Directory $installDir).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if ((Get-RunningInstances -Directory $installDir).Count -gt 0) {
        Write-Err 'L''applicazione (o lcia.exe) è ancora in esecuzione: chiudila e rilancia la disinstallazione.'
        Stop-Uninstall 1
    }
}

# ------------------------------------------------------------------ rimozione
foreach ($link in $links) {
    try { Remove-ShortcutIfOurs -Path $link -Directory $installDir } catch { Write-Warn "Collegamento non rimosso: $link" }
}
try { Remove-FromUserPath -Directory $installDir } catch { Write-Warn "PATH non aggiornato: $($_.Exception.Message)" }
if (Test-Path -LiteralPath $registryPath) {
    Remove-Item -LiteralPath $registryPath -Recurse -Force
    Write-Ok 'Voce rimossa da App installate.'
}

if ($isOurFolder) {
    # Se lo script viene eseguito dalla cartella del programma, esce da essa prima di eliminarla.
    Set-Location -LiteralPath ([System.IO.Path]::GetTempPath())
    try {
        Remove-Item -LiteralPath $installDir -Recurse -Force
        Write-Ok "Cartella del programma eliminata: $installDir"
    } catch {
        Write-Warn "Alcuni file non sono stati eliminati ($($_.Exception.Message)). Puoi cancellare a mano: $installDir"
    }
}

# ------------------------------------------------------------------ dati (solo su conferma)
if (Test-Path -LiteralPath $dataDir) {
    Write-Host ''
    Write-Info "Dati dell'applicazione: $dataDir"
    Write-Info '(indice dei repository, log, impostazioni; i repository analizzati non sono lì e non vengono toccati)'
    if (Confirm-Choice 'Eliminare anche questi dati?') {
        $safe = -not [string]::IsNullOrWhiteSpace($dataDir) -and
            ([System.IO.Path]::GetFullPath($dataDir).TrimEnd('\') -ne [System.IO.Path]::GetFullPath($env:USERPROFILE).TrimEnd('\')) -and
            ([System.IO.Path]::GetFullPath($dataDir).TrimEnd('\') -ne [System.IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')) -and
            ([System.IO.Path]::GetPathRoot($dataDir).TrimEnd('\') -ne [System.IO.Path]::GetFullPath($dataDir).TrimEnd('\'))
        if ($safe) {
            Remove-Item -LiteralPath $dataDir -Recurse -Force
            Write-Ok 'Dati eliminati.'
        } else {
            Write-Warn "Cartella dati non eliminata per sicurezza: $dataDir"
        }
    } else {
        Write-Info 'Dati conservati: verranno riutilizzati se reinstalli l''applicazione.'
    }
}

Write-Host ''
Write-Ok "$productName è stato disinstallato."
Stop-Uninstall 0
