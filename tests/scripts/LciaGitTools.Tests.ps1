# Test Pester 5 per le funzioni degli script di pubblicazione.
# Esecuzione: Invoke-Pester -Path .\tests\scripts -Output Detailed

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\LciaGitTools.psm1') -Force
}

Describe 'ConvertFrom-LciaPorcelain' {
    It 'interpreta file nuovi, modificati, eliminati e rinominati' {
        $nul = [char]0
        $text = "?? src/new.cs$nul M src/changed.cs$nul D old.txt${nul}R  renamed.cs${nul}original.cs$nul"
        $entries = ConvertFrom-LciaPorcelain -Text $text
        $entries.Count | Should -Be 4
        ($entries | Where-Object Path -eq 'src/new.cs').Status | Should -Be '??'
        ($entries | Where-Object Path -eq 'renamed.cs').Status | Should -Be 'R '
        $entries.Path | Should -Not -Contain 'original.cs'
    }

    It 'restituisce un elenco vuoto senza modifiche' {
        (ConvertFrom-LciaPorcelain -Text '').Count | Should -Be 0
    }
}

Describe 'Find-LciaForbiddenFiles' {
    It 'blocca output di build, database, log, esportazioni e file di segreti' {
        $paths = @('src/App/bin/Debug/App.dll', 'src/App/obj/x.json', 'index.db', 'logs/app.log', '.env', 'config/secrets.json', 'certs/dev.pfx', 'src/Program.cs')
        $result = Find-LciaForbiddenFiles -RelativePaths $paths -Root $TestDrive
        $result.Path | Should -Contain 'src/App/bin/Debug/App.dll'
        $result.Path | Should -Contain 'index.db'
        $result.Path | Should -Contain '.env'
        $result.Path | Should -Contain 'config/secrets.json'
        $result.Path | Should -Contain 'certs/dev.pfx'
        $result.Path | Should -Not -Contain 'src/Program.cs'
    }

    It 'segnala i file troppo grandi' {
        $big = Join-Path $TestDrive 'big.cs'
        $stream = [System.IO.File]::Create($big)
        $stream.SetLength(2MB)
        $stream.Dispose()
        $result = Find-LciaForbiddenFiles -RelativePaths @('big.cs') -Root $TestDrive -MaxSizeMB 1
        $result.Count | Should -Be 1
    }
}

Describe 'Find-LciaSecrets' {
    BeforeEach {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    }

    It 'trova token, chiavi private e password in connection string senza mostrarne il valore' {
        $token = 'ghp_' + ('a' * 36)
        $begin = '-----BEGIN ' + 'OPENSSH PRIVATE KEY-----'
        $conn = 'Server=db;Database=x;Password=' + 'RealPass99'
        Set-Content -LiteralPath (Join-Path $root 'a.cs') -Value @("var t = `"$token`";", $begin, "`"$conn`"")
        $result = Find-LciaSecrets -RelativePaths @('a.cs') -Root $root
        $result.Rule | Should -Contain 'token GitHub'
        $result.Rule | Should -Contain 'chiave privata'
        $result.Rule | Should -Contain 'password in connection string'
        ($result | Where-Object Rule -eq 'token GitHub').Line | Should -Be 1
        ($result | ForEach-Object { "$($_.Path) $($_.Rule)" }) -join ' ' | Should -Not -Match 'RealPass99'
    }

    It 'ignora segnaposto e codice che legge password da variabili' {
        Set-Content -LiteralPath (Join-Path $root 'b.json') -Value @('{ "Password": "${DB_PASSWORD}" }', '{ "Shop": "Server=.;Password={0};" }')
        Set-Content -LiteralPath (Join-Path $root 'c.cs') -Value @('var password = request.Password;')
        (Find-LciaSecrets -RelativePaths @('b.json', 'c.cs') -Root $root).Count | Should -Be 0
    }

    It 'rispetta la allowlist dei dati fittizi' {
        Set-Content -LiteralPath (Join-Path $root 'scripts\secret-scan-allowlist.txt') -Value @('# commento', 'fixtures/*.json')
        New-Item -ItemType Directory -Path (Join-Path $root 'fixtures') -Force | Out-Null
        $fake = '{ "Db": "Server=x;Password=' + 'FakeValue1;" }'
        Set-Content -LiteralPath (Join-Path $root 'fixtures\demo.json') -Value $fake
        (Find-LciaSecrets -RelativePaths @('fixtures/demo.json') -Root $root).Count | Should -Be 0
    }
}

Describe 'Read-LciaYesNo' {
    It 'accetta solo risposte affermative esplicite' {
        Mock -ModuleName LciaGitTools Read-Host { 'sì' }
        Read-LciaYesNo -Question 'Procedere?' | Should -BeTrue
        Mock -ModuleName LciaGitTools Read-Host { '' }
        Read-LciaYesNo -Question 'Procedere?' | Should -BeFalse
        Mock -ModuleName LciaGitTools Read-Host { 'forse' }
        Read-LciaYesNo -Question 'Procedere?' | Should -BeFalse
    }
}

Describe 'Repository del progetto' {
    It 'non contiene segreti fuori dalla allowlist' {
        $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        $files = Get-ChildItem -LiteralPath $root -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](\.git|bin|obj|artifacts|TestResults)[\\/]' } |
            ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') }
        $found = Find-LciaSecrets -RelativePaths $files -Root $root
        ($found | ForEach-Object { "$($_.Path):$($_.Line) $($_.Rule)" }) | Should -BeNullOrEmpty
    }
}

Describe 'Versione e changelog' {
    It 'legge la versione da Directory.Build.props' {
        $root = Join-Path $TestDrive 'ver'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Value '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><PropertyGroup><Version>1.4.2</Version></PropertyGroup></Project>'
        Get-LciaProjectVersion -Root $root | Should -Be '1.4.2'
    }

    It 'restituisce 0.0.0 senza Directory.Build.props' {
        Get-LciaProjectVersion -Root $TestDrive | Should -Be '0.0.0'
    }

    It 'confronta le versioni numericamente' {
        Compare-LciaVersion -Left '0.10.0' -Right '0.9.9' | Should -Be 1
        Compare-LciaVersion -Left 'v1.2.0' -Right '1.2' | Should -Be 0
        Compare-LciaVersion -Left '0.2.0' -Right '0.2.1' | Should -Be -1
        Compare-LciaVersion -Left '1.0.0-preview' -Right '1.0.0' | Should -Be 0
    }

    It 'estrae solo la sezione della versione richiesta' {
        $file = Join-Path $TestDrive 'CHANGELOG.md'
        Set-Content -LiteralPath $file -Value @('# Changelog', '', '## [0.3.0] - 2026-12-01', '', '### Aggiunto', '- uno', '', '## [0.2.0] - 2026-11-15', '- due')
        $notes = Get-LciaChangelogSection -Path $file -Version '0.3.0'
        ($notes -join '|') | Should -Be '### Aggiunto|- uno'
        ((Get-LciaChangelogSection -Path $file -Version 'v0.2.0') -join '|') | Should -Be '- due'
        (Get-LciaChangelogSection -Path $file -Version '9.9.9').Count | Should -Be 0
    }
}

Describe 'ConvertTo-LciaGitHubWebUrl' {
    It 'riconosce HTTPS e SSH e rimuove le credenziali' {
        ConvertTo-LciaGitHubWebUrl 'https://github.com/utente/Repo.git' | Should -Be 'https://github.com/utente/Repo'
        ConvertTo-LciaGitHubWebUrl 'git@github.com:utente/Repo.git' | Should -Be 'https://github.com/utente/Repo'
        ConvertTo-LciaGitHubWebUrl 'ssh://git@github.com/utente/Repo' | Should -Be 'https://github.com/utente/Repo'
        ConvertTo-LciaGitHubWebUrl 'https://qualcuno@github.com/utente/Repo.git' | Should -Be 'https://github.com/utente/Repo'
    }

    It 'ignora remote non GitHub' {
        ConvertTo-LciaGitHubWebUrl 'https://example.com/a/b.git' | Should -Be ''
        ConvertTo-LciaGitHubWebUrl '' | Should -Be ''
    }
}

Describe 'ConvertFrom-LciaNativeOutput' {
    It 'usa il testo delle righe di stderr e scarta quelle vuote' {
        $empty = New-Object System.Management.Automation.ErrorRecord((New-Object System.Management.Automation.RemoteException('')), 'NativeCommandError', 'NotSpecified', $null)
        $message = New-Object System.Management.Automation.ErrorRecord((New-Object System.Management.Automation.RemoteException('fatal: errore di prova')), 'NativeCommandError', 'NotSpecified', $null)
        $lines = ConvertFrom-LciaNativeOutput -InputObject @('riga stdout', $empty, $message)
        ($lines -join '|') | Should -Be 'riga stdout|fatal: errore di prova'
        ($lines -join '|') | Should -Not -Match 'RemoteException'
    }

    It 'restituisce un elenco vuoto senza output' {
        (ConvertFrom-LciaNativeOutput -InputObject @()).Count | Should -Be 0
        (ConvertFrom-LciaNativeOutput -InputObject $null).Count | Should -Be 0
    }
}

Describe 'Get-LciaNoReplyEmail' {
    It 'compone l''indirizzo noreply di GitHub' {
        Get-LciaNoReplyEmail -Login 'utente' -Id '12345' | Should -Be '12345+utente@users.noreply.github.com'
    }
}

Describe 'Git Flow' {
    It 'riconosce il tipo di branch' {
        Get-LciaBranchKind 'main' | Should -Be 'main'
        Get-LciaBranchKind 'master' | Should -Be 'main'
        Get-LciaBranchKind 'develop' | Should -Be 'develop'
        Get-LciaBranchKind 'feature/ricerca-sql' | Should -Be 'feature'
        Get-LciaBranchKind 'release/0.3.0' | Should -Be 'release'
        Get-LciaBranchKind 'hotfix/0.2.3' | Should -Be 'hotfix'
        Get-LciaBranchKind 'feature/' | Should -Be 'other'
        Get-LciaBranchKind 'prova' | Should -Be 'other'
    }

    It 'crea nomi di branch validi da testo libero' {
        ConvertTo-LciaBranchSlug 'Ricerca SQL & più filtri!' | Should -Be 'ricerca-sql-piu-filtri'
        ConvertTo-LciaBranchSlug '  --Tema Scuro--  ' | Should -Be 'tema-scuro'
        ConvertTo-LciaBranchSlug '***' | Should -Be ''
    }

    It 'calcola la versione successiva' {
        Get-LciaNextVersion -Version '0.2.3' -Part 'minor' | Should -Be '0.3.0'
        Get-LciaNextVersion -Version '0.2.3' -Part 'patch' | Should -Be '0.2.4'
        Get-LciaNextVersion -Version 'v1.9.9-preview' -Part 'major' | Should -Be '2.0.0'
        Test-LciaSemVer '1.2.3' | Should -BeTrue
        Test-LciaSemVer '1.2' | Should -BeFalse
    }

    It 'aggiorna solo la versione in Directory.Build.props' {
        $root = Join-Path $TestDrive 'props'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $file = Join-Path $root 'Directory.Build.props'
        [System.IO.File]::WriteAllText($file, "<Project>`r`n  <PropertyGroup>`r`n    <Version>0.2.3</Version>`r`n  </PropertyGroup>`r`n</Project>`r`n")
        Set-LciaProjectVersion -Root $root -Version '0.3.0' | Should -BeTrue
        Get-LciaProjectVersion -Root $root | Should -Be '0.3.0'
        [System.IO.File]::ReadAllText($file) | Should -Match "`r`n    <Version>0.3.0</Version>`r`n"
        Set-LciaProjectVersion -Root $root -Version '0.3.0' | Should -BeFalse
        { Set-LciaProjectVersion -Root $root -Version '0.3' } | Should -Throw
    }
}
