// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.BuildingBlocks.Helpers.System;

[PublicAPI]
public static class OsHelper
{
    public static string Line => Environment.NewLine;

    public static bool IsLinux => OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsMac => OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();
}
