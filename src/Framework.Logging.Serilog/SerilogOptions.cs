// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Reflection;
using Serilog;

namespace Framework.Logging;

[PublicAPI]
public sealed class SerilogOptions
{
    public bool WriteToFiles { get; init; } = true;
    public string LogDirectory { get; init; } = "Logs";
    public bool Buffered { get; init; } = true;
    public TimeSpan FlushToDiskInterval { get; init; } = TimeSpan.FromSeconds(1);
    public RollingInterval RollingInterval { get; init; } = RollingInterval.Day;
    public int RetainedFileCountLimit { get; init; } = 5;
    public int MaxHeaderLength { get; init; } = 512;
}
