namespace Lcia.Domain;

public enum ProjectKind
{
    Solution,
    Project,
}

public enum ProjectReferenceKind
{
    /// <summary>Progetto elencato in una soluzione.</summary>
    SolutionMember,

    /// <summary>ProjectReference di MSBuild.</summary>
    Project,

    /// <summary>Pacchetto NuGet (PackageReference o packages.config).</summary>
    Package,

    /// <summary>Riferimento ad assembly (Reference Include=...).</summary>
    Assembly,
}

/// <summary>Riferimento dichiarato da una soluzione o da un progetto.</summary>
public sealed record ProjectReferenceInfo
{
    public ProjectReferenceKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }

    /// <summary>Per riferimenti a progetti: percorso relativo alla radice del repository (se interno).</summary>
    public string? TargetPath { get; init; }

    /// <summary>True se il progetto referenziato si trova nella cartella analizzata.</summary>
    public bool ExistsInRepository { get; init; }
}

/// <summary>Soluzione o progetto rilevato nella cartella analizzata.</summary>
public sealed record ProjectInfo
{
    public long Id { get; init; }
    public long RepositoryId { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public ProjectKind Kind { get; init; }
    public string? Sdk { get; init; }
    public string? TargetFrameworks { get; init; }
    public string? OutputType { get; init; }
    public string? AssemblyName { get; init; }
    public string? RootNamespace { get; init; }

    /// <summary>Errore di lettura del file di progetto, se presente (il progetto resta elencato).</summary>
    public string? ParseError { get; init; }

    public IReadOnlyList<ProjectReferenceInfo> References { get; init; } = [];

    /// <summary>Cartella relativa del progetto ("" per la radice).</summary>
    public string Directory
    {
        get
        {
            var index = RelativePath.LastIndexOf('/');
            return index < 0 ? string.Empty : RelativePath[..index];
        }
    }
}
