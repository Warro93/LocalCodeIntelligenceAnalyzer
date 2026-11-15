using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using ICSharpCode.AvalonEdit.Highlighting;
using Lcia.App.Services;
using Lcia.App.ViewModels;

namespace Lcia.App.Views;

/// <summary>
/// Collega l'editor AvalonEdit (le cui proprietà Text e posizione non sono bindabili)
/// allo stato del ViewModel: testo del file e riga richiesta. Adatta i colori della sintassi al tema.
/// </summary>
public partial class ExplorerView : UserControl
{
    private ExplorerViewModel? _viewModel;
    private string? _shownText;
    private int _shownNavigation = -1;

    public ExplorerView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as ExplorerViewModel);
        Loaded += (_, _) =>
        {
            if (!WeakReferenceMessenger.Default.IsRegistered<ThemeChangedMessage>(this))
            {
                WeakReferenceMessenger.Default.Register<ExplorerView, ThemeChangedMessage>(this, static (view, message) => view.OnThemeChanged(message.IsDark));
            }

            Attach(DataContext as ExplorerViewModel);
        };
        Unloaded += (_, _) =>
        {
            WeakReferenceMessenger.Default.Unregister<ThemeChangedMessage>(this);
            Detach();
        };
    }

    private void OnThemeChanged(bool dark)
    {
        HighlightingPalette.Apply(Editor.SyntaxHighlighting, dark);
        Editor.TextArea.TextView.Redraw();
    }

    private void Attach(ExplorerViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _viewModel))
        {
            Sync();
            return;
        }

        Detach();
        _viewModel = viewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _shownText = null;
        _shownNavigation = -1;
        Sync();
    }

    private void Detach()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.FileText) or nameof(ExplorerViewModel.NavigationVersion))
        {
            Sync();
        }
    }

    private void Sync()
    {
        var viewModel = _viewModel;
        if (viewModel is null)
        {
            return;
        }

        if (!ReferenceEquals(_shownText, viewModel.FileText))
        {
            var highlighting = HighlightingManager.Instance.GetDefinitionByExtension(viewModel.FileExtension);
            HighlightingPalette.Apply(highlighting, ThemeService.IsDarkActive);
            Editor.SyntaxHighlighting = highlighting;
            Editor.Text = viewModel.FileText;
            _shownText = viewModel.FileText;
            _shownNavigation = -1;
        }

        if (_shownNavigation == viewModel.NavigationVersion || viewModel.RequestedLine <= 0 || Editor.Document.LineCount == 0)
        {
            return;
        }

        _shownNavigation = viewModel.NavigationVersion;
        var line = Math.Min(viewModel.RequestedLine, Editor.Document.LineCount);

        // Lo scorrimento avviene dopo il layout, così funziona anche al primo caricamento della vista.
        Dispatcher.InvokeAsync(() =>
        {
            if (line > Editor.Document.LineCount)
            {
                return;
            }

            var documentLine = Editor.Document.GetLineByNumber(line);
            Editor.ScrollTo(line, 0);
            Editor.Select(documentLine.Offset, documentLine.Length);
            Editor.TextArea.Caret.Offset = documentLine.Offset;
        }, DispatcherPriority.Loaded);
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ExplorerViewModel viewModel)
        {
            viewModel.SelectedNode = e.NewValue as FileTreeNode;
        }
    }
}
