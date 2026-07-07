using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class OptionsWindow : Window
{
    public OptionsWindow(OptionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
