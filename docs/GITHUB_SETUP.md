# Pubblicare il progetto su GitHub (da Windows)

Tutto avviene sul tuo PC, con il tuo account. Gli script non contengono né chiedono token: l'autenticazione la
gestisce GitHub CLI (`gh`) nel browser. **Non incollare mai token in chat o nei file del progetto.**

## 1. Strumenti

```powershell
winget install --id Git.Git -e
winget install --id GitHub.cli -e
```

Chiudi e riapri PowerShell, poi verifica: `git --version` e `gh --version`.

## 2. Accesso a GitHub (una volta sola)

```powershell
gh auth login
```

Scegli *GitHub.com* → *HTTPS* → *Login with a web browser*, copia il codice mostrato e conferma nel browser.
Controllo: `gh auth status`.

Identità Git (autore dei commit): se manca, gli script te la propongono in automatico dal tuo account GitHub,
con l'indirizzo *noreply* (`ID+utente@users.noreply.github.com`, mantiene privata la tua email ed è valido per
"Verified"), e la impostano dopo conferma. In alternativa, a mano:

```powershell
git config --global user.name  "Nome Cognome"
git config --global user.email "tuo-indirizzo@example.com"
```

## 3. Commit "Verified" (firma SSH)

GitHub mostra **Verified** solo per i commit firmati con una chiave registrata come *signing key*.

```powershell
ssh-keygen -t ed25519 -C "firma commit GitHub"          # Invio per il percorso predefinito; scegli una passphrase
gh auth refresh -h github.com -s admin:ssh_signing_key  # permesso per caricare chiavi di firma
gh ssh-key add "$env:USERPROFILE\.ssh\id_ed25519.pub" --type signing --title "Firma commit PC"
```

Configurazione solo per questo repository (lo script `03-review-and-push.ps1` può farla per te):

```powershell
git config --local gpg.format ssh
git config --local user.signingkey "$env:USERPROFILE\.ssh\id_ed25519.pub"
git config --local commit.gpgsign true
```

L'email di `user.email` deve essere un indirizzo verificato del tuo account, altrimenti il commit risulta
*Unverified* anche se firmato. Se hai già una chiave GPG o SSH di firma configurata globalmente, gli script la
rilevano e non cambiano nulla.

## 4. Preparare la cartella

Se il progetto arriva da uno zip, estrailo (ad es. in `C:\src\LocalCodeIntelligenceAnalyzer`) e sblocca i file:

```powershell
cd C:\src\LocalCodeIntelligenceAnalyzer
Get-ChildItem -Recurse | Unblock-File
```

Consigliato prima di pubblicare: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.

## 5. Procedura guidata

Doppio clic su **`GitHub-1-prima-pubblicazione.bat`** (equivale a
`powershell -ExecutionPolicy Bypass -File .\scripts\publish-to-github.ps1`).

Esegue in ordine i tre script seguenti (puoi anche lanciarli singolarmente):

| Script | Cosa fa | Operazioni remote |
|---|---|---|
| `01-init-git.ps1` | `git init` (solo se manca), branch `main`, controlli di `.gitignore`, identità e firma | Nessuna |
| `02-create-github-repo.ps1` | Verifica `gh auth status`, chiede nome e visibilità (**privato** o **pubblico**), crea il repository vuoto e collega `origin` | Creazione del repository, dopo conferma |
| `03-review-and-push.ps1` | Mostra i file, blocca file vietati e possibili segreti, propone la firma, poi `git add`, `git commit`, `git push` | Push, dopo conferma |

Ogni passaggio chiede conferma (`s`/`n`). Gli script si possono rilanciare: non duplicano repository né remote.
Se un controllo blocca la pubblicazione (codice di uscita 2 = file vietati, 3 = possibili segreti) correggi e
rilancia. I dati fittizi di test sono elencati in `scripts\secret-scan-allowlist.txt`.

**Pubblico o privato?** Un repository privato è visibile solo a te e ai collaboratori che inviti; uno pubblico a
chiunque. Puoi cambiare idea dopo da *Settings → General → Danger Zone → Change visibility*. Prima di renderlo
pubblico scegli una licenza (vedi README).

## 6. Comandi manuali equivalenti

```powershell
git init
git branch -M main
git add .
git status                       # rileggi l'elenco prima di continuare
git commit -S -m "Prima versione: Fase 1 e 2"
gh repo create LocalCodeIntelligenceAnalyzer --private --source . --remote origin
git push -u origin main
```

(Usa `--public` al posto di `--private` per un repository pubblico.)

## 7. Verifiche

```powershell
git remote -v                    # origin deve puntare a github.com/<utente>/LocalCodeIntelligenceAnalyzer
git log --show-signature -1      # "Good ... signature"
gh repo view --web               # apre il repository nel browser
```

Su GitHub, accanto al commit deve comparire **Verified**. La scheda *Actions* esegue la CI (build e test su Windows).

## 8. Aggiornamenti successivi

Doppio clic su **`GitHub-2-invia-modifiche.bat`**: mostra i file cambiati, blocca segreti e file non pubblicabili,
chiede il messaggio, crea il commit firmato e lo invia (con conferma a ogni passo). Equivalente:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\03-review-and-push.ps1 -Message "Fase 3: Roslyn semantico"
```

oppure a mano: `git add .` → `git commit -S -m "..."` → `git push`.

Se hai ricevuto una nuova versione come zip, `Aggiorna-sorgenti-da-zip.bat` aggiorna i sorgenti e alla fine
propone direttamente questo passaggio.

## 9. Pubblicare una release (zip scaricabile)

1. Verifica che `Directory.Build.props` contenga la nuova versione e che `CHANGELOG.md` abbia la sezione `## [X.Y.Z]`.
2. Invia le modifiche (`GitHub-2-invia-modifiche.bat`).
3. Doppio clic su **`GitHub-3-crea-release.bat`**: controlla che tutto sia già su GitHub e che il tag non esista,
   crea il tag **firmato** `vX.Y.Z` e, dopo conferma, lo invia.
4. GitHub Actions (`release.yml`) compila, esegue i test, crea lo zip win-x64 self-contained e pubblica la release
   con il testo del CHANGELOG. Avanzamento nella scheda *Actions* (circa 5–10 minuti).

Con un repository privato la release è visibile solo a te e ai collaboratori.

## 10. Cosa non deve finire su GitHub

Già escluso da `.gitignore` e controllato dagli script: `bin/`, `obj/`, `artifacts/`, database (`*.db`,
`*.sqlite`), log, esportazioni (`*.lcia-export.*`), `.env`, `secrets.json`, chiavi e certificati (`*.pfx`, `*.pem`,
`*.key`, `*.snk`, `id_rsa*`), file personali di Visual Studio. Non aggiungere repository analizzati o sorgenti
di terzi non autorizzati.

## Problemi comuni

| Messaggio | Soluzione |
|---|---|
| `gh: command not found` / non riconosciuto | Riapri PowerShell dopo l'installazione |
| `You are not logged into any GitHub hosts` | `gh auth login` |
| `Name already exists on this account` | Il repository esiste già: lo script propone di collegarlo come `origin` |
| `error: gpg failed to sign the data` o `ssh-keygen` non trovato | Installa/aggiorna Git per Windows; verifica `user.signingkey` |
| `Updates were rejected` | Il repository remoto ha commit che non hai: `git pull --rebase origin main`, poi rilancia |
| Script bloccati da ExecutionPolicy | Usa `powershell -ExecutionPolicy Bypass -File ...` |
