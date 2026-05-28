using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class ManageToolsDialog : Window
{
    public ManageToolsDialog(ManageToolsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
