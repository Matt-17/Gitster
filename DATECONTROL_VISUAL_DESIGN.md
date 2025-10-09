# DateControl Visual Design

## Layout

```
┌────────────────────────────────────────┐
│  MainWindow                            │
│                                        │
│  ┌──────────────────────────────────┐ │
│  │ Repository Path: [TextBox       ]│ │
│  └──────────────────────────────────┘ │
│                                        │
│  ┌──────────────────────────────────┐ │
│  │ Selected Commit: [Info...    🕑]│ │
│  └──────────────────────────────────┘ │
│                                        │
│  ┌──────────────────────────────────┐ │
│  │ Current Commit:  [Info...    🕑]│ │
│  └──────────────────────────────────┘ │
│                                        │
│  [Sliders: Days, Months, Hours, Mins] │
│                                        │
│  ┌─────────────────────────────────┐  │
│  │ NEW DateControl:                │  │
│  │ ┌─────────────────┬───┬───┐     │  │
│  │ │  01.01.2024     │📅│ H │     │  │ ◄── Replaces DatePicker
│  │ └─────────────────┴───┴───┘     │  │
│  └─────────────────────────────────┘  │
│                                        │
│  [TimeTextBlock]  [Amend Button]      │
│                                        │
└────────────────────────────────────────┘
```

## DateControl Expanded View

### Closed State (Normal)
```
┌─────────────────────────────────┐
│  15.06.2024          │ 📅 │ H │  │
└─────────────────────────────────┘
     ▲                   ▲    ▲
     │                   │    │
  Text Input       Calendar  Today
  (editable)        Button  Button
```

### Open State (Popup Visible)
```
┌─────────────────────────────────┐
│  15.06.2024          │ 📅 │ H │  │
└─────────────────────────────────┘
       │
       └─▶ ┌────────────────────────────┐
           │   Juni          2024       │ ◄── Interactive headers
           ├────────────────────────────┤
           │ Mo Di Mi Do Fr Sa So       │ ◄── Day headers
           ├────────────────────────────┤
           │                 1  2       │
           │  3  4  5  6  7  8  9       │
           │ 10 11 12 13 14 [15] 16     │ ◄── Selected date
           │ 17 18 19 20 21 22 23       │
           │ 24 25 26 27 28 29 30       │
           │  1  2  3  4  5  6  7       │ ◄── Next month (grayed)
           └────────────────────────────┘
```

## Component Breakdown

### 1. Text Input Field
```
┌───────────────────────┐
│  15.06.2024           │  ← Type date directly
└───────────────────────┘
   ▲
   │ Supports formats:
   │ • dd.MM.yyyy
   │ • dd-MM-yyyy
   │ • dd MM yyyy
   │ • Many more...
```

### 2. Calendar Button
```
┌───┐
│📅│  ← Click to open calendar popup
└───┘
```

### 3. Today Button
```
┌───┐
│ H │  ← Click to set today's date ("Heute")
└───┘
   Tooltip: "Heute"
```

## Calendar Popup Details

### Month/Year Header (Interactive)
```
┌────────────────────────────┐
│   Juni          2024       │
│    ▲             ▲         │
│    │             │         │
│  Left/Right   Left/Right   │
│  click to     click to     │
│  change       change year  │
│  month                     │
│                            │
│  Mouse wheel works too!    │
└────────────────────────────┘
```

### Day Grid
```
┌────────────────────────────┐
│ Mo Di Mi Do Fr Sa So       │ ◄── Headers
├────────────────────────────┤
│ 27 28 29 30 31  1  2       │ ◄── Previous month (dim)
│  3  4  5  6  7  8  9       │
│ 10 11 12 13 14 [15] 16     │ ◄── Current month
│ 17 18 19 20 21 22 23       │    [15] = selected
│ 24 25 26 27 28 29 30       │    Bold = today
│  1  2  3  4  5  6  7       │ ◄── Next month (dim)
└────────────────────────────┘
    ▲                         
    │                         
  Click to select             
  Double-click to select & close
```

## Visual States

### Day Cell States
1. **Normal Day** (current month)
   - Black text, normal weight
   - White background
   - Opacity: 100%

2. **Today**
   - Black text, **bold** weight
   - White background
   - Opacity: 100%

3. **Selected Day**
   - **White text**
   - **Blue background** (#007ACC)
   - Bold if today
   - Opacity: 100%

4. **Other Month Day**
   - Gray text, normal weight
   - White background
   - **Opacity: 50%** (dim)

5. **Hover State**
   - Light gray background (#E0E0E0)
   - Changes on mouse over

### Month/Year Header States
1. **Normal**
   - Transparent background
   - Black text

2. **Hover**
   - Light gray background (#E0E0E0)
   - Black text

3. **Active (Mouse Down)**
   - Light blue background (#CCE4F7)
   - Black text

## Color Scheme

```
Primary Colors:
├─ AccentMainBrush:          #007ACC (Blue)
├─ AccentMainLightestBrush:  #CCE4F7 (Light Blue)
├─ HoverBrush:               #E0E0E0 (Light Gray)
├─ White:                    #FFFFFF
└─ Black:                    #000000
```

## Interactions Summary

### Mouse Interactions
| Action                    | Result                      |
|---------------------------|-----------------------------|
| Click calendar button     | Open/close popup            |
| Click "H" button          | Set to today                |
| Left-click month header   | Previous month              |
| Right-click month header  | Next month                  |
| Left-click year header    | Previous year               |
| Right-click year header   | Next year                   |
| Mouse wheel on month      | Navigate months             |
| Mouse wheel on year       | Navigate years              |
| Click day                 | Select day                  |
| Double-click day          | Select day & close popup    |
| Click outside popup       | Close popup                 |

### Keyboard Shortcuts
| Key       | Result                      |
|-----------|-----------------------------|
| H         | Set to today (Heute)        |
| M         | Set to tomorrow (Morgen)    |
| ↑         | Next day                    |
| ↓         | Previous day                |
| Enter     | Confirm date entry          |
| Escape    | Reset to previous value     |

## Dimensions

```
DateControl:
├─ Width:  200px
├─ Height: 30px
└─ MinWidth: 90px

Calendar Popup:
├─ Width:  ~210px (auto)
├─ Height: ~240px (auto)
├─ Border: 2px solid #007ACC
└─ Background: White

Day Cell:
├─ Width:  30px
└─ Height: 28px
```

## Integration Example

```xaml
<!-- Before (Standard DatePicker) -->
<DatePicker SelectedDate="{Binding SelectedDate}" />

<!-- After (Custom DateControl) -->
<controls:DateControl SelectedDate="{Binding SelectedDate}" />
```

Both use the same binding, making the replacement seamless!

## Benefits Over Standard DatePicker

1. ✅ **Better Visual Design** - Custom styling matching app theme
2. ✅ **Keyboard Shortcuts** - H, M, arrows for power users
3. ✅ **Flexible Input** - Multiple date format support
4. ✅ **Interactive Navigation** - Mouse wheel, left/right click
5. ✅ **Quick Access** - "H" button for today
6. ✅ **Better Feedback** - Hover states, selection highlighting
7. ✅ **German Localization** - Day names and tooltips in German
8. ✅ **Consistent Styling** - Uses app's color scheme

## Future Enhancement Possibilities

### EditFormat Support (from original issue)
```
<controls:DateControl 
    SelectedDate="{Binding SelectedDate}"
    EditFormat="DateTime" />  ← Could add time picker
    
Options:
- DateOnly (current implementation)
- TimeOnly (future: show time picker only)
- DateTime (future: show date + time picker)
```

### Time Picker Extension
```
┌─────────────────────────────────┐
│  15.06.2024  14:30   │ 📅 │ H │  │
└─────────────────────────────────┘
       │
       └─▶ ┌────────────────────────────┐
           │   Juni          2024       │
           ├────────────────────────────┤
           │ [Calendar Grid...]         │
           ├────────────────────────────┤
           │ Time: ┌───┐ : ┌───┐        │ ◄── Time picker
           │       │14 │   │30 │        │
           │       └───┘   └───┘        │
           │       [OK]  [Cancel]       │
           └────────────────────────────┘
```

This visual design document shows what the DateControl looks like and how it behaves!
