// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>
/// The Sequences dashboard module: a read-only view of every stored counter across host and tenant scopes, with its
/// value, last formatted number, partition, policy, and next reset. Every read goes through
/// <see cref="ISequenceInspector"/>, which never issues, reserves, or changes a number, and the module declares no
/// command.
/// </summary>
/// <remarks>
/// Its sidebar count <c>counters</c> is the number of stored counters, capped at 10,000. Its one object kind is
/// <c>sequence-counter</c>, identified by <see cref="SequenceCounterId"/>. Its API is mapped by
/// <see cref="SequencesEndpoints"/>.
/// </remarks>
internal sealed class SequencesModule(
    ISequenceInspector inspector,
    IOptionsMonitor<SequencesOptions> options,
    ILogger<SequencesEndpoints> logger
) : IDashboardModule, IDashboardCountSource, IDashboardObjectResolver
{
    /// <summary>The sidebar count key of the stored counters.</summary>
    public const string CountersCountKey = "counters";

    // The endpoints class validates the dependencies, so the module does not repeat the checks.
    private readonly SequencesEndpoints _endpoints = new(inspector, options, logger);

    public string Id => SequencesDashboardOptions.ModuleId;

    public string DisplayName => "Sequences";

    public DashboardModuleAssets Assets { get; } = SequencesAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _endpoints.Map(endpoints);
    }

    public async Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        var summary = await inspector
            .GetSummaryAsync(new SequenceSummaryQuery(), cancellationToken)
            .ConfigureAwait(false);

        return new Dictionary<string, DashboardCount>(StringComparer.Ordinal)
        {
            [CountersCountKey] = new() { Count = summary.Total, Capped = summary.Capped },
        };
    }

    /// <summary>Answers the one counter an exact id names; a name fragment or any other text answers none.</summary>
    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        if (!SequenceCounterId.TryParse(request.Query, out var key))
        {
            return [];
        }

        SequenceCounter? counter;

        try
        {
            counter = await _endpoints.FindAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // Text that parses as an id but holds a key part no counter can have names no counter.
            return [];
        }

        return counter is null ? [] : [SequencesObjects.From(counter)];
    }
}
