# Gitster – Review pass: audit the Fixup-workflow CLI half (Phase 2b, Steps E–H)

You are reviewing code in **Gitster**, a WPF Git surgery tool. Phase 2b Steps E–H (the Git-CLI-dependent fixup workflow) were implemented in a previous session by a less thorough model. Your job is to **audit this implementation rigorously and fix what's wrong**, because the rebase-class operations are the highest-risk code in the entire project — a subtle bug here corrupts users' history.

This is a review-and-fix task, not a rewrite. Don't replace working code. Find real defects.

## Scope of review

The features under audit:
- `HybridGitBackend` (routes rebase-class ops to CLI, everything else to libgit2)
- `GitCliBackend` + the `GitCli` process runner
- Fixup-into-commit (non-interactive autosquash)
- Reword any commit (HEAD via libgit2, older via CLI)
- Squash with date control
- Cherry-pick with timestamp

## Audit checklist — verify each, fix if broken

### 1. The non-interactive editor trap

This is the single most common way to get autosquash rebase wrong. Verify:

- Does the rebase invocation actually prevent git from opening an interactive editor? Check that `GIT_SEQUENCE_EDITOR` (or `-c sequence.editor=...`) is set to a no-op, AND `GIT_EDITOR`/`core.editor` is also handled where a commit message editor could open.
- Test mentally: a fixup autosquash needs the *sequence* editor suppressed (for the todo list). A reword needs the *commit message* editor controlled (to supply the new message). These are two different editors. Confirm both are handled in their respective operations, not conflated.
- On Windows specifically: `:` (shell no-op) does not exist as a command. Verify the no-op works on Windows — `true` may not exist either depending on environment. The safe choice is often a `cmd /c exit 0` equivalent or git's `-c sequence.editor=true` only if `true.exe` is on PATH (it usually isn't on Windows). **This is a likely bug.** Check what was actually used and whether it works on Windows.

### 2. Conflict rollback integrity

The spec requires: on any conflict, abort and restore to BeforeSha, never leave a half-finished rebase. Verify:

- Is `git rebase --abort` actually called on conflict? Is its success checked?
- After abort, is the repo verified to be back at BeforeSha? An abort restores to the pre-rebase state, but if the implementation also did a `reset` the order matters.
- Is there any code path where a conflict leaves the repo mid-rebase (a `.git/rebase-merge` or `.git/rebase-apply` directory present)? If the app crashes or the user closes mid-operation, is there cleanup on next open? At minimum, on repo attach, detect an in-progress rebase state and surface it ("Repository has an unfinished rebase — Gitster does not resolve conflicts; run `git rebase --abort` or resolve in your terminal").
- Cherry-pick conflict: same questions. Is `git cherry-pick --abort` (or libgit2 equivalent) called and verified?

### 3. BeforeSha / AfterSha correctness for undo

The OperationsLog undo relies on accurate BeforeSha. Verify:

- Is BeforeSha captured **before** the operation starts, as the actual HEAD commit SHA (not a reflog index)?
- After a fixup/reword/squash, the rewritten commits have **new SHAs**. Is AfterSha the new HEAD? Is the OperationsLog entry's undo target the BeforeSha?
- Does undo of a fixup correctly restore? Test mentally: fixup commit X into Y, undo, do you get back exactly the pre-fixup HEAD with the staged changes restored? (Note: the staged changes that were fixup'd — are they lost or restored on undo? Document the behavior. Ideally undo restores the working state, but at minimum it must restore HEAD and not silently lose work.)

### 4. The HEAD-reword fast path

Spec: reword of HEAD uses libgit2 amend (no CLI), reword of older commits uses CLI. Verify:

- Is the HEAD case actually detected and routed to the fast path?
- Does HEAD reword work when Git CLI is NOT installed? (It must — it's a non-CLI operation.)
- Is the capability gating correct: HEAD reword available always, older-commit reword gated behind `FixupAutosquash`?

### 5. Squash path selection

Spec: selection-includes-HEAD uses `git reset --soft` + recommit; selection-not-including-HEAD uses rebase. Verify:

- Is the path selection logic correct? Does it correctly detect whether the selection includes HEAD?
- The soft-reset path: `git reset --soft <base>` then one commit. Is `<base>` the parent of the *oldest* selected commit? Off-by-one here squashes the wrong range.
- Is the chosen date actually applied to the resulting commit (both author and committer date as intended)?
- Is the combined message correct (all original messages available to the user, not silently dropped)?
- Are the selected commits required to be **contiguous**? Non-contiguous squash is ill-defined. Is that validated, with a clear message if the user selects a gap?

### 6. Cherry-pick + timestamp

Verify:
- Is the libgit2 cherry-pick path used for the conflict-free case, with CLI fallback only on conflict?
- After cherry-pick, is the timestamp override a separate amend (reusing the Phase-1 combined-amend), producing the user's chosen date?
- Does the resulting commit have a NEW sha (cherry-pick always creates a new commit)? Is that reflected in the OperationsLog?

### 7. HybridGitBackend routing

Verify:
- Are exactly the rebase-class operations (fixup, older-reward, rebase-path squash) routed to CLI, and everything else (status, log, amend-HEAD, stash, fetch/pull/push, cherry-pick-no-conflict) to libgit2?
- Is `Capabilities` computed correctly — `FixupAutosquash | InteractiveRebase` only added when `_cli.IsAvailable`?
- When CLI is unavailable, do the CLI-routed methods throw a clear, catchable exception (not a raw `Win32Exception` from a failed process spawn)?
- Is `HybridGitBackend` the registered `IGitBackend` in DI? Are there any lingering direct `LibGit2Backend` registrations that bypass it?

### 8. Process runner hygiene

Verify the `GitCli` runner:
- Sets the working directory to the repo path.
- Captures both stdout and stderr.
- Has a timeout (a hung git process must not freeze the app).
- Supports cancellation.
- Does not deadlock on large output (reading stdout and stderr must not block each other — use async reads or separate threads; the classic deadlock is reading stdout to end while stderr's buffer fills).
- Handles paths with spaces correctly (Windows paths like `D:\Development\My Repo`).
- Runs with `CreateNoWindow` so no console flashes.

### 9. Synced-commit force-push warning

Verify the warning fires for fixup/reword/squash on synced commits, consistent with the Phase-1 pattern. A rewrite of a pushed commit without warning is a serious UX safety failure.

### 10. Snapshot + log + refresh on every op

Verify each of the four operations: snapshots before, logs after, fires `HeadChanged`. A missing `HeadChanged` means the list shows stale state after the operation (the exact class of bug seen in Phase 1's undo).

## Deliverable

For each checklist item: state whether it's correct as-is, or describe the defect and fix it. Produce a short audit summary at the end listing what was wrong and what you changed. If everything in a section is correct, say so explicitly — don't pad.

Pay special attention to items 1 (Windows editor no-op) and 2 (conflict rollback), as those are the most likely to be subtly broken and the most damaging if they are.

Build: 0 errors, 0 warnings after fixes.

# Gitster – Phase 3 Plan: Branch Operations & Custom Tools

You are implementing Phase 3 of **Gitster**, a WPF Git surgery tool. Phases 1, 2a, and 2b are complete: safety net, mode sidebar, Stash-Killer, and the fixup workflow (with a HybridGitBackend that routes rebase-class operations to Git CLI). The Branches and Worktrees modes currently exist as placeholders in the sidebar.

UI language: **English**. Stack: CommunityToolkit.Mvvm, LibGit2Sharp + HybridGitBackend, custom theme, Capability attached-property system, OperationsLogService with snapshots.

## Goal of this phase

Eliminate context-switching between branches and make Gitster customizable per user. Five features:
- Commit to another branch without stash/switch
- Branch snapshot (named lightweight branch from current state)
- Worktrees as first-class
- Branch list sorted by activity
- Custom Tools menu

## What stays out of scope

- No conflict-resolution UI (permanent non-goal — abort and report on conflict).
- No commit reordering/splitting (Phase 5).
- No full multi-branch graph (backlog).

---

## Step A — Branches mode: the list

Replace the Branches placeholder with a real mode. This is the foundation the other branch features build on.

**Backend additions to `IGitBackend`:**

```csharp
Task<IReadOnlyList<BranchListItem>> GetBranchesAsync();
Task CheckoutBranchAsync(string branchName);
Task<string> CreateBranchAsync(string name, string startPointSha);
Task DeleteBranchAsync(string name, bool force);
Task RenameBranchAsync(string oldName, string newName);
```

**BranchListItem model:**

```csharp
public record BranchListItem(
    string Name,
    string? UpstreamName,        // tracking branch, null if none
    string TipSha,
    string TipMessage,
    DateTimeOffset LastActivity, // tip commit committer date — for sorting
    int Ahead,                   // commits ahead of upstream
    int Behind,                  // commits behind upstream
    bool IsCurrent,
    bool IsRemote,               // local vs remote-tracking branch
    bool IsMerged);              // merged into current branch (safe to delete)
```

**UI (Branches mode):**

- Two groups (fixed headers, like the commit-list sections): **Local** and **Remote**.
- **Sorted by `LastActivity` descending by default** — most recently active branch on top. This is the Phase-3 vision item "Branch-Liste nach Datum"; nobody wants `feature/xyz-old` alphabetically first. Offer an alternate alphabetical sort via column header.
- Columns: branch name (current branch in accent/bold with a marker), ahead/behind badges (`↑3 ↓1`), last-activity (relative), tip message (trimmed).
- The current branch is visually distinct (accent dot or bold + accent text).
- Filter box at top: filter by branch name substring.

**Action panel (right, mode-specific):**

- Selected-branch card: name, upstream, ahead/behind, last-activity.
- Primary action: **Checkout** (disabled if already current, or if working tree is dirty — see note).
- Secondary: Rename, Delete (Delete in danger color; disabled for current branch; warn if not merged), Create branch from here.
- **Dirty-working-tree handling on checkout:** if the working tree has uncommitted changes that would conflict, don't silently fail. Offer: "Stash changes and checkout" / "Cancel". (Reuse the stash backend from Phase 2b.) This ties the features together nicely.

**Sidebar badge:** optionally show the count of local branches, but low priority — skip if not trivial.

---

## Step B — Commit to another branch without switching

The signature Phase-3 feature: you're working on branch A, realize your staged changes belong on branch B, and you want them committed to B **without** stashing, switching, committing, switching back.

**This is subtle. Get the mechanics right.**

**User flow:**
1. User has staged (and/or unstaged) changes on the current branch.
2. User picks "Commit to another branch..." (from the Commits-mode action panel or a menu).
3. A dialog: target branch (existing dropdown or new-branch name), commit message, author/committer (reuse combined-amend author UI), and a choice of *which* changes (staged only / staged + unstaged).
4. Gitster commits those changes onto the **tip of the target branch**, advancing the target branch ref, while **leaving the current branch and working tree exactly as they were** (minus the changes that were moved, if the user chose to move rather than copy).

**Mechanics (the careful part):**

The clean way to do this without disturbing the working tree:
1. Capture the selected changes as a tree. With libgit2: build a tree from the index (for staged) or from index+worktree (for staged+unstaged) — effectively the tree you'd get if you committed right now.
2. Create a commit object with that tree, parented on the **target branch tip**, with the chosen message/author. This commit is created directly via the object database — no checkout, no index manipulation of the current branch.
3. Update the target branch ref to point at the new commit.
4. **Decide copy vs. move:**
   - **Copy** (default, safest): the current branch and working tree are untouched; the changes now also exist as a commit on the target branch. The user still has the changes locally to do with as they please.
   - **Move:** after creating the commit on the target, reset the current branch's index/worktree to remove the moved changes (e.g. `git restore --staged` + discard, or reset the index). This is more dangerous — only offer it with a clear checkbox "Remove these changes from the current branch" and a snapshot beforehand.

Default to **copy**. Moving is opt-in.

**Why this is better than stash-switch-commit-switch:** no checkout means no risk of the working tree being disturbed, no risk of a dirty-tree checkout failure, instant operation.

**Edge cases to handle:**
- Target branch is the current branch → degenerate; just a normal commit. Detect and simplify.
- Target branch is checked out in another worktree → committing to it is fine (ref update), but warn that the other worktree's HEAD will be behind.
- No changes selected → disable the action.
- Snapshot before, OperationsLog entry after (kind `CommitToBranch`), `HeadChanged` (and a refresh of the target branch's state).

This feature does NOT require Git CLI — it's all object-database work in libgit2. Keep it on the libgit2 path.

---

## Step C — Branch snapshot

A lightweight, named alternative to stashing: "save my current state as a branch I can come back to."

**User flow:**
1. "Snapshot to branch..." action.
2. Dialog: branch name (pre-filled with a timestamp-based suggestion like `snapshot/2026-05-28-1430`), and whether to include uncommitted changes.
3. Gitster creates a branch at the current HEAD. If "include uncommitted changes" is checked, it also creates a commit on that new branch capturing the working state (so the snapshot is complete), without disturbing the current working tree.

**Mechanics:** similar to Step B's tree-capture technique. Create the branch ref at HEAD; if including uncommitted changes, create a commit (tree from index+worktree) on the new branch with a message like "snapshot: uncommitted changes". The current branch and working tree stay exactly as they are.

**Difference from stash:** a branch snapshot is named, browsable in the Branches mode, survives clearly, and doesn't live in the opaque stash stack. This is the "benannter als Stash, leichtgewichtiger als Stash" vision item.

Snapshot + log + refresh as usual. No CLI needed.

---

## Step D — Worktrees as first-class

Worktrees let you have multiple working directories from one repo, each on a different branch — the *correct* answer to "I need to quickly work on another branch" but unusable today due to CLI friction.

**Capability:** worktree operations are most reliable via Git CLI. LibGit2Sharp's worktree support exists but is incomplete/version-dependent. Route worktree operations through the CLI backend and gate the mode's actions behind a new `GitCapabilities.Worktrees` flag (set when CLI is available). When CLI is unavailable, the Worktrees mode shows a clear "Requires Git command-line tool" state.

**Backend (CLI-based) additions:**

```csharp
Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync();       // git worktree list --porcelain
Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch);
Task RemoveWorktreeAsync(string path, bool force);
Task PruneWorktreesAsync();                                   // git worktree prune
```

**WorktreeInfo model:**

```csharp
public record WorktreeInfo(
    string Path,
    string BranchName,
    string HeadSha,
    bool IsMain,          // the primary worktree
    bool IsLocked,
    bool IsPrunable,      // directory missing → can be pruned
    bool IsCurrent);      // the worktree Gitster currently has open
```

Parse `git worktree list --porcelain` for reliable structured output.

**UI (Worktrees mode):**

- List of worktrees: path, branch, head sha, status badges (main / locked / prunable / current).
- Actions:
  - **Add worktree...** — dialog: directory path (with browse button), branch (existing or new). Runs `git worktree add`.
  - **Open in file explorer** — opens the worktree path.
  - **Open in Gitster** — switches Gitster to that worktree path (it's a valid repo working dir). This is the killer convenience: jump between worktrees within Gitster.
  - **Remove** — `git worktree remove` (force option if dirty, with warning).
  - **Prune** — `git worktree prune` for stale entries, with a preview of what will be pruned.
- Highlight the current worktree.

**Edge cases:**
- Adding a worktree for a branch already checked out elsewhere → git refuses; surface the error clearly.
- Removing the main worktree → not allowed; disable.
- Prunable worktrees (directory deleted manually) → offer prune.

---

## Step E — Custom Tools menu

The `git gui` feature nobody else copied: user-defined commands as menu items. This is what makes Gitster *yours*.

**Configuration model:**

Support two sources, merged (repo-specific overrides global):
1. **Git's native `[guitool "name"]` sections** in gitconfig — read these for compatibility with existing `git gui` users.
2. **Gitster's own format** in `%AppData%/Gitster/custom-tools.json` (global) and `.git/gitster/custom-tools.json` (repo-specific).

**CustomTool model:**

```csharp
public record CustomTool(
    string Name,              // menu label
    string Command,          // shell command, may contain placeholders
    string? Confirm,         // optional confirmation prompt text
    bool NeedsCommit,        // requires a selected commit (passes its sha)
    string? Prompt,          // optional: ask user for a value, substituted as $ARGS
    CustomToolScope Scope);  // Global or Repository

public enum CustomToolScope { Global, Repository }
```

**Placeholder substitution in the command:**
- `$REVISION` / `$CUR` → selected commit SHA (if `NeedsCommit`)
- `$ARGS` → value from the prompt dialog (if `Prompt` set)
- `$BRANCH` → current branch name
- `$REPO` → repository path

These mirror `git gui`'s guitool variables where sensible.

**UI:**
- A **Tools** menu (new top-level menu, or a section in an existing menu) lists all custom tools, repo-specific first then global, with a separator.
- Each tool runs its command via the GitCli runner (or a general shell runner — these may be arbitrary commands, not just git), in the repo working directory, with placeholders substituted.
- If `Confirm` is set, show the confirmation dialog first.
- If `Prompt` is set, show an input dialog and substitute `$ARGS`.
- Output: show stdout/stderr in a simple result dialog (or the status bar for quick commands). Non-zero exit shows an error.
- **A "Manage tools..." dialog** to add/edit/remove custom tools, choose scope (global vs this repo), and set the placeholders. This is how users without gitconfig-editing skills create tools.

**Safety:** custom tools run arbitrary shell commands — that's their purpose. Don't sandbox, but: show the exact command in the confirmation dialog when `Confirm` is set, and never auto-run anything on repo open. Tools only run on explicit user click.

**Example tools to ship as suggestions (not auto-installed, just offered as templates in the Manage dialog):**
- "Create feature branch" — prompt for name, run `git checkout -b feature/$ARGS develop`
- "Open commit on GitHub" — needs commit, run a browser-open to the remote URL + sha (best-effort URL construction)
- "Run tests" — `dotnet test` or similar

---

## Cross-cutting requirements

- Mutating operations (commit-to-branch, snapshot, worktree add/remove, branch create/delete/rename): snapshot before, OperationsLog entry after, `HeadChanged` / branch-list refresh.
- Capability gating: worktree actions behind `Worktrees` capability (CLI). Branch ops and commit-to-branch and snapshot are libgit2 — no gating.
- Custom tools that happen to invoke destructive git commands are the user's responsibility — but still snapshot before running any custom tool (cheap insurance), since Gitster can't know what a tool does.
- Force-push warnings still apply if any branch operation rewrites synced history (rare in this phase, but branch delete of an un-pushed branch warns about losing commits).

---

## Acceptance criteria

1. Branches mode lists local + remote branches, sorted by last activity, with ahead/behind, current-branch marker, filter.
2. Checkout, create, rename, delete branches work; delete warns if unmerged; checkout offers stash-and-checkout on dirty tree.
3. Commit-to-another-branch works without disturbing the current working tree; copy is default, move is opt-in with a snapshot; degenerate same-branch case simplified.
4. Branch snapshot creates a named branch (optionally capturing uncommitted changes) without disturbing the working tree.
5. Worktrees mode lists worktrees with status, supports add/remove/prune/open-in-explorer/open-in-Gitster; gated behind Worktrees capability when no CLI.
6. Custom Tools menu reads `[guitool]` sections + Gitster's own JSON; repo overrides global; placeholders substitute correctly; Manage dialog adds/edits/removes tools with scope.
7. Every mutating op snapshots, logs, refreshes; custom tools snapshot before running.
8. Build: 0 errors, 0 warnings. VISION.md updated — Phase 3 items marked.

---

## Suggested commit boundaries

1. Branches mode list + basic ops (Step A) — shippable
2. Commit-to-another-branch (Step B) — the signature feature
3. Branch snapshot (Step C)
4. Worktrees mode (Step D)
5. Custom Tools menu + manage dialog (Step E)

Each boundary builds clean and leaves the app usable.

## Notes for the implementer

- Step B is the trickiest. The tree-capture-and-commit-without-checkout technique is the heart of it — get that right and tested before moving on. Verify the working tree is byte-for-byte unchanged after a copy-mode commit-to-branch.
- Reuse everything: the stash backend (for dirty-checkout), the combined-amend author UI (for commit-to-branch authoring), the OperationsLog and snapshot infrastructure, the Capability adorner.
- Worktrees and Custom Tools both lean on the GitCli runner from Phase 2b — reuse it, don't build a second process runner.