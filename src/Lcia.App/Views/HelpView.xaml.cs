using System.ComponentModel;
using System.Windows.Controls;
using Lcia.App.ViewModels;

namespace Lcia.App.Views;

/// <summary>Mostra l'argomento selezionato della guida come FlowDocument (il documento non è bindabile).</summary>
public partial class HelpView : UserControl
{
    private HelpViewModel? _viewModel;

    public HelpView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as HelpViewModel);
        Loaded += (_, _) => Attach(DataContext as HelpViewModel);
        Unloaded += (_, _) => Detach();
    }

    private void Attach(HelpViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _viewModel))
        {
            return;
        }

        Detach();
        _viewModel = viewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Render();
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
        if (e.PropertyName is nameof(HelpViewModel.CurrentBlocks) or nameof(HelpViewModel.HighlightTerms))
        {
            Render();
        }
    }

    private void Render()
    {
        if (_viewModel is null)
        {
            return;
        }

        Viewer.Document = HelpDocumentBuilder.Build(_viewModel.CurrentBlocks, _viewModel.HighlightTerms);
    }
}
