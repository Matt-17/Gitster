# Remote Operations Documentation

## Overview

Gitster now includes full support for remote repository operations including Fetch, Pull, Push, and Sync functionality.

## Features

### Remote Selection
- A dropdown (ComboBox) displays all configured remotes for the current repository
- The first remote is automatically selected when a repository is opened
- The selected remote is used for all operations

### Operations

#### Fetch
- Downloads objects and refs from the selected remote
- Does not modify your working directory
- Updates remote-tracking branches
- **Use case**: Check what changes are available without merging them

#### Pull
- Fetches from the selected remote and merges changes into the current branch
- Updates your working directory with remote changes
- Uses the configured signature from git config
- **Use case**: Get the latest changes from remote and merge them locally

#### Push
- Uploads your local changes to the selected remote
- Pushes the current branch
- **Use case**: Share your committed changes with the remote repository

#### Sync
- Performs a complete synchronization with the remote
- Operations performed in order:
  1. Fetch from remote
  2. Pull (fetch + merge)
  3. Push to remote
- **Use case**: Ensure your local and remote repositories are fully synchronized

## UI Layout

All remote operation buttons are located at the bottom right of the main window:

```
[Fetch] [Pull] [Push] [Sync] [Remote Dropdown ▼] [Amend]
```

## Requirements

- All operations require a valid git repository to be open
- Buttons are enabled only when a repository is successfully loaded
- Operations use the remote selected in the dropdown
- If no remote is selected, the first available remote is used

## Error Handling

- All operations display user-friendly error messages via MessageBox
- Common errors include:
  - No remote configured
  - Authentication failures
  - Network connectivity issues
  - Merge conflicts (for Pull/Sync)

## Notes

- Fetch is a safe operation that never changes your working directory
- Pull and Sync can modify your working directory and may result in merge conflicts
- Push requires appropriate permissions on the remote repository
- The UI refreshes automatically after successful Fetch, Pull, and Sync operations
