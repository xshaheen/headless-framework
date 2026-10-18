// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// Offers every registered schema contribution as one stateful resource, so <c>resources setup</c> applies the same
/// steps the deploy script holds, export-only contributions included.
/// </summary>
internal sealed class SchemaResourceSource(
    IEnumerable<SchemaContribution> contributions,
    IOptions<SchemaRunnerOptions> options,
    ILogger<SchemaRunner>? logger = null,
    TimeProvider? timeProvider = null
) : IStatefulResourceSource
{
    public async Task<IReadOnlyList<IStatefulResource>> GetResourcesAsync(CancellationToken cancellationToken)
    {
        // The runner applies a feature registered twice for one schema of one database once; so does the resource list.
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
            .GroupBy(x => SchemaContributionResource.BaseName(x.Contribution), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Skip(1).Any())
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. distinct.Select(x => new SchemaContributionResource(
                x.Contribution,
                options.Value,
                logger,
                timeProvider,
                shared.Contains(SchemaContributionResource.BaseName(x.Contribution)) ? x.Database : null
            )),
        ];
    }
}

/// <summary>One feature's schema steps in one database, applied and verified by a runner of its own.</summary>
internal sealed class SchemaContributionResource : IStatefulResource
{
    private readonly SchemaRunner _runner;
    private readonly SchemaContribution _contribution;

    public SchemaContributionResource(
        SchemaContribution contribution,
        SchemaRunnerOptions options,
        ILogger<SchemaRunner>? logger,
        TimeProvider? timeProvider,
        string? database = null
    )
    {
        _contribution = contribution;

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

    public string Type => "Schema";

    public string Name { get; }

    public async Task SetupAsync(CancellationToken cancellationToken)
    {
        await _runner.RunAsync(SchemaRunnerMode.Apply, cancellationToken).ConfigureAwait(false);
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var mismatches = await _runner.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var missing = mismatches.Where(static m => m.Kind == SchemaMismatchKind.Missing).ToList();
        var changed = mismatches.Where(static m => m.Kind == SchemaMismatchKind.Checksum).ToList();

        if (changed.Count > 0)
        {
            throw new StatefulResourceException(
                $"Steps {_Versions(changed)} of {_contribution.Feature} were applied with different SQL than this "
                    + "build holds. A released step was edited: add a new step instead."
            );
        }

        if (missing.Count > 0)
        {
            throw new StatefulResourceException(
                $"Steps {_Versions(missing)} of {_contribution.Feature} are not applied. Run 'resources setup', or "
                    + "apply the exported schema script."
            );
        }
    }

    // Host-state tables keep their rows: the running host wrote them at startup or keeps them live, and never writes
    // them again. Sequences keep their position, so a generation they issue never repeats.
    public async Task ClearStateAsync(CancellationToken cancellationToken)
    {
        _EnsureObjectsDeclared();
        await _runner.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TeardownAsync(CancellationToken cancellationToken)
    {
        _EnsureObjectsDeclared();
        await _runner.TeardownAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _EnsureObjectsDeclared()
    {
        if (_contribution.Objects.Count == 0)
        {
            throw new StatefulResourceUnsupportedException(
                $"{_contribution.Feature} declares no schema objects (SchemaContribution.Objects), so its tables are unknown."
            );
        }
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
