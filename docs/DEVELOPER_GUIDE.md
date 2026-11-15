# Guida per sviluppatori

## Struttura

```text
src/
  Lcia.Domain               modelli puri (file, simboli, relazioni, progetti, linguaggi)
  Lcia.Application          contratti dei motori, IndexingService, testo, percorsi, segreti, query FTS
  Lcia.Infrastructure       SQLite (schema, store, ricerca), lettura file, Git in sola lettura, log, impostazioni
  Lcia.Scanner              scansione del file system, soluzioni e progetti
  Lcia.Analysis.CSharp      analisi C# con Roslyn (sintassi)
  Lcia.Export               Full Project Context Export (TXT, Markdown), suddivisione in parti
  Lcia.Hosting              composizione dei servizi (DI), condivisa da App, CLI e test
  Lcia.App                  WPF (MVVM, CommunityToolkit.Mvvm, AvalonEdit)
  Lcia.Cli                  riga di comando "lcia"
tests/
  Lcia.Tests                xUnit: unità e integrazione su samples/DemoShop
  scripts                   Pester per i moduli PowerShell
samples/DemoShop            progetto dimostrativo (escluso dalla build della solution)
scripts/                    build, distribuzione, pubblicazione su GitHub
```

Regola di dipendenza: `Domain ← Application ← (Infrastructure, Scanner, Analysis.CSharp, Export) ← Hosting ← (App, Cli, Tests)`.
I motori non si conoscono tra loro: comunicano tramite le interfacce di `Lcia.Application`.

## Comandi

```powershell
dotnet build LocalCodeIntelligenceAnalyzer.slnx
dotnet test  LocalCodeIntelligenceAnalyzer.slnx
dotnet test  --filter "FullyQualifiedName~Search"          # un sottoinsieme
dotnet run   --project src\Lcia.Cli -- index samples\DemoShop --data-dir $env:TEMP\lcia-dev
Invoke-Pester tests\scripts                                 # Pester 5
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Usa sempre `--data-dir` (o `LCIA_DATA_DIR`) durante lo sviluppo per non sporcare l'indice personale.

## Aggiungere un analizzatore di linguaggio

1. Implementa `ISourceAnalyzer` (in un nuovo progetto `Lcia.Analysis.<Linguaggio>`): `Name`,
   `CanAnalyze(SourceLanguage)` e `Analyze(relativePath, text, cancellationToken)` che restituisce
   `SourceAnalysisResult` con simboli, relazioni e diagnostiche. Niente I/O: il testo arriva già decodificato.
2. Registra l'implementazione in `Lcia.Hosting/ServiceCollectionExtensions.cs` (più analizzatori possono
   coesistere; viene usato il primo che accetta il linguaggio).
3. Aggiorna il livello di supporto in `LanguageCatalog` e la tabella in `docs/ARCHITECTURE.md`.
4. Incrementa `IndexFormat.Version` se cambia ciò che viene salvato: forza la rianalisi dei file già indicizzati.
5. Aggiungi test di unità sull'analizzatore e, se serve, file al progetto DemoShop con test di integrazione.

## Relazioni ed evidenze

Ogni `SymbolRelation` ha un `EvidenceLevel`:

| Livello | Quando |
|---|---|
| `DirectVerified` | Il nome è risolto dal compilatore (Fase 3) o la sintassi non è ambigua (es. `AddScoped<I, T>()`) |
| `IndirectVerified` | Catena di relazioni verificate |
| `TextualReference` | Solo corrispondenza di nome (analisi sintattica) |
| `InferredWithEvidence` | Deduzione con indizi conservati nel campo evidenza |
| `Dynamic` | Reflection, factory, SQL dinamico: non risolvibile staticamente |

Non promuovere mai una relazione a un livello superiore senza la prova corrispondente.

## Database

Schema e migrazioni: `docs/DATABASE_SCHEMA.md` e `src/Lcia.Infrastructure/Sqlite/SqliteSchema.cs`.
Per cambiare lo schema aggiungi una migrazione (nuovo `user_version`), mai modificare quelle esistenti.
Le scritture sono in batch da 100 file per transazione, da un solo consumatore; le letture usano connessioni
separate (WAL).

## UI

- Ogni pagina è un `PageViewModel`; le viste sono associate tramite `DataTemplate` per tipo in `MainWindow.xaml`.
- Stato condiviso (repository corrente, indicizzazione) in `WorkspaceContext`; selezione per l'export in `ExportSelection`.
- Comunicazione tra pagine con `WeakReferenceMessenger` (es. `OpenFileMessage`).
- Nel progetto WPF il namespace `Lcia.Application` nasconde `System.Windows.Application`: usa il nome completo.
- **Colori**: mai valori fissi nelle viste. Usa le chiavi `Lcia.*` delle palette (`Resources/Theme.Light.xaml` e
  `Theme.Dark.xaml`, da tenere allineate) con `DynamicResource`, oppure lascia i colori al tema Fluent.
  Nuove chiavi vanno aggiunte a entrambe le palette.
- **Guida**: il manuale in linea è `docs/USER_GUIDE.md` (risorsa incorporata). Aggiornalo insieme alle funzioni;
  `MarkdownLiteTests` verifica che si legga correttamente (titoli, tabelle, codice).
- Lavoro pesante sempre fuori dal thread UI; aggiornamenti dell'interfaccia con `Dispatcher` o `IProgress<T>`.

## Sicurezza (non negoziabile)

- I percorsi letti passano da `PathUtilities.ResolveInside` (protezione da path traversal); i link che escono dalla
  radice non vengono seguiti.
- Nessun processo esterno sui repository analizzati (Git letto come file).
- Nessun valore segreto in log, messaggi di errore o export (salvo conferma esplicita dell'utente per l'export).
- Le esportazioni non possono essere scritte dentro il repository analizzato.

## Release

1. Aggiorna `Version` in `Directory.Build.props`, aggiungi la sezione `## [X.Y.Z]` in `CHANGELOG.md`, aggiorna `ROADMAP.md`.
2. `GitHub-2-invia-modifiche.bat` (commit firmato e push).
3. `GitHub-3-crea-release.bat`: controlla che tutto sia inviato, crea il tag firmato `vX.Y.Z` e lo invia.
4. Il workflow `release.yml` compila, testa, crea lo zip win-x64 e la release con il testo del CHANGELOG.

Manualmente: `git tag -s v0.2.1 -m "Local Code Intelligence Analyzer 0.2.1"` e `git push origin v0.2.1`.

## Script di installazione

`scripts/install-app.ps1` usa `scripts/lib/LciaInstallTools.psm1` (funzioni testate in
`tests/scripts/LciaInstallTools.Tests.ps1`). `scripts/uninstall-app.ps1` è volutamente autonomo: viene copiato
come `uninstall.ps1` nella cartella del programma ed eseguito da Impostazioni > App. I `.bat` nella radice
contengono solo la chiamata a PowerShell in un blocco `( ... )`, così possono essere aggiornati anche mentre
sono in esecuzione.
