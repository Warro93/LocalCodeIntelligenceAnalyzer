# =====================================================================
# LciaInstallTools.psm1 - installazione per l'utente corrente e aggiornamento dei sorgenti.
# Compatibile con Windows PowerShell 5.1 e PowerShell 7+.
# Nessun privilegio di amministratore: scrive solo in %LOCALAPPDATA%, nel menu Start,
# sul desktop (se richiesto), nel PATH dell'utente (se richiesto) e in HKCU.
# =====================================================================

Set-StrictMode -Version Latest

$script:ProductName = 'Local Code Intelligence Analyzer'
$script:ProductFolder = 'LocalCodeIntelligenceAnalyzer'
$script:MainExecutable = 'LocalCodeIntelligenceAnalyzer.exe'
$script:CliExecutable = 'lcia.exe'
$script:InstallInfoFile = 'install-info.json'
$script:UninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCodeIntelligenceAnalyzer'

# Cartelle mai considerate sorgenti (output, ambienti locali, metadati Git).
$script:SourceExcludedDirectories = @('.git', '.vs', '.idea', 'bin', 'obj', 'artifacts', 'TestResults', 'node_modules', 'publish')

# Estensioni confrontate come testo (le differenze CRLF/LF non contano come modifiche).
$script:TextExtensions = @(
    '.cs', '.xaml', '.csproj', '.props', '.targets', '.slnx', '.sln', '.json', '.md', '.txt', '.sql', '.ps1', '.psm1',
    '.bat', '.cmd', '.yml', '.yaml', '.xml', '.config', '.manifest', '.resx', '.editorconfig', '.gitignore',
    '.gitattributes', '.sqlproj', '.http', '.html', '.css', '.js', '.ts'
)

# --------------------------------------------------------------------- percorsi

function Get-LciaInstallDirectory {
    return (Join-Path $env:LOCALAPPDATA ('Programs\' + $script:ProductFolder))
}

function Get-LciaDataDirectory {
    <#
    .SYNOPSIS
    Cartella dei dati dell'applicazione (indice, log, impostazioni), come in AppPaths.cs.
    #>
    if (-not [string]::IsNullOrWhiteSpace($env:LCIA_DATA_DIR)) { return $env:LCIA_DATA_DIR }
    return (Join-Path $env:LOCALAPPDATA $script:ProductFolder)
}

function Get-LciaShortcutPaths {
    $name = $script:ProductName + '.lnk'
    return [pscustomobject]@{
        StartMenu = Join-Path ([Environment]::GetFolderPath('Programs')) $name
        Desktop   = Join-Path ([Environment]::GetFolderPath('Desktop')) $name
    }
}

# --------------------------------------------------------------------- informazioni sull'installazione

function Get-LciaInstallInfo {
    <#
    .SYNOPSIS
    Legge install-info.json dalla cartella di installazione; $null se non esiste o non è leggibile.
    #>
    param([Parameter(Mandatory)][string]$InstallDirectory)
    $file = Join-Path $InstallDirectory $script:InstallInfoFile
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    try {
        return ([System.IO.File]::ReadAllText($file) | ConvertFrom-Json)
    } catch {
        return $null
    }
}

function Get-LciaInfoValue {
    <#
    .SYNOPSIS
    Legge una proprietà da un oggetto (anche $null) restituendo un valore predefinito se manca.
    #>
    param($InputObject, [Parameter(Mandatory)][string]$Name, $Default = $null)
    if ($null -eq $InputObject) { return $Default }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function Assert-LciaInstallDirectory {
    <#
    .SYNOPSIS
    Impedisce di sovrascrivere una cartella che non contiene un'installazione di LCIA.
    #>
    param([Parameter(Mandatory)][string]$InstallDirectory)
    if ((Split-Path -Leaf $InstallDirectory) -ne $script:ProductFolder) {
        throw "Cartella di installazione non valida (deve chiamarsi $($script:ProductFolder)): $InstallDirectory"
    }
    if (-not (Test-Path -LiteralPath $InstallDirectory)) { return }
    $items = @(Get-ChildItem -LiteralPath $InstallDirectory -Force)
    if ($items.Count -eq 0) { return }
    $known = (Test-Path -LiteralPath (Join-Path $InstallDirectory $script:InstallInfoFile)) -or
             (Test-Path -LiteralPath (Join-Path $InstallDirectory $script:MainExecutable))
    if (-not $known) {
        throw "La cartella $InstallDirectory esiste ma non contiene un'installazione di $($script:ProductName): non viene toccata."
    }
}

function Install-LciaDirectory {
    <#
    .SYNOPSIS
    Copia SourceDirectory in TargetDirectory sostituendo in modo sicuro il contenuto precedente:
    copia in una cartella temporanea accanto, poi scambio con rinomina. Se lo scambio fallisce,
    la versione precedente viene ripristinata.
    #>
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$TargetDirectory,
        [hashtable]$ExtraFiles = @{}
    )
    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw "Cartella di origine non trovata: $SourceDirectory"
    }
    $parent = Split-Path -Parent $TargetDirectory
    $leaf = Split-Path -Leaf $TargetDirectory
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $staging = Join-Path $parent ($leaf + '.new')
    $backup = Join-Path $parent ($leaf + '.old')
    foreach ($temporary in @($staging, $backup)) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Get-ChildItem -LiteralPath $SourceDirectory -Force | Copy-Item -Destination $staging -Recurse -Force
    foreach ($name in $ExtraFiles.Keys) {
        Copy-Item -LiteralPath $ExtraFiles[$name] -Destination (Join-Path $staging $name) -Force
    }

    $hadPrevious = Test-Path -LiteralPath $TargetDirectory
    if ($hadPrevious) {
        try {
            Rename-Item -LiteralPath $TargetDirectory -NewName (Split-Path -Leaf $backup)
        } catch {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
            throw "Impossibile sostituire la versione installata (file in uso?): $($_.Exception.Message)"
        }
    }
    try {
        Rename-Item -LiteralPath $staging -NewName $leaf
    } catch {
        if ($hadPrevious) { Rename-Item -LiteralPath $backup -NewName $leaf -ErrorAction SilentlyContinue }
        throw "Installazione non riuscita, versione precedente ripristinata: $($_.Exception.Message)"
    }
    if ($hadPrevious) { Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue }
}

# --------------------------------------------------------------------- processi in esecuzione

function Get-LciaRunningInstances {
    param([Parameter(Mandatory)][string]$InstallDirectory)
    $prefix = $InstallDirectory.TrimEnd('\') + '\'
    $result = New-Object System.Collections.Generic.List[object]
    foreach ($process in @(Get-Process -Name 'LocalCodeIntelligenceAnalyzer', 'lcia' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $process.Path } catch { $path = $null }
        if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $result.Add($process) }
    }
    return ,$result.ToArray()
}

function Close-LciaRunningInstances {
    <#
    .SYNOPSIS
    Chiede la chiusura normale delle finestre dell'applicazione e attende fino a TimeoutSeconds.
    Non termina mai i processi in modo forzato. Restituisce $true se non resta nulla in esecuzione.
    #>
    param(
        [Parameter(Mandatory)][string]$InstallDirectory,
        [int]$TimeoutSeconds = 15
    )
    foreach ($process in (Get-LciaRunningInstances -InstallDirectory $InstallDirectory)) {
        try { [void]$process.CloseMainWindow() } catch { }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Get-LciaRunningInstances -InstallDirectory $InstallDirectory).Count -eq 0) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return ((Get-LciaRunningInstances -InstallDirectory $InstallDirectory).Count -eq 0)
}

# --------------------------------------------------------------------- collegamenti

function New-LciaShortcut {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Target,
        [string]$WorkingDirectory = '',
        [string]$Description = ''
    )
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($Path)
        $link.TargetPath = $Target
        if ($WorkingDirectory) { $link.WorkingDirectory = $WorkingDirectory }
        if ($Description) { $link.Description = $Description }
        $link.IconLocation = "$Target,0"
        $link.Save()
    } finally {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Remove-LciaShortcut {
    <#
    .SYNOPSIS
    Elimina un collegamento solo se punta alla cartella di installazione indicata.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$InstallDirectory
    )
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $target = $shell.CreateShortcut($Path).TargetPath
    } finally {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
    if ($target -and $target.StartsWith($InstallDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $Path -Force
        return $true
    }
    return $false
}

# --------------------------------------------------------------------- PATH dell'utente

function Add-LciaPathEntry {
    <#
    .SYNOPSIS
    Aggiunge una cartella a un valore PATH (separato da ;) se non è già presente. Funzione pura.
    #>
    param(
        [AllowEmptyString()][AllowNull()][string]$PathValue,
        [Parameter(Mandatory)][string]$Entry
    )
    $parts = @(([string]$PathValue).Split(';') | Where-Object { $_ -ne '' })
    foreach ($part in $parts) {
        if ($part.Trim().TrimEnd('\') -ieq $Entry.TrimEnd('\')) { return [string]$PathValue }
    }
    return (@($parts) + $Entry) -join ';'
}

function Remove-LciaPathEntry {
    <#
    .SYNOPSIS
    Rimuove una cartella da un valore PATH (separato da ;). Funzione pura.
    #>
    param(
        [AllowEmptyString()][AllowNull()][string]$PathValue,
        [Parameter(Mandatory)][string]$Entry
    )
    $parts = @(([string]$PathValue).Split(';') | Where-Object { $_ -ne '' -and $_.Trim().TrimEnd('\') -ine $Entry.TrimEnd('\') })
    return ($parts -join ';')
}

function Get-LciaUserPath {
    <#
    .SYNOPSIS
    Valore grezzo del PATH dell'utente (le variabili come %USERPROFILE% non vengono espanse).
    #>
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $false)
    if ($null -eq $key) { return '' }
    try {
        return [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    } finally {
        $key.Close()
    }
}

function Set-LciaUserPath {
    param([AllowEmptyString()][Parameter(Mandatory)][string]$Value)
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
    try {
        $key.SetValue('Path', $Value, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    } finally {
        $key.Close()
    }
    Send-LciaEnvironmentChange
}

function Send-LciaEnvironmentChange {
    <#
    .SYNOPSIS
    Avvisa Windows che le variabili d'ambiente sono cambiate (i nuovi terminali vedranno il PATH aggiornato).
    #>
    try {
        if (-not ('LciaNative.Environment' -as [type])) {
            Add-Type -Namespace LciaNative -Name Environment -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
'@
        }
        $result = [UIntPtr]::Zero
        [void][LciaNative.Environment]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result)
    } catch {
        # Non bloccante: il PATH aggiornato sarà comunque visibile dopo il prossimo accesso.
    }
}

# --------------------------------------------------------------------- "App installate" di Windows

function Register-LciaUninstallEntry {
    param(
        [Parameter(Mandatory)][string]$InstallDirectory,
        [Parameter(Mandatory)][string]$Version
    )
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $uninstaller = Join-Path $InstallDirectory 'uninstall.ps1'
    $sizeKb = [int][Math]::Ceiling(((Get-ChildItem -LiteralPath $InstallDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1KB)
    if (-not (Test-Path -LiteralPath $script:UninstallRegistryPath)) {
        New-Item -Path $script:UninstallRegistryPath -Force | Out-Null
    }
    $values = [ordered]@{
        DisplayName     = $script:ProductName
        DisplayVersion  = $Version
        Publisher       = $script:ProductName
        InstallLocation = $InstallDirectory
        DisplayIcon     = (Join-Path $InstallDirectory $script:MainExecutable) + ',0'
        UninstallString = "`"$powershell`" -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`""
        InstallDate     = (Get-Date).ToString('yyyyMMdd')
    }
    foreach ($name in $values.Keys) {
        New-ItemProperty -LiteralPath $script:UninstallRegistryPath -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null
    }
    foreach ($name in @('NoModify', 'NoRepair')) {
        New-ItemProperty -LiteralPath $script:UninstallRegistryPath -Name $name -Value 1 -PropertyType DWord -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $script:UninstallRegistryPath -Name 'EstimatedSize' -Value $sizeKb -PropertyType DWord -Force | Out-Null
}

# --------------------------------------------------------------------- aggiornamento dei sorgenti

function Get-LciaSourceFiles {
    <#
    .SYNOPSIS
    Elenca (percorsi relativi con "/") i file sorgente di una cartella, escludendo output di build,
    metadati Git, cartelle locali, file *.user/*.suo e collegamenti simbolici.
    #>
    param([Parameter(Mandatory)][string]$Root)
    $rootFull = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    $result = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($rootFull)
    while ($stack.Count -gt 0) {
        $directory = $stack.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            if ($item.PSIsContainer) {
                if ($script:SourceExcludedDirectories -contains $item.Name) { continue }
                $stack.Push($item.FullName)
                continue
            }
            if ($item.Name -like '*.user' -or $item.Name -like '*.suo') { continue }
            $result.Add($item.FullName.Substring($rootFull.Length + 1).Replace('\', '/'))
        }
    }
    $array = $result.ToArray()
    [Array]::Sort($array, [StringComparer]::OrdinalIgnoreCase)
    return ,$array
}

function Test-LciaSameContent {
    <#
    .SYNOPSIS
    $true se i due file hanno lo stesso contenuto. Per i file di testo le differenze tra fine riga
    CRLF e LF non contano (Git le normalizza comunque).
    #>
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )
    $extension = [System.IO.Path]::GetExtension($Left).ToLowerInvariant()
    $name = [System.IO.Path]::GetFileName($Left).ToLowerInvariant()
    $isText = ($script:TextExtensions -contains $extension) -or ($script:TextExtensions -contains $name)
    if ($isText) {
        $latin1 = [System.Text.Encoding]::GetEncoding(28591)
        $a = $latin1.GetString([System.IO.File]::ReadAllBytes($Left)).Replace("`r", '')
        $b = $latin1.GetString([System.IO.File]::ReadAllBytes($Right)).Replace("`r", '')
        return [string]::Equals($a, $b, [StringComparison]::Ordinal)
    }
    if ((Get-Item -LiteralPath $Left).Length -ne (Get-Item -LiteralPath $Right).Length) { return $false }
    return ((Get-FileHash -LiteralPath $Left -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $Right -Algorithm SHA256).Hash)
}

function Compare-LciaSourceTrees {
    <#
    .SYNOPSIS
    Confronta la cartella del progetto con una nuova versione: file aggiunti, modificati, rimossi, invariati.
    #>
    param(
        [Parameter(Mandatory)][string]$CurrentRoot,
        [Parameter(Mandatory)][string]$NewRoot
    )
    $current = Get-LciaSourceFiles -Root $CurrentRoot
    $incoming = Get-LciaSourceFiles -Root $NewRoot
    $currentSet = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList @(, [System.StringComparer]::OrdinalIgnoreCase)
    $incomingSet = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList @(, [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $current) { [void]$currentSet.Add($path) }
    foreach ($path in $incoming) { [void]$incomingSet.Add($path) }

    $added = New-Object System.Collections.Generic.List[string]
    $changed = New-Object System.Collections.Generic.List[string]
    $unchanged = New-Object System.Collections.Generic.List[string]
    $removed = New-Object System.Collections.Generic.List[string]
    foreach ($path in $incoming) {
        if (-not $currentSet.Contains($path)) { $added.Add($path); continue }
        if (Test-LciaSameContent -Left (Join-Path $CurrentRoot $path) -Right (Join-Path $NewRoot $path)) {
            $unchanged.Add($path)
        } else {
            $changed.Add($path)
        }
    }
    foreach ($path in $current) {
        if (-not $incomingSet.Contains($path)) { $removed.Add($path) }
    }
    return [pscustomobject]@{
        Added     = $added.ToArray()
        Changed   = $changed.ToArray()
        Removed   = $removed.ToArray()
        Unchanged = $unchanged.ToArray()
    }
}

function Find-LciaProjectRoot {
    <#
    .SYNOPSIS
    Cerca, entro due livelli, la cartella che contiene LocalCodeIntelligenceAnalyzer.slnx.
    #>
    param([Parameter(Mandatory)][string]$Directory)
    $marker = 'LocalCodeIntelligenceAnalyzer.slnx'
    if (Test-Path -LiteralPath (Join-Path $Directory $marker)) { return (Resolve-Path -LiteralPath $Directory).Path }
    foreach ($child in @(Get-ChildItem -LiteralPath $Directory -Directory -Force)) {
        if (Test-Path -LiteralPath (Join-Path $child.FullName $marker)) { return $child.FullName }
        foreach ($grandChild in @(Get-ChildItem -LiteralPath $child.FullName -Directory -Force)) {
            if (Test-Path -LiteralPath (Join-Path $grandChild.FullName $marker)) { return $grandChild.FullName }
        }
    }
    return $null
}

function Expand-LciaZip {
    <#
    .SYNOPSIS
    Estrae uno zip in una cartella nuova verificando che nessun file finisca fuori dalla destinazione.
    #>
    param(
        [Parameter(Mandatory)][string]$ZipPath,
        [Parameter(Mandatory)][string]$Destination
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $destinationFull = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    New-Item -ItemType Directory -Path $destinationFull -Force | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($destinationFull, $entry.FullName))
            if (-not $target.StartsWith($destinationFull, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Lo zip contiene un percorso non valido: $($entry.FullName)"
            }
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                continue
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally {
        $archive.Dispose()
    }
}

Export-ModuleMember -Function *-Lcia*
