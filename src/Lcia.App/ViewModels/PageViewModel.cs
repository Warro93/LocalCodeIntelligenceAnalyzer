using CommunityToolkit.Mvvm.ComponentModel;

namespace Lcia.App.ViewModels;

/// <summary>Pagina navigabile dal menu laterale.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string glyph, string description)
    {
        Title = title;
        Glyph = glyph;
        Description = description;
    }

    public string Title { get; }

    /// <summary>Icona (carattere del font Segoe Fluent Icons / MDL2 Assets).</summary>
    public string Glyph { get; }

    public string Description { get; }

    /// <summary>Chiamato quando la pagina diventa visibile.</summary>
    public virtual Task OnActivatedAsync() => Task.CompletedTask;
}
