# DateControl - Quick Start Guide

## What is DateControl?

DateControl is a custom WPF UserControl that replaces the standard DatePicker with a feature-rich calendar control. It provides an intuitive interface for date selection with keyboard shortcuts, mouse interactions, and flexible date input formats.

## Usage

### Basic Usage in XAML

```xaml
<!-- Add namespace -->
xmlns:controls="clr-namespace:Gitster.Controls"

<!-- Use the control -->
<controls:DateControl SelectedDate="{Binding SelectedDate, Mode=TwoWay}" />
```

That's it! The DateControl works just like a standard DatePicker but with enhanced features.

## Features at a Glance

### 🖱️ Mouse Interactions
- **Click** calendar icon to open popup
- **Click** "H" button for today's date
- **Left/Right-click** month/year headers to navigate
- **Mouse wheel** on headers for quick navigation
- **Click** day to select
- **Double-click** day to select and close

### ⌨️ Keyboard Shortcuts
- **H** - Set to today (Heute)
- **M** - Set to tomorrow (Morgen)
- **↑** - Next day
- **↓** - Previous day
- **Enter** - Confirm entry
- **Escape** - Reset to previous value

### 📝 Text Input
Supports multiple date formats:
- `15.06.2024` (German format)
- `15-06-2024`
- `15 06 2024`
- `06/15/2024` (US format)
- And many more variations

### 🎨 Visual Features
- Highlights today's date (bold)
- Shows selected date (blue background)
- Dims days from other months (50% opacity)
- Hover effects on interactive elements
- German day abbreviations (Mo, Di, Mi, ...)

## Component Files

```
Gitster/
├── Controls/
│   ├── DateControl.xaml          # UI layout
│   └── DateControl.xaml.cs       # Control logic
├── ViewModels/
│   └── DateControlViewModel.cs   # Calendar state management
├── Models/
│   └── DateTimeHolder.cs         # Day cell data model
├── Converters/
│   └── BoolConverter.cs          # Boolean conversions
├── Behaviors/
│   └── FocusBehavior.cs          # Auto-select text behavior
└── Helpers/
    └── SystemTime.cs             # Date/time abstraction
```

## Properties

### SelectedDate
```csharp
DateTime? SelectedDate { get; set; }
```
- Gets or sets the currently selected date
- Supports two-way binding
- Can be null (no date selected)

## Integration Example

### Before (Standard DatePicker)
```xaml
<DatePicker SelectedDate="{Binding SelectedDate, Mode=TwoWay}" 
            Margin="5" Height="30" Width="200" />
```

### After (DateControl)
```xaml
<controls:DateControl SelectedDate="{Binding SelectedDate, Mode=TwoWay}" 
                      Margin="5" Height="30" Width="200" />
```

The binding stays exactly the same - just replace the control!

## Customization

### Available Resources (App.xaml)
```xaml
<SolidColorBrush x:Key="HoverBrush" Color="#E0E0E0" />
<SolidColorBrush x:Key="AccentMainBrush" Color="#007ACC" />
<SolidColorBrush x:Key="AccentMainLightestBrush" Color="#CCE4F7" />
```

Modify these in your App.xaml to customize colors:
- **HoverBrush** - Background color when hovering over elements
- **AccentMainBrush** - Main accent color (header background, selected day)
- **AccentMainLightestBrush** - Light accent color (pressed state)

## Architecture

```
MainWindow (ViewModel)
    ↓ Binding
DateControl (DependencyProperty)
    ↓ Internal
DateControlViewModel
    ↓ Manages
DateTimeHolder[] (42 days = 6 weeks)
```

## Dependencies

- **CommunityToolkit.Mvvm** (8.4.0) - For MVVM patterns
- **Microsoft.Xaml.Behaviors.Wpf** (1.1.122) - For XAML behaviors
- **.NET 9.0** with WPF support

## Documentation

For more details, see:
- **DATECONTROL_SUMMARY.md** - Complete feature list
- **DATECONTROL_IMPLEMENTATION.md** - Technical details
- **DATECONTROL_ARCHITECTURE.md** - Component diagrams
- **DATECONTROL_VISUAL_DESIGN.md** - UI mockups

## Quick Reference Card

| Action | Result |
|--------|--------|
| Type date | Enter in various formats |
| Click 📅 | Open/close calendar |
| Click H | Set to today |
| H key | Set to today (Heute) |
| M key | Set to tomorrow (Morgen) |
| ↑/↓ keys | Navigate days |
| Enter | Confirm |
| Escape | Reset |
| Left-click header | Previous month/year |
| Right-click header | Next month/year |
| Mouse wheel | Navigate months/years |
| Click day | Select |
| Double-click day | Select & close |

## Troubleshooting

### DateControl doesn't show up
- Check that you've added the namespace: `xmlns:controls="clr-namespace:Gitster.Controls"`
- Verify that DateControl.xaml is set as "Page" build action
- Make sure Microsoft.Xaml.Behaviors.Wpf package is installed

### Date binding doesn't work
- Ensure binding mode is `TwoWay`: `SelectedDate="{Binding ..., Mode=TwoWay}"`
- Check that your ViewModel property is `DateTime?` (nullable)
- Verify property change notification is working

### Calendar popup doesn't appear
- Check that resources are defined in App.xaml (HoverBrush, AccentMainBrush, etc.)
- Verify no z-index issues with overlapping controls
- Ensure popup placement target is accessible

## Future Enhancements

The control is designed to support future extensions:
- **EditFormat property** - DateOnly, TimeOnly, DateTime modes
- **Time picker** - Add time selection to date selection
- **Custom templates** - Allow fully custom day cell rendering
- **Date ranges** - Min/Max date restrictions
- **Disabled dates** - Mark certain dates as non-selectable

## Contributing

When modifying DateControl:
1. Keep MVVM pattern - logic in ViewModel, UI in XAML
2. Update DateControlViewModel for state management
3. Use dependency properties for bindable properties
4. Add keyboard shortcuts thoughtfully
5. Test on actual Windows environment
6. Update documentation

## License

Part of the Gitster project. See LICENSE.txt for details.

---

**Quick Start**: Just use `<controls:DateControl SelectedDate="{Binding ...}" />` and you're ready to go! 🚀
