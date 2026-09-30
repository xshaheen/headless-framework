// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>What the schema runner does at host startup.</summary>
[PublicAPI]
public enum SchemaRunnerMode
{
    /// <summary>Apply every missing step, then start. The zero-config default for development and for hosts allowed to run DDL.</summary>
    Apply = 0,

    /// <summary>
    /// Write nothing. Fail startup when a registered step is missing or its checksum changed, for hosts whose DDL is
    /// deployed from the exported script. A database that lacks a step then fails at boot, not at the first query.
    /// </summary>
    Verify = 1,
}

/// <summary>Startup options of the Headless schema runner, shared by every contributing feature.</summary>
[PublicAPI]
public sealed class SchemaRunnerOptions
{
    /// <summary>Gets or sets the startup mode. Default: <see cref="SchemaRunnerMode.Apply"/>.</summary>
    public SchemaRunnerMode Mode { get; set; } = SchemaRunnerMode.Apply;

    /// <summary>
    /// Gets or sets the timeout of every statement the runner sends, DDL included. Default:
    /// <see cref="SchemaRunner.DefaultCommandTimeout"/> (10 minutes), because an index build on a populated table
    /// outlasts an OLTP command timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = SchemaRunner.DefaultCommandTimeout;

    /// <summary>Gets or sets how long a runner waits for another runner's lock. Default: <see cref="SchemaRunner.DefaultLockTimeout"/>.</summary>
    public TimeSpan LockTimeout { get; set; } = SchemaRunner.DefaultLockTimeout;
}

/// <summary>
/// Runs the schema runner once at host startup, before any hosted service can reach the objects it creates. The
/// <see cref="HostedInitializer"/> base owns the completion promise and host-restart semantics that every
/// hand-written initializer used to re-declare.
/// </summary>
internal sealed partial class SchemaRunnerInitializer(
    SchemaRunner runner,
    IOptions<SchemaRunnerOptions> options,
    ILogger<SchemaRunnerInitializer>? logger = null
) : HostedInitializer
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SchemaMismatch> mismatches;
        SchemaMismatchKind[] fatal;

        if (options.Value.Mode == SchemaRunnerMode.Verify)
        {
            mismatches = await runner.VerifyAsync(cancellationToken).ConfigureAwait(false);
            fatal = [SchemaMismatchKind.Missing, SchemaMismatchKind.Checksum];
        }
        else
        {
            mismatches = (await runner.ApplyAsync(cancellationToken).ConfigureAwait(false)).Mismatches;
            fatal = [SchemaMismatchKind.Checksum];
        }

        foreach (var unknown in mismatches.Where(m => m.Kind == SchemaMismatchKind.Unknown))
        {
            LogUnknownStep(_logger, unknown.Schema, unknown.Feature, unknown.Version);
        }

        var failures = mismatches.Where(m => fatal.Contains(m.Kind)).ToList();

        if (failures.Count > 0)
        {
            throw new SchemaRunnerException(
                $"Headless schema runner ({options.Value.Mode} mode): the database history disagrees with the "
                    + $"registered steps: {string.Join("; ", failures)}. A missing step needs the exported deploy "
                    + "script or Apply mode; a changed checksum means a shipped step was edited, so add a new step "
                    + "instead."
            )
            {
                Mismatches = failures,
            };
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
