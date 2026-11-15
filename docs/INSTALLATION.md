# Installazione

## 1. Prerequisiti (per compilare)

| Componente | Come installarlo |
|---|---|
| .NET 10 SDK | `winget install --id Microsoft.DotNet.SDK.10 -e` oppure https://dotnet.microsoft.com/download/dotnet/10.0 |
| Git per Windows | `winget install --id Git.Git -e` |
| Visual Studio 2026 (facoltativo) | Carico di lavoro "Sviluppo di applicazioni desktop .NET" |
| GitHub CLI (solo per pubblicare) | `winget install --id GitHub.cli -e` |

Verifica: `dotnet --list-sdks` deve mostrare una versione `10.x`.

La prima compilazione scarica i pacchetti NuGet (serve internet una sola volta); dopo, tutto funziona offline.

## 2. Sblocco degli script (se il progetto arriva da uno zip)

Windows marca i file scaricati; PowerShell può rifiutarsi di eseguire gli script. Dalla cartella del progetto:

```powershell
Get-ChildItem -Recurse | Unblock-File
```

In alternativa esegui ogni script con `powershell -ExecutionPolicy Bypass -File .\scripts\<nome>.ps1`.

## 3. Compilazione e test

Doppio clic su **`Compila-e-testa.bat`**, oppure:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Lo script: ripristina i pacchetti, compila in Release, esegue tutti i test, poi indicizza `samples\DemoShop`,
esegue due ricerche ed esporta un dossier di prova in una cartella temporanea, verificando che la password fittizia
sia stata oscurata.

Comandi equivalenti:

```powershell
dotnet restore LocalCodeIntelligenceAnalyzer.slnx
dotnet build   LocalCodeIntelligenceAnalyzer.slnx -c Release
dotnet test    LocalCodeIntelligenceAnalyzer.slnx -c Release
dotnet run --project src\Lcia.App -c Release
```

Da Visual Studio: apri `LocalCodeIntelligenceAnalyzer.slnx`, tasto destro su **Lcia.App** → *Imposta come progetto di
avvio*, F5. I test sono in *Esplora test*.

## 4. Installazione e aggiornamento (consigliato)

Doppio clic su **`Installa-o-aggiorna-app.bat`**. Lo script:

1. compila, esegue i test e la prova della CLI (se qualcosa fallisce si ferma senza toccare la versione installata);
2. crea la versione self-contained per Windows x64;
3. la installa in `%LOCALAPPDATA%\Programs\LocalCodeIntelligenceAnalyzer` — se c'è già una versione, la sostituisce
   solo a copia completata (e la ripristina in caso di errore); se l'app è aperta propone di chiuderla;
4. crea il collegamento nel menu Start e, se vuoi, sul desktop; aggiunge, se vuoi, `lcia` al PATH del tuo utente;
5. registra l'app in *Impostazioni > App > App installate* (da lì si può anche disinstallare).

Le scelte su desktop e PATH vengono chieste solo la prima volta. Per le versioni future basta rilanciare lo
stesso `.bat`. Indice, log e impostazioni non vengono toccati. Non servono privilegi di amministratore.

**Nuova versione ricevuta come zip:** doppio clic su **`Aggiorna-sorgenti-da-zip.bat`**. Trova l'ultimo
`LocalCodeIntelligenceAnalyzer*.zip` nei Download (o te lo fa scegliere), mostra file nuovi, modificati e rimossi,
salva una copia dei file sostituiti in `artifacts\source-backups\` e, dopo conferma, aggiorna i sorgenti; poi
propone installazione e invio su GitHub.

**Disinstallazione:** `Disinstalla-app.bat` o *Impostazioni > App*. I dati vengono eliminati solo se lo confermi.

## 5. Solo pacchetto Windows x64 (senza installare)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

Produce `artifacts\publish\LocalCodeIntelligenceAnalyzer-<versione>-win-x64\` e lo zip corrispondente:

- `LocalCodeIntelligenceAnalyzer.exe` — applicazione desktop;
- `lcia.exe` — riga di comando;
- documentazione essenziale.

La versione è **self-contained**: non richiede .NET sul PC di destinazione. Dimensione indicativa 150–250 MB
(cartella), 60–100 MB (zip); il valore esatto viene stampato dallo script. Opzioni: `-ReadyToRun` (avvio più rapido,
file più grandi), `-FrameworkDependent` (molto più piccolo, richiede il runtime .NET 10 Desktop installato).

La cartella si può copiare su un altro PC e avviare direttamente. Un installer `.msi`/`.exe` firmato è previsto
nella Fase 10.

## 6. Dati dell'applicazione

| Cosa | Dove |
|---|---|
| Indice | `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\index\lcia-index.db` |
| Log (14 giorni) | `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\logs\` |
| Impostazioni | `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\settings.json` |
| Errori di avvio | `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\errore-avvio.log` |
| Programma installato | `%LOCALAPPDATA%\Programs\LocalCodeIntelligenceAnalyzer\` |

La cartella dati si può cambiare con la variabile d'ambiente `LCIA_DATA_DIR` o con `--data-dir` nella CLI.
Disinstallare = eliminare la cartella dell'applicazione e, se vuoi, la cartella dati.

## 7. Problemi comuni

| Sintomo | Soluzione |
|---|---|
| `NETSDK1045` / SDK non trovato | Installa .NET 10 SDK; `global.json` accetta qualsiasi 10.0.x |
| Script bloccati | Vedi il punto 2 |
| L'app non parte, nessuna finestra | Leggi `errore-avvio.log` nella cartella dati |
| Aspetto non Windows 11 | Impostazioni → *Tema Fluent* (attivo al riavvio) |
| Indice corrotto o di una versione futura | Chiudi l'app ed elimina la cartella `index` (i sorgenti non vengono toccati) |
| "Windows ha protetto il PC" aprendo un `.bat` | Il file viene da uno zip scaricato: *Ulteriori informazioni > Esegui comunque*, oppure sblocca i file (punto 2) |
| `lcia` non riconosciuto dopo l'installazione | Apri un **nuovo** terminale (il PATH si aggiorna solo per le finestre aperte dopo) |
