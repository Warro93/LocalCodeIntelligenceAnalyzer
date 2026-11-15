using System.Windows.Controls;

namespace Lcia.App.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
    }
}
