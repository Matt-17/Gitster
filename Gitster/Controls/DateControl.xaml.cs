using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private bool _isPopupMouseOver;

    static DateControl()
    {
    }

    public DateControl()
    {
        InitializeComponent();
        LayoutRoot.DataContext = new DateControlViewModel();
        ViewModel.SetDate(SystemTime.Today);
        ViewModel.SelectedDateChanged += DateChanged;
        
        // Handle popup mouse events to prevent closing when interacting with popup
        PopupContent.MouseEnter += (s, e) => _isPopupMouseOver = true;
        PopupContent.MouseLeave += (s, e) => _isPopupMouseOver = false;
        
        // Listen for mouse events to close popup when clicking outside
        this.Loaded += (s, e) => AttachClickOutsideHandler();
    }

    private void AttachClickOutsideHandler()
    {
        // Get the root window
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewMouseDown += Window_PreviewMouseDown;
            window.Deactivated += Window_Deactivated;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Close popup when window loses focus
        if (ViewModel.IsOpen)
        {
            ViewModel.IsOpen = false;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // If popup is open, check if click is outside the control
        if (ViewModel.IsOpen && !_isPopupMouseOver && !PopupContent.IsMouseOver)
        {
            // Check if the click is within this control
            var clickedElement = e.OriginalSource as FrameworkElement;
            if (clickedElement != null && !IsDescendantOf(this, clickedElement))
            {
                // Click is outside, close the popup
                ViewModel.IsOpen = false;
            }
        }
    }

    private static bool IsDescendantOf(FrameworkElement parent, FrameworkElement child)
    {
        var current = child;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = VisualTreeHelper.GetParent(current) as FrameworkElement;
        }
        return false;
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
    }

    private void TbDate_GotFocus(object sender, RoutedEventArgs e)
    {
        // Don't auto-open popup on focus
    }

    private void TbDate_LostFocus(object sender, RoutedEventArgs e)
    {
        // Popup closing is handled by click-outside detection
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
                ViewModel.IsOpen = false;
                break;
            case Key.Escape:
                ViewModel.Reset();
                TbDate.SelectAll();
                ViewModel.IsOpen = false;
                e.Handled = true;
                break;
            case Key.H:
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ViewModel.SelectedDate = SystemTime.Today;
                    TbDate.SelectAll();
                    e.Handled = true;
                }
                break;
            case Key.M:
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ViewModel.SelectedDate = SystemTime.Today.AddDays(1);
                    TbDate.SelectAll();
                    e.Handled = true;
                }
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

    private void TbTime_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var be = ((TextBox)sender).GetBindingExpression(TextBox.TextProperty);
                Debug.Assert(be != null, nameof(be) + " != null");
                be.UpdateSource();
                TbTime.SelectAll();
                ViewModel.IsOpen = false;
                break;
            case Key.Escape:
                ViewModel.Reset();
                TbTime.SelectAll();
                ViewModel.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.ChangeMinute(-1);
                TbTime.SelectAll();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.ChangeMinute(1);
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
    }
}
