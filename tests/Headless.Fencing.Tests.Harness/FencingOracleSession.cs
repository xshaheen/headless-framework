// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Checks;
using Headless.Fencing;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// Runs one history's operations against one provider and reports what each step observed, normalized: generations
/// are renumbered in order of first appearance, and the stored rows of every key are read after each step.
/// </summary>
public sealed class FencingOracleSession : IAsyncDisposable
{
    private readonly ILeasesFixture _fixture;
    private readonly LeasesHost _host;
    private readonly string[] _kinds;
    private readonly LeaseKey[] _keys;
    private readonly List<long>[] _generations;
    private readonly Dictionary<long, int> _ordinals = [];
    private readonly bool _enlistedGrantRefused;

    private FencingOracleSession(
        ILeasesFixture fixture,
        LeasesHost host,
        FencingOracleHistory history,
        string runId,
        bool enlistedGrantRefused
    )
    {
        _fixture = fixture;
        _host = host;
        _enlistedGrantRefused = enlistedGrantRefused;
        _kinds = [.. FencingOracleGenerator.KindSuffixes.Select(suffix => runId + suffix)];
        _keys =
        [
            .. history.Keys.Select(k => new LeaseKey(
                FencingOracleGenerator.Tenants[k.TenantIndex] ?? string.Empty,
                _kinds[k.KindIndex],
                FencingOracleGenerator.Resources[k.ResourceIndex]
            )),
        ];
        _generations = [.. _keys.Select(static _ => new List<long>())];
    }

    /// <summary>Builds a host on <paramref name="fixture" /> for one run of <paramref name="history" />.</summary>
    /// <param name="runId">A prefix unique to this run, so its kinds, sweeps, and purges touch only its own rows.</param>
    /// <param name="enlistedGrantRefused">
    /// Whether the provider under comparison refuses enlisted grants; both sessions then report every enlisted grant
    /// as refused, and the refusing one proves it by its own call.
    /// </param>
    public static async Task<FencingOracleSession> StartAsync(
        ILeasesFixture fixture,
        FencingOracleHistory history,
        string runId,
        bool enlistedGrantRefused,
        CancellationToken cancellationToken
    )
    {
        var host = await fixture.CreateHostAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new FencingOracleSession(fixture, host, history, runId, enlistedGrantRefused);
    }

    public async Task<FencingOracleObservation> ExecuteAsync(FencingOracleOp op, CancellationToken cancellationToken)
    {
        string outcome;
        long? ttlError = null;

        try
        {
            (outcome, ttlError) = op switch
            {
                FencingOracleOp.Grant g => await _GrantAsync(g.Key, g.Duration, cancellationToken)
                    .ConfigureAwait(false),
                FencingOracleOp.Contend c => (await _ContendAsync(c, cancellationToken).ConfigureAwait(false), null),
                FencingOracleOp.GrantPadded p => (
                    await _GrantPaddedAsync(p, cancellationToken).ConfigureAwait(false),
                    null
                ),
                FencingOracleOp.Renew r => (await _RenewAsync(r, cancellationToken).ConfigureAwait(false), null),
                FencingOracleOp.Settle s => (
                    "settle:"
                        + (
                            await _host
                                .Leases.SettleAsync(_Lease(s.Key, s.Slot), cancellationToken)
                                .ConfigureAwait(false)
                        ).ToString(),
                    null
                ),
                FencingOracleOp.Release r => (
                    "release:"
                        + (
                            await _host
                                .Leases.ReleaseAsync(_Lease(r.Key, r.Slot), cancellationToken)
                                .ConfigureAwait(false)
                        ).ToString(),
                    null
                ),
                FencingOracleOp.FenceAndSettle f => (
                    await _FenceAndSettleAsync(f, cancellationToken).ConfigureAwait(false),
                    null
                ),
                FencingOracleOp.EnlistedGrant e => await _EnlistedGrantAsync(e, cancellationToken)
                    .ConfigureAwait(false),
                FencingOracleOp.Sweep s => (await _SweepAsync(s, cancellationToken).ConfigureAwait(false), null),
                FencingOracleOp.Purge p => (
                    "purge:deleted="
                        + (
                            await _host
                                .Leases.PurgeAsync(_kinds[p.KindIndex], p.OlderThan, cancellationToken)
                                .ConfigureAwait(false)
                        ).ToString(CultureInfo.InvariantCulture),
                    null
                ),
                FencingOracleOp.AdvanceTime a => (
                    await _AdvanceAsync(a.By, cancellationToken).ConfigureAwait(false),
                    null
                ),
                _ => throw new InvalidOperationException($"Unknown operation {op}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A provider error is an observable outcome to compare, not a harness failure.
        catch (Exception e)
#pragma warning restore CA1031
        {
            outcome = "threw:" + _Describe(e);
        }

        return new FencingOracleObservation(
            $"{outcome} | rows[{await _RowsAsync(cancellationToken).ConfigureAwait(false)}]",
            ttlError
        );
    }

    private async Task<(string, long?)> _GrantAsync(int key, TimeSpan duration, CancellationToken cancellationToken)
    {
        LeaseGrantResult result;

        using (_host.CurrentTenant.Change(_keys[key].PublicTenantId))
        {
            result = await _host
                .Leases.GrantAsync(_keys[key].Kind, _keys[key].Resource, duration, cancellationToken)
                .ConfigureAwait(false);
        }

        return (
            "grant:" + _DescribeGrant(key, result),
            await _TtlErrorAsync(key, result, duration, cancellationToken).ConfigureAwait(false)
        );
    }

    private async Task<string> _ContendAsync(FencingOracleOp.Contend op, CancellationToken cancellationToken)
    {
        var key = _keys[op.Key];

        // The tenant is ambient per flow: each contender changes it on its own async flow.
        var results = await Task.WhenAll(
                Enumerable
                    .Range(0, op.Contenders)
                    .Select(async _ =>
                    {
                        using (_host.CurrentTenant.Change(key.PublicTenantId))
                        {
                            return await _host
                                .Leases.GrantAsync(key.Kind, key.Resource, op.Duration, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    })
            )
            .ConfigureAwait(false);

        // Which contender wins is scheduling; how many win, and what the winner displaced, is the contract.
        var acquired = results.Where(static r => r.IsAcquired).OrderBy(static r => r.Lease!.Generation).ToList();
        var held = results.Count(static r => r.Status == LeaseGrantStatus.Held);
        var winners = string.Join(',', acquired.Select(r => _DescribeGrant(op.Key, r)));

        return string.Create(CultureInfo.InvariantCulture, $"contend:acquired={acquired.Count}[{winners}],held={held}");
    }

    private async Task<string> _GrantPaddedAsync(FencingOracleOp.GrantPadded op, CancellationToken cancellationToken)
    {
        var key = _keys[op.Key];
        var tenant = op.Part == 2 ? (key.PublicTenantId ?? "tenant") + " " : key.PublicTenantId;
        var kind = op.Part == 0 ? key.Kind + " " : key.Kind;
        var resource = op.Part == 1 ? key.Resource + " " : key.Resource;

        using (_host.CurrentTenant.Change(tenant))
        {
            var result = await _host
                .Leases.GrantAsync(kind, resource, TimeSpan.FromMinutes(5), cancellationToken)
                .ConfigureAwait(false);

            return "padded-grant:" + result.Status;
        }
    }

    private async Task<string> _RenewAsync(FencingOracleOp.Renew op, CancellationToken cancellationToken)
    {
        var lease = _Lease(op.Key, op.Slot);
        var result = op.WithProgress
            ? await _host
                .Leases.RenewAsync(lease, op.Duration, new LeaseProgress([1, 2, 3], "oracle/v1"), cancellationToken)
                .ConfigureAwait(false)
            : await _host.Leases.RenewAsync(lease, op.Duration, cancellationToken).ConfigureAwait(false);

        return "renew:" + result.Status;
    }

    private async Task<string> _FenceAndSettleAsync(
        FencingOracleOp.FenceAndSettle op,
        CancellationToken cancellationToken
    )
    {
        var lease = _Lease(op.Key, op.Slot);
        await using var unit = await _fixture.BeginUnitAsync(_host, cancellationToken).ConfigureAwait(false);

        try
        {
            await unit.Unit.Leases.FenceAsync(lease, cancellationToken).ConfigureAwait(false);
        }
        catch (StaleLeaseException e)
        {
            await unit.RollbackAsync().ConfigureAwait(false);

            return "fence:" + e.Reason;
        }

        var settled = await unit.Unit.Leases.SettleAsync(lease, cancellationToken).ConfigureAwait(false);

        if (op.Commit)
        {
            await unit.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await unit.RollbackAsync().ConfigureAwait(false);
        }

        return $"fence:Current,settle:{settled},{(op.Commit ? "commit" : "rollback")}";
    }

    private async Task<(string, long?)> _EnlistedGrantAsync(
        FencingOracleOp.EnlistedGrant op,
        CancellationToken cancellationToken
    )
    {
        var key = _keys[op.Key];
        LeaseGrantResult result;

        if (_enlistedGrantRefused)
        {
            return (await _RefusedEnlistedGrantAsync(op, cancellationToken).ConfigureAwait(false), null);
        }

        await using (var unit = await _fixture.BeginUnitAsync(_host, cancellationToken).ConfigureAwait(false))
        {
            using (_host.CurrentTenant.Change(key.PublicTenantId))
            {
                result = await unit
                    .Unit.Leases.GrantAsync(key.Kind, key.Resource, op.Duration, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (op.Commit)
            {
                await unit.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await unit.RollbackAsync().ConfigureAwait(false);
            }
        }

        var described = $"enlisted-grant:{_DescribeGrant(op.Key, result)},{(op.Commit ? "commit" : "rollback")}";

        return (
            described,
            op.Commit
                ? await _TtlErrorAsync(op.Key, result, op.Duration, cancellationToken).ConfigureAwait(false)
                : null
        );
    }

    /// <summary>
    /// Compared with a provider that refuses enlisted grants, both sides run the grant in a unit that is then rolled
    /// back, so argument refusals still match and nothing either side wrote survives. The refusing provider must
    /// refuse; the model's grant is discarded, and the generation it drew is never observed.
    /// </summary>
    private async Task<string> _RefusedEnlistedGrantAsync(
        FencingOracleOp.EnlistedGrant op,
        CancellationToken cancellationToken
    )
    {
        var key = _keys[op.Key];
        await using var unit = await _fixture.BeginUnitAsync(_host, cancellationToken).ConfigureAwait(false);

        try
        {
            using (_host.CurrentTenant.Change(key.PublicTenantId))
            {
                await unit
                    .Unit.Leases.GrantAsync(key.Kind, key.Resource, op.Duration, cancellationToken)
                    .ConfigureAwait(false);
            }

            return _fixture.SupportsEnlistedGrant ? "enlisted-grant:refused" : "enlisted-grant:accepted";
        }
        catch (NotSupportedException) when (!_fixture.SupportsEnlistedGrant)
        {
            return "enlisted-grant:refused";
        }
        finally
        {
            await unit.RollbackAsync().ConfigureAwait(false);
        }
    }

    private async Task<string> _SweepAsync(FencingOracleOp.Sweep op, CancellationToken cancellationToken)
    {
        var result = await _host
            .Leases.SweepExpiredAsync(
                _kinds[op.KindIndex],
                static (_, _, _) => ValueTask.CompletedTask,
                op.Limit,
                cancellationToken
            )
            .ConfigureAwait(false);

        var handed = result.Handled.Select(lease =>
        {
            var index = Array.FindIndex(
                _keys,
                k =>
                    string.Equals(k.TenantId, lease.TenantId ?? string.Empty, StringComparison.Ordinal)
                    && string.Equals(k.Kind, lease.Kind, StringComparison.Ordinal)
                    && string.Equals(k.Resource, lease.Resource, StringComparison.Ordinal)
            );

            return string.Create(
                CultureInfo.InvariantCulture,
                $"k{index}@g{_Ordinal(lease.Generation)}/t{lease.TakeoverCount}/p{(lease.Progress is null ? 0 : 1)}"
            );
        });

        return string.Create(
            CultureInfo.InvariantCulture,
            $"sweep:[{string.Join(',', handed)}],failed={result.Failures.Count}"
        );
    }

    private async Task<string> _AdvanceAsync(TimeSpan by, CancellationToken cancellationToken)
    {
        foreach (var key in _keys)
        {
            // Only keys with a readable row: a purge may have deleted one, and the fixtures refuse to age a missing row.
            if ((await _TryReadAsync(key, cancellationToken).ConfigureAwait(false)).Row is not null)
            {
                await _fixture.ShiftIntoPastAsync(key, by, cancellationToken).ConfigureAwait(false);
            }
        }

        return "advance";
    }

    private async Task<long?> _TtlErrorAsync(
        int key,
        LeaseGrantResult result,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        if (
            !result.IsAcquired
            || (await _TryReadAsync(_keys[key], cancellationToken).ConfigureAwait(false)).Row is not { } row
        )
        {
            return null;
        }

        return (row.ExpiresAt - row.GrantedAt - duration).Ticks;
    }

    private string _DescribeGrant(int key, LeaseGrantResult result)
    {
        var builder = new StringBuilder(result.Status.ToString());

        if (result.Lease is { } lease)
        {
            _generations[key].Insert(0, lease.Generation);
            builder.Append(CultureInfo.InvariantCulture, $"(g{_Ordinal(lease.Generation)}");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $"(holder=g{_Ordinal(result.HolderGeneration!.Value)}");
        }

        if (result.PreviousGeneration is { } previous)
        {
            builder.Append(CultureInfo.InvariantCulture, $",prev=g{_Ordinal(previous)}");
        }

        builder.Append(CultureInfo.InvariantCulture, $",t{result.TakeoverCount},p{(result.Progress is null ? 0 : 1)})");

        return builder.ToString();
    }

    private async Task<string> _RowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<string>(_keys.Length);

        foreach (var key in _keys)
        {
            var (row, error) = await _TryReadAsync(key, cancellationToken).ConfigureAwait(false);

            rows.Add(
                error is not null ? "unreadable:" + error
                : row is null ? "-"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"g{_Ordinal(row.Generation)}:{row.State}{(row.EndedAt is null ? "" : "+ended")}"
                )
            );
        }

        return string.Join(' ', rows);
    }

    // A key the database cannot even look up is an observation (a provider that cannot store the key), not a harness
    // failure, so it is reported in the row snapshot and compared like any other outcome.
    private async Task<(StoredLease? Row, string? Error)> _TryReadAsync(
        LeaseKey key,
        CancellationToken cancellationToken
    )
    {
        // A key call validation refuses never reaches a store, so it has no row to compare, and the fixtures' own raw
        // reads cannot even look it up on every engine (PostgreSQL fails on NUL), so they are not asked.
        if (_IsUnportable(key))
        {
            return (null, "refused-key");
        }

        try
        {
            return (await _fixture.ReadLeaseAsync(key, cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // See the method comment: the failure is the observation.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return (null, _Describe(e));
        }
    }

    private static bool _IsUnportable(LeaseKey key)
    {
        return !_IsPortable(key.TenantId) || !_IsPortable(key.Kind) || !_IsPortable(key.Resource);

        static bool _IsPortable(string value)
        {
            try
            {
                Argument.IsPortableKey(value);

                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    private FencedLease _Lease(int key, int slot)
    {
        var seen = _generations[key];

        // A slot past what the history saw names a generation no store issues, so every provider must call it stale.
        var generation = slot < seen.Count ? seen[slot] : long.MaxValue - slot;

        return _keys[key].ToLease(generation);
    }

    private int _Ordinal(long generation)
    {
        if (!_ordinals.TryGetValue(generation, out var ordinal))
        {
            ordinal = _ordinals.Count + 1;
            _ordinals[generation] = ordinal;
        }

        return ordinal;
    }

    private static string _Describe(Exception e)
    {
        var type = e.GetType().Name;

        // Read by name so the harness stays free of driver references: Npgsql's SqlState, SqlClient's Number.
        var code = e.GetType().GetProperty("SqlState")?.GetValue(e) ?? e.GetType().GetProperty("Number")?.GetValue(e);

        return code is null ? type : $"{type}({Convert.ToString(code, CultureInfo.InvariantCulture)})";
    }

    public ValueTask DisposeAsync()
    {
        return _host.DisposeAsync();
    }
}
