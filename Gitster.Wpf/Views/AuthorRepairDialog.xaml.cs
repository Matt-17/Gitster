using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class AuthorRepairDialog : Window
{
    public AuthorRepairDialog(AuthorRepairViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.RewriteCompleted += () => DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
