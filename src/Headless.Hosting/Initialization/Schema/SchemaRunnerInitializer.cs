// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Hosting.Initialization.Schema;

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
        var mismatches = await runner.RunAsync(options.Value.Mode, cancellationToken).ConfigureAwait(false);

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
