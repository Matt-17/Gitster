namespace Gitster.Core.Ui;

/// <summary>Host-level application lifetime actions (e.g. File ▸ Exit), abstracted away from WPF.</summary>
public interface IAppLifetime
{
    void Shutdown();
}
