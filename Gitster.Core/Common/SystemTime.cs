using System;

namespace Gitster.Core.Common;

/// <summary>
/// Helper class to get current date/time. This abstraction allows for easier testing.
/// </summary>
public static class SystemTime
{
    public static DateTime Today => DateTime.Today;
    public static DateTime Now => DateTime.Now;
}
