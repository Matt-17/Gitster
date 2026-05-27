# Gitster – AI Context

## Project Overview
WPF desktop app (.NET 9, Windows) for amending Git commit timestamps. Uses LibGit2Sharp for Git operations and CommunityToolkit.Mvvm 8.2.2 for MVVM.

## Tech Stack
- **UI**: WPF, XAML
- **MVVM**: `CommunityToolkit.Mvvm` — `[ObservableProperty]`, `[RelayCommand]`, `ObservableRecipient`
- **Git**: `LibGit2Sharp`
- **Behaviors**: `Microsoft.Xaml.Behaviors.Wpf`

## Project Structure
```
Gitster/
├── ViewModels/
│   ├── BaseViewModel.cs            # abstract, extends ObservableRecipient
│   ├── MainWindowViewModel.cs      # main logic, properties, commands
│   ├── CommitDetailViewModel.cs    # commit message + date display
│   ├── FilterWindowViewModel.cs
│   ├── StatusBarViewModel.cs
│   ├── CommitFilter.cs
│   └── DateControlViewModel.cs     # calendar state for DateControl
├── Controls/
│   ├── DateControl.xaml/.cs        # custom date/time picker UserControl
│   ├── DatePickerPopup.xaml/.cs    # calendar popup used by DateControl
│   └── SelectableTextBlock.xaml/.cs
├── Models/
│   └── DateTimeHolder.cs           # one day cell in the calendar grid
├── Converters/
│   └── BoolConverter.cs            # bool → Visibility / FontWeight / Opacity
├── Behaviors/
│   └── FocusBehavior.cs            # SelectOnFocus attached behavior for TextBox
├── Helpers/
│   └── SystemTime.cs               # abstraction over DateTime.Today / DateTime.Now
├── MainWindow.xaml/.cs             # minimal code-behind (~25 lines)
├── FilterWindow.xaml/.cs
├── CommitItem.cs                   # ObservableObject: Message, Date, CommitId
├── App.xaml                        # global brushes: HoverBrush, AccentMainBrush, AccentMainLightestBrush
└── Gitster.csproj
```

## MVVM Architecture

`MainWindow.xaml.cs` only creates `MainWindowViewModel` and sets `DataContext`. All logic lives in the ViewModel.

### MainWindowViewModel – Key Properties
| Property | Type | Purpose |
|---|---|---|
| `FolderPath` | `string` | Repository path (bound to TextBox) |
| `SelectedDate` | `DateTime?` | Bound to `DateControl.SelectedDate` |
| `HoursValue`, `MinutesValue`, `DaysValue`, `MonthsValue` | `double` | Slider values |
| `TimeText` | `string` | Formatted time display |
| `IsGoButtonEnabled` | `bool` | Enabled only when repo is valid |
| `SelectedCommit` | `CommitItem?` | Currently selected item in ListView |
| `CurrentCommitDetail` | `CommitDetailViewModel` | HEAD commit |
| `PreviousCommitDetail` | `CommitDetailViewModel` | Selected/parent commit |
| `Commits` | `ObservableCollection<CommitItem>` | All commits |
| `Remotes` | `ObservableCollection<string>` | All configured remotes |
| `SelectedRemote` | `string?` | Active remote for operations |

### MainWindowViewModel – Commands
| Command | Description |
|---|---|
| `AmendCommitCommand` | Amends current commit timestamp |
| `ReadPreviousCommitTimeCommand` | Loads time from selected/parent commit |
| `ReadCurrentCommitTimeCommand` | Loads time from HEAD commit |
| `FetchCommand(string? remote)` | `git fetch <remote>` |
| `PullCommand(string? remote)` | `git pull <remote>` |
| `PushCommand(string? remote)` | `git push <remote>` |
| `SyncCommand(string? remote)` | Fetch → Pull → Push |

Remote commands receive `SelectedRemote` as `CommandParameter`.

## DateControl Component

Custom `UserControl` replacing the standard WPF `DatePicker`. Dependency property: `SelectedDate` (`DateTime?`, two-way bindable).

### Usage
```xaml
xmlns:controls="clr-namespace:Gitster.Controls"

<controls:DateControl SelectedDate="{Binding SelectedDate, Mode=TwoWay}"
                      EditMode="DateOnly" />
```

### EditMode Enum
| Value | Display | Width |
|---|---|---|
| `DateOnly` (default) | `dd.MM.yyyy` | ~200px |
| `TimeOnly` | `HH:mm` | ~200px |
| `DateTime` | `dd.MM.yyyy HH:mm` | ~400px |

When `EditMode` is not `DateOnly`, a time editor (hour/minute selectors) is shown on the right.

### Keyboard Shortcuts (when DateControl is focused)
| Key | Action |
|---|---|
| `H` | Set today (Heute) |
| `M` | Set tomorrow (Morgen) |
| `↑` / `↓` | Next / previous day |
| `Enter` | Confirm |
| `Escape` | Reset |

### Supported Text Input Formats
`dd.MM.yyyy`, `dd-MM-yyyy`, `dd MM yyyy`, `d.M.yyyy`, `dd.MM.yy`, `HH:mm`, `dd.MM.yyyy HH:mm`, and more — see `DateControlViewModel.GetDateTime()`.

### Calendar Popup Interactions
- Left-click month/year header → previous; Right-click → next
- Mouse wheel on month/year header → navigate
- Click day → select; Double-click → select and close

### DateControlViewModel – Key Members
- `SelectedDate`, `Text`, `IsOpen`, `Days` (`DateTimeHolder[]`), `Date` (displayed month)
- `Hour`, `Minute` (time editor)
- Methods: `ChangeMonth(int)`, `Select(DateTimeHolder)`, `ChangeDate(int)`, `ChangeHour(int)`, `ChangeMinute(int)`

## Remote Operations
Toolbar layout (bottom of main window):
```
[Fetch] [Pull] [Push] [Sync] [origin ▼] [Amend]
```
- All four operation buttons pass `SelectedRemote` as `CommandParameter`.
- `Remotes` is populated from `repo.Network.Remotes` in `UpdateElements()`.
- First remote is auto-selected on repository load.
- All operations show a `MessageBox` on success or failure.
- UI refreshes automatically after Fetch, Pull, and Sync.

## Key Patterns

### Observable Property
```csharp
[ObservableProperty] private string _folderPath = string.Empty;
// Generates: public string FolderPath { get; set; } + OnFolderPathChanged partial
```

### Relay Command
```csharp
[RelayCommand] private void AmendCommit() { ... }
// Generates: AmendCommitCommand (IRelayCommand)
```

### Dependency Property (DateControl)
```csharp
public static readonly DependencyProperty SelectedDateProperty =
    DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DateControl), ...);
```

### BoolConverter Usage
```xaml
<converter:BoolConverter x:Key="BoolToVisibility" Mode="Visible" />
<converter:BoolConverter x:Key="BoolToCollapsed" Mode="Collapsed" />
<converter:BoolConverter x:Key="BoolToBold" Mode="FontWeightBold" />
<converter:BoolConverter x:Key="BoolToOpaque" Mode="Opaque" />
```

### Global Brushes (App.xaml)
- `HoverBrush` — `#E0E0E0` (light gray)
- `AccentMainBrush` — `#007ACC` (blue)
- `AccentMainLightestBrush` — `#CCE4F7` (light blue selection)

- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.
