# DateControl Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                         MainWindow.xaml                             │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │              MainWindowViewModel                              │  │
│  │  - SelectedDate: DateTime?  ◄─────────────────┐              │  │
│  │  - HoursValue: double                         │              │  │
│  │  - MinutesValue: double                       │              │  │
│  └───────────────────────────────────────────────┼───────────────┘  │
│                                                   │                  │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  <controls:DateControl                                         │ │
│  │      SelectedDate="{Binding SelectedDate, Mode=TwoWay}" />     │ │
│  └────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┼───────────────────┘
                                                  │
                                                  │ TwoWayBinding
                                                  │
┌─────────────────────────────────────────────────▼───────────────────┐
│                   DateControl.xaml                                  │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  ┌──────────────┐  ┌──────┐  ┌───┐                          │  │
│  │  │  [TextBox]   │  │ [📅] │  │[H]│                          │  │
│  │  │  dd.MM.yyyy  │  │      │  │   │  ◄── TbDate, Button      │  │
│  │  └──────────────┘  └──────┘  └───┘                          │  │
│  │       ▲                │                                      │  │
│  │       │                │ Opens Popup                          │  │
│  │       │                ▼                                      │  │
│  │  ┌────────────────────────────────┐                          │  │
│  │  │  Popup (IsOpen="{Binding}")    │                          │  │
│  │  │  ┌──────────────────────────┐  │                          │  │
│  │  │  │   [Januar]    [2024]     │  │  ◄── Month/Year Nav     │  │
│  │  │  ├──────────────────────────┤  │                          │  │
│  │  │  │ Mo Di Mi Do Fr Sa So     │  │  ◄── Day Headers        │  │
│  │  │  ├──────────────────────────┤  │                          │  │
│  │  │  │  1  2  3  4  5  6  7     │  │                          │  │
│  │  │  │  8  9 10 11 12 13 14     │  │  ◄── Calendar Days      │  │
│  │  │  │ 15 16 17 18 19 20 21     │  │      (DateTimeHolder[]) │  │
│  │  │  │ 22 23 24 25 26 27 28     │  │                          │  │
│  │  │  │ 29 30 31  1  2  3  4     │  │                          │  │
│  │  │  └──────────────────────────┘  │                          │  │
│  │  └────────────────────────────────┘                          │  │
│  │                                                               │  │
│  │  DataContext:                                                 │  │
│  │  ┌───────────────────────────────────────────────────────┐   │  │
│  │  │       DateControlViewModel                            │   │  │
│  │  │  - SelectedDate: DateTime?                            │   │  │
│  │  │  - Text: string                                       │   │  │
│  │  │  - IsOpen: bool                                       │   │  │
│  │  │  - Days: DateTimeHolder[]                             │   │  │
│  │  │  - Date: DateTime (current month/year)                │   │  │
│  │  │  + ChangeMonth(int)                                   │   │  │
│  │  │  + Select(DateTimeHolder)                             │   │  │
│  │  │  + ChangeDate(int)                                    │   │  │
│  │  └───────────────────────────────────────────────────────┘   │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘

Supporting Components:
┌─────────────────────────────────────────────────────────────────────┐
│  Models/DateTimeHolder.cs                                           │
│    - Date: DateTime                                                 │
│    - IsToday: bool                                                  │
│    - IsSelectedMonth: bool                                          │
│    - IsSelected: bool (observable)                                  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Converters/BoolConverter.cs                                        │
│    - Convert bool to: Visibility, FontWeight, Opacity               │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Behaviors/FocusBehavior.cs                                         │
│    - SelectOnFocus: bool                                            │
│    - Auto-selects text when TextBox gets focus                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Helpers/SystemTime.cs                                              │
│    + Today: DateTime                                                │
│    + Now: DateTime                                                  │
└─────────────────────────────────────────────────────────────────────┘

Keyboard Shortcuts:
  H     → Set to today (Heute)
  M     → Set to tomorrow (Morgen)
  ↑     → Next day
  ↓     → Previous day
  Enter → Confirm
  Esc   → Reset

Mouse Interactions:
  Left-click month/year   → Previous month/year
  Right-click month/year  → Next month/year
  Mouse wheel on month    → Navigate months
  Mouse wheel on year     → Navigate years
  Click day               → Select day
  Double-click day        → Select and close popup

Data Flow:
  MainWindow SelectedDate ◄──► DateControl SelectedDate (DependencyProperty)
                               ◄──► DateControlViewModel.SelectedDate
                                    ◄──► DateTimeHolder.IsSelected
```

## Architecture Highlights

1. **Dependency Property**: DateControl.SelectedDate is a DependencyProperty that supports two-way binding with the parent ViewModel.

2. **Internal ViewModel**: DateControl has its own DateControlViewModel that manages the calendar state independently.

3. **Synchronization**: The SelectedDateCallback in DateControl.xaml.cs ensures that changes flow from the parent binding to the internal ViewModel.

4. **Event Propagation**: DateControlViewModel.SelectedDateChanged event propagates changes back to the DependencyProperty.

5. **Observable Collection**: Days array contains 42 DateTimeHolder objects (6 weeks × 7 days) that update when the month changes.

6. **MVVM Compliance**: The control follows MVVM pattern with clear separation between View (XAML), ViewModel, and Model (DateTimeHolder).
