using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Gitster.Controls;

/// <summary>
/// Wraps the content of high-volume row templates (commit rows, diff lines, file tree nodes) and ends the
/// UI Automation tree there. While any automation client is listening (accessibility tools, input helpers,
/// password managers…), WPF otherwise re-walks every TextBlock of every realized row after each layout
/// pass, which made selection changes stall for 100 ms and more.
/// </summary>
public sealed class AutomationLeaf : Decorator
{
    protected override AutomationPeer OnCreateAutomationPeer() => new LeafAutomationPeer(this);

    private sealed class LeafAutomationPeer(AutomationLeaf owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override bool IsControlElementCore() => false;
    }
}
