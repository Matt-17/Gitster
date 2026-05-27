# Gitster – Umsetzungsplan v2.1

Detaillierter Implementierungsplan für die nächste Etappe. Alle offenen Entscheidungen aus v2 sind eingearbeitet. Code-Snippets sind so vollständig wie möglich, damit GHCP Sonnet direkt umsetzen kann.

UI-Sprache: **Englisch**. Code-Kommentare können englisch bleiben (Konsistenz mit existierendem Code-Stil im Projekt).

---

## Übersicht der Schritte

| Schritt | Inhalt                                              | Aufwand    |
|---------|-----------------------------------------------------|------------|
| A       | Status-Bar mit Live-Watch + Auto-Fetch              | 1.5 Tage   |
| B       | Aufräumen + Design-Politur                          | 1 Tag      |
| C       | Menü-Leiste + Recent Repos + Settings-Hülle         | 1 Tag      |
| D       | Operations-Log + `IGitBackend` + Capability-System  | 2.5 Tage   |

**Gesamt: ~6 Arbeitstage.**

---

## Schritt A – Status-Bar mit Live-Watch + Auto-Fetch (1.5 Tage)

### A.1 – `IGitBackend`-Abstraktion (Vorbereitung, ½ Tag)

Bevor Services dazukommen: das Interface anlegen. Nur eine Implementierung dahinter, aber alle neuen Services arbeiten gegen das Interface.

**Anlegen: `Services/Git/IGitBackend.cs`**

```csharp
namespace Gitster.Services.Git;

public interface IGitBackend
{
    // Basics
    string? RepositoryPath { get; }
    Task OpenAsync(string path);
    
    // Status
    Task<WorkingTreeState> GetWorkingTreeStateAsync();
    Task<BranchInfo> GetCurrentBranchAsync();
    
    // History
    Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(CommitFilter? filter = null);
    Task<CommitDetails> GetCommitAsync(string sha);
    
    // Operations
    Task<string> AmendAsync(AmendRequest request);
    Task FetchAsync(string remoteName = "origin");
    Task PullAsync(string remoteName = "origin");
    Task PushAsync(string remoteName = "origin", bool forceWithLease = false);
    
    // Reflog-based undo
    Task<string> GetReflogSelectorForHeadAsync();
    Task ResetHardAsync(string targetReference);
    
    // Capabilities (the backend declares what it can do)
    GitCapabilities Capabilities { get; }
}
```

**Anlegen: `Services/Git/GitCapabilities.cs`**

```csharp
namespace Gitster.Services.Git;

[Flags]
public enum GitCapabilities
{
    None              = 0,
    // Phase 1
    Read              = 1 << 0,    // log, status, branches
    BasicWrite        = 1 << 1,    // commit, amend, fetch, pull, push
    ReflogUndo        = 1 << 2,
    // Phase 2+
    InteractiveRebase = 1 << 3,
    FixupAutosquash   = 1 << 4,
    PickaxeSearch     = 1 << 5,
    RangeDiff         = 1 << 6,
    Worktrees         = 1 << 7,
    CommitSigning     = 1 << 8,
}
```

**Anlegen: `Services/Git/LibGit2Backend.cs`**

Stub mit `Capabilities = Read | BasicWrite | ReflogUndo`. Implementierung der einzelnen Methoden zieht die bestehende `LibGit2Sharp`-Logik aus `MainWindowViewModel` und sonstigen Stellen rein. **Das ist Refactoring, kein Neuschreiben** — die Logik existiert, sie zieht nur um.

DI-Registrierung in `App.xaml.cs` oder bestehender Bootstrap-Stelle:

```csharp
services.AddSingleton<IGitBackend, LibGit2Backend>();
```

Bestehende ViewModels (`CommitListViewModel`, `TimestampEditViewModel`) bekommen `IGitBackend` per Constructor-Injection statt direkten LibGit2Sharp-Zugriff.

### A.2 – `RepositoryStateService` (½ Tag)

**Anlegen: `Services/RepositoryStateService.cs`**

```csharp
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Services.Git;

namespace Gitster.Services;

public partial class RepositoryStateService : ObservableObject, IDisposable
{
    private readonly IGitBackend _git;
    private FileSystemWatcher? _indexWatcher;
    private FileSystemWatcher? _workingDirWatcher;
    private System.Timers.Timer? _debounceTimer;
    private const int DebounceMs = 200;

    [ObservableProperty]
    private WorkingTreeState _workingTreeState = new WorkingTreeState.Clean();

    [ObservableProperty]
    private string? _currentBranch;

    [ObservableProperty]
    private string? _repositoryPath;

    public RepositoryStateService(IGitBackend git)
    {
        _git = git;
        _debounceTimer = new System.Timers.Timer(DebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += async (_, _) => await RefreshAsync();
    }

    public async Task AttachAsync(string repoPath)
    {
        DetachWatchers();
        RepositoryPath = repoPath;

        var gitDir = Path.Combine(repoPath, ".git");
        if (File.Exists(gitDir))
        {
            // .git is a file (worktree, submodule) – resolve it.
            var content = await File.ReadAllTextAsync(gitDir);
            if (content.StartsWith("gitdir:"))
                gitDir = content.Substring(7).Trim();
        }

        // Watch .git/index for staging changes
        var indexPath = Path.Combine(gitDir, "index");
        if (File.Exists(indexPath))
        {
            _indexWatcher = new FileSystemWatcher(gitDir)
            {
                Filter = "index",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _indexWatcher.Changed += OnGitChanged;
        }

        // Watch working directory for file changes
        _workingDirWatcher = new FileSystemWatcher(repoPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _workingDirWatcher.Changed += OnWorkingDirChanged;
        _workingDirWatcher.Created += OnWorkingDirChanged;
        _workingDirWatcher.Deleted += OnWorkingDirChanged;
        _workingDirWatcher.Renamed += OnWorkingDirChanged;

        await RefreshAsync();
    }

    private void OnGitChanged(object sender, FileSystemEventArgs e) => RequestRefresh();

    private void OnWorkingDirChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore changes inside .git/
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            return;
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        // Debounce – multiple events within 200ms collapse to one refresh
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    public async Task RefreshAsync()
    {
        if (RepositoryPath is null) return;
        try
        {
            // Marshal to UI thread for property updates
            var state = await _git.GetWorkingTreeStateAsync();
            var branch = await _git.GetCurrentBranchAsync();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                WorkingTreeState = state;
                CurrentBranch = branch.Name;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RepositoryStateService.RefreshAsync failed: {ex}");
        }
    }

    private void DetachWatchers()
    {
        _indexWatcher?.Dispose();
        _indexWatcher = null;
        _workingDirWatcher?.Dispose();
        _workingDirWatcher = null;
    }

    public void Dispose()
    {
        DetachWatchers();
        _debounceTimer?.Dispose();
    }
}
```

**Modelle anlegen: `Models/WorkingTreeState.cs`**

```csharp
namespace Gitster.Models;

public abstract record WorkingTreeState
{
    public sealed record Clean : WorkingTreeState;
    public sealed record Dirty(int Modified, int Staged, int Untracked) : WorkingTreeState;
    public sealed record Merging(string FromBranch) : WorkingTreeState;
    public sealed record Rebasing(int CurrentStep, int TotalSteps) : WorkingTreeState;
    public sealed record CherryPicking(string Sha) : WorkingTreeState;
}
```

`MainWindow_Activated` ruft zusätzlich `_stateService.RefreshAsync()` als Sicherheitsnetz auf — falls FileSystemWatcher Events verpasst hat.

### A.3 – `OperationFeedbackService` (¼ Tag)

**Anlegen: `Services/OperationFeedbackService.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Services;

public abstract record OperationFeedback
{
    public sealed record Running(string Verb, DateTime StartedAt) : OperationFeedback;
    public sealed record Success(string Verb, string? Detail, DateTime CompletedAt) : OperationFeedback;
    public sealed record Failure(string Verb, string Reason, DateTime CompletedAt) : OperationFeedback;
}

public partial class OperationFeedbackService : ObservableObject
{
    private const int SuccessFadeOutMs = 3000;
    private CancellationTokenSource? _fadeOutCts;

    [ObservableProperty]
    private OperationFeedback? _current;

    public async Task<T> RunAsync<T>(string verb, Func<Task<T>> action, Func<T, string?>? detailSelector = null)
    {
        CancelPendingFadeOut();
        Current = new OperationFeedback.Running(verb, DateTime.Now);
        try
        {
            var result = await action();
            Current = new OperationFeedback.Success(verb, detailSelector?.Invoke(result), DateTime.Now);
            ScheduleFadeOut();
            return result;
        }
        catch (Exception ex)
        {
            Current = new OperationFeedback.Failure(verb, ex.Message, DateTime.Now);
            // No fade-out for failures – user must dismiss manually.
            throw;
        }
    }

    public Task RunAsync(string verb, Func<Task> action)
        => RunAsync<object?>(verb, async () => { await action(); return null; });

    public void Dismiss() => Current = null;

    private void CancelPendingFadeOut()
    {
        _fadeOutCts?.Cancel();
        _fadeOutCts = null;
    }

    private void ScheduleFadeOut()
    {
        _fadeOutCts = new CancellationTokenSource();
        var token = _fadeOutCts.Token;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SuccessFadeOutMs, token);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (Current is OperationFeedback.Success) Current = null;
                });
            }
            catch (TaskCanceledException) { /* expected */ }
        });
    }
}
```

DI-registriert als Singleton.

### A.4 – `AutoFetchService` (¼ Tag)

**Anlegen: `Services/AutoFetchService.cs`**

```csharp
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Services.Git;

namespace Gitster.Services;

public partial class AutoFetchService : ObservableObject, IDisposable
{
    private readonly IGitBackend _git;
    private readonly Timer _timer;
    private const int DefaultIntervalSeconds = 60;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _intervalSeconds = DefaultIntervalSeconds;

    [ObservableProperty]
    private DateTime? _lastFetchAt;

    public AutoFetchService(IGitBackend git)
    {
        _git = git;
        _timer = new Timer { AutoReset = true };
        _timer.Elapsed += async (_, _) => await TickAsync();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _timer.Interval = IntervalSeconds * 1000;
        _timer.Enabled = value && !IsWindowMinimized();
    }

    partial void OnIntervalSecondsChanged(int value)
    {
        _timer.Interval = Math.Max(10, value) * 1000;
    }

    public void OnWindowStateChanged(WindowState state)
    {
        // Pause when minimized
        _timer.Enabled = IsEnabled && state != WindowState.Minimized;
    }

    private static bool IsWindowMinimized()
        => Application.Current.MainWindow?.WindowState == WindowState.Minimized;

    private async Task TickAsync()
    {
        try
        {
            await _git.FetchAsync();
            await Application.Current.Dispatcher.InvokeAsync(() => LastFetchAt = DateTime.Now);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoFetch failed: {ex}");
            // Silent failure – user sees ahead/behind stop updating, that's feedback enough.
        }
    }

    public void Dispose() => _timer.Dispose();
}
```

Hook in `MainWindow.xaml.cs`:

```csharp
protected override void OnStateChanged(EventArgs e)
{
    base.OnStateChanged(e);
    (DataContext as MainWindowViewModel)?.AutoFetch.OnWindowStateChanged(WindowState);
}
```

### A.5 – `StatusBar.xaml` als UserControl (¼ Tag)

**Anlegen: `Controls/StatusBar.xaml`**

```xml
<UserControl x:Class="Gitster.Controls.StatusBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="26">
    <Border Background="{DynamicResource BackgroundSecondary}"
            BorderBrush="{DynamicResource BorderTertiary}"
            BorderThickness="0,0.5,0,0">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>   <!-- left: working tree state -->
                <ColumnDefinition Width="*"/>      <!-- middle: operation echo -->
                <ColumnDefinition Width="Auto"/>   <!-- right: repo path -->
            </Grid.ColumnDefinitions>

            <!-- LEFT: working-tree state -->
            <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="10,0">
                <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                         Fill="{Binding StateIndicatorBrush}"/>
                <TextBlock Text="{Binding StateText}" Margin="6,0,0,0"
                           VerticalAlignment="Center"
                           FontSize="12"
                           Foreground="{DynamicResource TextSecondary}"/>
            </StackPanel>

            <!-- MIDDLE: operation echo (running / success / failure) -->
            <Grid Grid.Column="1" HorizontalAlignment="Center"
                  Visibility="{Binding HasFeedback, Converter={StaticResource BooleanToVisibilityConverter}}">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <!-- Ring spinner (running) -->
                    <controls:RingSpinner Width="12" Height="12"
                                          Visibility="{Binding IsRunning, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                    <!-- Success/failure icon -->
                    <TextBlock Text="{Binding FeedbackIcon}"
                               FontSize="11"
                               Foreground="{Binding FeedbackBrush}"
                               VerticalAlignment="Center"
                               Visibility="{Binding IsRunning, Converter={StaticResource InverseBoolVisibilityConverter}}"/>
                    <TextBlock Text="{Binding FeedbackText}" Margin="6,0,0,0"
                               FontSize="12"
                               Foreground="{Binding FeedbackBrush}"
                               VerticalAlignment="Center"/>
                    <Button Margin="6,0,0,0" Padding="2"
                            Background="Transparent" BorderThickness="0"
                            Width="16" Height="16"
                            Command="{Binding DismissFeedbackCommand}"
                            Visibility="{Binding IsFailure, Converter={StaticResource BooleanToVisibilityConverter}}"
                            ToolTip="Dismiss">
                        <Path Stretch="Uniform" Width="8" Height="8"
                              Stroke="{DynamicResource TextSecondary}" StrokeThickness="1.5"
                              Data="M2,2 L10,10 M10,2 L2,10"/>
                    </Button>
                </StackPanel>
            </Grid>

            <!-- RIGHT: repo path -->
            <TextBlock Grid.Column="2" Text="{Binding RepositoryPathDisplay}"
                       FontFamily="{DynamicResource FontFamilyMono}"
                       FontSize="11"
                       Foreground="{DynamicResource TextTertiary}"
                       VerticalAlignment="Center"
                       Margin="10,0"
                       ToolTip="{Binding RepositoryPath}"/>
        </Grid>
    </Border>
</UserControl>
```

**Anlegen: `Controls/RingSpinner.xaml`** — kleines wiederverwendbares Control mit einem `RotateTransform` und `DoubleAnimation` 0→360 in 1s, `RepeatBehavior=Forever`.

**Anlegen: `ViewModels/StatusBarViewModel.cs`**

Properties:
- `StateText` — z.B. `"Clean · main"`, `"3 modified, 1 staged"`, `"REBASING (2/5)"`. Pattern-Match auf `RepositoryStateService.WorkingTreeState`.
- `StateIndicatorBrush` — green/amber/red/blue Brush je nach State.
- `HasFeedback`, `IsRunning`, `IsFailure`, `FeedbackText`, `FeedbackIcon`, `FeedbackBrush` — abgeleitet aus `OperationFeedbackService.Current`.
- `RepositoryPathDisplay` — gekürzt mit `~` für Home-Directory: `"~/repos/laekb"`.
- `DismissFeedbackCommand` — ruft `OperationFeedbackService.Dismiss()`.

Subscribes über `PropertyChanged` auf `RepositoryStateService` und `OperationFeedbackService`.

### A.6 – TitleBar erweitern um Auto-Fetch-Toggle (¼ Tag)

In `Controls/TitleBar.xaml` neben dem Ahead/Behind-Counter:

```xml
<ToggleButton IsChecked="{Binding AutoFetchEnabled}"
              Width="28" Height="28"
              Background="Transparent" BorderThickness="0"
              ToolTip="{Binding AutoFetchTooltip}"
              Margin="6,0,0,0">
    <Grid>
        <Path Stretch="Uniform" Width="14" Height="14"
              Stroke="{DynamicResource TextSecondary}" StrokeThickness="1.5"
              Fill="Transparent"
              Data="M3,10 a7,7 0 0,1 14,0 M17,10 L14,7 M17,10 L20,7
                    M17,10 a7,7 0 0,1 -14,0 M3,10 L6,13 M3,10 L0,13"/>
        <!-- Small green dot in corner when enabled -->
        <Ellipse Width="5" Height="5" Fill="{DynamicResource AccentSuccess}"
                 HorizontalAlignment="Right" VerticalAlignment="Top"
                 Margin="0,2,2,0"
                 Visibility="{Binding AutoFetchEnabled, Converter={StaticResource BooleanToVisibilityConverter}}"/>
    </Grid>
</ToggleButton>
```

Tooltip dynamisch: `"Auto-fetch: off"` vs. `"Auto-fetch: every 60s · last fetched 2m ago"`.

### A.7 – Verkabelung in `MainWindow.xaml`

Status-Bar einbinden:

```xml
<DockPanel>
    <controls:TitleBar DockPanel.Dock="Top" DataContext="{Binding TitleBarVM}"/>
    <controls:UndoBar  DockPanel.Dock="Top" DataContext="{Binding UndoBarVM}"/>
    <controls:StatusBar DockPanel.Dock="Bottom" DataContext="{Binding StatusBarVM}"/>
    <Grid> <!-- Main split --> </Grid>
</DockPanel>
```

`MainWindowViewModel` bekommt zusätzlich `StatusBarVM` und exponiert die neuen Services über die jeweiligen Sub-VMs (DI macht die Hauptarbeit).

---

## Schritt B – Aufräumen + Design-Politur (1 Tag)

### B.1 – Aufräumen in `MainWindow.xaml`

- Inline-Styles in `Grid.Resources` (TextBox, ButtonStyle, IconButtonStyle) entfernen — durch Theme-Resources ersetzt.
- `BooleanToVisibilityConverter` aus dem Window heraus in `App.xaml` ziehen. Zusätzlich `InverseBoolVisibilityConverter` dort registrieren (für die Spinner-Logik in der Status-Bar).
- Alte 6-spaltige Status-Bar-Implementierung im `MainWindow.xaml` weg.
- Repo-Pfad und „Browse..." sind schon in TitleBar — verbliebene Reste prüfen.
- **Akzeptanz: `MainWindow.xaml` unter 50 Zeilen.**

### B.2 – Suchfeld in `CommitListView`

```xml
<!-- Aktuell zu hoch und zu lang -->
<Grid Height="30">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <Border Grid.Column="0"
            BorderBrush="{DynamicResource BorderTertiary}" BorderThickness="0.5"
            CornerRadius="4" Background="{DynamicResource BackgroundPrimary}"
            Padding="8,0">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <Path Grid.Column="0" Stretch="Uniform" Width="12" Height="12"
                  Stroke="{DynamicResource TextTertiary}" StrokeThickness="1.5"
                  Fill="Transparent"
                  Data="M9,9 m-6,0 a6,6 0 1,0 12,0 a6,6 0 1,0 -12,0 M13.5,13.5 L18,18"
                  VerticalAlignment="Center"/>
            <TextBox Grid.Column="1" Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged}"
                     BorderThickness="0" Background="Transparent"
                     VerticalContentAlignment="Center" Margin="6,0,0,0"
                     FontSize="13"
                     Tag="Search commits...">
                <!-- Placeholder via Tag + adorner-Style or use a TextBoxHelper. -->
            </TextBox>
            <Button Grid.Column="2" Width="20" Height="20" Padding="2"
                    Background="Transparent" BorderThickness="0"
                    Command="{Binding ShowFilterSyntaxHelpCommand}"
                    ToolTip="Filter syntax: author:name  type:feat  free text">
                <Path Stretch="Uniform" Width="12" Height="12"
                      Stroke="{DynamicResource TextTertiary}" StrokeThickness="1.5"
                      Fill="Transparent"
                      Data="M10,10 m-8,0 a8,8 0 1,0 16,0 a8,8 0 1,0 -16,0 M10,6 L10,11 M10,14 L10,14.5"/>
            </Button>
        </Grid>
    </Border>
    <Button Grid.Column="1" Margin="6,0,0,0" Width="30" Height="30"
            Command="{Binding OpenFilterCommand}" ToolTip="Advanced filter"
            Style="{StaticResource GhostButton}">
        <Path Stretch="Uniform" Width="14" Height="14"
              Stroke="{DynamicResource TextSecondary}" StrokeThickness="1.5"
              Fill="Transparent"
              Data="M2,4 L18,4 M5,10 L15,10 M8,16 L12,16"/>
    </Button>
</Grid>
```

Placeholder via `TextBoxHelper`-Pattern (kleiner Inline-Adorner) oder über einen leichten Style mit `VisualBrush`. Wenn das Projekt schon ein `Placeholder`-AttachedProperty hat: nutzen.

### B.3 – Commit-Liste

- **Header-Styling:**
  ```xml
  <Style TargetType="GridViewColumnHeader">
      <Setter Property="FontWeight" Value="Normal"/>
      <Setter Property="Foreground" Value="{DynamicResource TextSecondary}"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="Padding" Value="8,4"/>
      <Setter Property="Background" Value="{DynamicResource BackgroundSecondary}"/>
      <Setter Property="BorderBrush" Value="{DynamicResource BorderTertiary}"/>
      <Setter Property="BorderThickness" Value="0,0,0,0.5"/>
  </Style>
  ```

- **Selected-Row Doppelrahmen-Fix:**
  ```xml
  <ListView.Resources>
      <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"
                       Color="{Binding Source={StaticResource AccentBlueBackground}, Path=Color}"/>
      <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"
                       Color="{Binding Source={StaticResource TextPrimary}, Path=Color}"/>
      <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"
                       Color="{Binding Source={StaticResource AccentBlueBackground}, Path=Color}"/>
      <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}"
                       Color="{Binding Source={StaticResource TextPrimary}, Path=Color}"/>
  </ListView.Resources>
  ```

- **SHA-Spalte Mono-Font:**
  ```xml
  <GridViewColumn Header="SHA" Width="70">
      <GridViewColumn.CellTemplate>
          <DataTemplate>
              <TextBlock Text="{Binding CommitId}"
                         FontFamily="{DynamicResource FontFamilyMono}"
                         FontSize="11"
                         Foreground="{DynamicResource TextSecondary}"/>
          </DataTemplate>
      </GridViewColumn.CellTemplate>
  </GridViewColumn>
  ```

### B.4 – Action-Spalte: visuelle Hierarchie

**`CommitContextCard.xaml`** — Message kräftiger als Meta:

```xml
<Border Style="{StaticResource PanelCard}">
    <StackPanel>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
            <Path Stretch="Uniform" Width="12" Height="12"
                  Stroke="{DynamicResource TextSecondary}" StrokeThickness="1.5"
                  Fill="Transparent"
                  Data="M10,10 m-7,0 a7,7 0 1,0 14,0 a7,7 0 1,0 -14,0 M10,10 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0"/>
            <TextBlock Text="Selected commit" Margin="6,0,0,0"
                       FontSize="11" Foreground="{DynamicResource TextSecondary}"/>
        </StackPanel>
        <TextBlock Text="{Binding SelectedCommit.Message}"
                   FontWeight="Medium" FontSize="13"
                   TextTrimming="CharacterEllipsis"/>
        <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
            <TextBlock Text="{Binding SelectedCommit.CommitId}"
                       FontFamily="{DynamicResource FontFamilyMono}"
                       FontSize="11" Foreground="{DynamicResource TextTertiary}"/>
            <TextBlock Text="·" Margin="6,0" Foreground="{DynamicResource TextTertiary}"/>
            <TextBlock Text="{Binding SelectedCommit.Date, StringFormat='dd.MM.yyyy HH:mm'}"
                       FontSize="11" Foreground="{DynamicResource TextTertiary}"/>
        </StackPanel>
    </StackPanel>
</Border>
```

### B.5 – Preview-Box `before / after`

In `TimestampEditPanel.xaml`:

```xml
<Border Background="{DynamicResource BackgroundSecondary}"
        CornerRadius="4" Padding="8,6" Margin="0,8">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition/>
            <RowDefinition/>
        </Grid.RowDefinitions>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <TextBlock Grid.Row="0" Grid.Column="0" Text="before"
                   FontSize="11" Foreground="{DynamicResource TextTertiary}"
                   Margin="0,0,12,0"/>
        <TextBlock Grid.Row="0" Grid.Column="1"
                   FontFamily="{DynamicResource FontFamilyMono}" FontSize="11">
            <Run Text="{Binding PreviewBeforeText}"/>
            <Run Text="·"/>
            <Run Text="{Binding PreviewBeforeSha}" Foreground="{DynamicResource TextTertiary}"/>
        </TextBlock>

        <TextBlock Grid.Row="1" Grid.Column="0" Text="after"
                   FontSize="11" Foreground="{DynamicResource TextTertiary}"
                   Margin="0,2,12,0"/>
        <TextBlock Grid.Row="1" Grid.Column="1" Margin="0,2,0,0"
                   FontFamily="{DynamicResource FontFamilyMono}" FontSize="11">
            <Run Text="{Binding PreviewAfterText}"/>
            <Run Text="·"/>
            <Run Text="→ new" Foreground="{DynamicResource AccentWarning}"/>
        </TextBlock>
    </Grid>
</Border>
```

Labels (`before`/`after`) sind jetzt sans-serif `TextTertiary`, Werte mono.

### B.6 – Quick-Actions kompakter

`QuickActionsPanel.xaml` — Buttons auf 32px Höhe:

```xml
<Border Style="{StaticResource PanelCard}">
    <StackPanel>
        <TextBlock Text="More actions" FontSize="11"
                   Foreground="{DynamicResource TextSecondary}"
                   Margin="0,0,0,6"/>
        <UniformGrid Rows="2" Columns="2" Margin="-2">
            <Button Margin="2" Height="32" HorizontalContentAlignment="Left" Padding="8,0"
                    Command="{Binding RewordCommand}" Style="{StaticResource GhostButton}"
                    ToolTip="{Binding RewordTooltip}">
                <StackPanel Orientation="Horizontal">
                    <Path Stretch="Uniform" Width="12" Height="12"
                          Stroke="{DynamicResource TextSecondary}" StrokeThickness="1.5"
                          Fill="Transparent"
                          Data="M2,14 L14,2 L18,6 L6,18 L2,18 Z"
                          VerticalAlignment="Center"/>
                    <TextBlock Text="Reword" Margin="8,0,0,0" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
            <!-- Cherry-pick, Author, Fixup analog -->
        </UniformGrid>
    </StackPanel>
</Border>
```

Disabled-State und Tooltip kommen aus dem Capability-System (siehe Schritt D.4).

### B.7 – TitleBar: Switch repo als SplitButton

Sieht das Projekt bereits einen `SplitButton` vor? Falls nicht (Standard-WPF hat keinen), bleibt es bei Button + separates Menü unter `File > Recent`. Sonst:

```xml
<ui:SplitButton Header="Switch repo" Command="{Binding BrowseRepoCommand}">
    <ui:SplitButton.Flyout>
        <ItemsControl ItemsSource="{Binding RecentRepos}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <MenuItem Header="{Binding DisplayPath}"
                              ToolTip="{Binding FullPath}"
                              Command="{Binding DataContext.OpenRepoCommand, ...}"
                              CommandParameter="{Binding FullPath}"/>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ui:SplitButton.Flyout>
</ui:SplitButton>
```

Wenn kein SplitButton verfügbar: einfach normaler Button bleibt, Recent kommt rein ins File-Menü (Schritt C).

### B.8 – Keyboard-Bindings

```xml
<Window.InputBindings>
    <KeyBinding Key="F" Modifiers="Ctrl" Command="{Binding FocusSearchCommand}"/>
    <KeyBinding Key="Z" Modifiers="Ctrl" Command="{Binding UndoBarVM.UndoCommand}"/>
    <KeyBinding Key="F5"                 Command="{Binding FetchCommand}"/>
    <KeyBinding Key="Enter" Modifiers="Ctrl" Command="{Binding TimestampEditVM.AmendCommand}"/>
</Window.InputBindings>
```

`FocusSearchCommand` schickt ein Message-Bus-Event, das `CommitListView` empfängt und `SearchTextBox.Focus()` aufruft.

### B.9 – Window-Title-Konvention

In `MainWindow.xaml` Title-Property dynamisch binden:

```xml
Title="{Binding WindowTitle}"
```

`MainWindowViewModel.WindowTitle` = `"<RepoName> · <Branch> – Gitster"`. Wenn kein Repo offen: nur `"Gitster"`. Das macht die Taskbar-Erkennung bei mehreren Fenstern eindeutig.

---

## Schritt C – Menü-Leiste + Recent Repos + Settings-Hülle (1 Tag)

### C.1 – Menü-Struktur

`MainWindow.xaml`, ganz oben in `DockPanel`:

```xml
<Menu DockPanel.Dock="Top" Background="{DynamicResource BackgroundPrimary}"
      BorderBrush="{DynamicResource BorderTertiary}" BorderThickness="0,0,0,0.5">
    <MenuItem Header="_File">
        <MenuItem Header="Open repository..." Command="{Binding BrowseRepoCommand}"
                  InputGestureText="Ctrl+O"/>
        <MenuItem Header="Recent" ItemsSource="{Binding RecentRepos}">
            <MenuItem.ItemTemplate>
                <DataTemplate>
                    <MenuItem Header="{Binding DisplayPath}"
                              ToolTip="{Binding FullPath}"
                              Command="{Binding DataContext.OpenRepoCommand,
                                        RelativeSource={RelativeSource AncestorType=Menu}}"
                              CommandParameter="{Binding FullPath}"/>
                </DataTemplate>
            </MenuItem.ItemTemplate>
        </MenuItem>
        <Separator/>
        <MenuItem Header="E_xit" Command="{Binding ExitCommand}"/>
    </MenuItem>

    <MenuItem Header="_Repository">
        <MenuItem Header="Fetch"           Command="{Binding FetchCommand}" InputGestureText="F5"/>
        <MenuItem Header="Pull"            Command="{Binding PullCommand}"/>
        <MenuItem Header="Push"            Command="{Binding PushCommand}"/>
        <Separator/>
        <MenuItem Header="Switch branch..." Command="{Binding SwitchBranchCommand}"/>
        <Separator/>
        <MenuItem Header="Operations log..." Command="{Binding OpenOperationsLogCommand}"/>
        <MenuItem Header="Repository settings..." Command="{Binding OpenRepoSettingsCommand}"/>
    </MenuItem>

    <MenuItem Header="_Edit">
        <MenuItem Header="Undo"   Command="{Binding UndoBarVM.UndoCommand}" InputGestureText="Ctrl+Z"/>
        <MenuItem Header="Redo"   Command="{Binding UndoBarVM.RedoCommand}" InputGestureText="Ctrl+Y"/>
        <Separator/>
        <MenuItem Header="Filter..." Command="{Binding CommitListVM.OpenFilterCommand}"
                  InputGestureText="Ctrl+F"/>
    </MenuItem>

    <MenuItem Header="_View">
        <MenuItem Header="Refresh" Command="{Binding RefreshCommand}" InputGestureText="F5"/>
        <Separator/>
        <MenuItem Header="Dark mode" IsCheckable="True" IsChecked="{Binding IsDarkMode}"
                  IsEnabled="False" ToolTip="Coming in phase 2"/>
    </MenuItem>

    <MenuItem Header="_Help">
        <MenuItem Header="Documentation"      Command="{Binding OpenDocsCommand}"/>
        <MenuItem Header="Keyboard shortcuts" Command="{Binding OpenShortcutsCommand}"/>
        <Separator/>
        <MenuItem Header="About Gitster"      Command="{Binding OpenAboutCommand}"/>
    </MenuItem>
</Menu>
```

### C.2 – Recent-Repos Service

**Anlegen: `Services/RecentReposService.cs`**

```csharp
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Services;

public record RecentRepoEntry(string FullPath, DateTime LastOpenedAt)
{
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return FullPath.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                ? "~" + FullPath.Substring(home.Length).Replace('\\', '/')
                : FullPath;
        }
    }
}

public partial class RecentReposService : ObservableObject
{
    private const int MaxEntries = 10;
    private readonly string _storagePath;

    [ObservableProperty]
    private ObservableCollection<RecentRepoEntry> _entries = new();

    public RecentReposService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Gitster");
        Directory.CreateDirectory(dir);
        _storagePath = Path.Combine(dir, "recent-repos.json");
        Load();
    }

    public void Record(string path)
    {
        var existing = Entries.FirstOrDefault(e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) Entries.Remove(existing);
        Entries.Insert(0, new RecentRepoEntry(path, DateTime.Now));
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var json = File.ReadAllText(_storagePath);
            var list = JsonSerializer.Deserialize<List<RecentRepoEntry>>(json) ?? new();
            Entries = new ObservableCollection<RecentRepoEntry>(list);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecentReposService.Load: {ex}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Entries.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecentReposService.Save: {ex}");
        }
    }
}
```

DI als Singleton, in `MainWindowViewModel` injected. `BrowseRepoCommand` und `OpenRepoCommand` rufen nach erfolgreichem Open `_recentRepos.Record(path)`.

### C.3 – Settings-Dialog (Hülle)

**Anlegen: `Views/RepositorySettingsWindow.xaml`** als modaler Dialog:

```xml
<Window x:Class="Gitster.Views.RepositorySettingsWindow"
        Title="Repository settings – Gitster"
        Width="600" Height="450"
        WindowStartupLocation="CenterOwner"
        ResizeMode="CanResize">
    <TabControl>
        <TabItem Header="General">
            <StackPanel Margin="16">
                <TextBlock Text="General settings (placeholder)" FontSize="13"/>
                <!-- z.B. Author-Override für dieses Repo, später -->
            </StackPanel>
        </TabItem>
        <TabItem Header="Git config healthcheck">
            <StackPanel Margin="16">
                <TextBlock Text="Recommendations for this repository:" FontSize="13"
                           Margin="0,0,0,12"/>
                <ItemsControl ItemsSource="{Binding HealthcheckItems}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="{DynamicResource BorderTertiary}" BorderThickness="0.5"
                                    CornerRadius="4" Padding="12,8" Margin="0,0,0,6">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <StackPanel>
                                        <TextBlock Text="{Binding Title}" FontWeight="Medium"/>
                                        <TextBlock Text="{Binding Description}" FontSize="12"
                                                   Foreground="{DynamicResource TextSecondary}"
                                                   TextWrapping="Wrap"/>
                                    </StackPanel>
                                    <Button Grid.Column="1" Content="Apply"
                                            Command="{Binding ApplyCommand}"
                                            Style="{StaticResource GhostButton}"
                                            Visibility="{Binding IsApplied, Converter={StaticResource InverseBoolVisibilityConverter}}"/>
                                    <TextBlock Grid.Column="1" Text="✓ Applied" 
                                               Foreground="{DynamicResource AccentSuccess}"
                                               VerticalAlignment="Center"
                                               Visibility="{Binding IsApplied, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </TabItem>
    </TabControl>
</Window>
```

Healthcheck-Items in Phase 1 mit Platzhalter-Liste (`diff.algorithm = histogram`, `fetch.prune = true`, `rerere.enabled = true`, `push.autoSetupRemote = true`). Echte Anwendungslogik kann in Phase 1 oder direkt danach implementiert werden — wichtig ist dass der Dialog *existiert* und das Pattern für künftige Settings steht.

---

## Schritt D – Operations-Log + Capability-System (2.5 Tage)

### D.1 – Capability-Attribute und WPF-Helper (½ Tag)

Wir adaptieren euer `Recht`-Pattern. Der Unterschied zum Original: Capabilities können sich *dynamisch* ändern (Git-CLI wird im Hintergrund detected, ein Repo wird mit anderem Backend-Set geöffnet), darum subscribe-basiert statt nur einmal beim Setzen evaluiert.

**Anlegen: `Services/Capabilities/CapabilityService.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Services.Git;

namespace Gitster.Services.Capabilities;

public partial class CapabilityService : ObservableObject
{
    private readonly IGitBackend _git;

    [ObservableProperty]
    private bool _isGitCliAvailable;

    [ObservableProperty]
    private string? _gitCliVersion;

    public CapabilityService(IGitBackend git)
    {
        _git = git;
        _ = DetectGitCliAsync();
    }

    public bool HasCapability(GitCapabilities cap) => _git.Capabilities.HasFlag(cap);

    public bool Requires(string capabilityName) => capabilityName switch
    {
        "GitCli"            => IsGitCliAvailable,
        "InteractiveRebase" => HasCapability(GitCapabilities.InteractiveRebase),
        "FixupAutosquash"   => HasCapability(GitCapabilities.FixupAutosquash),
        "PickaxeSearch"     => HasCapability(GitCapabilities.PickaxeSearch),
        "RangeDiff"         => HasCapability(GitCapabilities.RangeDiff),
        "Worktrees"         => HasCapability(GitCapabilities.Worktrees),
        "CommitSigning"     => HasCapability(GitCapabilities.CommitSigning),
        _ => true   // unknown capability: assume available
    };

    public string GetMissingReason(string capabilityName) => capabilityName switch
    {
        "GitCli"            => "Requires Git command-line tool to be installed.",
        "InteractiveRebase" => "Requires Git CLI (install Git to enable).",
        "FixupAutosquash"   => "Requires Git CLI (install Git to enable).",
        "PickaxeSearch"     => "Coming in phase 4.",
        "RangeDiff"         => "Coming in phase 4 (requires Git CLI).",
        "Worktrees"         => "Coming in phase 3.",
        "CommitSigning"     => "Coming later (requires Git CLI and GPG/SSH setup).",
        _ => "Unavailable."
    };

    private async Task DetectGitCliAsync()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("git", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                IsGitCliAvailable = true;
                GitCliVersion = output.Trim();
            }
        }
        catch
        {
            IsGitCliAvailable = false;
        }
    }
}
```

**Anlegen: `Views/Helper/Capability.cs`** — Attached Properties, an euer `Recht`-Pattern angelehnt:

```csharp
using System.Windows.Documents;
using Gitster.Services.Capabilities;

namespace Gitster.Views.Helper;

public class Capability : DependencyObject
{
    public static readonly DependencyProperty RequiresProperty = DependencyProperty.RegisterAttached(
        "Requires", typeof(string), typeof(Capability),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRequiresChanged));

    public static readonly DependencyProperty VisibleIfProperty = DependencyProperty.RegisterAttached(
        "VisibleIf", typeof(string), typeof(Capability),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnVisibleIfChanged));

    public static string GetRequires(DependencyObject obj) => (string)obj.GetValue(RequiresProperty);
    public static void SetRequires(DependencyObject obj, string value) => obj.SetValue(RequiresProperty, value);

    public static string GetVisibleIf(DependencyObject obj) => (string)obj.GetValue(VisibleIfProperty);
    public static void SetVisibleIf(DependencyObject obj, string value) => obj.SetValue(VisibleIfProperty, value);

    private static CapabilityService? _service;
    public static void Initialize(CapabilityService service)
    {
        _service = service;
        service.PropertyChanged += (_, _) => RefreshAll();
    }

    private static readonly List<WeakReference<FrameworkElement>> _trackedElements = new();

    private static void OnRequiresChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        Track(element);
        ApplyRequires(element, (string?)e.NewValue);
    }

    private static void OnVisibleIfChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        Track(element);
        ApplyVisibleIf(element, (string?)e.NewValue);
    }

    private static void Track(FrameworkElement element)
    {
        _trackedElements.RemoveAll(wr => !wr.TryGetTarget(out _));
        if (!_trackedElements.Any(wr => wr.TryGetTarget(out var t) && t == element))
            _trackedElements.Add(new WeakReference<FrameworkElement>(element));
    }

    private static void RefreshAll()
    {
        foreach (var wr in _trackedElements.ToList())
        {
            if (wr.TryGetTarget(out var element))
            {
                ApplyRequires(element, GetRequires(element));
                ApplyVisibleIf(element, GetVisibleIf(element));
            }
        }
    }

    private static void ApplyRequires(FrameworkElement element, string? capability)
    {
        if (string.IsNullOrEmpty(capability) || _service is null)
        {
            element.IsEnabled = true;
            CapabilityAdorner.Detach(element);
            return;
        }

        var available = _service.Requires(capability);
        element.IsEnabled = available;
        if (!available)
        {
            var reason = _service.GetMissingReason(capability);
            CapabilityAdorner.Attach(element, reason);
        }
        else
        {
            CapabilityAdorner.Detach(element);
        }
    }

    private static void ApplyVisibleIf(FrameworkElement element, string? capability)
    {
        if (string.IsNullOrEmpty(capability) || _service is null)
        {
            element.Visibility = Visibility.Visible;
            return;
        }
        element.Visibility = _service.Requires(capability) ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

**Anlegen: `Views/Helper/CapabilityAdorner.cs`** — der visuelle Indikator, dass *strukturell* etwas fehlt:

```csharp
using System.Windows.Documents;
using System.Windows.Media;

namespace Gitster.Views.Helper;

public class CapabilityAdorner : Adorner
{
    private readonly string _reason;
    private static readonly Pen StrikethroughPen;
    private static readonly Brush LockBrush;

    static CapabilityAdorner()
    {
        StrikethroughPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), 1);
        StrikethroughPen.Freeze();
        LockBrush = (Brush)Application.Current.FindResource("TextTertiary");
    }

    public CapabilityAdorner(UIElement adornedElement, string reason) : base(adornedElement)
    {
        _reason = reason;
        IsHitTestVisible = true;
        ToolTip = $"Unavailable\n{reason}";
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(AdornedElement.RenderSize);

        // Small dotted overlay in top-right corner indicating "structurally disabled"
        var dotSize = 6.0;
        var center = new Point(rect.Right - 8, rect.Top + 8);
        dc.DrawEllipse(LockBrush, null, center, dotSize / 2, dotSize / 2);
    }

    public static void Attach(FrameworkElement element, string reason)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) { element.Loaded += DeferredAttach; return; }
        Detach(element);
        layer.Add(new CapabilityAdorner(element, reason));

        void DeferredAttach(object? sender, RoutedEventArgs e)
        {
            element.Loaded -= DeferredAttach;
            Attach(element, reason);
        }
    }

    public static void Detach(FrameworkElement element)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) return;
        var existing = layer.GetAdorners(element);
        if (existing is null) return;
        foreach (var ad in existing.OfType<CapabilityAdorner>())
            layer.Remove(ad);
    }
}
```

**Verwendung im XAML:**

```xml
<Button Content="Reword"
        helper:Capability.Requires="FixupAutosquash"
        Command="{Binding RewordCommand}"/>
```

Resultat:
- Wenn `FixupAutosquash` verfügbar: Button funktioniert normal.
- Wenn nicht: Button ist disabled, kleiner Punkt in der oberen rechten Ecke, Tooltip erklärt warum.

**Initialisierung in `App.xaml.cs`:**

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    var capService = ServiceProvider.GetRequiredService<CapabilityService>();
    Capability.Initialize(capService);
}
```

**Unterschied zu temporär-disabled Buttons:**

- Capability-Adorner = struktureller Punkt + Tooltip mit „Was fehlt und wie behebe ich's".
- Standard `IsEnabled=false` (z.B. „kein Commit ausgewählt") = nur ausgegraut, kein Adorner.

Damit ist der Unterschied visuell und tooltip-mäßig klar.

### D.2 – Operations-Log Persistenz (½ Tag)

**Anlegen: `Services/OperationsLog/OperationsLogService.cs`**

```csharp
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Services.OperationsLog;

public partial class OperationsLogService : ObservableObject
{
    private string? _storagePath;

    [ObservableProperty]
    private ObservableCollection<OperationRecord> _records = new();

    public OperationRecord? MostRecentActive
        => Records.FirstOrDefault(r => r.Status == OperationStatus.Active);

    public async Task AttachAsync(string repoPath)
    {
        _storagePath = ResolveStoragePath(repoPath);
        await LoadAsync();
    }

    public async Task RecordAsync(OperationRecord record)
    {
        Records.Insert(0, record);
        await SaveAsync();
        OnPropertyChanged(nameof(MostRecentActive));
    }

    public async Task MarkUndoneAsync(string recordId)
    {
        var record = Records.FirstOrDefault(r => r.Id == recordId);
        if (record is null) return;
        var idx = Records.IndexOf(record);
        Records[idx] = record with { Status = OperationStatus.Undone };
        await SaveAsync();
        OnPropertyChanged(nameof(MostRecentActive));
    }

    public async Task MarkExpiredAsync(string recordId)
    {
        var record = Records.FirstOrDefault(r => r.Id == recordId);
        if (record is null) return;
        var idx = Records.IndexOf(record);
        Records[idx] = record with { Status = OperationStatus.Expired };
        await SaveAsync();
    }

    private static string ResolveStoragePath(string repoPath)
    {
        // Primary: .git/gitster/operations.json
        var gitDir = Path.Combine(repoPath, ".git");
        if (Directory.Exists(gitDir))
        {
            var dir = Path.Combine(gitDir, "gitster");
            try
            {
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "operations.json");
            }
            catch { /* fall through */ }
        }
        // Fallback: .gitster/operations.json in repo root + ensure .gitignore entry
        var fallbackDir = Path.Combine(repoPath, ".gitster");
        Directory.CreateDirectory(fallbackDir);
        EnsureGitIgnore(repoPath, ".gitster/");
        return Path.Combine(fallbackDir, "operations.json");
    }

    private static void EnsureGitIgnore(string repoPath, string entry)
    {
        var path = Path.Combine(repoPath, ".gitignore");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        if (!lines.Contains(entry))
        {
            lines.Add(entry);
            File.WriteAllLines(path, lines);
        }
    }

    private async Task LoadAsync()
    {
        if (_storagePath is null || !File.Exists(_storagePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_storagePath);
            var list = JsonSerializer.Deserialize<List<OperationRecord>>(json) ?? new();
            Records = new ObservableCollection<OperationRecord>(list);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OperationsLog.Load: {ex}");
        }
    }

    private async Task SaveAsync()
    {
        if (_storagePath is null) return;
        try
        {
            var json = JsonSerializer.Serialize(Records.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_storagePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OperationsLog.Save: {ex}");
        }
    }
}
```

**Anlegen: `Models/OperationRecord.cs`**

```csharp
namespace Gitster.Services.OperationsLog;

public enum OperationKind { Amend, Reword, Reset, Rebase, CherryPick, CommitOnBranch }
public enum OperationStatus { Active, Undone, Replaced, Expired }

public record OperationRecord(
    string Id,
    DateTimeOffset Timestamp,
    OperationKind Kind,
    string Description,
    string BranchName,
    string BeforeSha,
    string AfterSha,
    string? ReflogSelector,
    OperationStatus Status);
```

### D.3 – Undo-Mechanik (1 Tag)

**Erweitern: `Services/OperationsLog/OperationsLogService.cs`**

```csharp
public async Task<UndoPlan> PrepareUndoAsync(OperationRecord record, IGitBackend git)
{
    if (record.ReflogSelector is null)
        return new UndoPlan.NotAvailable("No reflog selector recorded.");

    // Check that reflog selector still resolves
    string? targetSha;
    try
    {
        targetSha = await git.ResolveRefAsync(record.ReflogSelector);
    }
    catch
    {
        await MarkExpiredAsync(record.Id);
        return new UndoPlan.Expired("Reflog entry no longer available.");
    }

    // Check what would be lost
    var currentHead = await git.GetHeadShaAsync();
    var commitsBetween = await git.GetCommitsBetweenAsync(targetSha, currentHead);
    var wouldBeDiscarded = commitsBetween.Where(c => c.Sha != record.AfterSha).ToList();

    return new UndoPlan.Ready(record, targetSha, wouldBeDiscarded);
}

public async Task ExecuteUndoAsync(UndoPlan.Ready plan, IGitBackend git)
{
    await git.ResetHardAsync(plan.TargetSha);
    await MarkUndoneAsync(plan.Record.Id);
}

public abstract record UndoPlan
{
    public sealed record Ready(OperationRecord Record, string TargetSha, IReadOnlyList<CommitInfo> WouldDiscard) : UndoPlan;
    public sealed record NotAvailable(string Reason) : UndoPlan;
    public sealed record Expired(string Reason) : UndoPlan;
}
```

**UndoBar-VM** baut darauf auf:

```csharp
[RelayCommand]
private async Task UndoAsync()
{
    var record = _opsLog.MostRecentActive;
    if (record is null) return;
    var plan = await _opsLog.PrepareUndoAsync(record, _git);
    
    if (plan is UndoPlan.Ready ready && ready.WouldDiscard.Count > 0)
    {
        var dialog = new UndoConfirmationDialog(ready);
        if (dialog.ShowDialog() != true) return;
    }
    if (plan is UndoPlan.Ready execReady)
    {
        await _feedback.RunAsync("Undo", () => _opsLog.ExecuteUndoAsync(execReady, _git));
    }
    else if (plan is UndoPlan.NotAvailable na)
    {
        MessageBox.Show(na.Reason, "Cannot undo", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    else if (plan is UndoPlan.Expired ex)
    {
        MessageBox.Show(ex.Reason, "Undo expired", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
```

**Anlegen: `Views/UndoConfirmationDialog.xaml`** — modaler Dialog mit Liste der zu verlierenden Commits, Default-Button ist „Cancel".

### D.4 – Operations-Log-Viewer (½ Tag)

**Anlegen: `Views/OperationsLogWindow.xaml`** — modaler Dialog erreichbar über Menü und UndoBar.

Inhalt:
- ListView aller Records mit Spalten: Timestamp (relative + absolute Tooltip), Kind, Description, Status (badge-colored), Before → After SHA.
- Filter-Zeile: Status-Toggle (Active/Undone/Expired), Kind-Filter.
- Pro Zeile: Undo-Button (nur sichtbar wenn Active und Plan = Ready), View-Diff-Button (öffnet einen kompakten Diff zwischen BeforeSha und AfterSha).

Window-Title: `"Operations log – <RepoName>"`.

### D.5 – Integration in bestehende ViewModels (¼ Tag)

`TimestampEditViewModel.AmendAsync`:

```csharp
[RelayCommand(CanExecute = nameof(CanAmend))]
private async Task AmendAsync()
{
    var beforeSha = await _git.GetHeadShaAsync();
    var reflogSelector = await _git.GetReflogSelectorForHeadAsync();
    
    var afterSha = await _feedback.RunAsync("Amend",
        () => _git.AmendAsync(new AmendRequest(SelectedDate.Value)),
        result => result.Substring(0, 7));

    await _opsLog.RecordAsync(new OperationRecord(
        Id: Guid.NewGuid().ToString(),
        Timestamp: DateTimeOffset.Now,
        Kind: OperationKind.Amend,
        Description: $"Amend {afterSha[..7]}",
        BranchName: _stateService.CurrentBranch ?? "?",
        BeforeSha: beforeSha,
        AfterSha: afterSha,
        ReflogSelector: reflogSelector,
        Status: OperationStatus.Active));
}
```

Push-Commands intern auf `forceWithLease: true` umstellen — wenn dein bestehender Code blindes `--force` macht, wird das hier gefixt.

### D.6 – Capabilities an Quick-Action-Buttons

```xml
<Button Content="Reword"
        helper:Capability.Requires="FixupAutosquash"
        Command="{Binding RewordCommand}"
        Style="{StaticResource GhostButton}"/>
<Button Content="Cherry-pick"
        Command="{Binding CherryPickCommand}"
        Style="{StaticResource GhostButton}"/>
<Button Content="Author"
        Command="{Binding ChangeAuthorCommand}"
        Style="{StaticResource GhostButton}"/>
<Button Content="Fixup"
        helper:Capability.Requires="FixupAutosquash"
        Command="{Binding FixupCommand}"
        Style="{StaticResource GhostButton}"/>
```

Reword und Fixup benötigen `--fixup`-Workflow → Git CLI. Cherry-pick und Author-Change funktionieren mit LibGit2Sharp → kein Capability-Tag, aber die Commands selbst sind in Phase 1 noch nicht implementiert (CanExecute=false bis Phase 2 sie aktiviert).

---

## Akzeptanzkriterien für Etappe 2.1

Nach Abschluss aller Schritte:

1. **Status-Bar zeigt live, was im Repo passiert.** Eigene Operationen und externe Änderungen (Terminal-`git add`) erscheinen innerhalb ~250ms.
2. **Auto-Fetch-Toggle in der TitleBar funktioniert**, läuft im aktiven Fenster alle 60s, pausiert bei Minimieren.
3. **Window-Title trägt Repo + Branch**, sodass mehrere Gitster-Instanzen in der Taskbar unterscheidbar sind.
4. **`MainWindow.xaml` ist unter 50 Zeilen**, alle Themes/Resources zentral.
5. **Menü-Leiste ist da**, Recent-Repos funktioniert (JSON-Persistenz in `%AppData%/Gitster/`).
6. **Settings-Dialog öffnet** (Inhalt teilweise Stub für phase-2-Inhalte).
7. **Operations-Log persistiert nach `.git/gitster/operations.json`**, Fallback auf `.gitster/` im Repo-Root mit Auto-`.gitignore`.
8. **UndoBar zeigt jüngste aktive Operation**, klickbar, mit Verlustwarnung wenn nötig.
9. **Operations-Log-Viewer öffnet** und listet alle Records mit Filter.
10. **`IGitBackend`-Abstraktion** ist eingeführt, `LibGit2Backend` ist die einzige Implementierung dahinter.
11. **`Capability.Requires`-Attribute** funktionieren an Buttons und Menüeinträgen, zeigen Adorner mit Erklärungs-Tooltip bei strukturell disabled.
12. **Sicherer Force-Push** (`--force-with-lease`) für alle Push-Operationen.
13. **Design-Politur** abgeschlossen: Suchfeld kompakt, Header dezent, SHA mono, Selected-Commit-Card mit klarer Hierarchie, Quick-Actions kompakt.

---

## Was NICHT in dieser Etappe ist

Phase-2-Features (Author über Range, Fixup-Workflow, Reword in History) kommen in der nächsten Etappe. Diese hier liefert das **Fundament**, auf dem sie aufsetzen.

Auch nicht hier: Healthcheck-Logik-Inhalte (nur Dialog-Hülle), Dark Mode, App-weite Akzent-Konfiguration, Culture-aware Date/Time-Formatierung (das sind alles Phase-2-Themen).