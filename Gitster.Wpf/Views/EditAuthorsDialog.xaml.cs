using System.Windows;
using Gitster.Services;
using Gitster.ApplicationLayer;
using Gitster.ViewModels;

namespace Gitster.Views;

public partial class EditAuthorsDialog : Window
{
    private readonly AuthorPanelViewModel _vm;

    public string SelectedAuthorText    => _vm.AuthorText;
    public string SelectedCommitterText => _vm.CommitterText;

    public EditAuthorsDialog(AuthorDirectoryService authorDir)
    {
        InitializeComponent();
        _vm = new AuthorPanelViewModel(null!, authorDir);
        DataContext = _vm;
    }

    private void OnSelect(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
