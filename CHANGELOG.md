# Changelog

Formato basato su [Keep a Changelog](https://keepachangelog.com/it-IT/1.1.0/); versioni secondo
[Semantic Versioning](https://semver.org/lang/it/).

## [0.2.3] - 2026-11-15 — Branch con Git Flow

### Aggiunto
- `GitHub-4-branch-git-flow.bat` e `scripts/git-flow.ps1`: menu per inizializzare Git Flow (`develop` da `main`),
  avviare `feature/<nome>`, `release/X.Y.Z` (da `develop`) e `hotfix/X.Y.Z` (da `main`), chiudere il branch
  corrente con merge `--no-ff` firmati, tag firmato e release su GitHub per release/hotfix, merge di ritorno in
  `develop` ed eliminazione del branch. Ogni passaggio chiede conferma; nessun push forzato.
- Aggiornamento automatico della versione in `Directory.Build.props` (con commit) all'avvio di release e hotfix.
- Test Pester per tipo di branch, nomi di branch, versioni successive e aggiornamento della versione.

### Modificato
- CI anche su `develop`, `release/**` e `hotfix/**`.
- `Aggiorna-sorgenti-da-zip.bat` mostra il branch corrente e, se sei su `main` con Git Flow attivo, propone di
  passare a `develop`.

## [0.2.2] - 2026-11-15 — Tema scuro, interfaccia, guida in linea

### Aggiunto
- **Tema chiaro, scuro o come Windows**, applicato subito e ricordato: pulsante in basso a sinistra,
  Ctrl+Maiusc+T o Impostazioni > Aspetto. Basato sul tema Fluent di WPF (`ThemeMode`) e su due palette
  (`Theme.Light.xaml`, `Theme.Dark.xaml`); segue anche i cambi di Windows mentre l'app è aperta.
  Barra del titolo scura, visualizzatore del codice e colori della sintassi adattati al tema.
- **Pagina Guida** (F1 o pulsante ? in alto): il manuale `docs/USER_GUIDE.md` incorporato nell'eseguibile,
  con indice degli argomenti, ricerca con evidenziazione, scorciatoie da tastiera e informazioni sull'app.
- Parser `MarkdownLite` (titoli, paragrafi, elenchi, codice, tabelle, citazioni, grassetto, corsivo, codice in
  linea, link) con test, incluso il manuale reale.
- **Scorciatoie da tastiera**: F1, Ctrl+O, Ctrl+Maiusc+O, F5, Ctrl+F, Ctrl+E, Ctrl+1…7, Ctrl+Maiusc+T.
- Manuale: sezioni su tema, scorciatoie, guida in linea, installazione e aggiornamenti; domande frequenti per argomento.

### Modificato
- Interfaccia: barra superiore e menu laterale ridisegnati, pulsanti con icone, schede (card) coerenti in
  Dashboard, Esporta (passi numerati) e Impostazioni, riquadro di benvenuto al primo avvio, avvisi evidenziati,
  segnaposto nel visualizzatore quando nessun file è aperto, barra di stato più compatta.
- Esplora: cambiando repository il file mostrato viene chiuso (resta aperto dopo un aggiornamento dell'indice).
- Impostazioni: il tema non richiede più il riavvio; lo stile Fluent sì.

### Corretto
- `03-review-and-push.ps1` e `create-release.ps1`: variabili seguite da `:` o `?` nelle stringhe (in Windows
  PowerShell 5.1 la prima impediva l'avvio dello script, le altre mostravano un nome vuoto nelle domande).
- Commit non riuscito con "Author identity unknown": gli script 01 e 03 ora verificano nome ed email Git e, se
  mancano, li propongono dall'account GitHub (email noreply, privata e valida per "Verified") e li impostano dopo
  conferma (per tutti i repository o solo per questo).
- Messaggi di Git: in Windows PowerShell 5.1 le righe vuote di errore comparivano come
  "System.Management.Automation.RemoteException"; ora vengono mostrate solo le righe con testo.
- `update-from-zip.ps1`: niente avviso di "modifiche locali" quando il repository non ha ancora commit.

## [0.2.1] - 2026-11-15 — Strumenti con doppio clic

### Aggiunto
- File `.bat` nella cartella principale: `Compila-e-testa`, `Installa-o-aggiorna-app`, `Disinstalla-app`,
  `Aggiorna-sorgenti-da-zip`, `GitHub-1-prima-pubblicazione`, `GitHub-2-invia-modifiche`, `GitHub-3-crea-release`.
- `scripts/install-app.ps1`: installazione e aggiornamento per l'utente corrente in
  `%LOCALAPPDATA%\Programs\LocalCodeIntelligenceAnalyzer`, con sostituzione sicura della versione precedente,
  collegamenti nel menu Start e sul desktop, comando `lcia` nel PATH dell'utente e voce in *App installate*.
- `scripts/uninstall-app.ps1`: disinstallazione (anche da Impostazioni > App); i dati vengono eliminati solo su conferma.
- `scripts/update-from-zip.ps1`: aggiornamento dei sorgenti da uno zip con riepilogo delle differenze, copia di
  sicurezza dei file sostituiti, controllo dei percorsi nello zip; elimina solo file gestiti da Git.
- `scripts/create-release.ps1`: tag firmato `vX.Y.Z` dopo i controlli (modifiche inviate, CHANGELOG, tag non esistente).
- La release su GitHub usa come testo la sezione del CHANGELOG della versione.
- Test Pester per installazione, PATH, confronto dei sorgenti, estrazione sicura degli zip, versioni e changelog.

### Modificato
- `publish-win-x64.ps1`: opzione `-NoZip`.
- Gli script restituiscono sempre un codice di uscita esplicito (0 = completato).

### Verificato
- Fase 2: `scripts\build.ps1` (compilazione, test xUnit, prova end-to-end della CLI con oscuramento dei segreti)
  completato con successo su Windows 11.

## [0.2.0] - 2026-11-15 — Fase 2 (MVP)

### Aggiunto
- **Repository Scanner**: cartelle, repository Git e soluzioni `.sln`/`.slnx`; esclusioni configurabili per nome e
  percorso; limite di dimensione e profondità; link simbolici e junction ignorati o seguiti solo dentro la radice.
- **Indice locale** SQLite con FTS5: scansione incrementale (dimensione, data, SHA-256), elaborazione parallela,
  scrittura in batch transazionali, annullamento e ripresa, storico delle esecuzioni con avvisi.
- **Analisi C#** con Roslyn (sintassi): namespace, tipi, membri, firme, righe, documentazione XML, attributi,
  tipi base, dipendenze da costruttore (anche primario) e membri, registrazioni DI, invocazioni e `new`,
  con livello di evidenza.
- **Soluzioni e progetti**: `.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`, `.sqlproj`; NuGet con Central Package
  Management e `packages.config`; riferimenti a progetti esterni segnalati.
- **Ricerca**: parole chiave (FTS5 con identificatori spezzati), testo esatto, regex con timeout, simboli con jolly,
  riferimenti; filtri per estensione, percorso, linguaggio, progetto, tipo di simbolo e di relazione.
- **Full Project Context Export** TXT e Markdown: intestazione con istruzioni per l'AI, albero, elenco file, progetti,
  indice dei tipi, riferimenti incrociati, blocchi file con metadati e codice integrale (numeri di riga opzionali),
  riepilogo; suddivisione in parti con file indice; stima di dimensione e token; oscuramento dei segreti con elenco
  delle righe oscurate; esclusione dei file sensibili; destinazione obbligatoriamente esterna al repository.
- **Applicazione WPF** (MVVM): Dashboard, Esplora con visualizzatore AvalonEdit e simboli, Cerca, Esporta, Log, Impostazioni;
  tema Fluent di Windows 11 opzionale; nessuna operazione pesante sul thread UI.
- **CLI** `lcia`: `index`, `search`, `export`, `stats`, `repos`, `remove`.
- **Logging** su file e in memoria con oscuramento dei segreti.
- **Test**: xUnit (unità e integrazione su `samples/DemoShop`), Pester per gli script, CI GitHub Actions su Windows.
- Progetto dimostrativo **DemoShop** (controller, service, repository, DI, stored procedure, tabelle, vista, funzione, trigger).

## [0.1.0] - 2026-11-15 — Fase 1

### Aggiunto
- Architettura modulare, solution `.slnx`, Central Package Management, `global.json` (.NET 10).
- Documentazione: README, ARCHITECTURE, DATABASE_SCHEMA, INSTALLATION, USER_GUIDE, DEVELOPER_GUIDE, GITHUB_SETUP,
  THIRD_PARTY_LICENSES, ROADMAP, CONTRIBUTING.
- `.gitignore`, `.gitattributes`, `.editorconfig`.
- Script PowerShell: `01-init-git`, `02-create-github-repo`, `03-review-and-push`, `publish-to-github`, `build`,
  `publish-win-x64`; controllo dei segreti e della firma dei commit prima della pubblicazione.
