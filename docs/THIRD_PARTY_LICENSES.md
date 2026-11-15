# Dipendenze e licenze

Versioni verificate su nuget.org a novembre 2026. Solo componenti gratuiti con licenze compatibili con l'uso
commerciale. Le versioni sono centralizzate in `Directory.Packages.props`.

## Usate nelle versioni 0.2.x

| Pacchetto | Versione | Licenza | Uso |
|---|---|---|---|
| Microsoft.CodeAnalysis.CSharp (Roslyn) | 5.0.0 | MIT | Analisi sintattica C# |
| Microsoft.Data.Sqlite | 10.0.11 | MIT | Indice locale |
| SQLitePCLRaw.bundle_e_sqlite3 (dipendenza) | 2.1.x | Apache-2.0 | Libreria nativa SQLite con FTS5 |
| SQLite | 3.x | Pubblico dominio | Motore del database |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | MIT | Dependency injection |
| Microsoft.Extensions.Logging (+ Abstractions) | 10.0.0 | MIT | Logging |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM (source generator) |
| AvalonEdit | 6.3.0.90 | MIT | Visualizzatore di codice con numeri di riga |
| .NET 10 / WPF | 10.0 | MIT | Runtime e interfaccia |

## Solo per i test

| Pacchetto | Versione | Licenza |
|---|---|---|
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 18.3.0 | MIT |
| Pester (PowerShell, solo CI) | 5.x | Apache-2.0 |

## Previste nelle fasi successive (licenza già verificata)

| Componente | Licenza | Fase | Note |
|---|---|---|---|
| Microsoft.CodeAnalysis.Workspaces.MSBuild + Microsoft.Build.Locator | MIT | 3 | Caricamento di soluzioni e analisi semantica |
| Microsoft.SqlServer.TransactSql.ScriptDom | MIT | 4 | Parser T-SQL (open source dal 2023) |
| LibGit2Sharp | MIT (libgit2: GPLv2 con linking exception) | 7 | In alternativa Git CLI in sola lettura |
| MSAGL (Microsoft Automatic Graph Layout) | MIT | 3/5 | Grafo delle dipendenze navigabile |
| Tree-sitter | MIT | 9 | Parsing sintattico di altri linguaggi |
| Ollama | MIT | 8 | Servizio esterno opzionale via HTTP locale, nessun pacchetto richiesto |

Le versioni dei componenti futuri verranno fissate e riverificate quando saranno introdotti.
