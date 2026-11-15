# Guida utente

Local Code Intelligence Analyzer (LCIA) lavora **solo sul tuo PC**: legge i sorgenti, costruisce un indice locale
e produce dossier di contesto da usare con qualsiasi AI. Non modifica mai i file analizzati, non esegue il codice
né gli script SQL, non invia nulla in rete.

## 1. Primo avvio

1. Avvia l'app dal menu Start (dopo `Installa-o-aggiorna-app.bat`) oppure, dai sorgenti, con
   `dotnet run --project src\Lcia.App -c Release`.
2. Clicca **Apri cartella…** (una cartella o un repository Git) oppure **Apri soluzione…** (`.sln` / `.slnx`).
   Con una soluzione viene indicizzata la cartella che la contiene, con i progetti della soluzione.
3. L'indicizzazione parte subito; la barra in basso mostra fase e avanzamento. Puoi continuare a usare l'app o
   premere **Annulla**: il lavoro già salvato resta, e la volta successiva riprende dai file mancanti.

L'elenco **Repository** in alto mostra i repository già indicizzati. **Aggiorna indice** rielabora solo i file
cambiati (dimensione, data o contenuto). Nella Dashboard ci sono anche **Reindicizza tutto** e **Elimina indice**
(che cancella solo l'indice: i sorgenti non vengono toccati).

## 2. Pagine

### Dashboard
Al primo avvio mostra un riquadro di benvenuto con i tre passi principali. Con un repository aperto mostra le
statistiche: file, righe, dimensione, linguaggi (con livello di supporto), progetti, simboli,
relazioni, ultime esecuzioni con eventuali avvisi (file troppo grandi, binari, illeggibili, link ignorati, errori di
sintassi), branch e commit correnti se è un repository Git.

Livelli di supporto: **Sintattico** (C# con Roslyn: struttura e relazioni), **Strutturale** (progetti, soluzioni,
XML/JSON), **Solo testo** (indicizzato per la ricerca, senza analisi). L'analisi semantica e SQL arriva nelle
Fasi 3–4.

### Esplora
Albero dei file indicizzati con filtro, visualizzatore del codice in sola lettura con numeri di riga ed
evidenziazione, e pannello **Simboli del file** (clic per andare alla riga). Se un file è cambiato dopo
l'indicizzazione compare un avviso. **Aggiungi a esportazione** mette il file o la cartella selezionata nella
selezione personalizzata.

Password, token, chiavi e connection string vengono mostrati già oscurati (`[REDACTED:<tipo>]`): un riquadro
indica quante righe sono state oscurate. **Copia per AI (oscurato)** copia negli appunti percorso e contenuto del
file pronti da incollare in un'AI online, sempre oscurati. **Mostra valori** rivela i valori originali solo sul
tuo schermo, dopo una conferma (vedi [Protezione dei dati sensibili](#5-protezione-dei-dati-sensibili)).

### Cerca
| Modalità | Cosa fa | Esempio |
|---|---|---|
| Parole | Parole in AND sull'indice FTS5; trova anche parti di identificatori | `order create`, `"order lines"`, `repo*`, `order -test` |
| Testo esatto | Sottostringa riga per riga | `CommandType.StoredProcedure` |
| Regex | Espressione regolare .NET per riga (timeout 1 s per riga) | `usp_\w+` |
| Simbolo | Nome o nome completo; `*` come jolly | `*Repository`, `DemoShop.Services.OrderService` |
| Riferimenti | Chi usa un tipo o membro: tipi base, dipendenze, DI, invocazioni, `new` | `IOrderRepository` |

Filtri combinabili: estensioni, percorso, progetto, tipo di simbolo, tipo di relazione, maiuscole/minuscole,
numero massimo di risultati. Ogni risultato riporta file, riga, anteprima, dettaglio e progetto; doppio clic o
**Apri nel visualizzatore** apre il file alla riga. Puoi selezionare più risultati e **aggiungerli
all'esportazione** o **copiarli** negli appunti. Anteprime e testo copiato hanno i valori sensibili oscurati.

I riferimenti in questa versione sono **sintattici**: un nome uguale in namespace diversi può dare falsi positivi.
Il livello di evidenza è indicato nel dettaglio (`verificata direttamente`, `riferimento testuale`, `dinamica`, …).

### Esporta (Full Project Context Export)
1. **Modalità**: *FULL* (tutto il progetto, con i filtri) o *CUSTOM* (solo i file selezionati da Cerca/Esplora).
2. **Formato**: TXT o Markdown.
3. **Contenuto**: albero, elenco file, progetti e pacchetti, indice dei simboli e riferimenti, codice integrale,
   numeri di riga, percorso assoluto della radice (spento per impostazione predefinita).
4. **Filtri**: estensioni, cartelle incluse, cartelle escluse.
5. **Sicurezza**: oscuramento dei segreti (attivo), connection string oscurate per intero (attivo), email e
   indirizzi IP (spento), inclusione dei file sensibili (spenta). Le scelte partono da quelle delle Impostazioni.
   Disattivare l'oscuramento o includere i file sensibili richiede una conferma esplicita.
6. **Suddivisione**: file unico o parti da ~32k / 100k / 200k / 500k / 1M token (stima: 4 caratteri ≈ 1 token).
   Con più parti viene creato anche un file indice che le elenca.
7. **Destinazione**: una cartella **esterna** al repository. **Stima** mostra file, dimensione, token e parti
   previste; **Esporta** scrive i file (non sovrascrive mai file esistenti).

### Log
Messaggi dell'applicazione in tempo reale, filtrabili per livello (informazioni, avvisi, errori). I log su disco sono in
`%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\logs` e non contengono i valori dei segreti.

### Impostazioni
- **Protezione dei dati sensibili**: oscuramento anche durante la consultazione (attivo), connection string per
  intero (attivo), email e indirizzi IP (spento). Valgono per Esplora, Cerca, copia negli appunti, riga di comando
  e come punto di partenza per Esporta.
- **Aspetto**: tema (come Windows, chiaro, scuro) applicato subito; stile Fluent di Windows 11 (al riavvio).
- **Scansione**: cartelle escluse per nome (es. `bin`, `obj`, `node_modules`, `.git`, `.vs`), percorsi relativi
  esclusi, dimensione massima per file (predefinita 2 MB), profondità massima, parallelismo, collegamenti simbolici.
- **Dati locali**: dove si trovano indice, log e impostazioni.

### Guida
Questo manuale, sempre disponibile anche senza internet (**F1** o il pulsante **?** in alto). A sinistra
l'indice degli argomenti e la ricerca: scrivi una o più parole per vedere solo gli argomenti che le contengono
tutte, con le parole evidenziate. In basso le scorciatoie da tastiera e le informazioni sull'applicazione.

## 3. Tema chiaro e scuro

- Il pulsante **Tema scuro / Tema chiaro** in basso a sinistra (o **Ctrl+Maiusc+T**) cambia tema all'istante.
- In **Impostazioni > Aspetto** puoi scegliere anche **Come Windows**: l'app segue l'impostazione chiaro/scuro di
  Windows, anche mentre è aperta.
- Il tema viene ricordato al prossimo avvio. Anche il visualizzatore del codice e i colori della sintassi si
  adattano al tema.
- Il tema scuro richiede lo **stile Fluent** (attivo per impostazione predefinita). Se lo disattivi, l'app usa
  l'aspetto classico di Windows, solo chiaro.

## 4. Scorciatoie da tastiera

| Tasti | Azione |
|---|---|
| `F1` | Apri la guida |
| `Ctrl+O` | Apri cartella |
| `Ctrl+Maiusc+O` | Apri soluzione (`.sln` / `.slnx`) |
| `F5` | Aggiorna indice (solo file cambiati) |
| `Ctrl+F` | Vai a Cerca (il cursore è già nella casella di ricerca) |
| `Ctrl+E` | Vai a Esporta |
| `Ctrl+1` … `Ctrl+7` | Dashboard, Esplora, Cerca, Esporta, Log, Impostazioni, Guida |
| `Ctrl+Maiusc+T` | Tema chiaro / scuro |
| `Invio` (in Cerca) | Avvia la ricerca |
| Doppio clic su un risultato | Apre il file alla riga trovata |

## 5. Protezione dei dati sensibili

LCIA è pensato per preparare codice da far analizzare anche a un'AI online: per questo i valori sensibili vengono
**oscurati prima di uscire dall'applicazione**, sia mentre consulti il codice sia nei dossier esportati. Il
valore viene sostituito da `[REDACTED:<tipo>]`; i file originali non vengono mai modificati.

| Cosa | Esempio | Sempre | Opzione |
|---|---|---|---|
| Password, segreti e chiavi API in configurazione, codice e SQL | `"Password": "..."`, `api_key = "..."`, `CREATE LOGIN ... PASSWORD = '...'` | ✓ | |
| Token e chiavi noti | GitHub, npm, NuGet, Slack, SendGrid, Stripe, AWS, Azure, Google, chiavi `sk-…`, JWT, `Bearer` e `Basic` | ✓ | |
| Chiavi private e certificati | blocchi `-----BEGIN ... PRIVATE KEY-----` | ✓ | |
| Connection string per intero | `Server=…;Database=…;User Id=…;Password=…` → `[REDACTED:connection-string]` | | attiva |
| Solo la password delle connection string | `Server=sql01;Database=Shop;Password=[REDACTED:...]` | | se spegni la precedente |
| Email e indirizzi IP | `mario.rossi@azienda.it`, `10.0.4.17` | | spenta |

Le connection string vengono riconosciute in JSON, XML e `.config`, YAML, file di testo e log, script SQL e
nelle stringhe del codice (C# e altri linguaggi). I segnaposto (`<password>`, `${VAR}`, `$(VAR)`, `%VAR%`,
`{{VAR}}`, `***`) restano visibili perché non sono segreti. Con l'opzione email e IP non vengono oscurati
`127.0.0.1`, `0.0.0.0`, le maschere di rete comuni e le righe con numeri di versione. I file sensibili (`.env`, `secrets.json`, `*.pfx`, `*.key`, …) non vengono mostrati
nelle anteprime di Cerca né esportati.

Dove agisce:
- **Esplora**: il codice è mostrato oscurato; **Copia per AI (oscurato)** copia percorso e contenuto oscurato;
  **Mostra valori** rivela gli originali solo sul tuo schermo (con conferma) e **Nascondi valori** li oscura di
  nuovo. Anche la selezione copiata con Ctrl+C è oscurata finché i valori sono nascosti.
- **Cerca**: anteprime e dettagli dei risultati, e il testo copiato con **Copia**. La ricerca avviene comunque sul
  testo originale, quindi trovi una riga anche cercando un valore che poi viene mostrato oscurato.
- **Esporta**: il dossier riporta nell'intestazione `REDACTION SCOPE` (cosa è stato oscurato) e, per ogni file,
  `REDACTED VALUES:` con le righe interessate, mai i valori.
- **Riga di comando**: `lcia search` oscura le anteprime (`--show-secrets` le mostra solo sul tuo terminale);
  `lcia export` usa le Impostazioni, con `--mask-personal-data` e `--password-only` per cambiarle al volo.
- **Log**: ogni riga passa dallo stesso filtro.

Le scelte si trovano in **Impostazioni > Protezione dei dati sensibili**. Il riconoscimento è euristico: un
formato sconosciuto o un segreto senza nomi riconoscibili (es. `var x = "a8F...";`) può sfuggire, e qualche valore
innocuo può essere oscurato per prudenza. **Rileggi sempre il testo prima di incollarlo in un servizio online.**

## 6. Formato del dossier esportato

Le etichette sono in inglese, stabili e pensate per essere lette da un modello AI:

```text
LOCAL CODE INTELLIGENCE ANALYZER - FULL PROJECT CONTEXT EXPORT
...
HOW TO READ THIS DOCUMENT
...
==================================================
FILE: Services/OrderService.cs
==================================================
LANGUAGE: C# (analysis level: Syntactic)
PROJECT: DemoShop
LINES: 55
SIZE: ... bytes
SHA256: ...
NAMESPACE: DemoShop.Services
CLASS: OrderService [lines 7-55]
  FULL NAME: DemoShop.Services.OrderService
  MODIFIERS: public sealed
  SUMMARY: Coordina la creazione, la lettura e l'annullamento degli ordini.
  BASE TYPES: IOrderService
  DEPENDENCIES:
    - IOrderRepository
    - ICustomerRepository
    - ILogger<OrderService>
  CONSTRUCTORS:
    - public OrderService(IOrderRepository orders, ...) [lines 13-18]
  METHODS:
    - public async Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken) [lines 21-38]
    ...

SOURCE CODE:
  1 | using DemoShop.Models;
  ...
END OF FILE: Services/OrderService.cs
```

Il dossier contiene, nell'ordine: intestazione e istruzioni di lettura, albero delle cartelle, elenco dei file,
soluzioni/progetti/pacchetti, indice dei tipi, riferimenti incrociati, un blocco per ogni file, riepilogo finale
con i **limiti dell'analisi** (cosa non è stato verificato). I file troppo grandi per una parte vengono spezzati
con gli avvisi `CONTINUES IN NEXT PART` / `CONTINUED FROM PREVIOUS PART`.

### Segreti
Vengono sostituiti con `[REDACTED:<tipo>]` come descritto in
[Protezione dei dati sensibili](#5-protezione-dei-dati-sensibili). L'intestazione riporta `REDACTION SCOPE`,
il blocco di ogni file `REDACTED VALUES:` con le righe interessate (mai i valori). I file sensibili (`.env`,
`secrets.json`, `*.pfx`, `*.key`, …) sono esclusi. Il riconoscimento è euristico: **rileggi sempre il dossier
prima di condividerlo**.

## 7. Riga di comando (`lcia.exe`)

Dopo l'installazione con il PATH attivato, `lcia` funziona da qualsiasi nuovo terminale.

```powershell
lcia index  C:\src\MioProgetto                    # indicizza (incrementale)
lcia index  C:\src\MioProgetto\Mio.sln --force    # reindicizzazione completa
lcia search C:\src\MioProgetto CreateOrder --mode symbol
lcia search C:\src\MioProgetto "order lines" --ext .cs,.sql --limit 50
lcia export C:\src\MioProgetto --out D:\Export --format md --max-tokens 200000
lcia stats  C:\src\MioProgetto
lcia repos
lcia remove C:\src\MioProgetto --yes              # rimuove solo l'indice
```

Opzioni di `export`: `--ext`, `--folder`, `--exclude-folder`, `--files`, `--name`, `--no-line-numbers`,
`--no-source`, `--no-tree`, `--no-symbols`, `--absolute-paths`, `--mask-personal-data` (oscura anche email e IP),
`--password-only` (delle connection string oscura solo la password). Disattivare l'oscuramento richiede entrambe
le opzioni `--no-redact --i-understand-secrets-risk`. Opzione di `search`: `--show-secrets` (anteprime non
oscurate, solo sul tuo terminale). Opzioni comuni: `--data-dir`, `--verbose`.
Codici di uscita: 0 ok, 1 errore, 2 uso non corretto, 3 annullato.

## 8. Installazione e aggiornamenti

- **Installare o aggiornare**: doppio clic su `Installa-o-aggiorna-app.bat` nella cartella del progetto. Compila,
  esegue i test e installa per il tuo utente (menu Start, desktop se vuoi, comando `lcia`). Se l'app è aperta
  propone di chiuderla; se un test fallisce la versione installata resta quella precedente.
- **Nuova versione ricevuta come zip**: `Aggiorna-sorgenti-da-zip.bat` mostra cosa cambia, salva una copia dei
  file sostituiti e poi propone installazione e invio su GitHub.
- **Disinstallare**: `Disinstalla-app.bat` oppure Impostazioni di Windows > App > App installate. Indice, log e
  impostazioni vengono eliminati solo se lo confermi.

## 9. Domande frequenti

### Dove si trova l'indice e quanto occupa?
In `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\index`. Contiene una copia del testo dei file indicizzati più
l'indice di ricerca, quindi occupa più dei sorgenti stessi. Si elimina dalla Dashboard (**Elimina indice**).

### Posso analizzare un repository su un disco di rete?
Sì, ma è più lento; l'indice resta comunque sul tuo PC.

### Perché alcuni file non compaiono?
Sono esclusi dalle impostazioni, troppo grandi, binari o illeggibili: la Dashboard elenca i motivi nell'ultima
indicizzazione.

### I commit o i branch del repository cambiano?
No: LCIA legge la cartella `.git` come file, senza eseguire comandi Git, e non scrive mai nel repository.

### Il codice viene inviato a un'AI o su internet?
No. L'analisi è interamente locale. Il dossier esportato resta nella cartella che scegli: sei tu a decidere se e
dove usarlo.

### Posso incollare in ChatGPT, Claude o Copilot il codice copiato da LCIA?
Sì, è lo scopo di **Copia per AI (oscurato)** e dell'esportazione: password, token, chiavi e connection string
sono già oscurati. Attiva anche **email e indirizzi IP** se il codice contiene dati personali o indirizzi interni,
e rileggi il testo prima di inviarlo: il riconoscimento non può garantire di trovare ogni segreto.

### Ho bisogno di vedere la password vera per fare una prova
In Esplora usa **Mostra valori**: il valore compare solo sullo schermo. Da riga di comando,
`lcia search ... --show-secrets`. In entrambi i casi non copiare quel testo in servizi online.
