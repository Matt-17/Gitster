using Gitster.Services;
using Gitster.Services.Capabilities;
using Gitster.Services.Features;
using Gitster.Services.Git;
using Gitster.Services.History;
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
