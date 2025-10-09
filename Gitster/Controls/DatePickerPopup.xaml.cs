using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Gitster.Helpers;
using Gitster.Models;
using Gitster.ViewModels;

namespace Gitster.Controls;

/// <summary>
/// Interaction logic for DatePickerPopup.xaml.
/// </summary>
public partial class DatePickerPopup : UserControl
{
    private static readonly Brush HoverBrush;
    private static readonly Brush MainBrush;
    private CancellationTokenSource? _source;

    static DatePickerPopup()
    {
        HoverBrush = (Brush)Application.Current.Resources["HoverBrush"];
        MainBrush = (Brush)Application.Current.Resources["AccentMainLightestBrush"];
    }

    public DatePickerPopup()
    {
        InitializeComponent();
    }

    private DateControlViewModel ViewModel => (DateControlViewModel)DataContext;

    private static void SetBorderLayout(object sender, int state)
    {
        if (sender is Border b)
        {
            switch (state)
            {
                case 0:
                    b.Background = Brushes.Transparent;
                    break;
                case 1:
                    b.Background = HoverBrush;
                    break;
                case 2:
                    b.Background = MainBrush;
                    break;
            }
        }
    }

    private async void StartChange(object sender, MouseButtonEventArgs e, int monthValue)
    {
        Abort();
        SetBorderLayout(sender, 2);
        var months = 0;
        if (e.LeftButton == MouseButtonState.Pressed)
            months -= monthValue;
        if (e.RightButton == MouseButtonState.Pressed)
            months += monthValue;
        _source = new CancellationTokenSource();

        await SetTimeout(() => ViewModel.ChangeMonth(months), _source.Token);
    }

    private async Task SetTimeout(Action action, CancellationToken token)
    {
        try
        {
            action();
            await Task.Delay(800, token);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                action();
                await Task.Delay(50, token);
            }
        }
        catch (Exception)
        {
            _source = null;
        }
    }

    private void Abort()
    {
        _source?.Cancel();
    }

    private void MonthOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        StartChange(sender, e, 1);
    }

    private void YearOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        StartChange(sender, e, 12);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        Abort();
        SetBorderLayout(sender, 1);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        Abort();
        SetBorderLayout(sender, 0);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        SetBorderLayout(sender, 1);
    }

    private void MonthOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ChangeMonth(-Math.Sign(e.Delta));
        e.Handled = true;
    }

    private void YearOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ChangeMonth(-Math.Sign(e.Delta) * 12);
        e.Handled = true;
    }

    private void DayOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var holder = (DateTimeHolder)(sender as FrameworkElement)!.DataContext;
            ViewModel.Select(holder);
            if (e.ClickCount == 2)
                ViewModel.IsOpen = false;
        }
    }

    private void Now_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = SystemTime.Now;
    }

    private void HourOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        StartTimeChange(sender, e, 1, true);
    }

    private void MinuteOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        StartTimeChange(sender, e, 1, false);
    }

    private void HourOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ChangeHour(-Math.Sign(e.Delta));
        e.Handled = true;
    }

    private void MinuteOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ChangeMinute(-Math.Sign(e.Delta));
        e.Handled = true;
    }

    private async void StartTimeChange(object sender, MouseButtonEventArgs e, int value, bool isHour)
    {
        Abort();
        SetBorderLayout(sender, 2);
        var change = 0;
        if (e.LeftButton == MouseButtonState.Pressed)
            change -= value;
        if (e.RightButton == MouseButtonState.Pressed)
            change += value;
        _source = new CancellationTokenSource();

        await SetTimeTimeout(() =>
        {
            if (isHour)
                ViewModel.ChangeHour(change);
            else
                ViewModel.ChangeMinute(change);
        }, _source.Token);
    }

    private async Task SetTimeTimeout(Action action, CancellationToken token)
    {
        try
        {
            action();
            await Task.Delay(800, token);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                action();
                await Task.Delay(50, token);
            }
        }
        catch (Exception)
        {
            _source = null;
        }
    }
}
