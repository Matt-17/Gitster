# Quick Reference Guide - MVVM Implementation

## Project Structure

```
Gitster/
├── ViewModels/                          # MVVM ViewModels
│   ├── BaseViewModel.cs                 # Base class for all ViewModels
│   ├── CommitDetailViewModel.cs         # Commit detail display logic
│   └── MainWindowViewModel.cs           # Main window logic and commands
├── MainWindow.xaml                       # View with data bindings
├── MainWindow.xaml.cs                    # Minimal code-behind (25 lines)
├── CommitItem.cs                         # Observable model class
├── App.xaml / App.xaml.cs               # Application entry point
└── Gitster.csproj                        # Project file with CommunityToolkit.Mvvm
```

## How It Works

### 1. BaseViewModel (ViewModels/BaseViewModel.cs)
```csharp
public abstract class BaseViewModel : ObservableRecipient
{
    // Provides INPC and messaging support to all ViewModels
}
```

### 2. CommitDetailViewModel (ViewModels/CommitDetailViewModel.cs)
Manages display of a single commit's details:
```csharp
- CommitMessage (observable property)
- CommitDate (observable property)
- UpdateCommit(message, date) method
- Clear() method
```

### 3. MainWindowViewModel (ViewModels/MainWindowViewModel.cs)
Main application logic with:

**Observable Properties:**
- `FolderPath` - Repository path (bound to TextBox)
- `SelectedDate` - Date picker value
- `HoursValue`, `MinutesValue`, `DaysValue`, `MonthsValue` - Slider values
- `TimeText` - Formatted time display
- `IsGoButtonEnabled` - Button enabled state
- `SelectedCommit` - Currently selected commit in ListView
- `CurrentCommitDetail` - HEAD commit details
- `PreviousCommitDetail` - Selected or parent commit details
- `Commits` - ObservableCollection of all commits

**Commands:**
- `AmendCommitCommand` - Amends current commit with new date/time
- `ReadPreviousCommitTimeCommand` - Loads time from previous commit
- `ReadCurrentCommitTimeCommand` - Loads time from current commit

**Methods:**
- `OnWindowActivated()` - Called when window activates
- `UpdateElements()` - Refreshes all data from repository

### 4. CommitItem (CommitItem.cs)
Observable model for commit list items:
```csharp
public partial class CommitItem : ObservableObject
{
    [ObservableProperty] private string _message;
    [ObservableProperty] private string _date;
    [ObservableProperty] private string _commitId;
}
```

### 5. MainWindow.xaml
View with data bindings - Key bindings:

```xml
<!-- ListView with selection -->
<ListView ItemsSource="{Binding Commits}" 
          SelectedItem="{Binding SelectedCommit, Mode=TwoWay}" />

<!-- Folder path -->
<TextBox Text="{Binding FolderPath, UpdateSourceTrigger=PropertyChanged}" />

<!-- Commit details in GroupBoxes -->
<GroupBox Header="Current Commit">
    <TextBox Text="{Binding CurrentCommitDetail.CommitMessage}" IsReadOnly="True" />
    <TextBox Text="{Binding CurrentCommitDetail.CommitDate}" IsReadOnly="True" />
</GroupBox>

<GroupBox Header="Previous Commit / Selected Commit">
    <TextBox Text="{Binding PreviousCommitDetail.CommitMessage}" IsReadOnly="True" />
    <TextBox Text="{Binding PreviousCommitDetail.CommitDate}" IsReadOnly="True" />
</GroupBox>

<!-- Date and time controls -->
<DatePicker SelectedDate="{Binding SelectedDate, Mode=TwoWay}" />
<Slider Value="{Binding HoursValue}" />
<Slider Value="{Binding MinutesValue}" />
<Slider Value="{Binding DaysValue}" />
<Slider Value="{Binding MonthsValue}" />

<!-- Commands -->
<Button Command="{Binding AmendCommitCommand}" Content="Amend" 
        IsEnabled="{Binding IsGoButtonEnabled}" />
<Button Command="{Binding ReadPreviousCommitTimeCommand}" Content="🕑" />
<Button Command="{Binding ReadCurrentCommitTimeCommand}" Content="🕑" />
```

### 6. MainWindow.xaml.cs
Minimal code-behind:
```csharp
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;  // Connect View to ViewModel
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        _viewModel.OnWindowActivated();
    }
}
```

## Data Flow

```
User clicks commit in ListView
    ↓
ListView.SelectedItem binding updates ViewModel.SelectedCommit
    ↓
MainWindowViewModel.OnSelectedCommitChanged() partial method fires
    ↓
PreviousCommitDetail.UpdateCommit() is called
    ↓
UI automatically updates via bindings (PreviousCommitDetail.CommitMessage/CommitDate)
```

## CommunityToolkit.Mvvm Features Used

### [ObservableProperty]
Generates property with INPC automatically:
```csharp
[ObservableProperty]
private string _folderPath;
// Generates: public string FolderPath { get; set; } with INPC
```

### [RelayCommand]
Generates ICommand implementation:
```csharp
[RelayCommand]
private void AmendCommit() { ... }
// Generates: public ICommand AmendCommitCommand { get; }
```

### Partial Methods (Property Change Handlers)
```csharp
partial void OnFolderPathChanged(string value)
{
    // Called automatically when FolderPath changes
}
```

## Benefits Over Old Code-Behind Approach

1. **Testability**: Can test ViewModels without UI
2. **Separation**: UI and logic are completely separated
3. **Maintainability**: Easier to find and modify logic
4. **Reusability**: ViewModels can work with different Views
5. **Less Code**: Source generators eliminate boilerplate
6. **Type Safety**: Compile-time binding errors with design-time DataContext

## Common Tasks

### Adding a New Property
1. Add `[ObservableProperty]` field to ViewModel
2. Add binding to XAML
3. No manual INPC needed!

### Adding a New Command
1. Add `[RelayCommand]` method to ViewModel
2. Bind button `Command` property to generated command
3. No manual ICommand implementation needed!

### Responding to Property Changes
```csharp
partial void OnMyPropertyChanged(string value)
{
    // This is automatically called when MyProperty changes
}
```
