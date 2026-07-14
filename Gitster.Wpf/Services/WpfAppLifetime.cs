using System.Windows;

using Gitster.ApplicationLayer.Ui;

namespace Gitster.Services;

/// <summary>WPF implementation of <see cref="IAppLifetime"/>.</summary>
public sealed class WpfAppLifetime : IAppLifetime
{
    public void Shutdown() => Application.Current?.Shutdown();
}
