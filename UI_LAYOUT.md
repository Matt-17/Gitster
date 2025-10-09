# UI Layout - Remote Operations

## New Button Toolbar

The bottom toolbar now includes remote operation buttons:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  [Commit ListView]              [Repository Path Input]                     │
│                                                                             │
│                                 [Selected Commit Group]                      │
│                                 [Current Commit Group]                       │
│                                                                             │
│                                 [Date Sliders]                              │
│                                                                             │
│                                 [Date Picker] [Time Display]                │
│                                                                             │
│                     [Fetch][Pull][Push][Sync][Remote ▼][Amend]            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Button Details

### Remote Operation Buttons (Left to Right)

1. **Fetch** (70px width)
   - Tooltip: "Download changes from remote without merging"
   - Command: `FetchCommand`
   - CommandParameter: `SelectedRemote`
   - Enabled: When repository is loaded

2. **Pull** (70px width)
   - Tooltip: "Fetch and merge changes from remote"
   - Command: `PullCommand`
   - CommandParameter: `SelectedRemote`
   - Enabled: When repository is loaded

3. **Push** (70px width)
   - Tooltip: "Upload local changes to remote"
   - Command: `PushCommand`
   - CommandParameter: `SelectedRemote`
   - Enabled: When repository is loaded

4. **Sync** (70px width)
   - Tooltip: "Synchronize with remote (fetch + pull + push)"
   - Command: `SyncCommand`
   - CommandParameter: `SelectedRemote`
   - Enabled: When repository is loaded

5. **Remote Dropdown** (120px width)
   - ComboBox showing all configured remotes
   - Tooltip: "Select remote repository"
   - Binding: `Remotes` collection (ItemsSource)
   - Selection: `SelectedRemote` (TwoWay binding)
   - Auto-selects first remote when repository is loaded

6. **Amend** (70px width)
   - Tooltip: "Amend the commit timestamp"
   - Command: `AmendCommitCommand`
   - Enabled: When repository is loaded
   - Original functionality preserved

## Data Flow

```
User opens repository
    ↓
UpdateElements() is called
    ↓
Remotes collection is populated from repo.Network.Remotes
    ↓
First remote is auto-selected in SelectedRemote
    ↓
User clicks [Fetch] / [Pull] / [Push] / [Sync]
    ↓
SelectedRemote is passed as CommandParameter
    ↓
Command method executes operation with selected remote
    ↓
Success/Error message displayed
    ↓
UpdateElements() refreshes UI (for Fetch, Pull, Sync)
```

## User Workflow Examples

### Example 1: Fetch from Origin
1. Open repository (remotes auto-load)
2. Verify "origin" is selected in Remote dropdown
3. Click **Fetch** button
4. See success message: "Fetched from origin successfully"

### Example 2: Push to a Different Remote
1. Open repository
2. Click Remote dropdown
3. Select "upstream" (or any other remote)
4. Click **Push** button
5. See success message: "Pushed to upstream successfully"

### Example 3: Full Synchronization
1. Open repository
2. Ensure desired remote is selected
3. Click **Sync** button
4. Operation performs: Fetch → Pull → Push
5. See success message: "Synced with [remote] successfully"

## Benefits

- **Clean UI**: All remote operations in one toolbar
- **Flexible**: Support for multiple remotes
- **User-friendly**: Tooltips explain each operation
- **Consistent**: Follows MVVM pattern with data binding
- **Safe**: Operations are transaction-based with LibGit2Sharp
