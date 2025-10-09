using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gitster.Helpers;
using Gitster.ViewModels;

namespace Gitster.Controls;

/// <summary>
/// Edit mode for the DateControl.
/// </summary>
public enum EditMode
{
    DateOnly,
    TimeOnly,
    DateTime
}

/// <summary>
/// Interaction logic for DateControl.xaml.
/// </summary>
public partial class DateControl : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DateControl), new FrameworkPropertyMetadata(default(DateTime?), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SelectedDateCallback));

    public static readonly DependencyProperty EditModeProperty =
        DependencyProperty.Register(nameof(EditMode), typeof(EditMode), typeof(DateControl), new PropertyMetadata(EditMode.DateOnly, EditModeChanged));

    static DateControl()
    {
    }

    public DateControl()
    {
        InitializeComponent();
        LayoutRoot.DataContext = new DateControlViewModel();
        ViewModel.SetDate(SystemTime.Today);
        ViewModel.SelectedDateChanged += DateChanged;
    }

    public DateTime? SelectedDate
    {
        get { return (DateTime?)GetValue(SelectedDateProperty); }
        set { SetValue(SelectedDateProperty, value); }
    }

    public EditMode EditMode
    {
        get { return (EditMode)GetValue(EditModeProperty); }
        set { SetValue(EditModeProperty, value); }
    }

    private DateControlViewModel ViewModel => (DateControlViewModel)LayoutRoot.DataContext;

    private static void EditModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DateControl)d;
        control.ViewModel.EditMode = (EditMode)e.NewValue;
    }

    private static void SelectedDateCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DateControl)d;
        try
        {
            control.ViewModel.SelectedDate = (DateTime?)e.NewValue;
        }
        catch (Exception)
        {
        }
    }

    private void DateChanged(object? sender, DateTime? e)
    {
        SelectedDate = e;
        ViewModel.IsOpen = false;
    }

    private void TbDate_GotFocus(object sender, RoutedEventArgs e)
    {
        //ViewModel.IsOpen = true;
    }

    private void TbDate_LostFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOpen = false;
    }

    private void TbDate_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var be = ((TextBox)sender).GetBindingExpression(TextBox.TextProperty);
                Debug.Assert(be != null, nameof(be) + " != null");
                be.UpdateSource();
                TbDate.SelectAll();
                break;
            case Key.Escape:
                ViewModel.Reset();
                TbDate.SelectAll();
                e.Handled = true;
                break;
            case Key.H:
                ViewModel.SelectedDate = SystemTime.Today;
                TbDate.SelectAll();
                e.Handled = true;
                break;
            case Key.M:
                ViewModel.SelectedDate = SystemTime.Today.AddDays(1);
                TbDate.SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.ChangeDate(1);
                TbDate.SelectAll();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.ChangeDate(-1);
                TbDate.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOpen = true;
    }

    private void TbDate_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            ((TextBox)sender).SelectAll();
            e.Handled = true;
        }
    }

    private void Date_Click(object sender, RoutedEventArgs e)
    {
        // Only set the date part, preserve time
        if (ViewModel.SelectedDate.HasValue)
        {
            var currentTime = ViewModel.SelectedDate.Value;
            ViewModel.SelectedDate = new DateTime(SystemTime.Today.Year, SystemTime.Today.Month, SystemTime.Today.Day, 
                currentTime.Hour, currentTime.Minute, currentTime.Second);
        }
        else
        {
            ViewModel.SelectedDate = SystemTime.Today;
        }
    }
}
