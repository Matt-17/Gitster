using Gitster.Services;

namespace Gitster.Tests;

[TestClass]
public sealed class ShortcutRegistryTests
{
    [TestMethod]
    public void All_IncludesMainWindowGestures()
    {
        var gestures = ShortcutRegistry.All.Select(s => s.Gesture).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var gesture in new[]
                 {
                     "Ctrl+O",
                     "Ctrl+K",
                     "Ctrl+F",
                     "Ctrl+Z",
                     "F5",
                     "Ctrl+/",
                     "F1",
                     "Ctrl+Shift+P",
                 })
        {
            Assert.IsTrue(gestures.Contains(gesture), $"Missing gesture: {gesture}");
        }
    }
}
