// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Fencing;

/// <summary>
/// Creates the fencing schema, lease table, indexes, and generation sequence at host startup, once, before any call
/// can reach them, from the one table description rendered by the provider's dialect.
/// </summary>
internal sealed partial class RelationalFencingStorageInitializer(
    RelationalFencingStorage storage,
    ILogger<RelationalFencingStorageInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger<RelationalFencingStorageInitializer> _logger =
        logger ?? NullLogger<RelationalFencingStorageInitializer>.Instance;

    protected override bool RunOnStartup => storage.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = storage.CreateConnection();

        await SqlSchemaInitialization
            .RunAsync(
                storage.Dialect,
                connection,
                storage.Table.Script,
                storage.Table.InitializationLock,
                storage.CommandTimeoutSeconds,
                e => LogSchemaRaceObserved(_logger, storage.Dialect.DisplayName, e.Message),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "FencingSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "{Engine} fencing initializer absorbed a concurrent-DDL race: {Detail}. Retrying the DDL once in a fresh transaction."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string engine, string detail);
}
