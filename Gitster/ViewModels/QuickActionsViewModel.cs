using System.Collections.Generic;

using Gitster.Models;

namespace Gitster.ViewModels;

public class QuickActionsViewModel : BaseViewModel
{
    public IReadOnlyList<QuickAction> Actions { get; } =
    [
        new QuickAction("✏",  "Reword",       false),
        new QuickAction("🍒", "Cherry-pick",  false),
        new QuickAction("👤", "Author",       false),
        new QuickAction("📌", "Fixup",        false),
    ];
}
