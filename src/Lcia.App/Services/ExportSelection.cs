using CommunityToolkit.Mvvm.ComponentModel;

namespace Lcia.App.Services;

/// <summary>File scelti dall'utente (da Ricerca o Esplora) per un'esportazione personalizzata.</summary>
public sealed partial class ExportSelection : ObservableObject
{
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private int _count;

    public long? RepositoryId { get; private set; }

    public IReadOnlyCollection<string> Paths => _paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Aggiunge file; se cambia repository la selezione precedente viene azzerata.</summary>
    public int Add(long repositoryId, IEnumerable<string> relativePaths)
    {
        if (RepositoryId != repositoryId)
        {
            _paths.Clear();
            RepositoryId = repositoryId;
        }

        var added = 0;
        foreach (var path in relativePaths)
        {
            if (_paths.Add(path))
            {
                added++;
            }
        }

        Count = _paths.Count;
        return added;
    }

    public void Clear()
    {
        _paths.Clear();
        RepositoryId = null;
        Count = 0;
    }
}
