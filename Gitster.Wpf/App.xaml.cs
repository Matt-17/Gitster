using System.Windows;
using System.IO;
using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Capabilities;
using Gitster.Core.Git;
using Gitster.Services.Features;
using Gitster.Core.Features;
using Gitster.Core.History;
using Gitster.Core.Logging;
using Gitster.Services.OperationsLog;
using Gitster.Views;
using Gitster.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Gitster.ApplicationLayer.Ui;

namespace Gitster;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	private IHost? _host;
	private ILogger<App>? _logger;

	public static IServiceProvider Services =>
		((App)Current)._host?.Services
		?? throw new InvalidOperationException("Host is not initialized.");

	protected override async void OnStartup(StartupEventArgs e)
	{
		try
		{
			base.OnStartup(e);
			RegisterGlobalExceptionHandlers();

			var persistentLoggingEnabled = new AppSettingsService().LoadUiSettings().PersistentLoggingEnabled;
			_host = Host.CreateDefaultBuilder(e.Args)
				.ConfigureLogging(logging =>
				{
					logging.ClearProviders();
					if (persistentLoggingEnabled)
						logging.AddProvider(new RollingFileLoggerProvider(GetLogDirectory()));
					logging.AddSimpleConsole(options =>
					{
						options.SingleLine = true;
						options.TimestampFormat = "HH:mm:ss ";
					});
					logging.AddDebug();
				})
				.ConfigureServices((_, services) =>
				{
					services.AddSingleton<WindowService>();
					services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<WindowService>());
					services.AddSingleton<IUserInteraction>(sp => sp.GetRequiredService<WindowService>());
					services.AddSingleton<IDispatcher, WpfDispatcher>();
					services.AddSingleton<IClipboard, WpfClipboard>();
					services.AddSingleton<IAppLifetime, WpfAppLifetime>();
					services.AddSingleton<AppSettingsService>();
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
					services.AddSingleton<ThemeService>();
					services.AddSingleton<GitCliTelemetryService>();
					services.AddSingleton<GitFeatureService>();
					services.AddSingleton<GitIgnoreTemplateService>();
					services.AddSingleton<UpdateCheckService>();
					services.AddSingleton<ISelectionContext, SelectionContext>();
					services.AddSingleton<StatusBarViewModel>();
					services.AddSingleton<TitleBarViewModel>();
					services.AddSingleton<CommitListViewModel>();
					services.AddSingleton<CommitRefNavigatorViewModel>();
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
					services.AddSingleton<MainWindow>();
				})
				.Build();

			await _host.StartAsync();

			_logger = _host.Services.GetRequiredService<ILogger<App>>();
			_logger.LogInformation("Gitster host started");
			_host.Services.GetRequiredService<ThemeService>().Start();
			if (_host.Services.GetRequiredService<UiPreferencesService>().PersistentLoggingEnabled)
				_host.Services.GetRequiredService<GitCliTelemetryService>().Start();

			var mainWindow = _host.Services.GetRequiredService<MainWindow>();
			MainWindow = mainWindow;
			mainWindow.Show();
		}
		catch (Exception ex)
		{
			_logger?.LogCritical(ex, "Gitster failed during startup");
			GitsterDialog.Show(
				null,
				$"Gitster could not start.\n\n{ex.Message}",
				"Gitster startup failed",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
			Shutdown(-1);
		}
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		try
		{
			if (_host is not null)
			{
				_host.Services.GetService<ThemeService>()?.Dispose();
				_host.Services.GetService<GitCliTelemetryService>()?.Dispose();
				await _host.StopAsync();
				_host.Dispose();
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Gitster failed during shutdown");
		}

		base.OnExit(e);
	}

	private void RegisterGlobalExceptionHandlers()
	{
		DispatcherUnhandledException += (_, args) =>
		{
			_logger?.LogCritical(args.Exception, "Unhandled UI exception");
			var result = GitsterDialog.Show(
				MainWindow,
				$"Gitster hit an unexpected problem.\n\n{args.Exception.Message}\n\nContinue running Gitster?",
				"Unexpected problem",
				MessageBoxButton.YesNo,
				MessageBoxImage.Error);
			args.Handled = result == MessageBoxResult.Yes;
			if (!args.Handled)
				Shutdown(-1);
		};

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			_logger?.LogError(args.Exception, "Unobserved task exception");
			args.SetObserved();
		};

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex)
				_logger?.LogCritical(ex, "Unhandled app-domain exception");
			else
				_logger?.LogCritical("Unhandled app-domain exception: {ExceptionObject}", args.ExceptionObject);
		};
	}

	private static string GetLogDirectory() =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Gitster",
			"logs");
}
