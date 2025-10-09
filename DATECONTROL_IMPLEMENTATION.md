# DateControl Implementation

## Overview
This document describes the implementation of the new DateControl component for the Gitster application, replacing the standard WPF DatePicker with a custom calendar control.

## Changes Made

### 1. New Directory Structure
Created the following new directories to organize the codebase:
- `Gitster/Controls/` - User controls
- `Gitster/Models/` - Data models
- `Gitster/ViewModels/` - Additional ViewModels (for controls)
- `Gitster/Converters/` - Value converters
- `Gitster/Behaviors/` - XAML behaviors
- `Gitster/Helpers/` - Helper classes

### 2. Core Components

#### DateControl (`Controls/DateControl.xaml` and `Controls/DateControl.xaml.cs`)
A custom UserControl that provides:
- **Text input field** with keyboard shortcuts
- **Calendar icon button** to open the popup calendar
- **"H" (Heute) button** for quick "today" selection
- **Interactive calendar popup** with month/year navigation
- **Dependency property** `SelectedDate` for two-way binding

**Features:**
- Keyboard shortcuts:
  - `H` - Set to today (Heute)
  - `M` - Set to tomorrow (Morgen)
  - `Up/Down` - Navigate days
  - `Enter` - Confirm date
  - `Escape` - Reset to previous value
- Mouse interactions:
  - Left-click month/year headers to navigate backward
  - Right-click month/year headers to navigate forward
  - Mouse wheel on month header to navigate months
  - Mouse wheel on year header to navigate years
  - Click days to select
  - Double-click to select and close popup
- Auto-select all text on focus
- Multiple date input formats supported (see DateControlViewModel)

#### DateControlViewModel (`ViewModels/DateControlViewModel.cs`)
ViewModel for the DateControl with:
- `SelectedDate` property with change notification
- `Days` array of DateTimeHolder for calendar rendering
- `IsOpen` property to control popup visibility
- `Text` property for text input with multiple format parsing
- Methods for date navigation and manipulation

**Supported date input formats:**
- `dd.MM.yyyy`, `dd.MM.yy`
- `dd-MM-yyyy`, `dd-MM-yy`
- `dd MM yyyy`, `dd MM yy`
- `d.M.yyyy`, `d.M.yy`
- And many more variations (see code for full list)

### 3. Supporting Classes

#### DateTimeHolder (`Models/DateTimeHolder.cs`)
Model representing a single day in the calendar:
- `Date` - The date value
- `IsToday` - Whether this is today's date
- `IsSelectedMonth` - Whether this day is in the selected month
- `IsSelected` - Whether this day is currently selected

#### SystemTime (`Helpers/SystemTime.cs`)
Abstraction for getting current date/time:
- `Today` - Gets today's date
- `Now` - Gets current date and time

This abstraction makes the code more testable.

#### BoolConverter (`Converters/BoolConverter.cs`)
Multi-purpose boolean converter supporting:
- `Visible` - Boolean to Visibility (true = Visible)
- `Collapsed` - Boolean to Visibility (true = Collapsed)
- `FontWeightBold` - Boolean to FontWeight (true = Bold)
- `Opaque` - Boolean to opacity (true = 1.0, false = 0.5)

#### FocusBehavior (`Behaviors/FocusBehavior.cs`)
XAML behavior for TextBox:
- Automatically selects all text when focused
- Supports `SelectOnFocus` property

### 4. Resource Updates

#### App.xaml
Added application-wide brush resources:
- `HoverBrush` (#E0E0E0) - Light gray for hover effects
- `AccentMainBrush` (#007ACC) - Blue accent color
- `AccentMainLightestBrush` (#CCE4F7) - Light blue for selections

#### MainWindow.xaml
- Added `xmlns:controls="clr-namespace:Gitster.Controls"` namespace
- Replaced `<DatePicker>` with `<controls:DateControl>`
- Maintained the same binding: `SelectedDate="{Binding SelectedDate, Mode=TwoWay}"`

### 5. Package Dependencies

#### Added Microsoft.Xaml.Behaviors.Wpf (v1.1.122)
Required for XAML behaviors support (used by FocusBehavior).

## Architecture Decisions

1. **MVVM Pattern**: The DateControl follows MVVM with its own ViewModel, maintaining separation of concerns.

2. **Observable Pattern**: Uses CommunityToolkit.Mvvm for property change notification, consistent with the rest of the application.

3. **Reusability**: The DateControl is a standalone UserControl that can be used anywhere in the application with simple binding.

4. **Two-Way Binding**: The control properly supports two-way binding through a DependencyProperty, integrating seamlessly with WPF's binding system.

5. **Localization Ready**: German text is used in tooltips and shortcuts (Heute/Morgen), maintaining consistency with the original specification.

## Integration with MainWindowViewModel

The DateControl integrates seamlessly with the existing MainWindowViewModel:
- Binds to the existing `SelectedDate` property
- No changes to ViewModel code required
- Maintains backward compatibility with existing functionality

## User Experience Improvements

Compared to the standard DatePicker, the DateControl offers:
1. **More intuitive calendar navigation** with mouse gestures
2. **Keyboard shortcuts** for power users
3. **Flexible date input** with multiple format support
4. **Visual feedback** with hover states and selected date highlighting
5. **Quick access** to today's date with "H" button
6. **Customizable appearance** through WPF resources

## Testing Considerations

**Note**: This is a WPF application that requires Windows to build and run. Testing should be performed on a Windows machine with Visual Studio or .NET SDK.

### Manual Testing Checklist
- [ ] Calendar popup opens when clicking calendar icon
- [ ] Date can be typed in various formats
- [ ] "H" button sets today's date
- [ ] Keyboard shortcuts work (H, M, Up, Down, Enter, Escape)
- [ ] Month/year navigation with mouse clicks and wheel
- [ ] Day selection with single and double-click
- [ ] Selected date syncs with MainWindow's SelectedDate property
- [ ] Date changes reflect in commit amendment operations

## Future Enhancements

The issue mentioned support for `EditFormat` (DateOnly, TimeOnly, DateTime), but the current implementation focuses on DateOnly. Future work could include:
1. Adding time selection capability
2. Implementing EditFormat property with options:
   - `DateOnly` (current implementation)
   - `TimeOnly` (time picker only)
   - `DateTime` (date + time picker)
3. More localization options
4. Custom styling through template properties

## Files Created/Modified

### Created Files
1. `Gitster/Behaviors/FocusBehavior.cs` - TextBox focus behavior
2. `Gitster/Controls/DateControl.xaml` - DateControl XAML
3. `Gitster/Controls/DateControl.xaml.cs` - DateControl code-behind
4. `Gitster/Converters/BoolConverter.cs` - Boolean converter
5. `Gitster/Helpers/SystemTime.cs` - System time abstraction
6. `Gitster/Models/DateTimeHolder.cs` - Calendar day model
7. `Gitster/ViewModels/DateControlViewModel.cs` - DateControl ViewModel

### Modified Files
1. `Gitster/App.xaml` - Added brush resources
2. `Gitster/Gitster.csproj` - Added Microsoft.Xaml.Behaviors.Wpf package
3. `Gitster/MainWindow.xaml` - Replaced DatePicker with DateControl

## Summary

The DateControl implementation provides a feature-rich, custom calendar control that enhances the user experience while maintaining the application's MVVM architecture and integrating seamlessly with existing code. The control is self-contained, reusable, and follows WPF best practices.
