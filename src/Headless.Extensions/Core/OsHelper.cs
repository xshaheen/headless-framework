// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>Helpers for querying the current operating system and platform conventions.</summary>
[PublicAPI]
public static class OsHelper
{
    /// <summary>Gets the newline sequence for the current environment (<see cref="Environment.NewLine"/>).</summary>
    public static string Line => Environment.NewLine;

    /// <summary>Gets a value indicating whether the current OS is Linux or FreeBSD.</summary>
    public static bool IsLinux => OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux();

    /// <summary>Gets a value indicating whether the current OS is Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Gets a value indicating whether the current OS is macOS or Mac Catalyst.</summary>
    public static bool IsMac => OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();
}
