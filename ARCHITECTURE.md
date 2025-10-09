# Architecture Comparison

## Before MVVM (Code-Behind Pattern)

```
┌─────────────────────────────────────────┐
│         MainWindow.xaml                  │
│  ┌────────────────────────────────────┐ │
│  │ UI Elements (ListView, TextBoxes,  │ │
│  │ DatePickers, Sliders, Buttons)     │ │
│  │                                    │ │
│  │ Event Handlers:                    │ │
│  │ - Button_Click                     │ │
│  │ - ButtonRead_Click                 │ │
│  │ - ButtonTime_Click                 │ │
│  │ - FolderTb_TextChanged             │ │
│  │ - TimeSliderValueChanged           │ │
│  │ - DatePicker_SelectedDateChanged   │ │
│  │ - DateSlider_ValueChanged          │ │
│  │ - Window_Activated                 │ │
│  └────────────────────────────────────┘ │
└─────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│      MainWindow.xaml.cs (215 lines)     │
│  ┌────────────────────────────────────┐ │
│  │ - Private fields (_path, _commits) │ │
│  │ - 8+ Event handler methods         │ │
│  │ - UpdateElements()                 │ │
│  │ - UpdateFolderTextBox()            │ │
│  │ - UpdateSettingsPath()             │ │
│  │ - Direct UI manipulation           │ │
│  │ - LibGit2Sharp operations          │ │
│  │ - All business logic here          │ │
│  └────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

## After MVVM (Current Architecture)

```
┌───────────────────────────────────────────────────────────┐
│                   MainWindow.xaml                          │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ UI Elements with Data Bindings:                      │ │
│  │ - ListView: ItemsSource={Binding Commits}            │ │
│  │            SelectedItem={Binding SelectedCommit}     │ │
│  │ - TextBoxes: Text={Binding FolderPath}               │ │
│  │ - DatePicker: SelectedDate={Binding SelectedDate}    │ │
│  │ - Sliders: Value={Binding DaysValue/HoursValue/...}  │ │
│  │ - Buttons: Command={Binding AmendCommitCommand}      │ │
│  │ - GroupBoxes: Display commit details via bindings    │ │
│  │                                                       │ │
│  │ NO Event Handlers (except Window_Activated)          │ │
│  └──────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
                           │
                           │ DataContext
                           ▼
┌───────────────────────────────────────────────────────────┐
│          MainWindow.xaml.cs (25 lines)                     │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ - Creates MainWindowViewModel                        │ │
│  │ - Sets DataContext                                   │ │
│  │ - Window_Activated calls ViewModel method            │ │
│  │ - NO business logic                                  │ │
│  └──────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
                           │
                           ▼
┌───────────────────────────────────────────────────────────┐
│            ViewModels (347 lines total)                    │
│                                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ BaseViewModel (11 lines)                           │  │
│  │ - Inherits from ObservableRecipient                │  │
│  │ - Base for all ViewModels                          │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ CommitDetailViewModel (33 lines)                   │  │
│  │ - [ObservableProperty] CommitMessage               │  │
│  │ - [ObservableProperty] CommitDate                  │  │
│  │ - UpdateCommit() method                            │  │
│  │ - Clear() method                                   │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ MainWindowViewModel (303 lines)                    │  │
│  │ - Observable properties for all UI state           │  │
│  │ - Commands:                                        │  │
│  │   * [RelayCommand] AmendCommit                     │  │
│  │   * [RelayCommand] ReadPreviousCommitTime          │  │
│  │   * [RelayCommand] ReadCurrentCommitTime           │  │
│  │ - ObservableCollection<CommitItem> Commits         │  │
│  │ - CommitDetailViewModel instances                  │  │
│  │ - All business logic (LibGit2Sharp operations)     │  │
│  │ - Property change handlers (partial methods)       │  │
│  │ - UpdateElements() method                          │  │
│  └────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────┘
                           │
                           ▼
┌───────────────────────────────────────────────────────────┐
│              CommitItem (22 lines)                         │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ Inherits from ObservableObject                       │ │
│  │ - [ObservableProperty] Message                       │ │
│  │ - [ObservableProperty] Date                          │ │
│  │ - [ObservableProperty] CommitId                      │ │
│  └──────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
```

## Key Improvements

### 1. Separation of Concerns
- **View (XAML)**: Only UI structure and bindings
- **ViewModel**: All business logic and state management
- **Code-Behind**: Minimal, only creates ViewModel

### 2. Code Reduction
- MainWindow.xaml.cs: **215 → 25 lines** (89% reduction)
- All logic moved to organized, testable ViewModels

### 3. Enhanced Features
- **ListView Selection**: Automatically updates "Previous Commit" display
- **GroupBox Organization**: Clear labeling of commit sections
- **Two-Way Bindings**: Automatic UI synchronization
- **Commands**: No event handler coupling

### 4. CommunityToolkit.Mvvm Benefits
- **[ObservableProperty]**: Automatic INPC implementation
- **[RelayCommand]**: Automatic ICommand implementation
- **Source Generators**: No boilerplate code
- **ObservableRecipient**: Messaging support for future expansion

### 5. Testability
ViewModels can now be unit tested independently:
```csharp
var vm = new MainWindowViewModel();
vm.FolderPath = "/test/repo";
Assert.IsNotEmpty(vm.Commits);
```
