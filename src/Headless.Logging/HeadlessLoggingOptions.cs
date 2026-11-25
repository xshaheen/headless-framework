// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Logging;

/// <summary>Options for the Headless logging profile and the startup logger.</summary>
[PublicAPI]
public sealed class HeadlessLoggingOptions
{
    /// <summary>
    /// The console format; <see langword="null"/> (the default) writes <see cref="HeadlessConsoleFormat.Text"/> in the
    /// Development environment and <see cref="HeadlessConsoleFormat.Json"/> everywhere else.
    /// </summary>
    public HeadlessConsoleFormat? ConsoleFormat { get; init; }

    /// <summary>The length request headers are truncated to when an API host adds them to log entries. Default 512.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int MaxHeaderLength
    {
        get;
        init => field = Argument.IsPositive(value);
    } = 512;

    internal HeadlessConsoleFormat ResolveConsoleFormat(string? environmentName)
    {
        if (ConsoleFormat is { } format)
        {
            return format;
        }

        // Matches IHostEnvironment.IsDevelopment(): a host with no environment configured runs as Production.
        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            ? HeadlessConsoleFormat.Text
            : HeadlessConsoleFormat.Json;
    }
}
