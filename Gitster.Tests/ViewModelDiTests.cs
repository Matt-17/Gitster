using Gitster.Services;
using Gitster.Services.Capabilities;
using Gitster.Services.Features;
using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.Services.OperationsLog;
using Gitster.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Gitster.Tests;

[TestClass]
public sealed class ViewModelDiTests
{
    [TestMethod]
    public void ChildViewModels_ResolveFromContainerAsSingletons()
    {
        var services = new ServiceCollection();
        RegisterViewModelGraph(services);
        using var provider = services.BuildServiceProvider();

        var childTypes = new[]
        {
            typeof(StatusBarViewModel),
            typeof(TitleBarViewModel),
            typeof(CommitListViewModel),
            typeof(TimestampEditViewModel),
            typeof(HistoryRewriteDraftViewModel),
            typeof(QuickActionsViewModel),
            typeof(UndoBarViewModel),
            typeof(AuthorPanelViewModel),
            typeof(SidebarViewModel),
            typeof(StashesViewModel),
            typeof(BranchesViewModel),
            typeof(WorktreesViewModel),
            typeof(CommitPanelViewModel),
            typeof(SearchViewModel),
        };

        foreach (var type in childTypes)
        {
            var first = provider.GetRequiredService(type);
            var second = provider.GetRequiredService(type);
            Assert.AreSame(first, second, $"{type.Name} should be registered as a singleton.");
        }
    }

    [TestMethod]
    public void MainWindowViewModel_PublicConstructor_StaysSlim()
    {
        var publicCtor = typeof(MainWindowViewModel).GetConstructors().Single();

        Assert.IsTrue(publicCtor.GetParameters().Length <= 8);
    }

    private static void RegisterViewModelGraph(ServiceCollection services)
    {
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IGitBackend, HybridGitBackend>();
        services.AddSingleton<RepositoryStateService>();
        services.AddSingleton<OperationFeedbackService>();
        services.AddSingleton<RecentReposService>();
        services.AddSingleton<BranchFavoritesService>();
        services.AddSingleton<CommitHistoryService>();
        services.AddSingleton<AuthorDirectoryService>();
        services.AddSingleton<AutoFetchService>();
        services.AddSingleton<CapabilityService>();
        services.AddSingleton<OperationsLogService>();
        services.AddSingleton<SnapshotService>();
        services.AddSingleton<SourceArchiveService>();
        services.AddSingleton<StashNameService>();
        services.AddSingleton<CustomToolsService>();
        services.AddSingleton<ICustomToolsService>(sp => sp.GetRequiredService<CustomToolsService>());
        services.AddSingleton<HeadRefreshCoordinator>();
        services.AddSingleton<RepositorySwitchCoordinator>();
        services.AddSingleton<RepositoryLifecycleCoordinator>();
        services.AddSingleton<RepositoryCommandContext>();
        services.AddSingleton<CommitSelectionCoordinator>();
        services.AddSingleton<CustomToolRunner>();
        services.AddSingleton<UiPreferencesService>();
        services.AddSingleton<GitFeatureService>();
        services.AddSingleton<GitIgnoreTemplateService>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<ISelectionContext, SelectionContext>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<TitleBarViewModel>();
        services.AddSingleton<CommitListViewModel>();
        services.AddSingleton<TimestampEditViewModel>();
        services.AddSingleton<HistoryRewriteDraftViewModel>();
        services.AddSingleton<QuickActionsViewModel>();
        services.AddSingleton<UndoBarViewModel>();
        services.AddSingleton<AuthorPanelViewModel>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<StashesViewModel>();
        services.AddSingleton<BranchesViewModel>();
        services.AddSingleton<WorktreesViewModel>();
        services.AddSingleton<CommitPanelViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<MainWindowServices>();
        services.AddSingleton<MainWindowCoordinators>();
        services.AddSingleton<MainWindowChildViewModels>();
        services.AddSingleton<MainWindowViewModel>();
    }
}
