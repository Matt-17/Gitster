# Gitster

A WPF application for Git repository management with a focus on commit manipulation and remote operations.

## Features

### Commit Management
- View commit history with message, date, and commit ID
- Filter commits by author name and time range
- Amend commit timestamps and dates
- Navigate through commit history
- Visual timeline with date/time sliders

### Remote Operations
- **Fetch**: Download changes from remote without merging
- **Pull**: Fetch and merge changes from remote
- **Push**: Upload local changes to remote
- **Sync**: Complete synchronization (fetch + pull + push)
- Remote selection via dropdown menu
- Support for multiple remotes

### User Interface
- Commit list view with selection
- Commit filtering with non-modal filter window
- Current and selected commit detail views
- Date and time manipulation controls
- Interactive sliders for date/time adjustment
- Remote operations toolbar

## Architecture

This project uses the MVVM (Model-View-ViewModel) pattern with:
- **CommunityToolkit.Mvvm** for observable properties and commands
- **LibGit2Sharp** for Git operations
- WPF for the user interface

For more details, see:
- [ARCHITECTURE.md](../ARCHITECTURE.md)
- [QUICK_REFERENCE.md](../QUICK_REFERENCE.md)
- [MVVM_CONVERSION.md](../MVVM_CONVERSION.md)
- [REMOTE_OPERATIONS.md](REMOTE_OPERATIONS.md)

## Requirements

- .NET 9.0
- Windows OS (WPF application)

## Usage

1. Open the application
2. Enter or select a Git repository path
3. View and manipulate commits
4. Use the Filter button to filter commits by author name or time range
5. Perform remote operations using the toolbar buttons

### Filtering Commits

The filter window allows you to filter the commit history by:
- **Author Name**: Select a specific author from the dropdown or "All" to show all authors
- **Time Range**: Set a "from" date and/or "to" date to filter commits within a date range
- Each filter has a clear button (✕) to remove that specific filter

Filter window buttons:
- **OK**: Apply filters and close the window
- **Apply**: Apply filters while keeping the window open
- **Cancel**: Close the window without applying changes

For detailed information about remote operations, see [REMOTE_OPERATIONS.md](REMOTE_OPERATIONS.md).