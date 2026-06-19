# Gitster Plan: Central Window Service (inspired by RepoTest)

## 1) What RepoTest does well (pattern to copy)

### A. Single entry point for dialogs/windows
- `IWindowService` is the only API used by ViewModels and services for showing dialogs, notifications, and helper windows.
- The concrete implementation (`DxWindowService`) creates the window once and assigns:
	- `Title`
	- `DataContext`
	- `Owner`
	- modal vs non-modal behavior

### B. Owner is configured once at startup
- App startup calls a single bootstrap hook to set owner-aware services (`UseWindowServices(MainWindow)`), instead of setting owners all over the app.
- Result: all dialogs are centered relative to main window and z-order behavior is consistent.

### C. Dialog ViewModel controls close result
- Dialog ViewModels expose `RequestCloseDialog` via `IDialogResultVmHelper`.
- Shared dialog host windows (`DialogWindow`, `ProgressWindow`, `WaitForTaskWindow`) subscribe once and map the event to `DialogResult`/`Close`.
- Dialog logic (OK/Cancel/validation) lives in ViewModel base (`DialogBaseViewModel`), not code-behind.

### D. Shared dialog chrome + ViewModel-to-View mapping
- A generic host window contains a `ContentPresenter` bound to current `DataContext`.
- DataTemplates are registered centrally at startup (`Bootstrapper.SetTemplateMappings`).
- Mapping uses:
	- explicit attribute `[ViewModel(typeof(...))]`
	- fallback convention (`ViewName` -> `ViewNameModel`)

### E. Testability is improved
- Tests substitute `IWindowService` and assert calls.
- Dialog ViewModels inherit a base test helper (`DialogViewModelTestBase<T>`) and can be validated without opening UI.

---

## 2) Current Gitster situation (why to change)

Gitster currently opens windows directly from many ViewModels and views (`new ...Dialog(...).ShowDialog()` with manual owner handling each time). This causes:
- duplicated window creation logic
- inconsistent owner and placement behavior
- hard-to-test UI flows
- friction to standardize confirmation/input/success patterns

Observed hotspots include `MainWindowViewModel`, `QuickActionsViewModel`, `BranchesViewModel`, `StashesViewModel`, `SearchViewModel`, `AuthorPanelViewModel`, `UndoBarViewModel`, `WorktreesViewModel`, and multiple view code-behind files.

---

## 3) Target architecture for Gitster

## A. Introduce a central service
- Add `IWindowService` in Gitster with minimal first interface:
	- `bool? ShowDialog(Window dialog)` (adapter for existing dialogs)
	- `bool Confirm(string title, string text)`
	- `void Info(string title, string text)`
	- `void Error(string title, string text)`
	- optional `Task<bool?> ShowDialogAsync(...)` later

## B. Owner resolution strategy
- Service sets owner automatically:
	- first choice: `Application.Current.MainWindow`
	- fallback: active window if needed
- No ViewModel should set `Owner` directly after migration.

## C. Two-level migration model
- Level 1 (quick): keep existing dialog windows, but open them only through `IWindowService`.
- Level 2 (full): introduce ViewModel-first dialogs with a shared host and close-event contract, similar to RepoTest.

## D. Optional full parity components (phase 2)
- `IDialogResultVmHelper` equivalent for Gitster
- `DialogBaseViewModel` with `OkCommand`, `CancelCommand`, validation hooks
- `DialogHostWindow` with `ContentPresenter`
- central VM-to-View template registration (attribute + convention)

---

## 4) Implementation plan (incremental, low risk)

## Step 0: Baseline and guardrails
- Record all `ShowDialog` and `new ...Window/Dialog` call sites.
- Add a small architecture rule: no new direct dialog creation in ViewModels.

## Step 1: Introduce `IWindowService` and concrete implementation
- Add service in `Gitster/Services`.
- Implement auto-owner handling and wrappers for `MessageBox` and existing dialogs.
- Wire service into `MainWindowViewModel` construction.

## Step 2: Migrate high-traffic call sites first
- Move direct dialog creation from:
	- `MainWindowViewModel`
	- `QuickActionsViewModel`
	- `BranchesViewModel`
	- `StashesViewModel`
	- `SearchViewModel`
	- `AuthorPanelViewModel`
- Keep dialog classes untouched in this step (only calling path changes).

## Step 3: Normalize common dialog contracts
- Add service methods for repeated patterns:
	- confirm yes/no
	- text input
	- operation result/error dialogs
- Replace ad-hoc `MessageBox.Show(...)` usage with service calls.

## Step 4: Add ViewModel-first dialog foundation (optional but recommended)
- Introduce a Gitster `DialogBaseViewModel` + close request event.
- Build one reusable `DialogHostWindow` shell.
- Map 1-2 existing dialogs first (pilot), such as simple input/confirm flows.

## Step 5: Template mapping/bootstrap (optional advanced)
- Add startup template registration (attribute + naming convention), mirroring RepoTest's clean mapping approach.
- Convert additional dialogs gradually after pilot stability.

## Step 6: Testing
- Unit tests:
	- mock `IWindowService` and verify expected calls/results
	- test command behavior without UI popups
- Integration/manual checks:
	- owner centering
	- modal behavior
	- keyboard defaults (Enter/Escape)
	- operation flows that currently open many dialogs

## Step 7: Cleanup and policy enforcement
- Remove direct `ShowDialog()` usages from ViewModels.
- Keep direct `ShowDialog` only in `IWindowService` implementation (and minimal app bootstrap code).
- Add simple lint guideline in repo docs.

---

## 5) Suggested execution order for Gitster

1. Implement Step 1 and Step 2 in one PR (fast value, minimal visual change).
2. Run through all existing dialog workflows and confirm no regressions.
3. Decide whether to stop at Level 1 or continue to Level 2 (shared host + dialog base VM).
4. If continuing, do Step 4/5 as separate PRs to keep review risk low.

---

## 6) Risks and mitigations

- Risk: owner assignment breaks for early startup dialogs.
	- Mitigation: null-safe owner fallback and explicit startup exceptions.
- Risk: behavior drift while migrating many call sites.
	- Mitigation: migrate per ViewModel with quick manual smoke test each batch.
- Risk: over-engineering too early.
	- Mitigation: do Level 1 first, prove value, then expand only if needed.

---

## 7) Definition of done

- All new dialogs are opened via `IWindowService`.
- Existing major dialog flows no longer instantiate windows directly in ViewModels.
- Owner behavior is consistent and centralized.
- At least one test path validates dialog-trigger logic with a mocked window service.
- Optional full-doD: shared dialog host and base ViewModel pattern is in use for new dialogs.

---

## Execution status (2026-05-31)

- Step 0: completed
	- Baseline inventory created in `window-service-step0-baseline.md`.
	- Baseline includes counts and line-level call sites for `ShowDialog`, direct `new Views.*` calls, and `MessageBox.Show`.
- Step 1: completed
	- Added `IWindowService` and `WindowService`.
	- Wired service at composition root (`MainWindow`), with owner configured once.
	- `MainWindowViewModel` now accepts an `IWindowService` dependency.

- Host pattern: completed
	- Switched startup to .NET Generic Host (`Microsoft.Extensions.Hosting`) in `App.xaml.cs`.
	- Added DI registrations for `IWindowService`, `AppSettingsService`, `IGitBackend`, the core singleton services used by the shell, `MainWindowViewModel`, and `MainWindow`.
	- `MainWindow` now uses constructor injection instead of constructing services/viewmodels directly.
	- `MainWindowViewModel` now consumes host-provided backend/services instead of constructing the core service graph internally.
	- DI-friendly child viewmodels (`StatusBarViewModel`, `CommitListViewModel`, `UndoBarViewModel`, `AuthorPanelViewModel`) are now host-registered and injected into `MainWindowViewModel`.
	- Added console and debug logging providers for easier diagnostics while running from Visual Studio.

- Step 2: in progress (batch 1 completed)
	- Migrated `MainWindowViewModel` window/message flows to `IWindowService` for major dialog entry points and user notifications.
	- Migrated `AuthorPanelViewModel` to injected `IWindowService` and removed direct owner/show calls.
	- `MainWindowViewModel` now passes the same `IWindowService` instance into `AuthorPanelViewModel`.

- Step 2: batch 2 completed
	- Migrated `QuickActionsViewModel` to injected `IWindowService` for all dialog opening, confirmations, warnings, and errors.
	- Migrated `BranchesViewModel` to injected `IWindowService` for stash-checkout prompt, rename/create dialogs, delete confirmations, and failure/info notifications.
	- `MainWindowViewModel` now passes the shared `IWindowService` into `QuickActionsViewModel` and `BranchesViewModel`.

- Step 2: batch 3 completed
	- Migrated `StashesViewModel` to injected `IWindowService` for new/rename/convert dialogs, confirmations, and warning/info messages.
	- Migrated `WorktreesViewModel` to injected `IWindowService` for add dialog, remove/prune confirmations, and warning messages.
	- Migrated `UndoBarViewModel` to injected `IWindowService` for undo confirmation dialog, info/error messages, and operations-log dialog.
	- `MainWindowViewModel` now passes the shared `IWindowService` into `StashesViewModel`, `WorktreesViewModel`, and `UndoBarViewModel`.

- Step 2: batch 4 completed
	- Migrated `SearchViewModel` to injected `IWindowService` for warnings and informational prompts.
	- Migrated `CommitPanelViewModel` to injected `IWindowService` for author dialog and stage/commit/stash warnings.
	- Migrated `ManageToolsViewModel` to injected `IWindowService` for validation warnings, delete confirmation, and save errors.
	- Extended `IWindowService` to cover `OpenFileDialog` and `OpenFolderDialog`, and migrated the remaining ViewModel/common-dialog call sites.
	- Migrated `AuthorRepairViewModel` to injected `IWindowService` and removed the last direct `MessageBox.Show(...)` in `Gitster/ViewModels`.
	- `MainWindowViewModel` now passes the shared `IWindowService` into `SearchViewModel`, `CommitPanelViewModel`, and `ManageToolsViewModel`.
