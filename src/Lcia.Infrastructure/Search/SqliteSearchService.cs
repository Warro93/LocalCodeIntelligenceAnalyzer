using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Lcia.Application;
using Lcia.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static Lcia.Infrastructure.SqliteHelpers;

namespace Lcia.Infrastructure;

/// <summary>
/// Search Engine su SQLite: FTS5 per le parole chiave, scansione dei blocchi indicizzati per testo
/// esatto e regex, tabelle dei simboli e delle relazioni per le ricerche strutturali.
/// Tutti i risultati riportano percorso e numero di riga.
/// </summary>
public sealed class SqliteSearchService : ISearchService
{
    private const int MaxPreviewLength = 300;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly SqliteConnectionFactory _factory;
    private readonly IIndexStore _store;
    private readonly ISecretScanner _secrets;
    private readonly ILogger<SqliteSearchService> _logger;

    public SqliteSearchService(SqliteConnectionFactory factory, IIndexStore store, ISecretScanner secrets, ILogger<SqliteSearchService> logger)
    {
        _factory = factory;
        _store = store;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return SearchResult.Empty("Inserisci un testo da cercare.");
        }

        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            using var connection = _factory.Open();
            try
            {
                var (hits, truncated, warning) = query.Mode switch
                {
                    SearchMode.Words => SearchWords(connection, query, cancellationToken),
                    SearchMode.ExactText => ScanContent(connection, query, BuildExactMatcher(query), cancellationToken),
                    SearchMode.Regex => SearchRegex(connection, query, cancellationToken),
                    SearchMode.Symbol => SearchSymbols(connection, query, cancellationToken),
                    SearchMode.References => SearchReferences(connection, query, cancellationToken),
                    _ => (new List<SearchHit>(), false, "Modalità di ricerca non supportata."),
                };
                if (query.MaskSensitive)
                {
                    hits = MaskPreviews(hits, query.Redaction ?? RedactionOptions.Default);
                }

                return new SearchResult(hits, truncated, stopwatch.Elapsed, warning);
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "Ricerca non riuscita ({Mode})", query.Mode);
                return new SearchResult([], false, stopwatch.Elapsed, "La ricerca non è riuscita: " + ex.Message);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Oscura i valori sensibili nelle anteprime: la ricerca usa il contenuto reale, ma ciò che viene mostrato
    /// o copiato non contiene password, token, chiavi né connection string. I file tipicamente sensibili
    /// (secrets.json, .env, chiavi) non mostrano alcuna anteprima.
    /// </summary>
    private List<SearchHit> MaskPreviews(List<SearchHit> hits, RedactionOptions options)
    {
        var masked = new List<SearchHit>(hits.Count);
        foreach (var hit in hits)
        {
            if (SensitiveFiles.IsSensitive(hit.RelativePath))
            {
                masked.Add(hit with { Preview = "[REDACTED:sensitive-file]" });
                continue;
            }

            var language = LanguageCatalog.FromPath(hit.RelativePath);
            var preview = _secrets.Redact(hit.Preview, language, options).Text;
            var detail = hit.Detail is null ? null : _secrets.Redact(hit.Detail, language, options).Text;
            masked.Add(hit with { Preview = preview, Detail = detail });
        }

        return masked;
    }

    // ------------------------------------------------------------------ parole chiave (FTS5)

    private static (List<SearchHit> Hits, bool Truncated, string? Warning) SearchWords(SqliteConnection connection, SearchQuery query, CancellationToken cancellationToken)
    {
        var parsed = FtsQueryBuilder.Build(query.Text);
        if (parsed is null)
        {
            return ([], false, "La query non contiene parole ricercabili.");
        }

        using var command = connection.CreateCommand();
        var filter = BuildFileFilter(command, query);
        command.CommandText =
            "SELECT c.start_line, f.relative_path, p.name, content_fts.content, bm25(content_fts) AS score " +
            "FROM content_fts " +
            "JOIN chunks c ON c.id = content_fts.rowid " +
            "JOIN files f ON f.id = c.file_id " +
            "LEFT JOIN projects p ON p.id = f.project_id " +
            "WHERE content_fts MATCH $match" + filter +
            " ORDER BY score LIMIT $limit;";
        command.Set("$match", parsed.MatchExpression);
        command.Set("$limit", Math.Max(50, query.MaxResults));

        var hits = new List<SearchHit>();
        var truncated = false;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startLine = reader.GetInt32(0);
            var path = reader.GetString(1);
            var project = reader.GetNullableString(2);
            var content = reader.GetString(3);
            var score = reader.GetDouble(4);
            var found = false;
            var lines = TextLines.Split(content);
            for (var i = 0; i < lines.Count; i++)
            {
                if (!parsed.Terms.Any(t => lines[i].Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                found = true;
                hits.Add(new SearchHit(path, startLine + i, Preview(lines[i]), SearchHitKind.Content, null, project, -score));
                if (hits.Count >= query.MaxResults)
                {
                    truncated = true;
                    break;
                }
            }

            if (!found && hits.Count < query.MaxResults)
            {
                // Corrispondenza su identificatori spezzati o frase su più righe: si indica l'inizio del blocco.
                var firstLine = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
                hits.Add(new SearchHit(path, startLine, Preview(firstLine), SearchHitKind.Content,
                    $"Corrispondenza nel blocco di righe {startLine}-{startLine + lines.Count - 1}", project, -score));
            }

            if (hits.Count >= query.MaxResults)
            {
                truncated = true;
                break;
            }
        }

        return (hits, truncated, null);
    }

    // ------------------------------------------------------------------ testo esatto e regex

    private static Func<string, bool> BuildExactMatcher(SearchQuery query)
    {
        var comparison = query.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = query.Text;
        return line => line.Contains(text, comparison);
    }

    private static (List<SearchHit> Hits, bool Truncated, string? Warning) SearchRegex(SqliteConnection connection, SearchQuery query, CancellationToken cancellationToken)
    {
        Regex regex;
        try
        {
            var options = RegexOptions.CultureInvariant | (query.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(query.Text, options, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            return ([], false, "Espressione regolare non valida: " + ex.Message);
        }

        var timeouts = 0;
        var result = ScanContent(connection, query, line =>
        {
            try
            {
                return regex.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                timeouts++;
                return false;
            }
        }, cancellationToken);

        var warning = timeouts > 0
            ? $"{timeouts} righe non valutate per timeout dell'espressione regolare: semplificala per risultati completi."
            : result.Warning;
        return (result.Hits, result.Truncated, warning);
    }

    private static (List<SearchHit> Hits, bool Truncated, string? Warning) ScanContent(
        SqliteConnection connection, SearchQuery query, Func<string, bool> isMatch, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        var filter = BuildFileFilter(command, query);
        command.CommandText =
            "SELECT c.start_line, f.relative_path, p.name, content_fts.content " +
            "FROM chunks c " +
            "JOIN content_fts ON content_fts.rowid = c.id " +
            "JOIN files f ON f.id = c.file_id " +
            "LEFT JOIN projects p ON p.id = f.project_id " +
            "WHERE 1 = 1" + filter +
            " ORDER BY f.relative_path, c.start_line;";

        var hits = new List<SearchHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startLine = reader.GetInt32(0);
            var path = reader.GetString(1);
            var project = reader.GetNullableString(2);
            var lines = TextLines.Split(reader.GetString(3));
            for (var i = 0; i < lines.Count; i++)
            {
                if (!isMatch(lines[i]))
                {
                    continue;
                }

                hits.Add(new SearchHit(path, startLine + i, Preview(lines[i]), SearchHitKind.Content, null, project, 0));
                if (hits.Count >= query.MaxResults)
                {
                    return (hits, true, null);
                }
            }
        }

        return (hits, false, null);
    }

    // ------------------------------------------------------------------ simboli e riferimenti

    private static (List<SearchHit> Hits, bool Truncated, string? Warning) SearchSymbols(SqliteConnection connection, SearchQuery query, CancellationToken cancellationToken)
    {
        var text = query.Text.Trim();
        var hasWildcard = text.Contains('*', StringComparison.Ordinal);
        var likeCore = EscapeLike(text).Replace("*", "%", StringComparison.Ordinal);

        using var command = connection.CreateCommand();
        var filter = BuildFileFilter(command, query);
        var kindFilter = new StringBuilder();
        if (query.SymbolKinds.Count > 0)
        {
            var names = new List<string>();
            var index = 0;
            foreach (var kind in query.SymbolKinds.Distinct())
            {
                var parameter = "$kind" + index++;
                names.Add(parameter);
                command.Set(parameter, kind.ToString());
            }

            kindFilter.Append(" AND s.kind IN (").Append(string.Join(", ", names)).Append(')');
        }

        command.CommandText =
            "SELECT s.name, s.full_name, s.kind, s.signature, s.start_line, f.relative_path, p.name, " +
            "CASE WHEN s.name = $exact THEN 0 WHEN s.name LIKE $prefix ESCAPE '\\' THEN 1 ELSE 2 END AS match_rank " +
            "FROM symbols s " +
            "JOIN files f ON f.id = s.file_id " +
            "LEFT JOIN projects p ON p.id = f.project_id " +
            "WHERE (s.name LIKE $pattern ESCAPE '\\' OR s.full_name LIKE $pattern ESCAPE '\\')" + filter + kindFilter +
            " ORDER BY match_rank, length(s.name), f.relative_path, s.start_line LIMIT $limit;";
        command.Set("$exact", text);
        command.Set("$prefix", likeCore + "%");
        command.Set("$pattern", hasWildcard ? likeCore : "%" + likeCore + "%");
        command.Set("$limit", query.MaxResults + 1);

        var hits = new List<SearchHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hits.Count >= query.MaxResults)
            {
                return (hits, true, null);
            }

            var kind = ParseEnum(reader.GetString(2), SymbolKind.Class);
            var signature = reader.GetNullableString(3);
            var preview = string.IsNullOrWhiteSpace(signature) ? reader.GetString(1) : signature;
            hits.Add(new SearchHit(
                reader.GetString(5),
                reader.GetInt32(4),
                Preview(preview),
                SearchHitKind.Symbol,
                $"{kind.ToItalian()} · {reader.GetString(1)}",
                reader.GetNullableString(6),
                2 - reader.GetInt32(7)));
        }

        return (hits, false, null);
    }

    private static (List<SearchHit> Hits, bool Truncated, string? Warning) SearchReferences(SqliteConnection connection, SearchQuery query, CancellationToken cancellationToken)
    {
        var text = query.Text.Trim();
        var simple = RelationExtensions.ToSimpleName(text);

        using var command = connection.CreateCommand();
        var filter = BuildFileFilter(command, query);
        var kindFilter = new StringBuilder();
        if (query.RelationKinds.Count > 0)
        {
            var names = new List<string>();
            var index = 0;
            foreach (var kind in query.RelationKinds.Distinct())
            {
                var parameter = "$relkind" + index++;
                names.Add(parameter);
                command.Set(parameter, kind.ToString());
            }

            kindFilter.Append(" AND r.kind IN (").Append(string.Join(", ", names)).Append(')');
        }

        command.CommandText =
            "SELECT r.kind, r.source_name, r.target_name, r.evidence, r.line, r.evidence_text, f.relative_path, p.name " +
            "FROM relations r " +
            "JOIN files f ON f.id = r.file_id " +
            "LEFT JOIN projects p ON p.id = f.project_id " +
            "WHERE (r.target_simple_name = $simple OR r.target_name LIKE $contains ESCAPE '\\' " +
            "OR (r.kind = 'DiRegistration' AND r.source_name LIKE $contains ESCAPE '\\'))" + filter + kindFilter +
            " ORDER BY f.relative_path, r.line LIMIT $limit;";
        command.Set("$simple", simple);
        command.Set("$contains", "%" + EscapeLike(text) + "%");
        command.Set("$limit", query.MaxResults + 1);

        var hits = new List<SearchHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hits.Count >= query.MaxResults)
            {
                return (hits, true, null);
            }

            var kind = ParseEnum(reader.GetString(0), RelationKind.Invocation);
            var evidence = ParseEnum(reader.GetString(3), EvidenceLevel.TextualReference);
            var preview = $"{reader.GetString(1)} → {reader.GetString(2)}";
            var note = reader.GetNullableString(5);
            hits.Add(new SearchHit(
                reader.GetString(6),
                reader.GetInt32(4),
                Preview(preview),
                SearchHitKind.Reference,
                $"{kind.ToItalian()} · {evidence.ToItalian()}" + (string.IsNullOrEmpty(note) ? string.Empty : " · " + note),
                reader.GetNullableString(7),
                0));
        }

        return (hits, false, null);
    }

    // ------------------------------------------------------------------ filtri combinabili

    private static string BuildFileFilter(SqliteCommand command, SearchQuery query)
    {
        var sql = new StringBuilder(" AND f.repository_id = $repo AND f.status = 'Indexed'");
        command.Set("$repo", query.RepositoryId);

        var extensions = query.Extensions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().StartsWith('.') ? e.Trim().ToLowerInvariant() : "." + e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        if (extensions.Count > 0)
        {
            var names = new List<string>();
            for (var i = 0; i < extensions.Count; i++)
            {
                var parameter = "$ext" + i;
                names.Add(parameter);
                command.Set(parameter, extensions[i]);
            }

            sql.Append(" AND f.extension IN (").Append(string.Join(", ", names)).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(query.PathContains))
        {
            sql.Append(" AND f.relative_path LIKE $path ESCAPE '\\'");
            command.Set("$path", "%" + EscapeLike(query.PathContains.Trim().Replace('\\', '/')) + "%");
        }

        if (query.Languages.Count > 0)
        {
            var names = new List<string>();
            var index = 0;
            foreach (var language in query.Languages.Distinct())
            {
                var parameter = "$lang" + index++;
                names.Add(parameter);
                command.Set(parameter, language.ToString());
            }

            sql.Append(" AND f.language IN (").Append(string.Join(", ", names)).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectName))
        {
            sql.Append(" AND p.name = $project");
            command.Set("$project", query.ProjectName.Trim());
        }

        return sql.ToString();
    }

    private static string Preview(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= MaxPreviewLength ? trimmed : trimmed[..MaxPreviewLength] + "…";
    }
}
