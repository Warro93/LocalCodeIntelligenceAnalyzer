# Local Code Intelligence Analyzer

Applicazione desktop per **Windows 11 x64**, gratuita e interamente **locale**, che analizza repository Git e
progetti software (.NET, SQL Server e altri linguaggi), ricostruisce strutture e relazioni e produce un
**dossier tecnico completo** — con il codice sorgente integrale — utilizzabile con qualsiasi AI.

- Nessun upload del codice, nessuna telemetria, nessun servizio cloud obbligatorio.
- Il codice analizzato non viene mai eseguito; i repository sono aperti in **sola lettura**.
- Funziona **senza AI**: l'integrazione con Ollama (AI locale) sarà opzionale (Fase 8).

> **Versione 0.2.3 — Fasi 1 e 2 (MVP) con tema scuro, guida in linea e Git Flow.** Lo stato verificato di ogni fase è in
> [ROADMAP.md](ROADMAP.md).

## Strumenti con doppio clic

Nella cartella principale del progetto (nessun privilegio di amministratore, nessun token):

| File | Cosa fa |
|---|---|
| `Compila-e-testa.bat` | Compila, esegue tutti i test e una prova end-to-end della CLI su `samples\DemoShop` |
| `Installa-o-aggiorna-app.bat` | Compila, testa e **installa o aggiorna** l'app per il tuo utente: menu Start, desktop (facoltativo), comando `lcia` nel PATH (facoltativo), voce in *App installate* |
| `Disinstalla-app.bat` | Rimuove programma, collegamenti, voce nel PATH e in *App installate*; i dati solo su conferma |
| `Aggiorna-sorgenti-da-zip.bat` | Applica al progetto una **nuova versione ricevuta come zip** (mostra nuovi/modificati/rimossi, salva una copia dei file sostituiti), poi propone installazione e push |
| `GitHub-1-prima-pubblicazione.bat` | Git locale, creazione del repository su GitHub (privato o pubblico), primo commit firmato e push |
| `GitHub-2-invia-modifiche.bat` | Controllo di file e segreti, commit **firmato** e push, con conferma a ogni passo |
| `GitHub-3-crea-release.bat` | Tag firmato `vX.Y.Z` della versione corrente: GitHub Actions compila, testa e pubblica la release con lo zip win-x64 |
| `GitHub-4-branch-git-flow.bat` | Branch secondo **Git Flow**: crea `develop`, avvia `feature/*`, `release/X.Y.Z`, `hotfix/X.Y.Z` e li chiude con merge firmati, tag e pulizia |

I `.bat` eseguono gli script PowerShell in `scripts\` (leggibili e modificabili); ogni operazione su GitHub
chiede conferma esplicita.

## Funzionalità della versione 0.2.x

| Area | Cosa fa | Stato |
|---|---|---|
| Repository Scanner | Cartelle, repository Git, soluzioni `.sln`/`.slnx`; esclusioni configurabili (`.git`, `bin`, `obj`, `node_modules`, …); limiti di dimensione e profondità; protezione da link simbolici esterni | Implementato |
| Indicizzazione | SQLite locale + FTS5; hash SHA-256; scansione **incrementale**; elaborazione parallela; annullamento e ripresa | Implementato |
| Analisi C# | Roslyn (sintassi): namespace, tipi, membri, firme, righe, documentazione XML, attributi, tipi base, dipendenze, registrazioni DI, invocazioni | Implementato |
| Soluzioni e progetti | `.sln`, `.slnx`, `.csproj`/`.vbproj`/`.fsproj`/`.sqlproj`, NuGet (anche Central Package Management e `packages.config`) | Implementato |
| Ricerca | Parole chiave (FTS5), testo esatto, regex, simboli, riferimenti; filtri combinabili; percorso e riga | Implementato |
| Full Project Context Export | TXT e Markdown, codice integrale, delimitatori per file, simboli e dipendenze, suddivisione in parti con file indice, stima token, **oscuramento segreti** | Implementato |
| Protezione dei dati sensibili | Connection string, password, token e chiavi oscurati anche mentre consulti, cerchi e copi per un'AI online; email e IP opzionali | Implementato |
| Interfaccia | WPF (MVVM): Dashboard, Esplora (codice con numeri di riga), Cerca, Esporta, Log, Impostazioni; **tema chiaro/scuro** (anche "come Windows"); scorciatoie da tastiera | Implementato |
| Guida in linea | Pagina **Guida** (F1) con il manuale completo, indice degli argomenti, ricerca e scorciatoie | Implementato |
| CLI | `lcia index / search / export / stats / repos / remove` | Implementato |
| Analisi semantica, call graph | Roslyn + MSBuild | Fase 3 |
| SQL Intelligence | ScriptDom: oggetti, dipendenze, collegamenti C# → SQL | Fase 4 |
| Feature Analyzer, report end-to-end | | Fase 5 |
| Export JSON, Feature/SQL/Dependency export | | Fase 6 |
| Git History e Impact Analysis | | Fase 7 |
| Ollama (opzionale) | | Fase 8 |

Livelli di supporto per linguaggio: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#livelli-di-supporto-per-linguaggio).

## Requisiti

- Windows 11 x64 (Windows 10 22H2 funziona, ma non è la piattaforma di riferimento).
- Per compilare: **.NET 10 SDK** e, facoltativamente, **Visual Studio 2026** (o successivo) con il carico di lavoro
  "Sviluppo di applicazioni desktop .NET".
- Per eseguire la versione self-contained (`scripts\publish-win-x64.ps1`) non serve .NET installato.

## Installazione e compilazione

Istruzioni dettagliate: [docs/INSTALLATION.md](docs/INSTALLATION.md).

Il modo più semplice: doppio clic su **`Installa-o-aggiorna-app.bat`** (compila, testa, installa). Comandi equivalenti:

```powershell
# compila, esegue i test e una prova end-to-end della CLI su samples\DemoShop
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1

# avvia l'applicazione desktop dai sorgenti
dotnet run --project src\Lcia.App -c Release

# installa o aggiorna per l'utente corrente (%LOCALAPPDATA%\Programs\LocalCodeIntelligenceAnalyzer)
powershell -ExecutionPolicy Bypass -File .\scripts\install-app.ps1

# solo distribuzione self-contained (cartella + zip in artifacts\)
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

Da Visual Studio: apri `LocalCodeIntelligenceAnalyzer.slnx`, imposta **Lcia.App** come progetto di avvio, premi F5.

## Utilizzo rapido

1. **Apri cartella…** oppure **Apri soluzione…**: il progetto viene indicizzato (la barra di stato mostra
   l'avanzamento; **Annulla** interrompe senza perdere il lavoro già salvato).
2. **Cerca** (Ctrl+F): parole chiave, testo esatto, regex, simboli (`*Repository`) o riferimenti (`IOrderRepository`).
   Doppio clic su un risultato per aprirlo alla riga esatta; selezionane più di uno per l'esportazione.
3. **Esporta**: scegli FULL o CUSTOM, formato, filtri e dimensione delle parti → **Stima** → **Esporta**.
   Il dossier viene scritto in una cartella **esterna** al repository.

Riga di comando (`lcia.exe` nella distribuzione, `dotnet run --project src\Lcia.Cli --` dai sorgenti):

```powershell
lcia index  C:\Repo\MiaSoluzione.sln
lcia search C:\Repo "CreateOrder" --ext .cs,.sql
lcia search C:\Repo IOrderRepository --mode refs
lcia export C:\Repo --out D:\Dossier --format md --max-tokens 200000
```

Guida completa: [docs/USER_GUIDE.md](docs/USER_GUIDE.md), disponibile anche dentro l'applicazione (pagina
**Guida**, tasto F1). Tema chiaro/scuro: pulsante in basso a sinistra o Ctrl+Maiusc+T.

## Sicurezza e privacy

- Dati locali in `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer` (indice, log, impostazioni).
  L'indice di un repository si elimina dalla Dashboard senza toccare i sorgenti.
- **Dati sensibili oscurati prima di uscire dall'app**: password, token, chiavi API, chiavi private, certificati
  e connection string (per intero: server, database, utente) diventano `[REDACTED:categoria]` nei dossier
  esportati, nel visualizzatore di Esplora, nelle anteprime di Cerca, nel testo copiato (**Copia per AI**) e
  nella riga di comando. Email e indirizzi IP su richiesta. I file come `secrets.json` e `.env` sono esclusi.
  Vedere i valori reali o disattivare le protezioni richiede una scelta esplicita; i file originali non
  vengono mai modificati.
- I log passano dallo stesso filtro e non contengono il contenuto dei file analizzati.
- Il percorso assoluto della radice (che può contenere il nome utente) è omesso dai dossier, salvo scelta esplicita.

## Pubblicazione su GitHub dal tuo PC

Gli script in `scripts\` preparano tutto senza mai usare o salvare token: l'autenticazione avviene solo con
`gh auth login` eseguito da te. Guida passo-passo, inclusa la **firma dei commit (Verified)**:
[docs/GITHUB_SETUP.md](docs/GITHUB_SETUP.md).

Doppio clic su `GitHub-1-prima-pubblicazione.bat` la prima volta, poi `GitHub-2-invia-modifiche.bat` per gli
aggiornamenti e `GitHub-3-crea-release.bat` per pubblicare una versione. Equivalente da PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-to-github.ps1
```

### Comandi manuali equivalenti

```powershell
git init
git branch -M main
git add --all
git commit -S -m "Fase 1-2: architettura, documentazione, script GitHub e MVP"
gh repo create <utente>/LocalCodeIntelligenceAnalyzer --private --source . --remote origin
git push -u origin main
```

Prima di ogni pubblicazione `scripts\03-review-and-push.ps1` blocca output di build, database locali,
esportazioni, chiavi e possibili segreti.

## Documentazione

| Documento | Contenuto |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Architettura, moduli, dipendenze, indicizzazione, test, rischi |
| [docs/DATABASE_SCHEMA.md](docs/DATABASE_SCHEMA.md) | Schema SQLite e migrazioni |
| [docs/INSTALLATION.md](docs/INSTALLATION.md) | Prerequisiti, build, distribuzione |
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | Uso dell'applicazione e della CLI, formato del dossier |
| [docs/DEVELOPER_GUIDE.md](docs/DEVELOPER_GUIDE.md) | Struttura del codice, convenzioni, come estendere |
| [docs/GITHUB_SETUP.md](docs/GITHUB_SETUP.md) | Git, GitHub CLI, firma dei commit, pubblicazione |
| [docs/THIRD_PARTY_LICENSES.md](docs/THIRD_PARTY_LICENSES.md) | Dipendenze e licenze |
| [ROADMAP.md](ROADMAP.md) | Fasi, criteri di accettazione, stato |
| [CHANGELOG.md](CHANGELOG.md) | Modifiche per versione |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Come contribuire |

## Licenza

Da definire prima della prima pubblicazione pubblica (vedi ROADMAP, Fase 10). Tutte le dipendenze usate sono
gratuite e con licenze compatibili con l'uso commerciale ([elenco](docs/THIRD_PARTY_LICENSES.md)).
