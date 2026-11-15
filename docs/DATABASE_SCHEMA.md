# Schema del database locale

File: `%LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer\index\lcia-index.db` (SQLite, modalità WAL).
Un unico database contiene l'indice di tutti i repository; eliminare un repository rimuove solo le sue righe.

## Versioni e migrazioni

- La versione è in `PRAGMA user_version`.
- Le migrazioni sono nell'array `Migrations` di `src/Lcia.Infrastructure/Sqlite/SqliteSchema.cs` e vengono applicate
  all'avvio, ciascuna in una transazione. Una migrazione pubblicata non si modifica mai: si aggiunge la successiva.
- Se il database ha una versione più recente di quella supportata, l'applicazione si ferma con un messaggio chiaro
  invece di alterarlo.
- `IndexFormat.Version` (in `Lcia.Application`) è distinto dalla versione dello schema: quando cambia, i file vengono
  rianalizzati alla prossima indicizzazione senza ricreare il database.

## Tabelle (versione 1)

### repositories
| Colonna | Tipo | Note |
|---|---|---|
| id | INTEGER PK | |
| root_path | TEXT UNIQUE NOCASE | percorso assoluto normalizzato |
| name | TEXT | nome cartella o soluzione |
| entry_path | TEXT | `.sln`/`.slnx` scelta come ingresso |
| created_utc, last_scan_utc | TEXT ISO 8601 | |
| git_branch, git_commit | TEXT | letti da `.git` in sola lettura |

### scan_runs
Esecuzioni di indicizzazione: `status` (`Running`, `Completed`, `Cancelled`, `Failed`), contatori
(`files_seen`, `files_indexed`, `files_unchanged`, `files_removed`, `files_skipped`, `errors`, `warning_count`),
`warnings` (prime 200 righe), `message`.

### projects / project_references
Soluzioni e progetti (`kind` = `Solution` | `Project`), SDK, framework, tipo di output, nome assembly, root namespace,
eventuale `parse_error`. I riferimenti hanno `kind` = `SolutionMember` | `Project` | `Package` | `Assembly`,
`version`, `target_path` (relativo) ed `exists_in_repository`.

### files
| Colonna | Note |
|---|---|
| relative_path | separatore `/`, UNIQUE per repository (NOCASE) |
| extension, language | |
| size_bytes, last_write_ticks | impronta per la scansione incrementale |
| sha256 | hash del contenuto letto |
| line_count, encoding | |
| status | `Indexed`, `TooLarge`, `Binary`, `Unreadable` |
| status_detail | errori di sintassi, motivi di esclusione |
| analysis_level | `TextOnly`, `Structural`, `Syntactic`, `Semantic` |
| index_version | `IndexFormat.Version` usato |
| project_id | progetto la cui cartella è la più vicina |

### symbols
`kind`, `name`, `full_name`, `container`, `namespace`, `signature`, `accessibility`, `modifiers`, `start_line`,
`end_line` (1-based, attributi inclusi), `documentation` (testo di `<summary>`), `attributes`, `parent_id`.

### relations
`source_symbol_id`, `source_name`, `target_name`, `target_simple_name`, `target_symbol_id` (Fase 3), `kind`
(`BaseType`, `ConstructorInjection`, `MemberType`, `Invocation`, `ObjectCreation`, `DiRegistration`,
`UsingNamespace`), `evidence` (vedi ARCHITECTURE.md), `line`, `evidence_text`.

### chunks / content_fts
`chunks` (id, file_id, start_line, end_line) e la tabella virtuale FTS5 `content_fts(content, idents)` con
`rowid = chunks.id` e tokenizer `unicode61 remove_diacritics 2`. Il trigger `trg_chunks_delete` mantiene allineata
la tabella FTS quando i blocchi vengono eliminati (anche per cascata).

## Indici
`ix_files_repository_extension`, `ix_files_project`, `ix_symbols_file`, `ix_symbols_repository_name`,
`ix_symbols_parent`, `ix_relations_file`, `ix_relations_repository_target`, `ix_relations_source_symbol`,
`ix_chunks_file`, `ix_scan_runs_repository`, `ix_project_references_project`.

## Integrità
- Chiavi esterne attive su ogni connessione; eliminazioni a cascata da `repositories`.
- Ogni batch di indicizzazione è una transazione: un'interruzione non lascia file a metà.
