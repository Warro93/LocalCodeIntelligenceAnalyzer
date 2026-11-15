using Lcia.Application;
using Lcia.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static Lcia.Infrastructure.SqliteHelpers;

namespace Lcia.Infrastructure;

/// <summary>
/// Indice locale su SQLite. Tutte le operazioni girano su thread del pool (mai sul thread UI).
/// Le scritture di un batch avvengono in un'unica transazione.
/// </summary>
public sealed class SqliteIndexStore : IIndexStore
{
    private const string FileColumns =
        "f.id, f.repository_id, f.project_id, f.relative_path, f.extension, f.language, f.size_bytes, f.last_write_ticks, " +
        "f.sha256, f.line_count, f.encoding, f.status, f.status_detail, f.analysis_level, f.index_version, f.indexed_utc";

    private const string SymbolColumns =
        "id, file_id, parent_id, kind, name, full_name, container, namespace, signature, accessibility, modifiers, " +
        "start_line, end_line, documentation, attributes";

    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<SqliteIndexStore> _logger;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteIndexStore(SqliteConnectionFactory factory, ILogger<SqliteIndexStore> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public string DatabasePath => _factory.DatabasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await Task.Run(() =>
            {
                using var connection = _factory.Open();
                SqliteSchema.Migrate(connection);
            }, cancellationToken).ConfigureAwait(false);
            _initialized = true;
            _logger.LogInformation("Indice locale pronto: {Path} (schema v{Version})", DatabasePath, SqliteSchema.CurrentVersion);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    // ------------------------------------------------------------------ repository

    public Task<RepositoryInfo> GetOrCreateRepositoryAsync(string rootPath, string name, string? entryPath, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var insert = Command(connection,
                "INSERT INTO repositories(root_path, name, entry_path, created_utc) VALUES($root, $name, $entry, $created) " +
                "ON CONFLICT(root_path) DO UPDATE SET name = excluded.name, entry_path = COALESCE(excluded.entry_path, repositories.entry_path);");
            insert.Set("$root", rootPath);
            insert.Set("$name", name);
            insert.Set("$entry", entryPath);
            insert.Set("$created", ToDb(DateTimeOffset.UtcNow));
            insert.ExecuteNonQuery();
            return QueryRepositories(connection, "WHERE root_path = $root", c => c.Set("$root", rootPath)).Single();
        }, cancellationToken);

    public Task<RepositoryInfo?> FindRepositoryAsync(string rootPath, CancellationToken cancellationToken = default) =>
        RunAsync(connection => QueryRepositories(connection, "WHERE root_path = $root", c => c.Set("$root", rootPath)).FirstOrDefault(), cancellationToken);

    public Task<RepositoryInfo?> GetRepositoryAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        RunAsync(connection => QueryRepositories(connection, "WHERE id = $id", c => c.Set("$id", repositoryId)).FirstOrDefault(), cancellationToken);

    public Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<RepositoryInfo>>(connection => QueryRepositories(connection, "ORDER BY name COLLATE NOCASE", _ => { }), cancellationToken);

    public Task UpdateRepositoryScanInfoAsync(long repositoryId, DateTimeOffset scanUtc, string? entryPath, GitHeadInfo? git, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = Command(connection,
                "UPDATE repositories SET last_scan_utc = $scan, entry_path = COALESCE($entry, entry_path), git_branch = $branch, git_commit = $commit WHERE id = $id;");
            command.Set("$scan", ToDb(scanUtc));
            command.Set("$entry", entryPath);
            command.Set("$branch", git?.Branch);
            command.Set("$commit", git?.Commit);
            command.Set("$id", repositoryId);
            command.ExecuteNonQuery();
            return true;
        }, cancellationToken);

    public Task DeleteRepositoryAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using (var fts = Command(connection,
                "DELETE FROM content_fts WHERE rowid IN (SELECT c.id FROM chunks c JOIN files f ON f.id = c.file_id WHERE f.repository_id = $id);", transaction))
            {
                fts.Set("$id", repositoryId);
                fts.ExecuteNonQuery();
            }

            using (var delete = Command(connection, "DELETE FROM repositories WHERE id = $id;", transaction))
            {
                delete.Set("$id", repositoryId);
                delete.ExecuteNonQuery();
            }

            transaction.Commit();
            _logger.LogInformation("Indice del repository {Id} eliminato (i file sorgente non sono stati toccati).", repositoryId);
            return true;
        }, cancellationToken);

    private static List<RepositoryInfo> QueryRepositories(SqliteConnection connection, string whereOrOrder, Action<SqliteCommand> bind)
    {
        using var command = Command(connection,
            "SELECT id, root_path, name, entry_path, created_utc, last_scan_utc, git_branch, git_commit FROM repositories " + whereOrOrder + ";");
        bind(command);
        using var reader = command.ExecuteReader();
        var list = new List<RepositoryInfo>();
        while (reader.Read())
        {
            list.Add(new RepositoryInfo
            {
                Id = reader.GetInt64(0),
                RootPath = reader.GetString(1),
                Name = reader.GetString(2),
                EntryPath = reader.GetNullableString(3),
                CreatedUtc = FromDb(reader.GetString(4)),
                LastScanUtc = FromDbNullable(reader.GetNullableString(5)),
                GitBranch = reader.GetNullableString(6),
                GitCommit = reader.GetNullableString(7),
            });
        }

        return list;
    }

    // ------------------------------------------------------------------ scan runs

    public Task<long> BeginScanRunAsync(long repositoryId, DateTimeOffset startedUtc, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = Command(connection,
                "INSERT INTO scan_runs(repository_id, started_utc, status) VALUES($repo, $started, $status) RETURNING id;");
            command.Set("$repo", repositoryId);
            command.Set("$started", ToDb(startedUtc));
            command.Set("$status", ScanRunStatus.Running.ToString());
            return command.ExecuteInt64();
        }, cancellationToken);

    public Task CompleteScanRunAsync(ScanRun run, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = Command(connection,
                "UPDATE scan_runs SET finished_utc = $finished, status = $status, files_seen = $seen, files_indexed = $indexed, " +
                "files_unchanged = $unchanged, files_removed = $removed, files_skipped = $skipped, errors = $errors, " +
                "warning_count = $warningCount, warnings = $warnings, message = $message WHERE id = $id;");
            command.Set("$finished", ToDb(run.FinishedUtc ?? DateTimeOffset.UtcNow));
            command.Set("$status", run.Status.ToString());
            command.Set("$seen", run.FilesSeen);
            command.Set("$indexed", run.FilesIndexed);
            command.Set("$unchanged", run.FilesUnchanged);
            command.Set("$removed", run.FilesRemoved);
            command.Set("$skipped", run.FilesSkipped);
            command.Set("$errors", run.Errors);
            command.Set("$warningCount", run.WarningCount);
            command.Set("$warnings", run.WarningsText);
            command.Set("$message", run.Message);
            command.Set("$id", run.Id);
            command.ExecuteNonQuery();
            return true;
        }, cancellationToken);

    public Task<IReadOnlyList<ScanRun>> ListScanRunsAsync(long repositoryId, int limit, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<ScanRun>>(connection =>
        {
            using var command = Command(connection,
                "SELECT id, repository_id, started_utc, finished_utc, status, files_seen, files_indexed, files_unchanged, files_removed, " +
                "files_skipped, errors, warning_count, warnings, message FROM scan_runs WHERE repository_id = $repo ORDER BY id DESC LIMIT $limit;");
            command.Set("$repo", repositoryId);
            command.Set("$limit", Math.Max(1, limit));
            using var reader = command.ExecuteReader();
            var list = new List<ScanRun>();
            while (reader.Read())
            {
                list.Add(new ScanRun
                {
                    Id = reader.GetInt64(0),
                    RepositoryId = reader.GetInt64(1),
                    StartedUtc = FromDb(reader.GetString(2)),
                    FinishedUtc = FromDbNullable(reader.GetNullableString(3)),
                    Status = ParseEnum(reader.GetString(4), ScanRunStatus.Failed),
                    FilesSeen = reader.GetInt32(5),
                    FilesIndexed = reader.GetInt32(6),
                    FilesUnchanged = reader.GetInt32(7),
                    FilesRemoved = reader.GetInt32(8),
                    FilesSkipped = reader.GetInt32(9),
                    Errors = reader.GetInt32(10),
                    WarningCount = reader.GetInt32(11),
                    WarningsText = reader.GetNullableString(12),
                    Message = reader.GetNullableString(13),
                });
            }

            return list;
        }, cancellationToken);

    public Task<int> MarkInterruptedScanRunsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = Command(connection,
                "UPDATE scan_runs SET status = $cancelled, finished_utc = COALESCE(finished_utc, $now), " +
                "message = 'Interrotta (applicazione chiusa durante la scansione). La prossima indicizzazione riprende saltando i file già salvati.' " +
                "WHERE status = $running;");
            command.Set("$cancelled", ScanRunStatus.Cancelled.ToString());
            command.Set("$running", ScanRunStatus.Running.ToString());
            command.Set("$now", ToDb(DateTimeOffset.UtcNow));
            return command.ExecuteNonQuery();
        }, cancellationToken);

    // ------------------------------------------------------------------ files

    public Task<IReadOnlyDictionary<string, FileFingerprint>> GetFileFingerprintsAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyDictionary<string, FileFingerprint>>(connection =>
        {
            using var command = Command(connection,
                "SELECT id, relative_path, size_bytes, last_write_ticks, sha256, status, index_version FROM files WHERE repository_id = $repo;");
            command.Set("$repo", repositoryId);
            using var reader = command.ExecuteReader();
            var result = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                result[reader.GetString(1)] = new FileFingerprint(
                    reader.GetInt64(0),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetNullableString(4),
                    ParseEnum(reader.GetString(5), FileIndexStatus.Unreadable),
                    reader.GetInt32(6));
            }

            return result;
        }, cancellationToken);

    public Task SaveFilesAsync(long repositoryId, IReadOnlyList<FileWorkItem> items, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var writer = new BatchWriter(connection, transaction, repositoryId);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Kind == FileWorkKind.MetadataOnly)
                {
                    writer.Touch(item.File);
                }
                else
                {
                    writer.Save(item);
                }
            }

            transaction.Commit();
            return true;
        }, cancellationToken);

    public Task RemoveFilesAsync(long repositoryId, IReadOnlyCollection<string> relativePaths, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var fts = Command(connection,
                "DELETE FROM content_fts WHERE rowid IN (SELECT c.id FROM chunks c JOIN files f ON f.id = c.file_id WHERE f.repository_id = $repo AND f.relative_path = $path);",
                transaction);
            using var delete = Command(connection, "DELETE FROM files WHERE repository_id = $repo AND relative_path = $path;", transaction);
            foreach (var path in relativePaths)
            {
                fts.Set("$repo", repositoryId);
                fts.Set("$path", path);
                fts.ExecuteNonQuery();
                delete.Set("$repo", repositoryId);
                delete.Set("$path", path);
                delete.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }, cancellationToken);

    public Task ReplaceProjectsAsync(long repositoryId, IReadOnlyList<ProjectInfo> projects, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using (var clear = Command(connection, "DELETE FROM projects WHERE repository_id = $repo;", transaction))
            {
                clear.Set("$repo", repositoryId);
                clear.ExecuteNonQuery();
            }

            using var insertProject = Command(connection,
                "INSERT INTO projects(repository_id, relative_path, name, kind, sdk, target_frameworks, output_type, assembly_name, root_namespace, parse_error) " +
                "VALUES($repo, $path, $name, $kind, $sdk, $tfm, $output, $assembly, $rootNs, $error) RETURNING id;", transaction);
            using var insertReference = Command(connection,
                "INSERT INTO project_references(project_id, kind, name, version, target_path, exists_in_repository) VALUES($project, $kind, $name, $version, $target, $exists);",
                transaction);
            using var assign = Command(connection,
                "UPDATE files SET project_id = $project WHERE repository_id = $repo AND ($prefix = '' OR relative_path LIKE $like ESCAPE '\\');",
                transaction);

            var assignments = new List<(string Directory, long Id)>();
            foreach (var project in projects)
            {
                insertProject.Set("$repo", repositoryId);
                insertProject.Set("$path", project.RelativePath);
                insertProject.Set("$name", project.Name);
                insertProject.Set("$kind", project.Kind.ToString());
                insertProject.Set("$sdk", project.Sdk);
                insertProject.Set("$tfm", project.TargetFrameworks);
                insertProject.Set("$output", project.OutputType);
                insertProject.Set("$assembly", project.AssemblyName);
                insertProject.Set("$rootNs", project.RootNamespace);
                insertProject.Set("$error", project.ParseError);
                var projectId = insertProject.ExecuteInt64();

                foreach (var reference in project.References)
                {
                    insertReference.Set("$project", projectId);
                    insertReference.Set("$kind", reference.Kind.ToString());
                    insertReference.Set("$name", reference.Name);
                    insertReference.Set("$version", reference.Version);
                    insertReference.Set("$target", reference.TargetPath);
                    insertReference.Set("$exists", reference.ExistsInRepository ? 1 : 0);
                    insertReference.ExecuteNonQuery();
                }

                if (project.Kind == ProjectKind.Project)
                {
                    assignments.Add((project.Directory, projectId));
                }
            }

            // Ogni file appartiene al progetto la cui cartella è la più vicina: si assegnano prima
            // le cartelle più esterne, poi quelle più interne che sovrascrivono.
            foreach (var (directory, id) in assignments.OrderBy(a => a.Directory.Length))
            {
                assign.Set("$project", id);
                assign.Set("$repo", repositoryId);
                assign.Set("$prefix", directory);
                assign.Set("$like", EscapeLike(directory) + "/%");
                assign.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }, cancellationToken);

    public Task<IReadOnlyList<IndexedFile>> ListFilesAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<IndexedFile>>(connection =>
        {
            using var command = Command(connection, $"SELECT {FileColumns} FROM files f WHERE f.repository_id = $repo ORDER BY f.relative_path;");
            command.Set("$repo", repositoryId);
            return ReadFiles(command);
        }, cancellationToken);

    public Task<IndexedFile?> GetFileAsync(long repositoryId, string relativePath, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            using var command = Command(connection, $"SELECT {FileColumns} FROM files f WHERE f.repository_id = $repo AND f.relative_path = $path;");
            command.Set("$repo", repositoryId);
            command.Set("$path", relativePath);
            return ReadFiles(command).FirstOrDefault();
        }, cancellationToken);

    private static List<IndexedFile> ReadFiles(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var list = new List<IndexedFile>();
        while (reader.Read())
        {
            list.Add(new IndexedFile
            {
                Id = reader.GetInt64(0),
                RepositoryId = reader.GetInt64(1),
                ProjectId = reader.GetNullableInt64(2),
                RelativePath = reader.GetString(3),
                Extension = reader.GetString(4),
                Language = ParseEnum(reader.GetString(5), SourceLanguage.Unknown),
                SizeBytes = reader.GetInt64(6),
                LastWriteUtc = new DateTime(reader.GetInt64(7), DateTimeKind.Utc),
                Sha256 = reader.GetNullableString(8),
                LineCount = reader.GetInt32(9),
                Encoding = reader.GetNullableString(10),
                Status = ParseEnum(reader.GetString(11), FileIndexStatus.Unreadable),
                StatusDetail = reader.GetNullableString(12),
                AnalysisLevel = ParseEnum(reader.GetString(13), SupportLevel.TextOnly),
                IndexVersion = reader.GetInt32(14),
                IndexedUtc = FromDb(reader.GetString(15)),
            });
        }

        return list;
    }

    // ------------------------------------------------------------------ projects, symbols, relations

    public Task<IReadOnlyList<ProjectInfo>> ListProjectsAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<ProjectInfo>>(connection =>
        {
            var references = new Dictionary<long, List<ProjectReferenceInfo>>();
            using (var referenceCommand = Command(connection,
                "SELECT r.project_id, r.kind, r.name, r.version, r.target_path, r.exists_in_repository FROM project_references r " +
                "JOIN projects p ON p.id = r.project_id WHERE p.repository_id = $repo ORDER BY r.kind, r.name;"))
            {
                referenceCommand.Set("$repo", repositoryId);
                using var reader = referenceCommand.ExecuteReader();
                while (reader.Read())
                {
                    var projectId = reader.GetInt64(0);
                    if (!references.TryGetValue(projectId, out var list))
                    {
                        list = [];
                        references[projectId] = list;
                    }

                    list.Add(new ProjectReferenceInfo
                    {
                        Kind = ParseEnum(reader.GetString(1), ProjectReferenceKind.Package),
                        Name = reader.GetString(2),
                        Version = reader.GetNullableString(3),
                        TargetPath = reader.GetNullableString(4),
                        ExistsInRepository = reader.GetInt64(5) != 0,
                    });
                }
            }

            using var command = Command(connection,
                "SELECT id, repository_id, relative_path, name, kind, sdk, target_frameworks, output_type, assembly_name, root_namespace, parse_error " +
                "FROM projects WHERE repository_id = $repo ORDER BY kind DESC, relative_path;");
            command.Set("$repo", repositoryId);
            using var projectReader = command.ExecuteReader();
            var projects = new List<ProjectInfo>();
            while (projectReader.Read())
            {
                var id = projectReader.GetInt64(0);
                projects.Add(new ProjectInfo
                {
                    Id = id,
                    RepositoryId = projectReader.GetInt64(1),
                    RelativePath = projectReader.GetString(2),
                    Name = projectReader.GetString(3),
                    Kind = ParseEnum(projectReader.GetString(4), ProjectKind.Project),
                    Sdk = projectReader.GetNullableString(5),
                    TargetFrameworks = projectReader.GetNullableString(6),
                    OutputType = projectReader.GetNullableString(7),
                    AssemblyName = projectReader.GetNullableString(8),
                    RootNamespace = projectReader.GetNullableString(9),
                    ParseError = projectReader.GetNullableString(10),
                    References = references.TryGetValue(id, out var refs) ? refs : [],
                });
            }

            return projects;
        }, cancellationToken);

    public Task<IReadOnlyList<CodeSymbol>> ListSymbolsAsync(long fileId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<CodeSymbol>>(connection =>
        {
            using var command = Command(connection, $"SELECT {SymbolColumns} FROM symbols WHERE file_id = $file ORDER BY start_line, id;");
            command.Set("$file", fileId);
            using var reader = command.ExecuteReader();
            var list = new List<CodeSymbol>();
            while (reader.Read())
            {
                list.Add(new CodeSymbol
                {
                    Id = reader.GetInt64(0),
                    FileId = reader.GetInt64(1),
                    ParentId = reader.GetNullableInt64(2),
                    Kind = ParseEnum(reader.GetString(3), SymbolKind.Class),
                    Name = reader.GetString(4),
                    FullName = reader.GetString(5),
                    ContainerName = reader.GetNullableString(6),
                    Namespace = reader.GetNullableString(7),
                    Signature = reader.GetNullableString(8),
                    Accessibility = reader.GetNullableString(9),
                    Modifiers = reader.GetNullableString(10),
                    StartLine = reader.GetInt32(11),
                    EndLine = reader.GetInt32(12),
                    Documentation = reader.GetNullableString(13),
                    Attributes = reader.GetNullableString(14),
                });
            }

            return list;
        }, cancellationToken);

    public Task<IReadOnlyList<SymbolRelation>> ListRelationsAsync(long fileId, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<SymbolRelation>>(connection =>
        {
            using var command = Command(connection,
                "SELECT id, file_id, source_symbol_id, source_name, target_name, target_simple_name, target_symbol_id, kind, evidence, line, evidence_text " +
                "FROM relations WHERE file_id = $file ORDER BY line, id;");
            command.Set("$file", fileId);
            using var reader = command.ExecuteReader();
            var list = new List<SymbolRelation>();
            while (reader.Read())
            {
                list.Add(new SymbolRelation
                {
                    Id = reader.GetInt64(0),
                    FileId = reader.GetInt64(1),
                    SourceSymbolId = reader.GetNullableInt64(2),
                    SourceName = reader.GetString(3),
                    TargetName = reader.GetString(4),
                    TargetSimpleName = reader.GetString(5),
                    TargetSymbolId = reader.GetNullableInt64(6),
                    Kind = ParseEnum(reader.GetString(7), RelationKind.Invocation),
                    Evidence = ParseEnum(reader.GetString(8), EvidenceLevel.TextualReference),
                    Line = reader.GetInt32(9),
                    EvidenceText = reader.GetNullableString(10),
                });
            }

            return list;
        }, cancellationToken);

    public Task<RepositoryStatistics> GetStatisticsAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            var byStatus = new Dictionary<FileIndexStatus, int>();
            using (var command = Command(connection, "SELECT status, COUNT(*) FROM files WHERE repository_id = $repo GROUP BY status;"))
            {
                command.Set("$repo", repositoryId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    byStatus[ParseEnum(reader.GetString(0), FileIndexStatus.Unreadable)] = reader.GetInt32(1);
                }
            }

            var languages = new List<LanguageStatistics>();
            using (var command = Command(connection,
                "SELECT language, COUNT(*), COALESCE(SUM(line_count), 0), COALESCE(SUM(size_bytes), 0) FROM files " +
                "WHERE repository_id = $repo AND status = 'Indexed' GROUP BY language ORDER BY COUNT(*) DESC;"))
            {
                command.Set("$repo", repositoryId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var language = ParseEnum(reader.GetString(0), SourceLanguage.Unknown);
                    languages.Add(new LanguageStatistics(language, reader.GetInt32(1), reader.GetInt64(2), reader.GetInt64(3), LanguageCatalog.Describe(language).CurrentSupport));
                }
            }

            long Count(string sql)
            {
                using var command = Command(connection, sql);
                command.Set("$repo", repositoryId);
                return command.ExecuteInt64();
            }

            var totalFiles = byStatus.Values.Sum();
            var indexed = byStatus.GetValueOrDefault(FileIndexStatus.Indexed);
            return new RepositoryStatistics(
                totalFiles,
                indexed,
                totalFiles - indexed,
                languages.Sum(l => l.Lines),
                languages.Sum(l => l.Bytes),
                (int)Count("SELECT COUNT(*) FROM symbols WHERE repository_id = $repo;"),
                (int)Count("SELECT COUNT(*) FROM relations WHERE repository_id = $repo;"),
                (int)Count("SELECT COUNT(*) FROM projects WHERE repository_id = $repo AND kind = 'Solution';"),
                (int)Count("SELECT COUNT(*) FROM projects WHERE repository_id = $repo AND kind = 'Project';"),
                languages,
                byStatus);
        }, cancellationToken);

    // ------------------------------------------------------------------ infrastruttura

    private Task<T> RunAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = _factory.Open();
            return work(connection);
        }, cancellationToken);

    /// <summary>Scrittura di un batch di file: comandi preparati e riutilizzati nella transazione.</summary>
    private sealed class BatchWriter : IDisposable
    {
        private readonly long _repositoryId;
        private readonly SqliteCommand _upsertFile;
        private readonly SqliteCommand _touchFile;
        private readonly SqliteCommand _deleteChunks;
        private readonly SqliteCommand _deleteRelations;
        private readonly SqliteCommand _deleteSymbols;
        private readonly SqliteCommand _insertSymbol;
        private readonly SqliteCommand _insertRelation;
        private readonly SqliteCommand _insertChunk;
        private readonly SqliteCommand _insertFts;

        public BatchWriter(SqliteConnection connection, SqliteTransaction transaction, long repositoryId)
        {
            _repositoryId = repositoryId;
            _upsertFile = Command(connection,
                "INSERT INTO files(repository_id, relative_path, extension, language, size_bytes, last_write_ticks, sha256, line_count, encoding, " +
                "status, status_detail, analysis_level, index_version, indexed_utc) " +
                "VALUES($repo, $path, $ext, $lang, $size, $ticks, $sha, $lines, $enc, $status, $detail, $level, $version, $indexed) " +
                "ON CONFLICT(repository_id, relative_path) DO UPDATE SET extension = excluded.extension, language = excluded.language, " +
                "size_bytes = excluded.size_bytes, last_write_ticks = excluded.last_write_ticks, sha256 = excluded.sha256, " +
                "line_count = excluded.line_count, encoding = excluded.encoding, status = excluded.status, status_detail = excluded.status_detail, " +
                "analysis_level = excluded.analysis_level, index_version = excluded.index_version, indexed_utc = excluded.indexed_utc " +
                "RETURNING id;", transaction);
            _touchFile = Command(connection,
                "UPDATE files SET size_bytes = $size, last_write_ticks = $ticks, indexed_utc = $indexed WHERE repository_id = $repo AND relative_path = $path;",
                transaction);
            _deleteChunks = Command(connection, "DELETE FROM chunks WHERE file_id = $file;", transaction);
            _deleteRelations = Command(connection, "DELETE FROM relations WHERE file_id = $file;", transaction);
            _deleteSymbols = Command(connection, "DELETE FROM symbols WHERE file_id = $file;", transaction);
            _insertSymbol = Command(connection,
                "INSERT INTO symbols(repository_id, file_id, parent_id, kind, name, full_name, container, namespace, signature, accessibility, modifiers, " +
                "start_line, end_line, documentation, attributes) VALUES($repo, $file, $parent, $kind, $name, $full, $container, $ns, $sig, $access, " +
                "$mods, $start, $end, $doc, $attrs) RETURNING id;", transaction);
            _insertRelation = Command(connection,
                "INSERT INTO relations(repository_id, file_id, source_symbol_id, source_name, target_name, target_simple_name, kind, evidence, line, evidence_text) " +
                "VALUES($repo, $file, $source, $sourceName, $target, $simple, $kind, $evidence, $line, $text);", transaction);
            _insertChunk = Command(connection, "INSERT INTO chunks(file_id, start_line, end_line) VALUES($file, $start, $end) RETURNING id;", transaction);
            _insertFts = Command(connection, "INSERT INTO content_fts(rowid, content, idents) VALUES($id, $content, $idents);", transaction);
        }

        public void Touch(IndexedFile file)
        {
            _touchFile.Set("$size", file.SizeBytes);
            _touchFile.Set("$ticks", file.LastWriteUtc.Ticks);
            _touchFile.Set("$indexed", ToDb(file.IndexedUtc));
            _touchFile.Set("$repo", _repositoryId);
            _touchFile.Set("$path", file.RelativePath);
            _touchFile.ExecuteNonQuery();
        }

        public void Save(FileWorkItem item)
        {
            var file = item.File;
            _upsertFile.Set("$repo", _repositoryId);
            _upsertFile.Set("$path", file.RelativePath);
            _upsertFile.Set("$ext", file.Extension);
            _upsertFile.Set("$lang", file.Language.ToString());
            _upsertFile.Set("$size", file.SizeBytes);
            _upsertFile.Set("$ticks", file.LastWriteUtc.Ticks);
            _upsertFile.Set("$sha", file.Sha256);
            _upsertFile.Set("$lines", file.LineCount);
            _upsertFile.Set("$enc", file.Encoding);
            _upsertFile.Set("$status", file.Status.ToString());
            _upsertFile.Set("$detail", file.StatusDetail);
            _upsertFile.Set("$level", file.AnalysisLevel.ToString());
            _upsertFile.Set("$version", file.IndexVersion);
            _upsertFile.Set("$indexed", ToDb(file.IndexedUtc));
            var fileId = _upsertFile.ExecuteInt64();

            // Il trigger su chunks elimina anche le righe corrispondenti di content_fts.
            foreach (var delete in new[] { _deleteChunks, _deleteRelations, _deleteSymbols })
            {
                delete.Set("$file", fileId);
                delete.ExecuteNonQuery();
            }

            var analysis = item.Analysis ?? SourceAnalysisResult.Empty;
            var symbolIds = new long[analysis.Symbols.Count];
            for (var i = 0; i < analysis.Symbols.Count; i++)
            {
                var symbol = analysis.Symbols[i];
                long? parentId = symbol.ParentIndex is int parentIndex && parentIndex >= 0 && parentIndex < i ? symbolIds[parentIndex] : null;
                _insertSymbol.Set("$repo", _repositoryId);
                _insertSymbol.Set("$file", fileId);
                _insertSymbol.Set("$parent", parentId);
                _insertSymbol.Set("$kind", symbol.Kind.ToString());
                _insertSymbol.Set("$name", symbol.Name);
                _insertSymbol.Set("$full", symbol.FullName);
                _insertSymbol.Set("$container", symbol.ContainerName);
                _insertSymbol.Set("$ns", symbol.Namespace);
                _insertSymbol.Set("$sig", symbol.Signature);
                _insertSymbol.Set("$access", symbol.Accessibility);
                _insertSymbol.Set("$mods", symbol.Modifiers);
                _insertSymbol.Set("$start", symbol.StartLine);
                _insertSymbol.Set("$end", symbol.EndLine);
                _insertSymbol.Set("$doc", symbol.Documentation);
                _insertSymbol.Set("$attrs", symbol.Attributes);
                symbolIds[i] = _insertSymbol.ExecuteInt64();
            }

            foreach (var relation in analysis.Relations)
            {
                long? sourceId = relation.SourceSymbolIndex is int sourceIndex && sourceIndex >= 0 && sourceIndex < symbolIds.Length ? symbolIds[sourceIndex] : null;
                _insertRelation.Set("$repo", _repositoryId);
                _insertRelation.Set("$file", fileId);
                _insertRelation.Set("$source", sourceId);
                _insertRelation.Set("$sourceName", relation.SourceName);
                _insertRelation.Set("$target", relation.TargetName);
                _insertRelation.Set("$simple", relation.TargetSimpleName);
                _insertRelation.Set("$kind", relation.Kind.ToString());
                _insertRelation.Set("$evidence", relation.Evidence.ToString());
                _insertRelation.Set("$line", relation.Line);
                _insertRelation.Set("$text", relation.EvidenceText);
                _insertRelation.ExecuteNonQuery();
            }

            if (item.Content is null)
            {
                return;
            }

            var lines = TextLines.Split(item.Content);
            for (var start = 0; start < lines.Count; start += IndexFormat.ChunkLines)
            {
                var count = Math.Min(IndexFormat.ChunkLines, lines.Count - start);
                var text = string.Join("\n", lines.GetRange(start, count));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                _insertChunk.Set("$file", fileId);
                _insertChunk.Set("$start", start + 1);
                _insertChunk.Set("$end", start + count);
                var chunkId = _insertChunk.ExecuteInt64();
                _insertFts.Set("$id", chunkId);
                _insertFts.Set("$content", text);
                _insertFts.Set("$idents", IdentifierSplitter.BuildIdentifierTerms(text));
                _insertFts.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            _upsertFile.Dispose();
            _touchFile.Dispose();
            _deleteChunks.Dispose();
            _deleteRelations.Dispose();
            _deleteSymbols.Dispose();
            _insertSymbol.Dispose();
            _insertRelation.Dispose();
            _insertChunk.Dispose();
            _insertFts.Dispose();
        }
    }
}
