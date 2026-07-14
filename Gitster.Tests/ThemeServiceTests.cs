using System.IO;
using System.Windows;

using Gitster.Services;
using Gitster.ApplicationLayer;

namespace Gitster.Tests;

[STATestClass]
[DoNotParallelize]
public sealed class ThemeServiceTests
{
    [STATestMethod]
    public async Task ApplySystemPreferenceChange_OffUiThread_QueuesDispatcherApply()
    {
        var app = EnsureApplication();
        var service = new ThemeService(CreatePreferences());

        var operation = await Task.Run(() => service.ApplySystemPreferenceChange());

        Assert.IsNotNull(operation);
        Assert.AreSame(app.Dispatcher, operation!.Dispatcher);
        operation.Abort();
    }

    private static Application EnsureApplication()
    {
        if (Application.Current is null)
            _ = new Application();

        return Application.Current!;
    }

    private static UiPreferencesService CreatePreferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new UiPreferencesService(Path.Combine(dir, "ui-settings.json"));
    }
}
