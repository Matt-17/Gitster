using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class RangeTimestampDialog : Window
{
    public RangeTimestampDialog(RangeTimestampViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.RewriteCompleted += () => DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
