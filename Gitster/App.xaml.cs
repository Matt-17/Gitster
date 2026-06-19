using System.Windows;
using Gitster.Services;
using Gitster.Services.Capabilities;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;
using Gitster.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gitster;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	private IHost? _host;

	public static IServiceProvider Services =>
		((App)Current)._host?.Services
		?? throw new InvalidOperationException("Host is not initialized.");

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		_host = Host.CreateDefaultBuilder(e.Args)
			.ConfigureLogging(logging =>
			{
				logging.ClearProviders();
				logging.AddSimpleConsole(options =>
				{
					options.SingleLine = true;
					options.TimestampFormat = "HH:mm:ss ";
				});
				logging.AddDebug();
			})
			.ConfigureServices((_, services) =>
			{
				services.AddSingleton<IWindowService, WindowService>();
				services.AddSingleton<AppSettingsService>();
				services.AddSingleton<IGitBackend, HybridGitBackend>();
				services.AddSingleton<RepositoryStateService>();
				services.AddSingleton<OperationFeedbackService>();
				services.AddSingleton<RecentReposService>();
				services.AddSingleton<AuthorDirectoryService>();
				services.AddSingleton<AutoFetchService>();
				services.AddSingleton<CapabilityService>();
				services.AddSingleton<OperationsLogService>();
				services.AddSingleton<SnapshotService>();
				services.AddSingleton<StashNameService>();
				services.AddSingleton<CustomToolsService>();
				services.AddSingleton<UiPreferencesService>();
				services.AddSingleton<StatusBarViewModel>();
				services.AddSingleton<CommitListViewModel>();
				services.AddSingleton<UndoBarViewModel>();
				services.AddSingleton<AuthorPanelViewModel>();

				services.AddSingleton<MainWindowViewModel>();
				services.AddSingleton<MainWindow>();
			})
			.Build();

		await _host.StartAsync();

		var logger = _host.Services.GetRequiredService<ILogger<App>>();
		logger.LogInformation("Gitster host started");

		var mainWindow = _host.Services.GetRequiredService<MainWindow>();
		MainWindow = mainWindow;
		mainWindow.Show();
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		if (_host is not null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}

		base.OnExit(e);
	}
}

