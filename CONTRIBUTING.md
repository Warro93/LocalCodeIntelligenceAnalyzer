# Contribuire

## Regole di base
- Nessun componente commerciale, a pagamento o con servizi cloud obbligatori. Ogni nuova dipendenza va aggiunta in
  `Directory.Packages.props` con la licenza verificata e registrata in `docs/THIRD_PARTY_LICENSES.md`.
- I repository analizzati sono sempre in sola lettura: nessuna funzione può scrivere al loro interno.
- Nessuna relazione può essere marcata come verificata senza risoluzione semantica; in caso di dubbio si usa il
  livello di evidenza più basso e si conserva l'evidenza testuale.
- Non dichiarare completa una funzione che esiste solo come UI o stub; niente `TODO` nelle funzioni dichiarate complete.
- Mai credenziali, token, database locali, esportazioni o sorgenti di terzi nel repository.

## Flusso di lavoro
1. Crea un branch: `git switch -c feature/nome-breve`.
2. Sviluppa con test: ogni motore ha test di unità e, se tocca l'indice, test di integrazione su `samples/DemoShop`.
3. Verifica in locale: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.
4. Aggiorna `CHANGELOG.md` (sezione della versione in corso) e, se cambia lo stato di una fase, `ROADMAP.md`.
5. Commit **firmati** e con messaggi descrittivi (es. `Ricerca: filtro per progetto`), poi pull request.

## Stile del codice
- C# moderno, `Nullable` attivo, namespace file-scoped, `.editorconfig` del repository.
- Interfacce dei motori in `Lcia.Application`; implementazioni nei progetti dei motori; registrazione in
  `Lcia.Hosting/ServiceCollectionExtensions.cs`.
- Nessuna operazione di I/O o CPU pesante sul thread UI: usa `Task.Run` o metodi già asincroni.
- Messaggi per l'utente in italiano; etichette del dossier esportato in inglese (stabili per le AI).
- Le modifiche allo schema SQLite si fanno solo aggiungendo una migrazione (vedi `docs/DATABASE_SCHEMA.md`).

## Segnalare problemi
Includi versione (`lcia --version`), passi per riprodurre, e le righe rilevanti di
`%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\logs` (sono già prive di segreti, ma rileggile prima di condividerle).
