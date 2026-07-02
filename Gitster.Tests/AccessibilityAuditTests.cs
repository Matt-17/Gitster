using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Gitster.Tests;

[TestClass]
public sealed class AccessibilityAuditTests
{
    [TestMethod]
    public void Palettes_TextTertiaryContrast_MeetsSmallTextThreshold()
    {
        var root = FindRepoRoot();
        foreach (var palette in new[] { "Palette.Light.xaml", "Palette.Dark.xaml" })
        {
            var brushes = LoadBrushColors(Path.Combine(root, "Gitster", "Themes", palette));
            AssertContrast(brushes["TextTertiary"], brushes["BackgroundPrimary"], palette, "BackgroundPrimary");
            AssertContrast(brushes["TextTertiary"], brushes["BackgroundSecondary"], palette, "BackgroundSecondary");
        }
    }

    [TestMethod]
    public void Xaml_IconOnlyButtons_HaveAutomationNames()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory.EnumerateFiles(Path.Combine(root, "Gitster"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        var offenders = new List<string>();
        foreach (var file in xamlFiles)
        {
            var document = XDocument.Load(file);
            foreach (var element in document.Descendants().Where(IsButtonElement))
            {
                if (HasAutomationName(element) || HasReadableText(element) || !LooksIconOnly(element))
                    continue;

                var line = (element as IXmlLineInfo)?.LineNumber ?? 0;
                offenders.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(Environment.NewLine, offenders),
            "Icon-only buttons need AutomationProperties.Name.");
    }

    private static Dictionary<string, Rgb> LoadBrushColors(string path)
    {
        var doc = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return doc.Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Where(e => e.Attribute(x + "Key") is not null && e.Attribute("Color") is not null)
            .ToDictionary(
                e => e.Attribute(x + "Key")!.Value,
                e => ParseRgb(e.Attribute("Color")!.Value),
                StringComparer.Ordinal);
    }

    private static void AssertContrast(Rgb foreground, Rgb background, string palette, string backgroundKey)
    {
        var ratio = ContrastRatio(foreground, background);
        Assert.IsTrue(
            ratio >= 4.5,
            $"{palette}: TextTertiary contrast against {backgroundKey} is {ratio:F2}:1, expected at least 4.5:1.");
    }

    private static bool IsButtonElement(XElement element) =>
        element.Name.LocalName is "Button" or "ToggleButton";

    private static bool HasAutomationName(XElement element) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name");

    private static bool LooksIconOnly(XElement element) =>
        element.Descendants().Any(descendant => descendant.Name.LocalName is "Path" or "Ellipse")
        || Symbolic(element.Attribute("Content")?.Value)
        || element.Descendants().Any(descendant => descendant.Name.LocalName == "TextBlock" && Symbolic(descendant.Attribute("Text")?.Value));

    private static bool HasReadableText(XElement element)
    {
        if (Readable(element.Attribute("Content")?.Value))
            return true;

        return element.Descendants()
            .Where(descendant => descendant.Name.LocalName == "TextBlock")
            .Select(descendant => descendant.Attribute("Text")?.Value)
            .Any(Readable);
    }

    private static bool Readable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.StartsWith("{Binding", StringComparison.Ordinal)
            || text.StartsWith("{DynamicResource", StringComparison.Ordinal)
            || text.StartsWith("{StaticResource", StringComparison.Ordinal))
        {
            return true;
        }

        return text.Count(char.IsLetterOrDigit) >= 3;
    }

    private static bool Symbolic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        return text.Length <= 3 && text.Count(char.IsLetterOrDigit) < 2;
    }

    private static double ContrastRatio(Rgb a, Rgb b)
    {
        var lighter = Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        var darker = Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Rgb rgb) =>
        0.2126 * Linear(rgb.R) + 0.7152 * Linear(rgb.G) + 0.0722 * Linear(rgb.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Rgb ParseRgb(string value)
    {
        var hex = value.TrimStart('#');
        return new Rgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gitster", "Gitster.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Gitster repo root.");
    }

    private readonly record struct Rgb(byte R, byte G, byte B);
}
