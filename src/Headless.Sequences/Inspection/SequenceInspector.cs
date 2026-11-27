// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>
/// Turns an inspection query into the reader's explicit selection of names and partitions, and each stored row into
/// a <see cref="SequenceCounter" /> with its policy, current-partition flag, last number, and next reset.
/// </summary>
/// <remarks>
/// It depends on no numbering service and no current tenant: an inspection call only reads, and reads every tenant.
/// </remarks>
internal sealed class SequenceInspector(
    ISequenceCounterReader reader,
    IOptionsMonitor<SequencesOptions> options,
    TimeProvider timeProvider
) : ISequenceInspector
{
    private const int _MaxLimit = 200;
    private const int _SummaryCap = 10_000;

    public async ValueTask<SequenceCounterPage> ListCountersAsync(
        SequenceCounterQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsInclusiveBetween(query.Limit, 1, _MaxLimit);

        if (query.After is { } after)
        {
            _ValidateKey(after);
        }

        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var filter = _Filter(
            settings,
            query.TenantId,
            query.Name,
            query.Search,
            query.Mode,
            query.CurrentPartitionsOnly,
            now
        );

        var rows = await reader
            .ListAsync(
                new SequenceCounterReadRequest
                {
                    Filter = filter,
                    After = query.After,
                    Limit = query.Limit + 1,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var items = _Page(rows, query.Limit, settings, now);

        return new SequenceCounterPage(items, rows.Count > query.Limit ? items[^1].Key : null);
    }

    public async ValueTask<SequencePartitionPage> ListPartitionsAsync(
        SequencePartitionQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsInclusiveBetween(query.Limit, 1, _MaxLimit);

        var tenantId = _ValidateTenantId(query.TenantId) ?? string.Empty;
        var name = _ValidateName(query.Name);
        var before = query.Before is null ? null : _ValidatePartition(query.Before);

        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();

        var rows = await reader
            .ListPartitionsAsync(
                new SequencePartitionReadRequest
                {
                    TenantId = tenantId,
                    Name = name,
                    Before = before,
                    Limit = query.Limit + 1,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var items = _Page(rows, query.Limit, settings, now);

        return new SequencePartitionPage(items, rows.Count > query.Limit ? items[^1].Key.Partition : null);
    }

    public async ValueTask<SequenceCounterSummary> GetSummaryAsync(
        SequenceSummaryQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);

        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var filter = _Filter(
            settings,
            query.TenantId,
            query.Name,
            query.Search,
            mode: null,
            query.CurrentPartitionsOnly,
            now
        );

        var counts = await reader
            .CountAsync(
                new SequenceCounterCountRequest
                {
                    Filter = filter,
                    Modes = new SequenceModeNames
                    {
                        Fast = _NamesOf(settings, SequenceMode.Fast),
                        GapFree = _NamesOf(settings, SequenceMode.GapFree),
                        Reported = _NamesOf(settings, SequenceMode.Reported),
                        DefaultMode = settings.DefaultPolicy.Mode,
                    },
                    Cap = _SummaryCap,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var nextReset = settings
            .Policies.Values.Append(settings.DefaultPolicy)
            .Select(policy => policy.NextResetAt(now))
            .Where(static reset => reset is not null)
            .Min();

        return new SequenceCounterSummary
        {
            Total = counts.Total,
            Fast = counts.Fast,
            GapFree = counts.GapFree,
            Reported = counts.Reported,
            Capped = counts.Capped,
            NextResetAt = nextReset,
        };
    }

    private static SequenceCounterFilter _Filter(
        SequencesOptions settings,
        string? tenantId,
        string? name,
        string? search,
        SequenceMode? mode,
        bool currentPartitionsOnly,
        DateTimeOffset now
    )
    {
        return new SequenceCounterFilter
        {
            TenantId = _ValidateTenantId(tenantId),
            Name = name is null ? null : _ValidateName(name),
            Search = _ValidateSearch(search),
            Selection = _Select(settings, mode, currentPartitionsOnly, now),
        };
    }

    /// <summary>
    /// The names and partitions the mode and current-partition filters keep, or <see langword="null" /> when neither
    /// filters. A row's mode is the mode of the policy its name resolves to now, so the selection lists the registered
    /// names by policy and treats every other name as the default policy's.
    /// </summary>
    private static SequenceCounterSelection? _Select(
        SequencesOptions settings,
        SequenceMode? mode,
        bool currentPartitionsOnly,
        DateTimeOffset now
    )
    {
        if (mode is null && !currentPartitionsOnly)
        {
            return null;
        }

        var names = new List<string>();
        var namePartitions = new List<SequenceNamePartition>();

        foreach (var (name, policy) in settings.Policies)
        {
            if (mode is { } wanted && policy.Mode != wanted)
            {
                continue;
            }

            // A policy that never resets has no current period, so every one of its partitions stays.
            if (currentPartitionsOnly && policy.PartitionAt(now) is { } partition)
            {
                namePartitions.Add(new SequenceNamePartition(name, partition));
            }
            else
            {
                names.Add(name);
            }
        }

        var defaultPolicy = settings.DefaultPolicy;
        var includeUnregistered = mode is null || defaultPolicy.Mode == mode;

        return new SequenceCounterSelection
        {
            RegisteredNames = [.. settings.Policies.Keys],
            Names = names,
            NamePartitions = namePartitions,
            IncludeUnregisteredNames = includeUnregistered,
            UnregisteredNamePartition =
                includeUnregistered && currentPartitionsOnly ? defaultPolicy.PartitionAt(now) : null,
        };
    }

    private static List<SequenceCounter> _Page(
        IReadOnlyList<SequenceCounterRecord> rows,
        int limit,
        SequencesOptions settings,
        DateTimeOffset now
    )
    {
        var items = new List<SequenceCounter>(Math.Min(rows.Count, limit));

        foreach (var row in rows.Take(limit))
        {
            items.Add(_Counter(row, settings.GetPolicy(row.Key.Name), now));
        }

        return items;
    }

    private static SequenceCounter _Counter(SequenceCounterRecord row, SequencePolicy policy, DateTimeOffset now)
    {
        var currentPartition = policy.PartitionAt(now);

        return new SequenceCounter
        {
            Key = row.Key,
            Value = row.Value,
            UpdatedAt = row.UpdatedAt,
            Policy = policy,
            IsCurrentPartition =
                currentPartition is null
                || string.Equals(row.Key.Partition, currentPartition, StringComparison.Ordinal),
            LastNumber = policy is { Format: not null, Mode: not SequenceMode.Reported }
                ? SequenceNumbers.Create(policy, row.Value, partition: null, _IssuedOn(policy, row)).Text
                : null,
            NextResetAt = policy.NextResetAt(now),
        };
    }

    /// <summary>
    /// The date the last number was most likely issued on: <see cref="SequenceCounterRecord.UpdatedAt" /> in the policy's
    /// zone when that date is in the row's period, otherwise the period's first day.
    /// </summary>
    /// <remarks>
    /// The row's time comes from the database clock and numbering dates from the app's clock, so near a period boundary
    /// the two can name different periods; the partition is what the number was issued in.
    /// </remarks>
    private static DateOnly _IssuedOn(SequencePolicy policy, SequenceCounterRecord row)
    {
        var updatedOn = SequenceNumberFormat.IssuedOn(policy, row.UpdatedAt);

        if (
            policy.Reset == SequenceReset.Never
            || string.Equals(
                SequenceNumberFormat.Partition(policy, updatedOn),
                row.Key.Partition,
                StringComparison.Ordinal
            )
        )
        {
            return updatedOn;
        }

        return SequenceNumberFormat.PeriodStart(policy, row.Key.Partition) ?? updatedOn;
    }

    private static string[] _NamesOf(SequencesOptions settings, SequenceMode mode)
    {
        return [.. settings.Policies.Where(entry => entry.Value.Mode == mode).Select(static entry => entry.Key)];
    }

    private static void _ValidateKey(SequenceKey key)
    {
        _ValidateTenantId(key.TenantId);
        _ValidateName(key.Name);

        if (key.Partition.Length > 0)
        {
            _ValidatePartition(key.Partition);
        }
    }

    /// <summary>The empty string is the stored host scope; any other id is checked as a numbering call checks it.</summary>
    private static string? _ValidateTenantId(string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return tenantId;
        }

        Argument.IsNotNullOrWhiteSpace(tenantId);
        Argument.HasMaxLength(tenantId, SequenceFieldLimits.TenantIdMaxLength);
        Argument.IsPortableKey(tenantId);

        return tenantId;
    }

    private static string _ValidateName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.HasMaxLength(name, SequenceFieldLimits.NameMaxLength);
        Argument.IsPortableKey(name);

        return name;
    }

    private static string _ValidatePartition(string partition)
    {
        Argument.IsNotNullOrWhiteSpace(partition);
        Argument.HasMaxLength(partition, SequenceFieldLimits.PartitionMaxLength);
        Argument.IsPortableKey(partition);

        return partition;
    }

    private static string? _ValidateSearch(string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return null;
        }

        Argument.HasMaxLength(search, SequenceFieldLimits.NameMaxLength);

        // Passed as typed: the reader folds ASCII case itself, because only ASCII folds the same way on every engine.
        return search;
    }
}
