using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class RepositorySettingsWindow : Window
{
    public RepositorySettingsWindow(RepositorySettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
