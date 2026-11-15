using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Export;

internal sealed record ExportHeaderModel(
    RepositoryInfo Repository,
    ExportRequest Request,
    int FileCount,
    long TotalLines,
    long SourceBytes,
    long EstimatedTokens,
    string ToolVersion,
    DateTimeOffset GeneratedUtc,
    string BaseName);

internal sealed record FileBlockModel(
    IndexedFile File,
    string? ProjectName,
    IReadOnlyList<CodeSymbol> Symbols,
    IReadOnlyList<SymbolRelation> Relations,
    IReadOnlyList<string>? SourceLines,
    string? UnavailableReason,
    IReadOnlyList<SecretFinding> Redactions,
    bool ModifiedSinceIndex,
    bool IncludeSource,
    bool IncludeLineNumbers);

internal sealed record ExportSummaryModel(
    int FilesExported,
    long TotalCharacters,
    long EstimatedTokens,
    int PartsSoFar,
    IReadOnlyList<ExportRedaction> Redactions,
    IReadOnlyList<string> ModifiedSinceIndex,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ExcludedSensitiveFiles,
    bool RedactionEnabled);

internal sealed record ExportIndexModel(
    ExportHeaderModel Header,
    IReadOnlyList<ExportPartInfo> Parts,
    IReadOnlyList<string> DirectoryPaths);
