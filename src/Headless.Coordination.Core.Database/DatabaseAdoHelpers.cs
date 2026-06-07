// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Shared ADO.NET helpers for the relational coordination providers and their schema initializers.</summary>
internal static class DatabaseAdoHelpers
{
    /// <summary>Converts a command-timeout <see cref="TimeSpan"/> into the whole-seconds value ADO.NET expects.</summary>
    public static int GetCommandTimeoutSeconds(TimeSpan timeout)
    {
        return timeout.TotalSeconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
    }
}
