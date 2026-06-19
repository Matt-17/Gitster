# Step 0 Baseline: Dialog and Window Calls (before WindowService migration)

Date: 2026-05-31
Scope: Gitster project (`Gitster/**/*.cs`)

## Summary counts

- `ShowDialog(...)` usages: 26
- Direct `new Views.*` dialog/window creations in ViewModels: 7
- `MessageBox.Show(...)` usages: 81

## `ShowDialog(...)` call sites

- `Gitster/Views/AddWorktreeDialog.xaml.cs:37`
- `Gitster/ViewModels/AuthorPanelViewModel.cs:114`
- `Gitster/ViewModels/AuthorPanelViewModel.cs:128`
- `Gitster/ViewModels/BranchesViewModel.cs:328`
- `Gitster/ViewModels/BranchesViewModel.cs:383`
- `Gitster/ViewModels/CommitPanelViewModel.cs:184`
- `Gitster/ViewModels/MainWindowViewModel.cs:225`
- `Gitster/ViewModels/MainWindowViewModel.cs:292`
- `Gitster/ViewModels/MainWindowViewModel.cs:301`
- `Gitster/ViewModels/MainWindowViewModel.cs:309`
- `Gitster/ViewModels/MainWindowViewModel.cs:323`
- `Gitster/ViewModels/MainWindowViewModel.cs:365`
- `Gitster/ViewModels/MainWindowViewModel.cs:394`
- `Gitster/ViewModels/MainWindowViewModel.cs:421`
- `Gitster/ViewModels/QuickActionsViewModel.cs:72`
- `Gitster/ViewModels/QuickActionsViewModel.cs:196`
- `Gitster/ViewModels/QuickActionsViewModel.cs:250`
- `Gitster/ViewModels/QuickActionsViewModel.cs:325`
- `Gitster/ViewModels/QuickActionsViewModel.cs:377`
- `Gitster/ViewModels/SearchViewModel.cs:234`
- `Gitster/ViewModels/StashesViewModel.cs:186`
- `Gitster/ViewModels/StashesViewModel.cs:306`
- `Gitster/ViewModels/StashesViewModel.cs:324`
- `Gitster/ViewModels/UndoBarViewModel.cs:101`
- `Gitster/ViewModels/UndoBarViewModel.cs:131`
- `Gitster/ViewModels/WorktreesViewModel.cs:122`

## Direct `new Views.*` dialog/window creation in ViewModels

- `Gitster/ViewModels/AuthorPanelViewModel.cs:124`
- `Gitster/ViewModels/CommitPanelViewModel.cs:183`
- `Gitster/ViewModels/MainWindowViewModel.cs:308`
- `Gitster/ViewModels/MainWindowViewModel.cs:321`
- `Gitster/ViewModels/MainWindowViewModel.cs:364`
- `Gitster/ViewModels/MainWindowViewModel.cs:388`
- `Gitster/ViewModels/MainWindowViewModel.cs:417`

## `MessageBox.Show(...)` baseline

`MessageBox.Show(...)` appears in many ViewModels and several dialog code-behind files. The largest concentration is currently in:

- `Gitster/ViewModels/MainWindowViewModel.cs`
- `Gitster/ViewModels/QuickActionsViewModel.cs`
- `Gitster/ViewModels/BranchesViewModel.cs`
- `Gitster/ViewModels/StashesViewModel.cs`
- `Gitster/ViewModels/WorktreesViewModel.cs`
- `Gitster/ViewModels/UndoBarViewModel.cs`
- `Gitster/ViewModels/SearchViewModel.cs`
- `Gitster/ViewModels/CommitPanelViewModel.cs`

This file is the reference baseline for Step 2 migration tracking.
