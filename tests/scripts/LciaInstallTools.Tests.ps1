# Test Pester 5 per le funzioni di installazione e di aggiornamento dei sorgenti.
# Esecuzione: Invoke-Pester -Path .\tests\scripts -Output Detailed

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\LciaInstallTools.psm1') -Force

    function New-TestFile {
        param([string]$Root, [string]$RelativePath, [string]$Content)
        $path = Join-Path $Root $RelativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [System.IO.File]::WriteAllText($path, $Content)
    }
}

Describe 'PATH dell''utente' {
    It 'aggiunge la cartella una sola volta' {
        $value = Add-LciaPathEntry -PathValue 'C:\Tools;%USERPROFILE%\bin' -Entry 'C:\App'
        $value | Should -Be 'C:\Tools;%USERPROFILE%\bin;C:\App'
        Add-LciaPathEntry -PathValue $value -Entry 'C:\App\' | Should -Be $value
        Add-LciaPathEntry -PathValue '' -Entry 'C:\App' | Should -Be 'C:\App'
    }

    It 'rimuove la cartella senza toccare le altre voci' {
        Remove-LciaPathEntry -PathValue 'C:\Tools;c:\app\;%USERPROFILE%\bin' -Entry 'C:\App' | Should -Be 'C:\Tools;%USERPROFILE%\bin'
        Remove-LciaPathEntry -PathValue 'C:\Tools' -Entry 'C:\App' | Should -Be 'C:\Tools'
    }
}

Describe 'Install-LciaDirectory' {
    It 'installa e poi sostituisce la versione precedente' {
        $source1 = Join-Path $TestDrive 'v1'
        $source2 = Join-Path $TestDrive 'v2'
        $target = Join-Path $TestDrive 'Programs\LocalCodeIntelligenceAnalyzer'
        New-TestFile $source1 'app.txt' 'uno'
        New-TestFile $source1 'old-only.txt' 'vecchio'
        New-TestFile $source2 'app.txt' 'due'
        New-TestFile $source2 'sub\nuovo.txt' 'nuovo'
        $extra = Join-Path $TestDrive 'extra.ps1'
        Set-Content -LiteralPath $extra -Value '# extra'

        Install-LciaDirectory -SourceDirectory $source1 -TargetDirectory $target
        Get-Content -LiteralPath (Join-Path $target 'app.txt') | Should -Be 'uno'

        Install-LciaDirectory -SourceDirectory $source2 -TargetDirectory $target -ExtraFiles @{ 'uninstall.ps1' = $extra }
        Get-Content -LiteralPath (Join-Path $target 'app.txt') | Should -Be 'due'
        Test-Path -LiteralPath (Join-Path $target 'old-only.txt') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $target 'sub\nuovo.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $target 'uninstall.ps1') | Should -BeTrue
        Test-Path -LiteralPath "$target.new" | Should -BeFalse
        Test-Path -LiteralPath "$target.old" | Should -BeFalse
    }

    It 'rifiuta una cartella estranea non vuota' {
        $foreign = Join-Path $TestDrive 'altro\LocalCodeIntelligenceAnalyzer'
        New-TestFile $foreign 'documento.txt' 'mio'
        { Assert-LciaInstallDirectory -InstallDirectory $foreign } | Should -Throw
        { Assert-LciaInstallDirectory -InstallDirectory (Join-Path $TestDrive 'NomeSbagliato') } | Should -Throw
        { Assert-LciaInstallDirectory -InstallDirectory (Join-Path $TestDrive 'nuova\LocalCodeIntelligenceAnalyzer') } | Should -Not -Throw
    }
}

Describe 'Confronto dei sorgenti' {
    BeforeEach {
        $current = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $incoming = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-TestFile $current 'src/Uguale.cs' "class A {}`r`n"
        New-TestFile $current 'src/Modificato.cs' 'class B {}'
        New-TestFile $current 'src/Rimosso.cs' 'class C {}'
        New-TestFile $current 'src/bin/Debug/out.dll' 'binario'
        New-TestFile $current '.git/config' '[core]'
        New-TestFile $current 'App.csproj.user' 'locale'
        New-TestFile $incoming 'src/Uguale.cs' "class A {}`n"
        New-TestFile $incoming 'src/Modificato.cs' 'class B { int x; }'
        New-TestFile $incoming 'docs/Nuovo.md' '# nuovo'
    }

    It 'esclude output di build, .git e file utente' {
        $files = Get-LciaSourceFiles -Root $current
        $files | Should -Contain 'src/Uguale.cs'
        $files | Should -Not -Contain 'src/bin/Debug/out.dll'
        $files | Should -Not -Contain '.git/config'
        $files | Should -Not -Contain 'App.csproj.user'
    }

    It 'classifica nuovi, modificati, rimossi e invariati ignorando CRLF/LF' {
        $diff = Compare-LciaSourceTrees -CurrentRoot $current -NewRoot $incoming
        ($diff.Added -join ',') | Should -Be 'docs/Nuovo.md'
        ($diff.Changed -join ',') | Should -Be 'src/Modificato.cs'
        ($diff.Removed -join ',') | Should -Be 'src/Rimosso.cs'
        ($diff.Unchanged -join ',') | Should -Be 'src/Uguale.cs'
    }

    It 'trova la radice del progetto dentro lo zip estratto' {
        New-TestFile $incoming 'LocalCodeIntelligenceAnalyzer/LocalCodeIntelligenceAnalyzer.slnx' '<Solution />'
        Find-LciaProjectRoot -Directory $incoming | Should -Be (Join-Path $incoming 'LocalCodeIntelligenceAnalyzer')
        Find-LciaProjectRoot -Directory $current | Should -BeNullOrEmpty
    }
}

Describe 'Expand-LciaZip' {
    It 'estrae uno zip valido' {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $source = Join-Path $TestDrive 'zipsrc'
        New-TestFile $source 'Progetto/file.txt' 'contenuto'
        $zip = Join-Path $TestDrive 'ok.zip'
        [System.IO.Compression.ZipFile]::CreateFromDirectory($source, $zip)
        $destination = Join-Path $TestDrive 'zipout'
        Expand-LciaZip -ZipPath $zip -Destination $destination
        Get-Content -LiteralPath (Join-Path $destination 'Progetto/file.txt') | Should -Be 'contenuto'
    }

    It 'rifiuta percorsi che escono dalla destinazione' {
        Add-Type -AssemblyName System.IO.Compression
        $zip = Join-Path $TestDrive 'evil.zip'
        $stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
        $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
        $entry = $archive.CreateEntry('../fuori.txt')
        $writer = New-Object System.IO.StreamWriter($entry.Open())
        $writer.Write('x')
        $writer.Dispose()
        $archive.Dispose()
        $stream.Dispose()
        { Expand-LciaZip -ZipPath $zip -Destination (Join-Path $TestDrive 'evilout') } | Should -Throw
        Test-Path -LiteralPath (Join-Path $TestDrive 'fuori.txt') | Should -BeFalse
    }
}
