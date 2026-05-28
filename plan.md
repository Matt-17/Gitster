# Gitster – Phase 2b Plan: Stash-Killer & Fixup-Workflow

You are implementing Phase 2b of **Gitster**, a WPF Git surgery tool. Phase 2a (mode sidebar) and Phase 1 (safety net, indicators, combined amend) are complete. The mode sidebar already has a **Stashes** placeholder mode and the action column is mode-aware.

UI language: **English**. The project uses CommunityToolkit.Mvvm, LibGit2Sharp, a custom theme (`Themes/Gitster.xaml`), an `IGitBackend` abstraction, a `Capability` attached-property system, and an `OperationsLogService` with snapshot capture.

## Key architectural decision for this phase

Phase 2b splits cleanly into two halves with different backend requirements:

- **Stash-Killer (Steps A–D)** runs entirely on **LibGit2Sharp**. No Git CLI needed. Ship this first — it's the universal, high-impact feature.
- **Fixup workflow (Steps E–H)** requires **interactive rebase / autosquash**, which LibGit2Sharp cannot do. This is where the second `IGitBackend` implementation — `GitCliBackend` — gets built for real. Features here are gated behind the `FixupAutosquash` capability and disabled (with the existing Capability adorner) when Git CLI isn't available.

Work the stash half completely before starting the CLI half. The stash half is shippable on its own.

---

## STASH-KILLER HALF (LibGit2Sharp)

### Step A — Backend: stash model and operations

Extend `IGitBackend` with stash operations. All implementable in `LibGit2Backend` via `repo.Stashes`.

```csharp
// IGitBackend additions
Task<IReadOnlyList<StashInfo>> GetStashesAsync();
Task<StashDiff> GetStashDiffAsync(int stashIndex);
Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true);
Task PopStashAsync(int stashIndex, bool reinstateIndex = true);
Task DropStashAsync(int stashIndex);
Task<string> CreateStashAsync(string message, bool includeUntracked = true);
Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName);
```

**StashInfo model:**

```csharp
public record StashInfo(
    int Index,                       // stash@{N}
    string RawMessage,               // git's own message "WIP on branch: sha subject"
    string BranchName,               // branch the stash was created on (parsed from raw message)
    DateTimeOffset CreatedAt,
    IReadOnlyList<StashFileChange> Files,  // for auto-naming and preview
    string AutoName);                // derived display name, see Step B

public record StashFileChange(string Path, ChangeKind Kind, int Added, int Removed);
public enum ChangeKind { Added, Modified, Deleted, Renamed }
```

**Parsing the branch from the stash:** LibGit2Sharp's `Stash.Message` is git's raw message like `"WIP on master: 5204ea4 Cleanup and Design"` or `"On feature/oauth: ..."`. Parse the branch name out of it. The created-at timestamp comes from the stash commit's committer date.

**ConvertStashToBranchAsync:** this is the signature feature. Git's native equivalent is `git stash branch <name> stash@{N}`, which: creates a new branch from the commit where the stash was made, checks it out, applies the stash, and drops the stash if applied cleanly. LibGit2Sharp doesn't have a single call for this — implement it as: create branch at the stash's base commit, checkout, apply the stash, drop the stash on success. If apply produces conflicts, leave the branch created and the stash intact, and report the conflict.

Add `GitCapabilities.StashManagement` flag, set on `LibGit2Backend`. (Stashes don't need CLI, so this is always available with the libgit2 backend.)

### Step B — Auto-naming heuristic

The whole point of the Stash-Killer is that `stash@{0}` is meaningless. Generate a human name from the stash content.

**Algorithm:**
1. If the user gave the stash an explicit message (not git's auto "WIP on..."), use that verbatim.
2. Otherwise, derive from the file changes:
   - Take up to 3 file names (just the filename, not full path) sorted by change size descending.
   - Prefix with a verb guess based on the dominant change kind: mostly-added → "add", mostly-deleted → "remove", mixed → "wip".
   - Append "+N" if more than 3 files.
   - Include the most common top-level directory if it's informative.
   - Example outputs: `"wip: 3 files in src/auth · login.tsx, auth.ts, +1"`, `"add: NewService.cs"`, `"wip: 12 files"`.

Keep the auto-name generation in a small pure helper (`StashNamer`) so it's unit-testable.

### Step C — Stashes mode UI

Replace the `StashesModeView` placeholder with the real UI. Layout mirrors the Commits mode: main list + right action panel. (No diff bottom-panel needed — the diff preview goes in the action panel for stashes.)

**Main area:**

- Filter row at top: a search box that filters stashes by auto-name, file path, or branch. Plus a "New stash..." button on the right.
- A list/grid with columns: stash ref (`stash@{N}`, mono), auto-name (the derived description), branch (mono), age (relative).
- Selecting a stash drives the right action panel.

**Right action panel (mode-specific):**

- Selected-stash card: auto-name (prominent), then `stash@{N} · branch · age · +X −Y` meta line.
- **"Convert to branch"** as the primary (accent) button. Clicking prompts for a branch name (pre-filled with a slug derived from the auto-name, e.g. `wip-auth-login`), then runs `ConvertStashToBranchAsync`.
- Secondary buttons in a 2×2 grid: Apply, Pop, Rename, Drop (Drop in danger color).
- Diff preview block below: file list with `A`/`M`/`D` badges and `+X −Y` counts, plus an inline unified-diff preview of the first file (or the selected file).

**Empty state:** when there are no stashes, show a centered message "No stashes. Create one from your working tree to save changes without committing." with a "New stash..." button.

### Step D — Stash operations wiring

- **New stash:** dialog asking for an optional message and a checkbox "include untracked files" (default on). Calls `CreateStashAsync`.
- **Apply / Pop:** straightforward. After pop, the stash disappears from the list (live-watch + explicit refresh). After apply, it stays.
- **Rename:** git has no native stash rename. Implement it the way the ecosystem does: drop and recreate is destructive and changes the SHA, so instead store user-assigned names in `.git/gitster/stash-names.json` keyed by stash commit SHA (the stash's own commit hash, which is stable until dropped). The auto-name is the fallback; a user-assigned name overrides it. This keeps git's stash stack untouched.
- **Drop:** confirmation dialog (stashes can't be undone via reflog as easily — warn clearly). Record an OperationsLog entry so it appears in history.
- **Convert to branch:** as in Step A. On success, switch to Branches mode or show a toast "Created branch <name> from stash." On conflict, keep both and explain.
- Every operation that changes stash state updates the sidebar badge count (the Stashes icon badge) and refreshes the list.
- Snapshot capture fires before destructive stash operations (Drop, Pop, Convert) since they change repo state.

The stash badge on the sidebar already exists from Phase 2a — verify it updates after these operations.

---

## FIXUP-WORKFLOW HALF (requires Git CLI)

### Step E — GitCliBackend foundation

This is the first real use of the CLI backend. Build it as a focused implementation, not a full mirror of LibGit2Backend.

**Create `Services/Git/GitCliBackend.cs`** implementing the parts of `IGitBackend` that require CLI. Strategy: this is NOT a replacement for `LibGit2Backend`. Instead, introduce a **composite/router backend** so most calls go to libgit2 (fast, no process spawn) and only the rebase-class operations go to CLI.

**Recommended structure:**

```csharp
public class HybridGitBackend : IGitBackend
{
    private readonly LibGit2Backend _lib;
    private readonly GitCliBackend _cli;
    private readonly CapabilityService _caps;

    // Most methods delegate to _lib.
    // Rebase-class methods delegate to _cli, throwing a clear
    // "Requires Git CLI" exception if _cli.IsAvailable is false.

    public GitCapabilities Capabilities =>
        _lib.Capabilities | (_cli.IsAvailable
            ? GitCapabilities.FixupAutosquash | GitCapabilities.InteractiveRebase
            : GitCapabilities.None);
}
```

Register `HybridGitBackend` as the `IGitBackend` in DI instead of `LibGit2Backend` directly. Existing callers are unaffected — they still get an `IGitBackend`.

**CLI invocation helper:** a small `GitCli` runner that spawns `git` with given args in the repo working directory, captures stdout/stderr, returns exit code + output, with a timeout and proper cancellation. Set `GIT_EDITOR=true` and `GIT_SEQUENCE_EDITOR` appropriately so rebases don't try to open an interactive editor (see Step F).

**CLI detection:** the `CapabilityService` already detects `git --version` from Phase 1. Reuse it. `GitCliBackend.IsAvailable` reflects that.

### Step F — Fixup per click

The core feature: assign staged changes to any existing commit without typing SHAs.

**User flow:**
1. User has staged changes (the status bar shows "N staged").
2. User selects a target commit in the Commits-mode list.
3. A "Fixup into this commit" action becomes available (in the action panel, gated behind `Capability.Requires="FixupAutosquash"`).
4. Gitster runs the equivalent of: `git commit --fixup=<target-sha>` then `git -c sequence.editor=true rebase --autosquash --interactive <target-sha>^`.

**Implementation detail — avoiding the interactive editor:**
Autosquash normally opens an editor showing the rebase todo. To run it non-interactively, set the sequence editor to a no-op that accepts the pre-arranged todo:
- Run with environment `GIT_SEQUENCE_EDITOR=:` (the shell no-op) or `GIT_SEQUENCE_EDITOR=true`. On Windows, use `--exec`-free invocation with `-c sequence.editor=...`. The cleanest cross-platform approach: `git -c core.editor=true -c sequence.editor=true rebase --autosquash --interactive <base>`.
- Since `--autosquash` pre-orders the fixup commit correctly, the no-op editor just accepts the todo as-is.

**Safety:**
- If the target commit is synced (on remote), show the force-push warning (reuse the Phase-1 banner pattern) before proceeding.
- Record an OperationsLog entry (kind `Fixup`) with BeforeSha = pre-rebase HEAD, AfterSha = post-rebase HEAD. Undo works through the standard reset-to-BeforeSha path.
- Snapshot before the operation.
- If the rebase hits a conflict, abort it (`git rebase --abort`), restore to BeforeSha, and report: "Fixup produced a conflict and was rolled back. Resolve manually or fixup a more recent commit."

### Step G — Reword any commit

Change the message of any commit in history, not just HEAD.

**For HEAD:** simple amend (already possible via LibGit2 — no CLI needed). Detect this case and use the fast path.

**For older commits:** use the CLI autosquash reword mechanism:
- `git commit --allow-empty --fixup=reword:<target-sha>` then autosquash rebase, OR
- the more portable approach: create the rebase todo with the target line changed to `reword`, supply the new message via `GIT_EDITOR`.

The simplest robust implementation: a small dialog where the user edits the message, then Gitster performs `git -c core.editor='<script that writes the new message>' rebase --autosquash --interactive <target>^`. On Windows, write the new message to a temp file and set `GIT_EDITOR` to a command that copies that file into the commit-message file. Provide a tiny helper executable or use `git`'s ability to read from a file. (A clean trick: `git rebase` with `--exec` isn't right here; instead set `core.editor` to a `cmd /c copy` that overwrites the message file.)

Gate behind `FixupAutosquash` capability. HEAD reword stays available even without CLI (uses libgit2 amend).

Record OperationsLog entry (kind `Reword`). Same conflict-rollback and force-push-warning rules as fixup.

### Step H — Squash with date control, Cherry-pick with timestamp

**Squash with date control:**
- User multi-selects contiguous commits in the list.
- "Squash selected" action (gated `FixupAutosquash`).
- A dialog shows the combined result: lets the user pick which commit's date to keep (or set a custom date), and edit the combined message (pre-filled with all messages concatenated).
- Runs via autosquash rebase or `git reset --soft` + recommit, whichever is cleaner. For a contiguous selection ending at HEAD, `git reset --soft <base>` then a single commit with the chosen date/message is simplest and avoids rebase entirely — prefer this when the selection includes HEAD.
- For a selection not including HEAD, use the rebase path.

**Cherry-pick with timestamp:**
- In a context where the user picks a commit from another branch (this needs a source-branch picker — a modal that lists branches, then their commits).
- `git cherry-pick <sha>` (or libgit2's cherry-pick, which works for the no-conflict case), then immediately amend the resulting commit's date to the user-specified value (reuse the Phase-1 combined-amend path).
- Cherry-pick itself can use libgit2 (`repo.CherryPick`) for the conflict-free case; fall back to CLI on conflicts with a clear message. The timestamp override is a plain amend afterward — no CLI needed for that part.

Squash and cherry-pick both record OperationsLog entries and capture snapshots.

---

## Cross-cutting requirements

- **Every** mutating operation: snapshot before, OperationsLog entry after, `HeadChanged` event fired so the commit list refreshes.
- **Force-push warning** appears whenever an operation rewrites a synced commit.
- **Capability gating:** fixup, reword-of-older, squash-of-older all carry `Capability.Requires="FixupAutosquash"`. With no Git CLI, they're disabled with the adorner + tooltip "Requires Git command-line tool". HEAD-only reword and stash operations work without CLI.
- **Conflict handling is uniform:** on any rebase/cherry-pick conflict, abort + restore to BeforeSha + report a clear message. Gitster does NOT enter a half-finished rebase state in this phase (no conflict-resolution UI — that's explicitly out of scope per the vision).
- **Performance:** the commit list must stay responsive. CLI operations run async with the ring-spinner feedback in the status bar (reuse `OperationFeedbackService`).

---

## Acceptance criteria

Stash-Killer:
1. Stashes mode lists all stashes with auto-generated names, branch context, age.
2. Auto-naming produces readable names from file content; user-assigned names persist in `.git/gitster/stash-names.json` and override the auto-name.
3. Filter works over name, file path, branch.
4. Diff preview shows files with A/M/D badges and a unified-diff of the selected file.
5. Convert-to-branch creates a branch from the stash, applies it, drops the stash; conflicts leave both intact with a clear message.
6. Apply, Pop, Drop, New stash, Rename all work; sidebar badge updates; drops are confirmed and logged.
7. Stash operations need no Git CLI.

Fixup workflow:
8. `HybridGitBackend` routes rebase-class operations to CLI, everything else to libgit2; registered in DI as `IGitBackend`.
9. Fixup-into-commit works without typing SHAs, via non-interactive autosquash; conflicts roll back cleanly.
10. Reword works for any commit (HEAD via libgit2, older via CLI autosquash); gated by capability for the CLI path.
11. Squash with date control works (soft-reset path when selection includes HEAD, rebase path otherwise).
12. Cherry-pick with timestamp works (libgit2 cherry-pick + amend; CLI fallback on conflict).
13. All CLI-dependent features are disabled with the capability adorner when Git CLI is unavailable; non-CLI features remain usable.
14. Every operation snapshots, logs, warns on synced commits, and refreshes the list.

Build: 0 errors, 0 warnings. `VISION.md` updated — Phase 2b items marked.

---

## Out of scope for Phase 2b (do not implement)

- Conflict-resolution UI (Gitster aborts and rolls back on conflict; no merge editor — this is a permanent non-goal).
- Commit reordering, splitting (Phase 5).
- Full multi-branch graph.
- Branch management beyond what convert-to-branch and the cherry-pick source-picker need (Phase 3).

## Suggested commit boundaries

1. Stash backend + auto-namer (Steps A–B)
2. Stashes mode UI + operations (Steps C–D) — **shippable milestone**
3. HybridGitBackend + GitCliBackend foundation (Step E)
4. Fixup (Step F)
5. Reword anywhere (Step G)
6. Squash + cherry-pick (Step H)

Each boundary should build clean and leave the app usable.