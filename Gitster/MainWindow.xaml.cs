using System.Windows;
using Gitster.Services;
using Gitster.ViewModels;

namespace Gitster;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AppSettingsService _appSettings = new();

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        RestoreWindowSettings();
    }

    private async void Window_Activated(object sender, EventArgs e)
    {
        await _viewModel.OnWindowActivatedAsync();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        _viewModel.AutoFetch.OnWindowStateChanged(WindowState);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        SaveWindowSettings();
    }

    private void RestoreWindowSettings()
    {
        var s = _appSettings.LoadWindowSettings();
        if (s == null) return;

        // Validate that the saved position is visible on any connected monitor.
        // Use VirtualScreen bounds to cover all monitors.
        var vLeft   = SystemParameters.VirtualScreenLeft;
        var vTop    = SystemParameters.VirtualScreenTop;
        var vWidth  = SystemParameters.VirtualScreenWidth;
        var vHeight = SystemParameters.VirtualScreenHeight;

        const double minVisible = 50; // at least 50px of title bar must be reachable
        bool isVisible =
            s.Left + s.Width  > vLeft   + minVisible &&
            s.Left            < vLeft   + vWidth  - minVisible &&
            s.Top             > vTop    - minVisible &&
            s.Top             < vTop    + vHeight - minVisible;

        if (!isVisible)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;

        // Apply restore bounds first, then set state — so un-maximizing later
        // returns to the correct normal size (avoids the classic maximized-bounds bug).
        Left   = s.Left;
        Top    = s.Top;
        Width  = s.Width;
        Height = s.Height;

        if (s.State == WindowState.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowSettings()
    {
        // Save the *restore* bounds, not the maximized bounds.
        // WPF's RestoreBounds gives the normal-state rect even while maximized.
        Rect bounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : new Rect(Left, Top, Width, Height);

        var state = WindowState == WindowState.Minimized
            ? WindowState.Normal   // never persist Minimized
            : WindowState;

        _appSettings.SaveWindowSettings(new AppSettingsService.WindowSettings(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, state));
    }
}
