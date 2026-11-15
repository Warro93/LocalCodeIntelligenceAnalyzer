# =====================================================================
# LciaGitTools.psm1 - funzioni comuni agli script di pubblicazione GitHub.
# Compatibile con Windows PowerShell 5.1 e PowerShell 7+.
# Non contiene e non richiede token: l'autenticazione avviene solo tramite
# "gh auth login" eseguito dall'utente.
# =====================================================================

Set-StrictMode -Version Latest

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    # Console non disponibile (es. host non interattivo): ignorato.
}

# --------------------------------------------------------------------- output

function Write-LciaTitle {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor DarkCyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host ('=' * 70) -ForegroundColor DarkCyan
}

function Write-LciaInfo { param([string]$Text) Write-Host "  $Text" }
function Write-LciaOk { param([string]$Text) Write-Host "  [OK] $Text" -ForegroundColor Green }
function Write-LciaWarn { param([string]$Text) Write-Host "  [ATTENZIONE] $Text" -ForegroundColor Yellow }
function Write-LciaErr { param([string]$Text) Write-Host "  [ERRORE] $Text" -ForegroundColor Red }

# --------------------------------------------------------------------- input

function Read-LciaYesNo {
    <#
    .SYNOPSIS
    Chiede una conferma esplicita. Restituisce $true solo per s/si/sì/y/yes.
    #>
    param(
        [Parameter(Mandatory)][string]$Question,
        [switch]$DefaultYes
    )
    $suffix = if ($DefaultYes) { '[S/n]' } else { '[s/N]' }
    $answer = Read-Host "  $Question $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return [bool]$DefaultYes }
    $normalized = $answer.Trim().ToLowerInvariant()
    return @('s', 'si', 'sì', 'y', 'yes') -contains $normalized
}

function Read-LciaText {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [string]$Default = ''
    )
    $label = if ([string]::IsNullOrEmpty($Default)) { $Prompt } else { "$Prompt [$Default]" }
    $value = Read-Host "  $label"
    if ([string]::IsNullOrWhiteSpace($value)) { return $Default }
    return $value.Trim()
}

# --------------------------------------------------------------------- ambiente

function Test-LciaCommand {
    param([Parameter(Mandatory)][string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-LciaProjectRoot {
    <#
    .SYNOPSIS
    Restituisce la cartella del progetto (padre della cartella scripts) e verifica che sia quella giusta.
    #>
    param([Parameter(Mandatory)][string]$ScriptsDirectory)
    $root = (Resolve-Path (Join-Path $ScriptsDirectory '..')).Path
    $marker = Join-Path $root 'LocalCodeIntelligenceAnalyzer.slnx'
    if (-not (Test-Path -LiteralPath $marker)) {
        throw "Cartella di progetto non riconosciuta: manca LocalCodeIntelligenceAnalyzer.slnx in $root"
    }
    return $root
}

function Invoke-LciaGit {
    <#
    .SYNOPSIS
    Esegue git senza far fallire lo script per l'output su stderr; restituisce un oggetto con
    ExitCode e Output (righe di testo).
    #>
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = & git @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    $lines = ConvertFrom-LciaNativeOutput -InputObject @($raw)
    if ($code -ne 0 -and -not $AllowFailure) {
        throw ("git {0} non riuscito (codice {1}): {2}" -f ($Arguments -join ' '), $code, ($lines -join ' | '))
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $lines }
}

function ConvertFrom-LciaNativeOutput {
    <#
    .SYNOPSIS
    Converte l'output di un programma esterno catturato con 2>&1 in righe di testo. In Windows PowerShell 5.1
    le righe di stderr arrivano come ErrorRecord: si usa il loro messaggio e si scartano quelle vuote (che
    altrimenti verrebbero mostrate come "System.Management.Automation.RemoteException").
    #>
    param([AllowNull()][AllowEmptyCollection()][object[]]$InputObject)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($InputObject)) {
        if ($null -eq $item) { continue }
        if ($item -is [System.Management.Automation.ErrorRecord]) {
            $text = ''
            if ($null -ne $item.Exception) { $text = [string]$item.Exception.Message }
            if (-not [string]::IsNullOrWhiteSpace($text)) { $lines.Add($text.TrimEnd()) }
            continue
        }
        $lines.Add($item.ToString())
    }
    return ,$lines.ToArray()
}

function Get-LciaGitValue {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $result = Invoke-LciaGit -Arguments $Arguments -AllowFailure
    if ($result.ExitCode -ne 0 -or $result.Output.Count -eq 0) { return '' }
    return ($result.Output -join "`n").Trim()
}

function Test-LciaGitRepository {
    param([Parameter(Mandatory)][string]$Root)
    return (Test-Path -LiteralPath (Join-Path $Root '.git'))
}

# --------------------------------------------------------------------- file in attesa

function ConvertFrom-LciaPorcelain {
    <#
    .SYNOPSIS
    Interpreta l'output di "git status --porcelain=v1 -z": restituisce oggetti Status/Path.
    #>
    param([AllowEmptyString()][string]$Text)
    $result = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrEmpty($Text)) { return ,$result.ToArray() }
    $entries = $Text.Split([char]0)
    $i = 0
    while ($i -lt $entries.Length) {
        $entry = $entries[$i]
        $i++
        if ($entry.Length -lt 4) { continue }
        $status = $entry.Substring(0, 2)
        $path = $entry.Substring(3)
        $result.Add([pscustomobject]@{ Status = $status; Path = $path })
        if ($status.Contains('R') -or $status.Contains('C')) {
            $i++  # il token successivo è il percorso di origine della rinomina/copia
        }
    }
    return ,$result.ToArray()
}

function Get-LciaPendingFiles {
    param([Parameter(Mandatory)][string]$Root)
    Push-Location -LiteralPath $Root
    try {
        $result = Invoke-LciaGit -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '-z', '--untracked-files=all')
        return ConvertFrom-LciaPorcelain -Text ($result.Output -join "`n")
    } finally {
        Pop-Location
    }
}

# --------------------------------------------------------------------- controlli prima della pubblicazione

$script:ForbiddenPatterns = @(
    'bin/*', '*/bin/*', 'obj/*', '*/obj/*', '.vs/*', '*/.vs/*', 'TestResults/*', '*/TestResults/*',
    'artifacts/*', 'publish/*', 'exports/*', 'node_modules/*', '*/node_modules/*',
    '*.db', '*.db-wal', '*.db-shm', '*.db-journal', '*.sqlite', '*.sqlite3',
    '*.user', '*.suo', '*.log', '*.lcia-export.*',
    '*.pfx', '*.p12', '*.pem', '*.key', '*.snk', '*.publishsettings', '*.pubxml.user',
    '.env', '*/.env', '.env.*', '*/.env.*', 'secrets.json', '*/secrets.json',
    'id_rsa*', '*/id_rsa*', 'id_ed25519*', '*/id_ed25519*'
)

function Find-LciaForbiddenFiles {
    <#
    .SYNOPSIS
    Segnala file che non devono finire nel repository: output di build, database locali,
    log, esportazioni, certificati, chiavi e file di segreti, file più grandi di MaxSizeMB.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$RelativePaths,
        [Parameter(Mandatory)][string]$Root,
        [int]$MaxSizeMB = 10
    )
    $found = New-Object System.Collections.Generic.List[object]
    foreach ($relative in $RelativePaths) {
        $normalized = $relative.Replace('\', '/')
        foreach ($pattern in $script:ForbiddenPatterns) {
            if ($normalized -like $pattern) {
                $found.Add([pscustomobject]@{ Path = $normalized; Reason = "corrisponde a '$pattern'" })
                break
            }
        }
        $full = Join-Path $Root $normalized
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $size = (Get-Item -LiteralPath $full).Length
            if ($size -gt ($MaxSizeMB * 1MB)) {
                $found.Add([pscustomobject]@{ Path = $normalized; Reason = ('file di {0:N1} MB (limite {1} MB)' -f ($size / 1MB), $MaxSizeMB) })
            }
        }
    }
    return ,$found.ToArray()
}

$script:SecretRules = @(
    @{ Name = 'chiave privata';            Pattern = '-----BEGIN [A-Z0-9 ]*PRIVATE KEY( BLOCK)?-----' },
    @{ Name = 'token GitHub';              Pattern = '\b(gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{22,255})\b' },
    @{ Name = 'chiave AWS';                Pattern = '\b(AKIA|ASIA)[0-9A-Z]{16}\b' },
    @{ Name = 'token Slack';               Pattern = '\bxox[abposr]-[A-Za-z0-9-]{10,}' },
    @{ Name = 'chiave Google API';         Pattern = '\bAIza[0-9A-Za-z_\-]{35}' },
    @{ Name = 'chiave Stripe';             Pattern = '\b[rs]k_(live|test)_[0-9A-Za-z]{16,}' },
    @{ Name = 'chiave API (sk-)';          Pattern = '\bsk-(ant-|proj-)?[A-Za-z0-9_\-]{20,}' },
    @{ Name = 'JWT';                       Pattern = '\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}' },
    @{ Name = 'chiave account Azure';      Pattern = '(?i)\bAccountKey\s*=\s*[A-Za-z0-9+/=]{20,}' },
    @{ Name = 'password in connection string'; Pattern = '(?i)\b(server|data source|host|address)\s*=[^;"]+;[^"\r\n]*?\b(password|pwd)\s*=\s*(?![{$%<*;\s"\[])[^;"''<>\s]{4,}' },
    @{ Name = 'segreto in JSON';           Pattern = '(?i)"[\w.\-]*(password|passwd|secret|apikey|api_key|clientsecret|client_secret|accesskey|access_key)[\w.\-]*"\s*:\s*"(?![{$%<*\[])[^"]{6,}"' }
)

function Get-LciaAllowlist {
    param([Parameter(Mandatory)][string]$Root)
    $file = Join-Path $Root 'scripts/secret-scan-allowlist.txt'
    if (-not (Test-Path -LiteralPath $file)) { return @() }
    return @([System.IO.File]::ReadAllLines($file) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { $_.Replace('\', '/') })
}

function Find-LciaSecrets {
    <#
    .SYNOPSIS
    Cerca possibili segreti nei file indicati (solo regole ad alta affidabilità).
    I file elencati in scripts/secret-scan-allowlist.txt (dati fittizi di test) vengono saltati.
    Non mostra mai il valore trovato: solo file, riga e tipo.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$RelativePaths,
        [Parameter(Mandatory)][string]$Root
    )
    $allow = Get-LciaAllowlist -Root $Root
    $found = New-Object System.Collections.Generic.List[object]
    foreach ($relative in $RelativePaths) {
        $normalized = $relative.Replace('\', '/')
        $skip = $false
        foreach ($pattern in $allow) {
            if ($normalized -like $pattern) { $skip = $true; break }
        }
        if ($skip) { continue }

        $full = Join-Path $Root $normalized
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $item = Get-Item -LiteralPath $full
        if ($item.Length -gt 5MB) { continue }

        $bytes = [System.IO.File]::ReadAllBytes($full)
        $probe = [Math]::Min($bytes.Length, 8000)
        $binary = $false
        for ($b = 0; $b -lt $probe; $b++) {
            if ($bytes[$b] -eq 0) { $binary = $true; break }
        }
        if ($binary) { continue }

        $lines = [System.IO.File]::ReadAllLines($full)
        for ($n = 0; $n -lt $lines.Length; $n++) {
            foreach ($rule in $script:SecretRules) {
                if ($lines[$n] -match $rule.Pattern) {
                    $found.Add([pscustomobject]@{ Path = $normalized; Line = $n + 1; Rule = $rule.Name })
                }
            }
        }
    }
    return ,$found.ToArray()
}

# --------------------------------------------------------------------- firma dei commit (Verified)

function Get-LciaSigningStatus {
    <#
    .SYNOPSIS
    Verifica se i commit verranno firmati (necessario perché GitHub li mostri come "Verified").
    #>
    param([Parameter(Mandatory)][string]$Root)
    Push-Location -LiteralPath $Root
    try {
        $gpgSign = Get-LciaGitValue -Arguments @('config', '--get', 'commit.gpgsign')
        $format = Get-LciaGitValue -Arguments @('config', '--get', 'gpg.format')
        $key = Get-LciaGitValue -Arguments @('config', '--get', 'user.signingkey')
    } finally {
        Pop-Location
    }
    if ([string]::IsNullOrEmpty($format)) { $format = 'openpgp' }
    $messages = New-Object System.Collections.Generic.List[string]
    $enabled = $gpgSign -eq 'true'
    if (-not $enabled) { $messages.Add('commit.gpgsign non è attivo') }
    if ([string]::IsNullOrEmpty($key)) { $messages.Add('user.signingkey non è configurata') }
    if ($format -eq 'ssh' -and -not [string]::IsNullOrEmpty($key)) {
        $keyPath = [Environment]::ExpandEnvironmentVariables($key.Replace('~', $HOME))
        if ((Test-Path -LiteralPath $keyPath) -eq $false -and -not $key.StartsWith('ssh-') -and -not $key.StartsWith('key::')) {
            $messages.Add("la chiave pubblica indicata non esiste: $keyPath")
        }
    }
    return [pscustomobject]@{
        Enabled    = $enabled
        Format     = $format
        SigningKey = $key
        Ready      = ($enabled -and -not [string]::IsNullOrEmpty($key) -and $messages.Count -eq 0)
        Messages   = $messages.ToArray()
    }
}

function Set-LciaSshSigning {
    <#
    .SYNOPSIS
    Configura la firma SSH dei commit SOLO per questo repository (git config --local).
    #>
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$PublicKeyPath
    )
    if (-not (Test-Path -LiteralPath $PublicKeyPath)) {
        throw "Chiave pubblica non trovata: $PublicKeyPath"
    }
    $normalizedKey = (Resolve-Path -LiteralPath $PublicKeyPath).Path.Replace('\', '/')
    Push-Location -LiteralPath $Root
    try {
        Invoke-LciaGit -Arguments @('config', '--local', 'gpg.format', 'ssh') | Out-Null
        Invoke-LciaGit -Arguments @('config', '--local', 'user.signingkey', $normalizedKey) | Out-Null
        Invoke-LciaGit -Arguments @('config', '--local', 'commit.gpgsign', 'true') | Out-Null
        Invoke-LciaGit -Arguments @('config', '--local', 'tag.gpgsign', 'true') | Out-Null
    } finally {
        Pop-Location
    }
}

# --------------------------------------------------------------------- identità Git (autore dei commit)

function Get-LciaNoReplyEmail {
    <#
    .SYNOPSIS
    Indirizzo "noreply" di GitHub (ID+login@users.noreply.github.com): privato e valido per lo stato Verified.
    #>
    param(
        [Parameter(Mandatory)][string]$Login,
        [Parameter(Mandatory)][string]$Id
    )
    return ('{0}+{1}@users.noreply.github.com' -f $Id.Trim(), $Login.Trim())
}

function Get-LciaGitHubAccount {
    <#
    .SYNOPSIS
    Login, id e nome dell'account GitHub tramite GitHub CLI già autenticata; $null se non disponibile.
    Nessun token viene letto o mostrato.
    #>
    if (-not (Test-LciaCommand 'gh')) { return $null }
    $values = @{}
    foreach ($field in @('login', 'id', 'name')) {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $raw = & gh api user --jq ".$field" 2>$null
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previous
        }
        if ($code -ne 0) { return $null }
        $text = [string](@($raw) | Select-Object -First 1)
        if ($null -eq $text -or $text -eq 'null') { $text = '' }
        $values[$field] = $text.Trim()
    }
    if ([string]::IsNullOrEmpty($values['login']) -or [string]::IsNullOrEmpty($values['id'])) { return $null }
    return [pscustomobject]@{
        Login        = $values['login']
        Id           = $values['id']
        Name         = $values['name']
        NoReplyEmail = Get-LciaNoReplyEmail -Login $values['login'] -Id $values['id']
    }
}

function Get-LciaGitIdentity {
    param([Parameter(Mandatory)][string]$Root)
    Push-Location -LiteralPath $Root
    try {
        $name = Get-LciaGitValue -Arguments @('config', '--get', 'user.name')
        $email = Get-LciaGitValue -Arguments @('config', '--get', 'user.email')
    } finally {
        Pop-Location
    }
    return [pscustomobject]@{
        Name     = $name
        Email    = $email
        Complete = (-not [string]::IsNullOrWhiteSpace($name)) -and (-not [string]::IsNullOrWhiteSpace($email))
    }
}

function Initialize-LciaGitIdentity {
    <#
    .SYNOPSIS
    Verifica nome ed email dell'autore dei commit. Se mancano propone i dati dell'account GitHub
    (email "noreply") e li imposta solo dopo conferma, globalmente o solo per questo repository.
    Restituisce $true se l'identità è pronta.
    #>
    param([Parameter(Mandatory)][string]$Root)
    $identity = Get-LciaGitIdentity -Root $Root
    if ($identity.Complete) {
        Write-LciaOk "Identità Git: $($identity.Name) <$($identity.Email)>"
        Write-LciaInfo 'Per lo stato Verified l''email deve essere verificata sul tuo account GitHub (o essere l''indirizzo noreply).'
        return $true
    }

    Write-LciaWarn 'Identità Git non configurata: per creare un commit servono un nome e un indirizzo email.'
    $defaultName = $identity.Name
    $defaultEmail = $identity.Email
    $account = Get-LciaGitHubAccount
    if ($null -ne $account) {
        if ([string]::IsNullOrWhiteSpace($defaultName)) {
            $defaultName = $account.Name
            if ([string]::IsNullOrWhiteSpace($defaultName)) { $defaultName = $account.Login }
        }
        if ([string]::IsNullOrWhiteSpace($defaultEmail)) { $defaultEmail = $account.NoReplyEmail }
        Write-LciaInfo "Proposta dal tuo account GitHub ($($account.Login)). L'email proposta è l'indirizzo noreply di GitHub:"
        Write-LciaInfo 'mantiene privata la tua email reale ed è valida per lo stato Verified. Premi Invio per accettare.'
    } else {
        Write-LciaInfo 'Usa un''email verificata del tuo account GitHub oppure l''indirizzo noreply che trovi in'
        Write-LciaInfo 'GitHub > Settings > Emails (formato ID+utente@users.noreply.github.com).'
    }

    $name = ''
    while ([string]::IsNullOrWhiteSpace($name)) {
        $name = Read-LciaText -Prompt 'Nome per i commit' -Default $defaultName
    }
    $email = ''
    while ($email -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') {
        $email = Read-LciaText -Prompt 'Email per i commit' -Default $defaultEmail
        if ($email -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { Write-LciaWarn 'Indirizzo email non valido.' }
    }
    $useGlobal = Read-LciaYesNo 'Usarli per tutti i tuoi repository (git config --global)? Con n valgono solo per questo progetto' -DefaultYes
    $scope = '--local'
    if ($useGlobal) { $scope = '--global' }
    if (-not (Read-LciaYesNo "Impostare l'autore dei commit: $name <$email> ($scope)?" -DefaultYes)) {
        Write-LciaInfo 'Identità non impostata.'
        return $false
    }

    Push-Location -LiteralPath $Root
    try {
        Invoke-LciaGit -Arguments @('config', $scope, 'user.name', $name) | Out-Null
        Invoke-LciaGit -Arguments @('config', $scope, 'user.email', $email) | Out-Null
    } finally {
        Pop-Location
    }
    Write-LciaOk "Autore dei commit impostato: $name <$email> ($scope)"
    return $true
}

# --------------------------------------------------------------------- branch (Git Flow)

function Get-LciaBranchKind {
    <#
    .SYNOPSIS
    Tipo di branch secondo Git Flow: main, develop, feature, release, hotfix oppure other.
    #>
    param([AllowEmptyString()][string]$Name)
    switch -Regex ($Name) {
        '^(main|master)$' { return 'main' }
        '^develop$' { return 'develop' }
        '^feature/.+' { return 'feature' }
        '^release/.+' { return 'release' }
        '^hotfix/.+' { return 'hotfix' }
        default { return 'other' }
    }
}

function ConvertTo-LciaBranchSlug {
    <#
    .SYNOPSIS
    Converte un testo libero in un nome di branch valido: minuscole, senza accenti, parole separate da "-".
    #>
    param([AllowEmptyString()][string]$Text)
    $decomposed = ([string]$Text).Trim().ToLowerInvariant().Normalize([System.Text.NormalizationForm]::FormD)
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $decomposed.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -ne [System.Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($ch)
        }
    }
    $slug = [regex]::Replace($builder.ToString(), '[^a-z0-9._-]+', '-')
    $slug = [regex]::Replace($slug, '-{2,}', '-').Trim('-', '.')
    if ($slug.Length -gt 60) { $slug = $slug.Substring(0, 60).Trim('-', '.') }
    return $slug
}

function Test-LciaSemVer {
    param([AllowEmptyString()][string]$Version)
    return ([string]$Version) -match '^\d+\.\d+\.\d+$'
}

function Get-LciaNextVersion {
    <#
    .SYNOPSIS
    Versione successiva: major (X+1.0.0), minor (X.Y+1.0) o patch (X.Y.Z+1). Eventuale suffisso ignorato.
    #>
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][ValidateSet('major', 'minor', 'patch')][string]$Part
    )
    $core = ($Version.Trim().TrimStart('v', 'V') -split '[-+]')[0]
    $numbers = @(0, 0, 0)
    $parts = @($core -split '\.')
    for ($i = 0; $i -lt 3 -and $i -lt $parts.Count; $i++) {
        $value = 0
        if ([int]::TryParse($parts[$i], [ref]$value)) { $numbers[$i] = $value }
    }
    switch ($Part) {
        'major' { return '{0}.0.0' -f ($numbers[0] + 1) }
        'minor' { return '{0}.{1}.0' -f $numbers[0], ($numbers[1] + 1) }
        default { return '{0}.{1}.{2}' -f $numbers[0], $numbers[1], ($numbers[2] + 1) }
    }
}

function Set-LciaProjectVersion {
    <#
    .SYNOPSIS
    Imposta la versione in Directory.Build.props (primo elemento Version) mantenendo codifica e fine riga.
    Restituisce $true se il file è cambiato.
    #>
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Version
    )
    if (-not (Test-LciaSemVer $Version)) { throw "Versione non valida: $Version (formato X.Y.Z)" }
    $file = Join-Path $Root 'Directory.Build.props'
    $bytes = [System.IO.File]::ReadAllBytes($file)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.IO.File]::ReadAllText($file)
    $pattern = New-Object System.Text.RegularExpressions.Regex('<Version>[^<]*</Version>')
    if (-not $pattern.IsMatch($text)) { throw 'Directory.Build.props non contiene l''elemento <Version>.' }
    $updated = $pattern.Replace($text, "<Version>$Version</Version>", 1)
    if ($updated -eq $text) { return $false }
    [System.IO.File]::WriteAllText($file, $updated, (New-Object System.Text.UTF8Encoding($hasBom)))
    return $true
}

# --------------------------------------------------------------------- versione, changelog, URL

function Get-LciaProjectVersion {
    <#
    .SYNOPSIS
    Legge la versione del prodotto da Directory.Build.props (elemento Version).
    #>
    param([Parameter(Mandatory)][string]$Root)
    $file = Join-Path $Root 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $file)) { return '0.0.0' }
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($file)
    $node = $xml.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) { return '0.0.0' }
    return $node.InnerText.Trim()
}

function Compare-LciaVersion {
    <#
    .SYNOPSIS
    Confronta due versioni "x.y.z" (eventuale suffisso -preview ignorato): -1, 0 oppure 1.
    #>
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )
    $parse = {
        param([string]$Text)
        $core = ($Text.Trim().TrimStart('v', 'V') -split '[-+]')[0]
        $parts = @($core -split '\.' | Where-Object { $_ -ne '' })
        while ($parts.Count -lt 2) { $parts += '0' }
        $parsed = $null
        if ([version]::TryParse(($parts -join '.'), [ref]$parsed)) { return $parsed }
        return [version]'0.0'
    }
    $a = & $parse $Left
    $b = & $parse $Right
    return [Math]::Sign($a.CompareTo($b))
}

function Get-LciaChangelogSection {
    <#
    .SYNOPSIS
    Restituisce le righe della sezione "## [versione]" di CHANGELOG.md (senza il titolo),
    senza righe vuote iniziali o finali. Elenco vuoto se la sezione non esiste.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version
    )
    if (-not (Test-Path -LiteralPath $Path)) { return ,@() }
    $header = '^## \[' + [regex]::Escape($Version.TrimStart('v', 'V')) + '\]'
    $lines = New-Object System.Collections.Generic.List[string]
    $inside = $false
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line -match '^## ') {
            if ($inside) { break }
            if ($line -match $header) { $inside = $true }
            continue
        }
        if ($inside) { $lines.Add($line) }
    }
    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[0])) { $lines.RemoveAt(0) }
    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[$lines.Count - 1])) { $lines.RemoveAt($lines.Count - 1) }
    return ,$lines.ToArray()
}

function Set-LciaChangelogRelease {
    <#
    .SYNOPSIS
    Trasforma la sezione "## [Non rilasciato]" (o "## [Unreleased]") di CHANGELOG.md nella sezione della versione
    ("## [X.Y.Z] - data") e lascia sopra una nuova sezione "Non rilasciato" vuota. $true se il file è cambiato.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version,
        [string]$Date = (Get-Date).ToString('yyyy-MM-dd')
    )
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $text = [System.IO.File]::ReadAllText($Path)
    if ($text -match ('(?m)^## \[' + [regex]::Escape($Version) + '\]')) { return $false }
    $pattern = New-Object System.Text.RegularExpressions.Regex('(?m)^## \[(?:Non rilasciato|Unreleased)\][^\r\n]*')
    if (-not $pattern.IsMatch($text)) { return $false }
    $newline = "`n"
    if ($text.Contains("`r`n")) { $newline = "`r`n" }
    $replacement = '## [Non rilasciato]' + $newline + $newline + '## [' + $Version + '] - ' + $Date
    $updated = $pattern.Replace($text, $replacement.Replace('$', '$$'), 1)
    [System.IO.File]::WriteAllText($Path, $updated, (New-Object System.Text.UTF8Encoding($false)))
    return $true
}

function ConvertTo-LciaGitHubWebUrl {
    <#
    .SYNOPSIS
    Converte l'URL di un remote GitHub (HTTPS o SSH) nell'indirizzo web del repository,
    senza eventuali credenziali. Stringa vuota se non è un repository GitHub.
    #>
    param([AllowEmptyString()][AllowNull()][string]$RemoteUrl)
    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) { return '' }
    $url = $RemoteUrl.Trim()
    $patterns = @(
        '^git@github\.com:(?<path>[^/\s]+/[^/\s]+?)(?:\.git)?/?$',
        '^ssh://git@github\.com/(?<path>[^/\s]+/[^/\s]+?)(?:\.git)?/?$',
        '^https?://(?:[^@/\s]+@)?github\.com/(?<path>[^/\s]+/[^/\s]+?)(?:\.git)?/?$'
    )
    foreach ($pattern in $patterns) {
        $match = [regex]::Match($url, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) { return 'https://github.com/' + $match.Groups['path'].Value }
    }
    return ''
}

function Test-LciaHeadSigned {
    param([Parameter(Mandatory)][string]$Root)
    Push-Location -LiteralPath $Root
    try {
        $content = Invoke-LciaGit -Arguments @('cat-file', 'commit', 'HEAD') -AllowFailure
        return (($content.Output -join "`n") -match '(?m)^gpgsig ')
    } finally {
        Pop-Location
    }
}

Export-ModuleMember -Function *-Lcia*
