// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Applies an additional outbox's schema contribution with a runner of its own. The contribution is not registered
/// with the host's runner, because the host's runner fails startup on an unreachable database, and an additional
/// outbox's database being down must leave the host running.
/// </summary>
internal sealed class OutboxSchemaInitializer(
    SchemaContribution contribution,
    IStorageTableNames tableNames,
    IOptions<SchemaRunnerOptions> runnerOptions,
    ILogger<SchemaRunner> logger,
    TimeProvider timeProvider
) : IOutboxStorageInitializer
{
    private readonly SchemaRunnerMode _mode = runnerOptions.Value.Mode;

    private readonly SchemaRunner _runner = new(
        [Argument.IsNotNull(contribution)],
        logger,
        timeProvider,
        runnerOptions.Value.LockTimeout,
        runnerOptions.Value.CommandTimeout
    );

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Same mode as the host's runner: a host that forbids DDL verifies its outboxes too.
        await _runner.RunAsync(_mode, cancellationToken).ConfigureAwait(false);
    }

    public string GetPublishedTableName() => tableNames.GetPublishedTableName();

    public string GetReceivedTableName() => tableNames.GetReceivedTableName();
}
