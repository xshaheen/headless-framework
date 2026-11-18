// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>States the host's isolation prefix once when the host starts, so a log shows which names it writes.</summary>
internal sealed partial class HeadlessPrefixStartupLog(
    IOptions<HeadlessPrefixOptions> options,
    ILogger<HeadlessPrefixStartupLog> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Prefix.Length > 0)
        {
            LogPrefix(logger, options.Value.Prefix);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 11,
        EventName = "HeadlessPrefix",
        Level = LogLevel.Information,
        Message = "Headless names in shared stores start with the prefix '{Prefix}'"
    )]
    private static partial void LogPrefix(ILogger logger, string prefix);
}
