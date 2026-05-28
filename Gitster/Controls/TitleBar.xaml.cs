using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Gitster.Controls;

public partial class TitleBar : UserControl
{
    public TitleBar()
    {
        InitializeComponent();
    }

    private void RecentRepositoriesPopup_Opened(object sender, EventArgs e)
    {
        RecentRepositoriesList.Focus();
    }

    private void RecentRepositoriesPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.TitleBarViewModel vm)
            return;

        if (e.Key == Key.Escape)
        {
            vm.CloseRecentRepositoriesPopupCommand.Execute(null);
            e.Handled = true;
        }
    }
}
