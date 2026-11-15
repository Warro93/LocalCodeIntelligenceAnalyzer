# Roadmap

Ogni fase si chiude solo quando: il codice è reale (niente stub nelle funzioni dichiarate), la solution compila,
i test pertinenti passano, documentazione e CHANGELOG sono aggiornati.

Legenda stato: ✅ completata e verificata · 🟡 implementata, verifica in sospeso · ⬜ da fare

## Stato attuale

| Fase | Contenuto | Stato |
|---|---|---|
| 1 | Architettura, solution, Git, documentazione, script GitHub, strumenti con doppio clic | 🟡 |
| 2 | MVP: UI (tema chiaro/scuro, guida in linea), apertura cartella/soluzione, scanner, analisi C# di base, ricerca, export TXT (+ Markdown) | 🟡 |
| 3 | Roslyn semantico, simboli risolti, riferimenti, call graph, dipendenze tra progetti | ⬜ |
| 4 | SQL Intelligence: ScriptDom, oggetti, dipendenze, collegamenti C# → SQL | ⬜ |
| 5 | Feature Analyzer e report end-to-end (sezioni A–O) | ⬜ |
| 6 | Full Context Export completo: JSON, modalità FEATURE/SQL/DEPENDENCY | ⬜ |
| 7 | Git History e Impact Analysis (sola lettura) | ⬜ |
| 8 | Ollama opzionale | ⬜ |
| 9 | Test, prestazioni, hardening, rifinitura UI, Tree-sitter | ⬜ |
| 10 | Release Windows, licenza, guida di pubblicazione | ⬜ |

**Verifica delle Fasi 1–2 (15/11/2026).** `scripts\build.ps1` è stato eseguito con successo su Windows 11:
compilazione, test xUnit e prova end-to-end della CLI (indicizzazione, ricerca, riferimenti, export con
oscuramento dei segreti). Restano da confermare: avvio e uso della GUI, test Pester degli script (girano in CI
al primo push), gli script di installazione/aggiornamento/release (0.2.1) e le modifiche all'interfaccia della
0.2.2 (tema scuro, guida, nuovi layout): scritte senza poter compilare né avviare WPF nell'ambiente di sviluppo,
controllate solo staticamente (XAML, bilanciamento del codice, parser Markdown provato con un port equivalente).

**Prossimo task concreto:** `Aggiorna-sorgenti-da-zip.bat` con lo zip 0.2.2 (compila, testa, installa) → prova della
GUI su un repository reale, in tema chiaro e scuro → `GitHub-2-invia-modifiche.bat` (la CI esegue anche Pester) →
marcare le Fasi 1–2 come ✅ → Fase 3.

## Criteri di accettazione

### Fase 1 — Architettura e repository
- [x] Solution `.slnx` con progetti separati per livello e motore; versioni NuGet centralizzate.
- [x] `.gitignore` per Visual Studio, VS Code, .NET, Windows, build, database locali, esportazioni, segreti.
- [x] README, ARCHITECTURE, ROADMAP, CHANGELOG, CONTRIBUTING, INSTALLATION, USER_GUIDE, DEVELOPER_GUIDE, GITHUB_SETUP.
- [x] Script `01-init-git`, `02-create-github-repo`, `03-review-and-push`, `publish-to-github` senza token,
      con conferma prima di ogni operazione remota; blocco di segreti e file non pubblicabili; controllo della firma dei commit.
- [x] `.bat` con doppio clic per build, installazione/aggiornamento, disinstallazione, aggiornamento da zip,
      pubblicazione, invio modifiche e release (0.2.1).
- [x] Branch secondo Git Flow (`develop`, `feature/*`, `release/*`, `hotfix/*`) con `GitHub-4-branch-git-flow.bat` (0.2.3).
- [ ] `Invoke-Pester tests\scripts` verde (eseguito in CI).
- [ ] Installazione, aggiornamento e disinstallazione provati su Windows 11.

### Fase 2 — MVP
- [x] `dotnet build` della solution senza errori.
- [x] `dotnet test`: tutti i test verdi (unità e integrazione su DemoShop).
- [ ] L'applicazione si avvia su Windows 11, apre una cartella o una soluzione e mostra l'avanzamento senza bloccare la UI.
- [ ] Tema chiaro/scuro/come Windows applicato senza riavvio e ricordato; testo leggibile in tutte le pagine.
- [ ] Pagina Guida: indice, ricerca ed evidenziazione funzionanti; F1 la apre da qualsiasi pagina.
- [x] Seconda indicizzazione senza modifiche: 0 file rielaborati (test `IndexesDemoShopWithSymbolsProjectsAndIncrementalUpdates`).
- [x] Il repository analizzato non cambia (test `NeverModifiesTheAnalyzedRepository`).
- [x] `CreateOrderAsync` trovato con percorso e riga esatti; filtri combinabili (test di ricerca).
- [x] Export TXT con delimitatori `FILE:`, codice integrale, segreti oscurati, suddivisione con file indice (test di export).
- [x] `scripts\build.ps1` completa la prova end-to-end della CLI.

### Fase 3 — Roslyn semantico
- Caricamento con `MSBuildWorkspace` (Build Locator); progetti non caricabili segnalati e analizzati in modo sintattico.
- Simboli risolti (`target_symbol_id`), implementazioni di interfacce, override, chiamanti/chiamati.
- Grafo navigabile (Dependency Explorer) con livello di evidenza su ogni arco.
- Accettazione: su DemoShop `OrdersController.Create → OrderService.CreateOrderAsync → OrderRepository.InsertOrderAsync`
  ricostruito con evidenza `DirectVerified`.

### Fase 4 — SQL Intelligence
- ScriptDom: procedure, funzioni, viste, trigger, tabelle, FK, indici, sequence, sinonimi, tipi; parametri e OUTPUT.
- Tabelle lette/scritte, chiamate tra oggetti, transazioni, TRY/CATCH, SQL dinamico marcato `Dynamic`.
- Collegamento C# → SQL per ADO.NET (`CommandType.StoredProcedure`), Dapper, EF, stringhe SQL.
- Supporto `.sqlproj` e DACPAC (se tecnicamente fattibile senza connessione al database). Nessuno script viene eseguito.
- Accettazione: `InsertOrderAsync → dbo.usp_Order_Create → dbo.Customers, dbo.Orders, dbo.OrderLines, dbo.fn_OrderTotal`.

### Fase 5 — Feature Analyzer
- Prompt in linguaggio naturale + parole chiave + filtri; espansione tramite grafo e SQL.
- Report con sezioni A–O, ogni affermazione con file e righe, distinzione fatti/inferenze/ipotesi.
- Accettazione: "Analizza la creazione di un ordine" su DemoShop produce il flusso completo con riferimenti verificabili.

### Fase 6 — Export completo
- JSON strutturato; modalità FEATURE, SQL, DEPENDENCY; selezione per filtri dalla UI con anteprima.
- Accettazione: i tre formati validati da test (JSON con schema), suddivisione e indice coerenti.

### Fase 7 — Git
- Branch, commit, autori, file modificati, diff, storia di un file, confronto versioni; tutto in sola lettura.
- Analisi d'impatto: file/simboli/oggetti SQL toccati da un intervallo di commit.

### Fase 8 — Ollama
- Endpoint, modello, parametri, timeout e limite di contesto configurabili; nessuna dipendenza obbligatoria.
- Il modello riceve solo contesto recuperato dal motore e deve citarne i riferimenti; risposte con modello indicato.

### Fase 9 — Qualità
- Test UI (FlaUI), test di prestazioni (repository > 50.000 file), Tree-sitter per JS/TS/Python/Java/Go/PHP.

### Fase 10 — Release
- Scelta della licenza, firma degli eseguibili (facoltativa), pacchetto installabile, release GitHub automatica (`release.yml`).
