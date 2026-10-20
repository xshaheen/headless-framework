// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// Verifies the registered schema once at host startup, before any hosted service can reach its objects, and fails the
/// start when a step is missing. The host never writes DDL: the deployment runs <c>resources provision</c>, and a
/// development host registers <c>AddDeployStepsOnStartup</c>, whose setup runs before this check.
/// </summary>
internal sealed partial class SchemaRunnerInitializer(
    SchemaRunner runner,
    ILogger<SchemaRunnerInitializer>? logger = null,
    ResourceProvisioningStartupService? resourceSetup = null
) : HostedInitializer
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (resourceSetup is not null)
        {
            // A host that starts its services concurrently runs this beside the setup; verify what the setup made.
            await resourceSetup.WaitForInitializationAsync(cancellationToken).ConfigureAwait(false);
        }

        var mismatches = await runner.RunAsync(SchemaRunnerMode.Verify, cancellationToken).ConfigureAwait(false);

        foreach (var unknown in mismatches.Where(m => m.Kind == SchemaMismatchKind.Unknown))
        {
            LogUnknownStep(_logger, unknown.Schema, unknown.Feature, unknown.Version);
        }
    }

    [LoggerMessage(
        EventId = 10,
        EventName = "SchemaRunnerUnknownStep",
        Level = LogLevel.Warning,
        Message = "Schema history of {Schema} records {Feature}/{Version}, which this host does not register; a newer deployment probably applied it."
    )]
    private static partial void LogUnknownStep(ILogger logger, string schema, string feature, string version);
}

/// <summary>What one <see cref="SchemaRunner.RunAsync"/> pass does.</summary>
[PublicAPI]
public enum SchemaRunnerMode
{
    /// <summary>Apply every missing step. What <c>resources provision</c> does for each feature.</summary>
    Apply = 0,

    /// <summary>
    /// Write nothing, and fail when a registered step is missing or its checksum changed. What every host start does,
    /// so a database that lacks a step fails at boot, not at the first query.
    /// </summary>
    Verify = 1,
}

/// <summary>Options of the Headless schema runner, shared by every contributing feature.</summary>
[PublicAPI]
public sealed class SchemaRunnerOptions
{
    /// <summary>
    /// Gets or sets the timeout of every statement the runner sends, DDL included. Default:
    /// <see cref="SchemaRunner.DefaultCommandTimeout"/> (10 minutes), because an index build on a populated table
    /// outlasts an OLTP command timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = SchemaRunner.DefaultCommandTimeout;

    /// <summary>Gets or sets how long a runner waits for another runner's lock. Default: <see cref="SchemaRunner.DefaultLockTimeout"/>.</summary>
    public TimeSpan LockTimeout { get; set; } = SchemaRunner.DefaultLockTimeout;
}
