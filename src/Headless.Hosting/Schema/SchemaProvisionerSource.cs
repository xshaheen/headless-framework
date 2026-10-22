// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Hosting.Schema;

/// <summary>
/// Offers every registered schema contribution as one resource provisioner, so <c>resources provision</c> applies the same
/// steps the deploy script holds, export-only contributions included.
/// </summary>
internal sealed partial class SchemaProvisionerSource(
    IEnumerable<SchemaContribution> contributions,
    IOptions<SchemaRunnerOptions> options,
    SchemaRunner hostRunner,
    ILogger<SchemaRunner>? logger = null,
    TimeProvider? timeProvider = null
) : IResourceProvisionerSource
{
    public async Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken)
    {
        // The runner applies a feature registered twice for one schema of one database once; so does the provisioner list.
        var seen = new HashSet<(string Dialect, string Database, string Schema, string Feature)>();
        var distinct = new List<(SchemaContribution Contribution, string Database)>();

        foreach (var contribution in contributions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = await contribution.GetDatabaseIdentityAsync().ConfigureAwait(false);

            if (seen.Add((contribution.Dialect.Name, identity, contribution.Schema, contribution.Feature)))
            {
                distinct.Add((contribution, identity));
            }
        }

        // The same feature in the same schema of two databases (an additional outbox on its own database, say) would
        // otherwise share one name, and '--name' could not tell them apart; only those names carry the database.
        var shared = distinct
            .GroupBy(x => SchemaContributionProvisioner.BaseName(x.Contribution), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Skip(1).Any())
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // One verify pass of the host's own runner answers every provisioner of this lookup, so a host start reads each
        // schema's history once, not once per feature.
        var hostVerification = new HostVerification(hostRunner, logger);

        return
        [
            .. distinct.Select(x => new SchemaContributionProvisioner(
                x.Contribution,
                options.Value,
                logger,
                timeProvider,
                shared.Contains(SchemaContributionProvisioner.BaseName(x.Contribution)) ? x.Database : null,
                hostVerification
            )),
        ];
    }

    /// <summary>The host runner's verify pass, run at most once and shared by the provisioners of one lookup.</summary>
    internal sealed partial class HostVerification(SchemaRunner runner, ILogger? logger)
    {
        private readonly Lock _gate = new();
        private Task<IReadOnlyList<SchemaMismatch>>? _pass;

        public Task<IReadOnlyList<SchemaMismatch>> GetAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                // A canceled or faulted pass is not cached, so the next caller (a retried readiness check) runs it again.
                if (_pass is not { IsCanceled: false, IsFaulted: false })
                {
                    _pass = _RunAsync(cancellationToken);
                }

                return _pass;
            }
        }

        private async Task<IReadOnlyList<SchemaMismatch>> _RunAsync(CancellationToken cancellationToken)
        {
            var mismatches = await runner.VerifyAsync(cancellationToken).ConfigureAwait(false);

            if (logger is not null)
            {
                foreach (var unknown in mismatches.Where(static m => m.Kind == SchemaMismatchKind.Unknown))
                {
                    LogUnknownStep(logger, unknown.Schema, unknown.Feature, unknown.Version);
                }
            }

            return mismatches;
        }

        [LoggerMessage(
            EventId = 10,
            EventName = "SchemaRunnerUnknownStep",
            Level = LogLevel.Warning,
            Message = "Schema history of {Schema} records {Feature}/{Version}, which this host does not register; a newer deployment probably applied it."
        )]
        private static partial void LogUnknownStep(ILogger logger, string schema, string feature, string version);
    }
}

/// <summary>One feature's schema steps in one database, applied and verified by a runner of its own.</summary>
internal sealed class SchemaContributionProvisioner : IResourceProvisioner
{
    private readonly SchemaRunner _runner;
    private readonly SchemaContribution _contribution;
    private readonly SchemaProvisionerSource.HostVerification? _hostVerification;

    public SchemaContributionProvisioner(
        SchemaContribution contribution,
        SchemaRunnerOptions options,
        ILogger<SchemaRunner>? logger,
        TimeProvider? timeProvider,
        string? database = null,
        SchemaProvisionerSource.HostVerification? hostVerification = null
    )
    {
        _contribution = contribution;

        // The host's pass reports by schema and feature only, so a feature that lives in two databases, and an
        // export-only one the host's runner skips, verifies with a runner of its own.
        _hostVerification = database is null && !contribution.ExportOnly ? hostVerification : null;

        // The deploy step applies what the deploy script holds: export-only marks what the host's own runner skips,
        // not what the deployment provisions.
        var deployed = new SchemaContribution(
            contribution.Feature,
            contribution.Dialect,
            contribution.CreateConnection,
            contribution.Schema,
            contribution.Steps,
            exportOnly: false,
            hostStateTables: contribution.HostStateTables,
            objects: contribution.Objects
        );

        _runner = new SchemaRunner([deployed], logger, timeProvider, options.LockTimeout, options.CommandTimeout);
        // A hash, not the identity itself: the identity holds the host name, and a name is printed to deploy logs.
        Name = database is null ? BaseName(contribution) : $"{BaseName(contribution)}@{_ShortHash(database)}";
    }

    private static string _ShortHash(string database) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(database)).AsSpan(0, 4));

    /// <summary>The resource's name before any database suffix: <c>{dialect}/{schema}/{feature}</c>.</summary>
    public static string BaseName(SchemaContribution contribution) =>
        $"{contribution.Dialect.Name}/{contribution.Schema}/{contribution.Feature}";

    public string Kind => "Schema";

    public string Name { get; }

    // Tables are what the host's own background services reach first, so a missing step stops the start. An export-only
    // contribution is deployed from the exported script and the host does not check it.
    public ResourceStartupCheck StartupCheck =>
        _contribution.ExportOnly ? ResourceStartupCheck.None : ResourceStartupCheck.FailStart;

    // Without declared objects the tables are unknown, so there is nothing this provisioner can safely purge or delete.
    public bool CanPurge => _contribution.Objects.Count > 0;

    public bool CanDelete => _contribution.Objects.Count > 0;

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        await _runner.RunAsync(SchemaRunnerMode.Apply, cancellationToken).ConfigureAwait(false);
    }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var mismatches = _hostVerification is null
            ? await _runner.VerifyAsync(cancellationToken).ConfigureAwait(false)
            :
            [
                .. (await _hostVerification.GetAsync(cancellationToken).ConfigureAwait(false)).Where(m =>
                    string.Equals(m.Schema, _contribution.Schema, StringComparison.Ordinal)
                    && string.Equals(m.Feature, _contribution.Feature, StringComparison.Ordinal)
                ),
            ];
        var missing = mismatches.Where(static m => m.Kind == SchemaMismatchKind.Missing).ToList();
        var changed = mismatches.Where(static m => m.Kind == SchemaMismatchKind.Checksum).ToList();

        if (changed.Count > 0)
        {
            throw new ResourceProvisioningException(
                $"Steps {_Versions(changed)} of {_contribution.Feature} were applied with different SQL than this "
                    + "build holds. A released step was edited: add a new step instead."
            );
        }

        if (missing.Count > 0)
        {
            throw new ResourceProvisioningException(
                $"Steps {_Versions(missing)} of {_contribution.Feature} are not applied. Run 'resources provision', or "
                    + "apply the exported schema script."
            );
        }
    }

    // Host-state tables keep their rows: the running host wrote them at startup or keeps them live, and never writes
    // them again. Sequences keep their position, so a generation they issue never repeats.
    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        await _runner.PurgeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await _runner.DeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var mismatches = await _runner.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var missing = mismatches.Count(static m => m.Kind == SchemaMismatchKind.Missing);
        var changed = mismatches.Count(static m => m.Kind == SchemaMismatchKind.Checksum);
        var unknown = mismatches.Count(static m => m.Kind == SchemaMismatchKind.Unknown);
        var steps = _contribution.Steps.Count;

        return
        [
            new("steps", _Count(steps)),
            new("applied", _Count(steps - missing)),
            new("missing", _Count(missing)),
            new("changed", _Count(changed)),
            new("unknown", _Count(unknown)),
        ];
    }

    private static string _Versions(IEnumerable<SchemaMismatch> mismatches) =>
        string.Join(", ", mismatches.Select(static m => m.Version));

    private static string _Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
