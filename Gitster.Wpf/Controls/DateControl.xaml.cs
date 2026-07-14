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
        catch (Exception ex)
        {
            Debug.WriteLine($"DateControl.SelectedDateCallback: {ex}");
        }
    }

    private void DateChanged(object? sender, DateTime? e)
    {
        SelectedDate = e;
    }

    private void TbDate_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender == TbDate)
            ViewModel.BeginDateTextEdit();
        else if (sender == TbTime)
            ViewModel.BeginTimeTextEdit();

        // Don't auto-open popup on focus
    }

    private void TbDate_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender == TbDate)
            ViewModel.EndDateTextEdit();
        else if (sender == TbTime)
            ViewModel.EndTimeTextEdit();

        // Popup is configured with StaysOpen=False and closes automatically on outside clicks.
    }

    private void TbDate_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (ViewModel.IsOpen)
                {
                    var be = ((TextBox)sender).GetBindingExpression(TextBox.TextProperty);
                    Debug.Assert(be != null, nameof(be) + " != null");
                    be.UpdateSource();
                    ViewModel.RefreshTextDisplay();
                    TbDate.SelectAll();
                    ViewModel.IsOpen = false;
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (ViewModel.IsOpen)
                {
                    ViewModel.Reset();
                    TbDate.SelectAll();
                    ViewModel.IsOpen = false;
                    e.Handled = true;
                }
                break;
            case Key.H when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                if (TbDate.SelectionLength == TbDate.Text.Length || string.IsNullOrEmpty(TbDate.Text))
                {
                    ViewModel.SelectedDate = SystemTime.Today;
                    ViewModel.RefreshTextDisplay();
                    TbDate.SelectAll();
                    e.Handled = true;
                }
                break;
            case Key.M when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                if (TbDate.SelectionLength == TbDate.Text.Length || string.IsNullOrEmpty(TbDate.Text))
                {
                    ViewModel.SelectedDate = SystemTime.Today.AddDays(1);
                    ViewModel.RefreshTextDisplay();
                    TbDate.SelectAll();
                    e.Handled = true;
                }
                break;
            case Key.Up:
                ViewModel.ChangeDate(1);
                ViewModel.RefreshTextDisplay();
                TbDate.SelectAll();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.ChangeDate(-1);
                ViewModel.RefreshTextDisplay();
                TbDate.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void TbTime_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (ViewModel.IsOpen)
                {
                    var be = ((TextBox)sender).GetBindingExpression(TextBox.TextProperty);
                    Debug.Assert(be != null, nameof(be) + " != null");
                    be.UpdateSource();
                    ViewModel.RefreshTextDisplay();
                    TbTime.SelectAll();
                    ViewModel.IsOpen = false;
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (ViewModel.IsOpen)
                {
                    ViewModel.Reset();
                    TbTime.SelectAll();
                    ViewModel.IsOpen = false;
                    e.Handled = true;
                }
                break;
            case Key.Up:
                ViewModel.ChangeMinute(-1);
                ViewModel.RefreshTextDisplay();
                TbTime.SelectAll();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.ChangeMinute(1);
                ViewModel.RefreshTextDisplay();
                TbTime.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOpen = !ViewModel.IsOpen;
    }

    private void TbDate_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            ((TextBox)sender).SelectAll();
            e.Handled = true;
        }
    }

    private void TbDate_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ChangeDate(Math.Sign(e.Delta));
        ViewModel.RefreshTextDisplay();
        e.Handled = true;
    }

    private void TbTime_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.ChangeMinute(-Math.Sign(e.Delta));
        ViewModel.RefreshTextDisplay();
        e.Handled = true;
    }

    private void Today_Click(object sender, RoutedEventArgs e)
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
        ViewModel.RefreshTextDisplay();
    }

    private void Tomorrow_Click(object sender, RoutedEventArgs e)
    {
        var tomorrow = SystemTime.Today.AddDays(1);
        // Only set the date part, preserve time
        if (ViewModel.SelectedDate.HasValue)
        {
            var currentTime = ViewModel.SelectedDate.Value;
            ViewModel.SelectedDate = new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, 
                currentTime.Hour, currentTime.Minute, currentTime.Second);
        }
        else
        {
            ViewModel.SelectedDate = tomorrow;
        }
        ViewModel.RefreshTextDisplay();
    }
}
