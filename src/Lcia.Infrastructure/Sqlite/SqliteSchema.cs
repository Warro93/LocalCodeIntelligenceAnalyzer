using Microsoft.Data.Sqlite;

namespace Lcia.Infrastructure;

/// <summary>
/// Schema dell'indice locale e migrazioni versionate tramite PRAGMA user_version.
/// Documentazione: docs/DATABASE_SCHEMA.md. Ogni nuova versione aggiunge una migrazione
/// in coda all'array <see cref="Migrations"/>; le migrazioni esistenti non si modificano mai.
/// </summary>
internal static class SqliteSchema
{
    public static int CurrentVersion => Migrations.Length;

    private static readonly string[] Migrations =
    [
        // ---------------------------------------------------------------- v1
        """
        CREATE TABLE IF NOT EXISTS repositories (
            id              INTEGER PRIMARY KEY,
            root_path       TEXT    NOT NULL COLLATE NOCASE UNIQUE,
            name            TEXT    NOT NULL,
            entry_path      TEXT,
            created_utc     TEXT    NOT NULL,
            last_scan_utc   TEXT,
            git_branch      TEXT,
            git_commit      TEXT
        );

        CREATE TABLE IF NOT EXISTS scan_runs (
            id              INTEGER PRIMARY KEY,
            repository_id   INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            started_utc     TEXT    NOT NULL,
            finished_utc    TEXT,
            status          TEXT    NOT NULL,
            files_seen      INTEGER NOT NULL DEFAULT 0,
            files_indexed   INTEGER NOT NULL DEFAULT 0,
            files_unchanged INTEGER NOT NULL DEFAULT 0,
            files_removed   INTEGER NOT NULL DEFAULT 0,
            files_skipped   INTEGER NOT NULL DEFAULT 0,
            errors          INTEGER NOT NULL DEFAULT 0,
            warning_count   INTEGER NOT NULL DEFAULT 0,
            warnings        TEXT,
            message         TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_scan_runs_repository ON scan_runs(repository_id, id);

        CREATE TABLE IF NOT EXISTS projects (
            id                INTEGER PRIMARY KEY,
            repository_id     INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            relative_path     TEXT    NOT NULL COLLATE NOCASE,
            name              TEXT    NOT NULL,
            kind              TEXT    NOT NULL,
            sdk               TEXT,
            target_frameworks TEXT,
            output_type       TEXT,
            assembly_name     TEXT,
            root_namespace    TEXT,
            parse_error       TEXT,
            UNIQUE (repository_id, relative_path)
        );

        CREATE TABLE IF NOT EXISTS project_references (
            id                   INTEGER PRIMARY KEY,
            project_id           INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
            kind                 TEXT    NOT NULL,
            name                 TEXT    NOT NULL,
            version              TEXT,
            target_path          TEXT,
            exists_in_repository INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_project_references_project ON project_references(project_id);

        CREATE TABLE IF NOT EXISTS files (
            id               INTEGER PRIMARY KEY,
            repository_id    INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            project_id       INTEGER REFERENCES projects(id) ON DELETE SET NULL,
            relative_path    TEXT    NOT NULL COLLATE NOCASE,
            extension        TEXT    NOT NULL COLLATE NOCASE,
            language         TEXT    NOT NULL,
            size_bytes       INTEGER NOT NULL,
            last_write_ticks INTEGER NOT NULL,
            sha256           TEXT,
            line_count       INTEGER NOT NULL DEFAULT 0,
            encoding         TEXT,
            status           TEXT    NOT NULL,
            status_detail    TEXT,
            analysis_level   TEXT    NOT NULL DEFAULT 'TextOnly',
            index_version    INTEGER NOT NULL,
            indexed_utc      TEXT    NOT NULL,
            UNIQUE (repository_id, relative_path)
        );
        CREATE INDEX IF NOT EXISTS ix_files_repository_extension ON files(repository_id, extension);
        CREATE INDEX IF NOT EXISTS ix_files_project ON files(project_id);

        CREATE TABLE IF NOT EXISTS symbols (
            id            INTEGER PRIMARY KEY,
            repository_id INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            file_id       INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
            parent_id     INTEGER REFERENCES symbols(id) ON DELETE CASCADE,
            kind          TEXT    NOT NULL,
            name          TEXT    NOT NULL COLLATE NOCASE,
            full_name     TEXT    NOT NULL COLLATE NOCASE,
            container     TEXT,
            namespace     TEXT,
            signature     TEXT,
            accessibility TEXT,
            modifiers     TEXT,
            start_line    INTEGER NOT NULL,
            end_line      INTEGER NOT NULL,
            documentation TEXT,
            attributes    TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_symbols_file ON symbols(file_id);
        CREATE INDEX IF NOT EXISTS ix_symbols_repository_name ON symbols(repository_id, name);
        CREATE INDEX IF NOT EXISTS ix_symbols_parent ON symbols(parent_id);

        CREATE TABLE IF NOT EXISTS relations (
            id                 INTEGER PRIMARY KEY,
            repository_id      INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            file_id            INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
            source_symbol_id   INTEGER REFERENCES symbols(id) ON DELETE CASCADE,
            source_name        TEXT    NOT NULL,
            target_name        TEXT    NOT NULL COLLATE NOCASE,
            target_simple_name TEXT    NOT NULL COLLATE NOCASE,
            target_symbol_id   INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
            kind               TEXT    NOT NULL,
            evidence           TEXT    NOT NULL,
            line               INTEGER NOT NULL,
            evidence_text      TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_relations_file ON relations(file_id);
        CREATE INDEX IF NOT EXISTS ix_relations_repository_target ON relations(repository_id, target_simple_name);
        CREATE INDEX IF NOT EXISTS ix_relations_source_symbol ON relations(source_symbol_id);

        CREATE TABLE IF NOT EXISTS chunks (
            id         INTEGER PRIMARY KEY,
            file_id    INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
            start_line INTEGER NOT NULL,
            end_line   INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_chunks_file ON chunks(file_id);

        -- Indice full-text: rowid = chunks.id. "idents" contiene gli identificatori spezzati (CamelCase, snake_case).
        CREATE VIRTUAL TABLE IF NOT EXISTS content_fts USING fts5(
            content,
            idents,
            tokenize = 'unicode61 remove_diacritics 2'
        );

        -- La tabella virtuale non partecipa alle chiavi esterne: pulizia esplicita.
        CREATE TRIGGER IF NOT EXISTS trg_chunks_delete AFTER DELETE ON chunks
        BEGIN
            DELETE FROM content_fts WHERE rowid = old.id;
        END;
        """,
    ];

    public static void Migrate(SqliteConnection connection)
    {
        EnsureFts5(connection);
        var version = ScalarLong(connection, "PRAGMA user_version;");
        if (version > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Il database dell'indice ha versione {version}, più recente di quella supportata ({CurrentVersion}). " +
                "Aggiorna l'applicazione oppure elimina la cartella dell'indice per ricrearlo.");
        }

        for (var next = (int)version + 1; next <= CurrentVersion; next++)
        {
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Migrations[next - 1];
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"PRAGMA user_version = {next};";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static void EnsureFts5(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS temp.lcia_fts5_probe USING fts5(x); DROP TABLE temp.lcia_fts5_probe;";
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException(
                "La libreria SQLite in uso non include FTS5, necessario per la ricerca full-text. " +
                "Verifica che SQLitePCLRaw.bundle_e_sqlite3 sia distribuito con l'applicazione. Dettaglio: " + ex.Message, ex);
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
