// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Serilog;

namespace Headless.Logging;

/// <summary>
/// Configuration options for the Headless Serilog integration, controlling file sinks,
/// rolling behaviour, and buffering.
/// </summary>
[PublicAPI]
public sealed class SerilogOptions
{
    /// <summary>
    /// Gets a value indicating whether log events should be written to rolling log files in addition
    /// to the console sink. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, separate files are created for Fatal, Error, and Warning levels
    /// under <see cref="LogDirectory"/>. Set to <see langword="false"/> in environments where file
    /// I/O is unavailable or undesirable (for example, containerised deployments that rely on
    /// stdout collection).
    /// </remarks>
    public bool WriteToFiles { get; init; } = true;

    /// <summary>
    /// Gets the directory path where log files are written. Defaults to <c>"Logs"</c> (relative
    /// to the process working directory).
    /// </summary>
    public string LogDirectory { get; init; } = "Logs";

    /// <summary>
    /// Gets a value indicating whether the file sink buffers log events before flushing to disk.
    /// Defaults to <see langword="true"/>. Disable when you need guaranteed durability on every
    /// write, at the cost of higher I/O pressure.
    /// </summary>
    public bool Buffered { get; init; } = true;

    /// <summary>
    /// Gets the interval at which buffered log events are flushed to disk when
    /// <see cref="Buffered"/> is <see langword="true"/>. Defaults to 1 second.
    /// </summary>
    public TimeSpan FlushToDiskInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the interval at which log files are rolled over to a new file. Defaults to
    /// <see cref="Serilog.RollingInterval.Day"/> (one file per calendar day).
    /// </summary>
    public RollingInterval RollingInterval { get; init; } = RollingInterval.Day;

    /// <summary>
    /// Gets the maximum number of rolled log files to retain per severity level before the oldest
    /// is deleted. Defaults to 5. Pass <see langword="null"/> to retain files indefinitely.
    /// </summary>
    public int RetainedFileCountLimit { get; init; } = 5;

    /// <summary>
    /// Gets the maximum length in bytes of HTTP request headers captured in structured log
    /// properties. Defaults to 512. Values longer than this limit are truncated before being
    /// attached to the log event.
    /// </summary>
    public int MaxHeaderLength { get; init; } = 512;
}
