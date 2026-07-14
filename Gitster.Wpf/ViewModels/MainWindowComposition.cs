using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Capabilities;
using Gitster.Services.Features;
using Gitster.Core.Features;
using Gitster.Core.Git;
using Gitster.Core.History;
using Gitster.Services.OperationsLog;

namespace Gitster.ViewModels;

public sealed record MainWindowServices(
    IWindowService WindowService,
    IGitBackend GitBackend,
    RepositoryStateService StateService,
    OperationFeedbackService FeedbackService,
    RecentReposService RecentRepos,
    AuthorDirectoryService AuthorDirectoryService,
    CommitHistoryService HistoryService,
    AutoFetchService AutoFetch,
    CapabilityService CapabilityService,
    OperationsLogService OpsLogService,
    SnapshotService SnapshotService,
    SourceArchiveService SourceArchiveService,
    StashNameService StashNameService,
    CustomToolsService CustomToolsService,
    CustomToolRunner CustomToolRunner,
    GitIgnoreTemplateService GitIgnoreTemplates,
    UpdateCheckService UpdateCheckService,
    UiPreferencesService UiPreferences,
    AppSettingsService AppSettings,
    ISelectionContext SelectionContext);

public sealed record MainWindowCoordinators(
    HeadRefreshCoordinator HeadRefresh,
    RepositoryLifecycleCoordinator RepositoryLifecycle,
    RepositoryCommandContext RepositoryCommands,
    CommitSelectionCoordinator SelectionCoordinator);

public sealed record MainWindowChildViewModels(
    StatusBarViewModel StatusBar,
    TitleBarViewModel TitleBar,
    CommitListViewModel CommitList,
    CommitRefNavigatorViewModel CommitRefNavigator,
    TimestampEditViewModel TimestampEdit,
    HistoryRewriteDraftViewModel HistoryRewriteDraft,
    QuickActionsViewModel QuickActions,
    UndoBarViewModel UndoBar,
    AuthorPanelViewModel AuthorPanel,
    SidebarViewModel Sidebar,
    StashesViewModel Stashes,
    BranchesViewModel Branches,
    WorktreesViewModel Worktrees,
    CommitPanelViewModel CommitPanel,
    SearchViewModel Search);
