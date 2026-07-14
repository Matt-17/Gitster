using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using Gitster.Core.Models;

using Microsoft.Win32;

using Gitster.Core;

namespace Gitster.Services;

public sealed class ThemeService : IDisposable
{
    private const string LightPalette = "Themes/Palette.Light.xaml";
    private const string DarkPalette = "Themes/Palette.Dark.xaml";

    private readonly UiPreferencesService _preferences;
    private bool _started;

    public ThemeService(UiPreferencesService preferences)
    {
        _preferences = preferences;
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _preferences.PropertyChanged += OnPreferencesChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    public void Apply()
    {
        var app = Application.Current;
        if (app is null)
            return;

        Apply(app);
    }

    internal DispatcherOperation? ApplySystemPreferenceChange()
    {
        if (_preferences.ThemePreference != ThemePreference.System)
            return null;

        var app = Application.Current;
        if (app is null)
            return null;

        if (app.Dispatcher.CheckAccess())
        {
            Apply(app);
            return null;
        }

        return app.Dispatcher.BeginInvoke(new Action(() => Apply(app)));
    }

    private void Apply(Application app)
    {
        var effectiveTheme = ResolveEffectiveTheme(_preferences.ThemePreference, IsSystemLightTheme);
        var palette = effectiveTheme == ThemePreference.Dark ? DarkPalette : LightPalette;
        ReplacePalette(app.Resources.MergedDictionaries, palette);
    }

    public static ThemePreference ResolveEffectiveTheme(
        ThemePreference requested,
        Func<bool> isSystemLightTheme) =>
        requested switch
        {
            ThemePreference.Dark => ThemePreference.Dark,
            ThemePreference.Light => ThemePreference.Light,
            _ => isSystemLightTheme() ? ThemePreference.Light : ThemePreference.Dark,
        };

    public void Dispose()
    {
        if (!_started)
            return;

        _preferences.PropertyChanged -= OnPreferencesChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _started = false;
    }

    private static void ReplacePalette(Collection<ResourceDictionary> dictionaries, string palette)
    {
        var source = new Uri(palette, UriKind.Relative);
        var replacement = new ResourceDictionary { Source = source };

        for (var i = 0; i < dictionaries.Count; i++)
        {
            var existing = dictionaries[i].Source?.OriginalString;
            if (existing is not null
                && existing.Contains("Palette.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries[i] = replacement;
                return;
            }
        }

        dictionaries.Insert(0, replacement);
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) != 0;
        }
        catch
        {
            return true;
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiPreferencesService.ThemePreference))
            Apply();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        ApplySystemPreferenceChange();
    }
}
