# DateControl EditMode Feature

## Overview
The DateControl now supports three EditMode options: DateOnly, TimeOnly, and DateTime. When EditMode is not DateOnly, the control displays a time editor on the right side.

## Visual Layouts

### DateOnly Mode (Width: ~200px)
```
┌─────────────────────────────────┐
│  15.06.2024          │ 📅 │ H │  │
└─────────────────────────────────┘
```

### DateTime Mode (Width: ~400px)
```
┌─────────────────────────────────┬────────────────┐
│  15.06.2024 14:30    │ 📅 │ H │  │ [14] : [30]   │
└─────────────────────────────────┴────────────────┘
         Date Section                  Time Section
```

### TimeOnly Mode (Width: ~200px)
```
┌─────────────────────────────────┐
│  14:30               │ 📅 │ H │  │
└─────────────────────────────────┘
```

## Time Editor Features

### Hour/Minute Selectors
```
┌──────┐   ┌──────┐
│  14  │ : │  30  │
└──────┘   └──────┘
   ▲         ▲
   │         │
  Hour    Minute
 (00-23)  (00-59)
```

### Mouse Interactions
- **Left-click**: Decrease value
- **Right-click**: Increase value
- **Mouse wheel**: Scroll to adjust
- **Hold click**: Continuous change (like month/year navigation)
- **Hover**: Visual feedback (light gray background)
- **Active**: Light blue background when pressing

### Keyboard Support
The text field now accepts datetime formats:
- `dd.MM.yyyy HH:mm` (e.g., "15.06.2024 14:30")
- `dd.MM.yyyy HH:mm:ss`
- `HH:mm` (time only)
- All previous date formats still work

## Implementation Details

### EditMode Enum
```csharp
public enum EditMode
{
    DateOnly,    // Only date selection (default)
    TimeOnly,    // Only time selection
    DateTime     // Date + Time selection
}
```

### New Properties
- `EditMode EditMode { get; set; }` - Controls which editor sections are visible
- `int Hour { get; set; }` - Current hour (0-23)
- `int Minute { get; set; }` - Current minute (0-59)

### New Methods
- `void ChangeHour(int hours)` - Adjust hour by delta
- `void ChangeMinute(int minutes)` - Adjust minute by delta
- `DateTime? GetDateTime(string text)` - Parse datetime strings

### Text Format by Mode
- **DateOnly**: `"dd.MM.yyyy"` (e.g., "15.06.2024")
- **TimeOnly**: `"HH:mm"` (e.g., "14:30")
- **DateTime**: `"dd.MM.yyyy HH:mm"` (e.g., "15.06.2024 14:30")

## Usage Example

```xaml
<!-- Date only (original behavior) -->
<controls:DateControl 
    SelectedDate="{Binding MyDate}"
    EditMode="DateOnly"
    Width="200" />

<!-- Date and Time -->
<controls:DateControl 
    SelectedDate="{Binding MyDateTime}"
    EditMode="DateTime"
    Width="400" />

<!-- Time only -->
<controls:DateControl 
    SelectedDate="{Binding MyTime}"
    EditMode="TimeOnly"
    Width="200" />
```

## Integration with MainWindow

The MainWindow now uses `EditMode="DateTime"` by default, replacing the separate TimeTextBlock display:

**Before:**
```xml
<DateControl SelectedDate="{Binding SelectedDate}" Width="200" />
<TextBox Text="{Binding TimeText}" IsReadOnly="True" Width="200" />
```

**After:**
```xml
<controls:DateControl 
    SelectedDate="{Binding SelectedDate}" 
    EditMode="DateTime"
    Width="400" />
```

The DateControl now handles both date and time in a single, unified control.

## Visual States

### Time Selector States

1. **Normal**
   - White background
   - Blue border
   - Black text

2. **Hover**
   - Light gray background (#E0E0E0)
   - Blue border
   - Black text

3. **Active (Mouse Down)**
   - Light blue background (#CCE4F7)
   - Blue border
   - Black text

### Time Display Format
Hours and minutes are always displayed with leading zeros:
- `14:05` (not `14:5`)
- `09:00` (not `9:0`)

## Synchronization with ViewModel

The DateControl automatically synchronizes the time components with the MainWindowViewModel's `HoursValue` and `MinutesValue` properties through the `SelectedDate` binding. When the user changes the time in the DateControl:

1. Hour/Minute properties update in DateControlViewModel
2. SelectedDate is reconstructed with new time
3. MainWindowViewModel receives updated DateTime through binding
4. The sliders and TimeText update automatically

This provides a seamless user experience with multiple ways to adjust the date and time.
