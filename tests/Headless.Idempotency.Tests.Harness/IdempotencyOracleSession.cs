// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Checks;
using Headless.Idempotency;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// Runs one history's operations against one provider and reports what each step observed, normalized: generations
/// are renumbered in order of first appearance, and the stored record of every key is read after each step.
/// </summary>
public sealed class IdempotencyOracleSession : IAsyncDisposable
{
    // Large enough that one purge deletes every eligible row of the run whatever order the engine visits them in.
    private const int _PurgeLimit = 100_000;

    private readonly IIdempotencyFixture _fixture;
    private readonly IdempotencyHost _host;
    private readonly IdempotencyRecordKey[] _keys;
    private readonly List<IdempotentAdmission>[] _admissions;
    private readonly Dictionary<long, int> _ordinals = [];

    private IdempotencyOracleSession(
        IIdempotencyFixture fixture,
        IdempotencyHost host,
        IdempotencyOracleHistory history,
        string runId
    )
    {
        _fixture = fixture;
        _host = host;
        _keys =
        [
            .. history.Keys.Select(k => new IdempotencyRecordKey(
                IdempotencyOracleGenerator.Tenants[k.TenantIndex] ?? string.Empty,
                IdempotencyOracleGenerator.KeyText(k.KeyIndex, runId)
            )),
        ];
        _admissions = [.. _keys.Select(static _ => new List<IdempotentAdmission>())];
    }

    /// <summary>Builds a host on <paramref name="fixture" /> for one run of <paramref name="history" />.</summary>
    /// <param name="runId">A prefix unique to this run, so its keys touch only its own rows.</param>
    public static async Task<IdempotencyOracleSession> StartAsync(
        IIdempotencyFixture fixture,
        IdempotencyOracleHistory history,
        string runId,
        CancellationToken cancellationToken
    )
    {
        var host = await fixture.CreateHostAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new IdempotencyOracleSession(fixture, host, history, runId);
    }

    public async Task<IdempotencyOracleObservation> ExecuteAsync(
        IdempotencyOracleOp op,
        CancellationToken cancellationToken
    )
    {
        string outcome;
        long? precisionError = null;

        try
        {
            (outcome, precisionError) = op switch
            {
                IdempotencyOracleOp.Admit a => await _AdmitAsync(a, cancellationToken).ConfigureAwait(false),
                IdempotencyOracleOp.Contend c => await _ContendAsync(c, cancellationToken).ConfigureAwait(false),
                IdempotencyOracleOp.AdmitPadded p => (
                    await _AdmitPaddedAsync(p, cancellationToken).ConfigureAwait(false),
                    null
                ),
                IdempotencyOracleOp.EnlistedAdmit e => await _EnlistedAdmitAsync(e, cancellationToken)
                    .ConfigureAwait(false),
                IdempotencyOracleOp.Complete c => (
                    await _CompleteAsync(c, cancellationToken).ConfigureAwait(false),
                    null
                ),
                IdempotencyOracleOp.ContendComplete c => (
                    await _ContendCompleteAsync(c, cancellationToken).ConfigureAwait(false),
                    null
                ),
                IdempotencyOracleOp.SetRecoveryPoint s => (
                    await _SetRecoveryPointAsync(s, cancellationToken).ConfigureAwait(false),
                    null
                ),
                IdempotencyOracleOp.Release r => (
                    "release:"
                        + (
                            await _host
                                .Operations.ReleaseAsync(_Admission(r.Key, r.Slot), cancellationToken)
                                .ConfigureAwait(false)
                        ).ToString(),
                    null
                ),
                IdempotencyOracleOp.Renew r => (await _RenewAsync(r, cancellationToken).ConfigureAwait(false), null),
                IdempotencyOracleOp.FenceAndComplete f => (
                    await _FenceAndCompleteAsync(f, cancellationToken).ConfigureAwait(false),
                    null
                ),
                IdempotencyOracleOp.Peek p => (await _PeekAsync(p, cancellationToken).ConfigureAwait(false), null),
                IdempotencyOracleOp.Purge p => (await _PurgeAsync(p, cancellationToken).ConfigureAwait(false), null),
                IdempotencyOracleOp.AdvanceTime a => (
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
        catch (StaleAdmissionException e)
        {
            outcome = "stale:" + e.Reason;
        }
#pragma warning disable CA1031 // A provider error is an observable outcome to compare, not a harness failure.
        catch (Exception e)
#pragma warning restore CA1031
        {
            outcome = "threw:" + _Describe(e);
        }

        return new IdempotencyOracleObservation(
            $"{outcome} | rows[{await _RowsAsync(cancellationToken).ConfigureAwait(false)}]",
            precisionError
        );
    }

    #region Operations

    private async Task<(string, long?)> _AdmitAsync(IdempotencyOracleOp.Admit op, CancellationToken cancellationToken)
    {
        var key = _keys[op.Key];
        var expected = op.Expect == 0 ? null : IdempotencyOracleGenerator.Contracts[op.Expect - 1];
        IdempotentAdmission admission;

        using (_host.CurrentTenant.Change(key.PublicTenantId))
        {
            admission = await _host
                .Operations.AdmitAsync(
                    key.Key,
                    IdempotencyOracleGenerator.Fingerprints[op.Fingerprint],
                    expected,
                    op.Lease,
                    op.Retention,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return (
            "admit:" + _DescribeAdmission(op.Key, admission),
            await _PrecisionErrorAsync(op.Key, admission, op.Lease, op.Retention, cancellationToken)
                .ConfigureAwait(false)
        );
    }

    private async Task<(string, long?)> _ContendAsync(
        IdempotencyOracleOp.Contend op,
        CancellationToken cancellationToken
    )
    {
        var key = _keys[op.Key];

        // The tenant is ambient per flow: each racer changes it on its own async flow.
        var results = await Task.WhenAll(
                Enumerable
                    .Range(0, op.Racers)
                    .Select(async _ =>
                    {
                        using (_host.CurrentTenant.Change(key.PublicTenantId))
                        {
                            return await _host
                                .Operations.AdmitAsync(
                                    key.Key,
                                    IdempotencyOracleGenerator.Fingerprints[0],
                                    expectedContract: null,
                                    op.Lease,
                                    op.Retention,
                                    cancellationToken
                                )
                                .ConfigureAwait(false);
                        }
                    })
            )
            .ConfigureAwait(false);

        // Which racer wins is scheduling; how many win, and what every other racer was told, is the contract.
        var admitted = results.Where(static r => r.IsAdmitted).ToList();
        var others = results
            .Where(static r => !r.IsAdmitted)
            .GroupBy(r => _DescribeAdmission(op.Key, r), StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => string.Create(CultureInfo.InvariantCulture, $"{g.Count()}x{g.Key}"));
        var winners = string.Join(',', admitted.Select(r => _DescribeAdmission(op.Key, r)));

        return (
            $"contend:admitted={admitted.Count}[{winners}],others=[{string.Join(',', others)}]",
            admitted.Count == 1
                ? await _PrecisionErrorAsync(op.Key, admitted[0], op.Lease, op.Retention, cancellationToken)
                    .ConfigureAwait(false)
                : null
        );
    }

    private async Task<string> _AdmitPaddedAsync(
        IdempotencyOracleOp.AdmitPadded op,
        CancellationToken cancellationToken
    )
    {
        var key = _keys[op.Key];
        var tenant = op.Part == 1 ? (key.PublicTenantId ?? "tenant") + " " : key.PublicTenantId;
        var text = op.Part == 0 ? key.Key + " " : key.Key;

        using (_host.CurrentTenant.Change(tenant))
        {
            var admission = await _host
                .Operations.AdmitAsync(
                    text,
                    IdempotencyOracleGenerator.Fingerprints[0],
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            return "padded-admit:" + admission.Disposition;
        }
    }

    private async Task<(string, long?)> _EnlistedAdmitAsync(
        IdempotencyOracleOp.EnlistedAdmit op,
        CancellationToken cancellationToken
    )
    {
        var key = _keys[op.Key];
        IdempotentAdmission admission;
        var completed = "";

        await using (var unit = await _fixture.BeginUnitAsync(_host, cancellationToken).ConfigureAwait(false))
        {
            using (_host.CurrentTenant.Change(key.PublicTenantId))
            {
                admission = await unit
                    .Unit.Idempotency.AdmitAsync(
                        key.Key,
                        IdempotencyOracleGenerator.Fingerprints[op.Fingerprint],
                        expectedContract: null,
                        op.Lease,
                        op.Retention,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (admission.IsAdmitted && op.CompleteInUnit)
            {
                await unit
                    .Unit.Idempotency.CompleteAsync(
                        admission,
                        _Payload(op.Key, 0),
                        IdempotencyOracleGenerator.Contracts[0],
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
                completed = ",completed";
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

        var described =
            $"enlisted-admit:{_DescribeAdmission(op.Key, admission)}{completed},{(op.Commit ? "commit" : "rollback")}";

        return (
            described,
            op.Commit && !op.CompleteInUnit
                ? await _PrecisionErrorAsync(op.Key, admission, op.Lease, op.Retention, cancellationToken)
                    .ConfigureAwait(false)
                : null
        );
    }

    private async Task<string> _CompleteAsync(IdempotencyOracleOp.Complete op, CancellationToken cancellationToken)
    {
        await _host
            .Operations.CompleteAsync(
                _Admission(op.Key, op.Slot),
                _Payload(op.Key, op.Contract),
                IdempotencyOracleGenerator.Contracts[op.Contract],
                op.Retention,
                cancellationToken
            )
            .ConfigureAwait(false);

        return "complete:ok";
    }

    private async Task<string> _ContendCompleteAsync(
        IdempotencyOracleOp.ContendComplete op,
        CancellationToken cancellationToken
    )
    {
        var admission = _Admission(op.Key, op.Slot);

        var outcomes = await Task.WhenAll(
                Enumerable
                    .Range(0, op.Racers)
                    .Select(async _ =>
                    {
                        try
                        {
                            await _host
                                .Operations.CompleteAsync(
                                    admission,
                                    _Payload(op.Key, 0),
                                    IdempotencyOracleGenerator.Contracts[0],
                                    cancellationToken: cancellationToken
                                )
                                .ConfigureAwait(false);

                            return "ok";
                        }
                        catch (StaleAdmissionException e)
                        {
                            return "stale:" + e.Reason;
                        }
#pragma warning disable CA1031 // A provider error is an observable outcome to compare, not a harness failure.
                        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
                        {
                            return "threw:" + _Describe(e);
                        }
                    })
            )
            .ConfigureAwait(false);

        var counted = outcomes
            .GroupBy(static o => o, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => string.Create(CultureInfo.InvariantCulture, $"{g.Count()}x{g.Key}"));

        return $"contend-complete:[{string.Join(',', counted)}]";
    }

    private async Task<string> _SetRecoveryPointAsync(
        IdempotencyOracleOp.SetRecoveryPoint op,
        CancellationToken cancellationToken
    )
    {
        await _host
            .Operations.SetRecoveryPointAsync(
                _Admission(op.Key, op.Slot),
                IdempotencyOracleGenerator.Points[op.Point],
                new byte[] { (byte)op.Point, 7 },
                "recovery/v1",
                cancellationToken
            )
            .ConfigureAwait(false);

        return "recovery-point:ok";
    }

    private async Task<string> _RenewAsync(IdempotencyOracleOp.Renew op, CancellationToken cancellationToken)
    {
        var renewal = await _host
            .Operations.RenewAsync(_Admission(op.Key, op.Slot), op.Lease, cancellationToken)
            .ConfigureAwait(false);

        return $"renew:{renewal.Status},expiry={(renewal.ExpiresAt is null ? "-" : "+")}";
    }

    private async Task<string> _FenceAndCompleteAsync(
        IdempotencyOracleOp.FenceAndComplete op,
        CancellationToken cancellationToken
    )
    {
        var admission = _Admission(op.Key, op.Slot);
        await using var unit = await _fixture.BeginUnitAsync(_host, cancellationToken).ConfigureAwait(false);

        try
        {
            await unit.Unit.Idempotency.FenceAsync(admission, cancellationToken).ConfigureAwait(false);
        }
        catch (StaleAdmissionException e)
        {
            await unit.RollbackAsync().ConfigureAwait(false);

            return "fence:" + e.Reason;
        }

        await unit
            .Unit.Idempotency.CompleteAsync(
                admission,
                _Payload(op.Key, 1),
                IdempotencyOracleGenerator.Contracts[1],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (op.Commit)
        {
            await unit.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await unit.RollbackAsync().ConfigureAwait(false);
        }

        return $"fence:Current,complete,{(op.Commit ? "commit" : "rollback")}";
    }

    private async Task<string> _PeekAsync(IdempotencyOracleOp.Peek op, CancellationToken cancellationToken)
    {
        var key = _keys[op.Key];

        using (_host.CurrentTenant.Change(key.PublicTenantId))
        {
            var status = await _host.Operations.PeekAsync(key.Key, cancellationToken).ConfigureAwait(false);

            return "peek:" + status.ToString();
        }
    }

    private async Task<string> _PurgeAsync(IdempotencyOracleOp.Purge op, CancellationToken cancellationToken)
    {
        var before = new bool[_keys.Length];

        for (var i = 0; i < _keys.Length; i++)
        {
            before[i] = (await _TryReadAsync(_keys[i], cancellationToken).ConfigureAwait(false)).Row is not null;
        }

        // The store's count includes rows of other runs sharing the database, so only this run's keys are compared.
        await _host.Store.PurgeAsync(op.OlderThan, _PurgeLimit, cancellationToken).ConfigureAwait(false);

        var deleted = new List<string>();

        for (var i = 0; i < _keys.Length; i++)
        {
            if (before[i] && (await _TryReadAsync(_keys[i], cancellationToken).ConfigureAwait(false)).Row is null)
            {
                deleted.Add(string.Create(CultureInfo.InvariantCulture, $"k{i}"));
            }
        }

        return $"purge:[{string.Join(',', deleted)}]";
    }

    private async Task<string> _AdvanceAsync(TimeSpan by, CancellationToken cancellationToken)
    {
        foreach (var key in _keys)
        {
            // Only keys with a readable row: a purge may have deleted one, and the fixtures refuse to age a missing row.
            if ((await _TryReadAsync(key, cancellationToken).ConfigureAwait(false)).Row is not { } row)
            {
                continue;
            }

            await _fixture.ShiftRecordIntoPastAsync(key, by, cancellationToken).ConfigureAwait(false);

            if (row.LeaseExpiresAt is not null)
            {
                await _fixture.ShiftLeaseIntoPastAsync(key, by, cancellationToken).ConfigureAwait(false);
            }
        }

        return "advance";
    }

    #endregion

    #region Observation

    /// <summary>
    /// The precision probe of an admission: an admission sets the lease and, unless an earlier retention reaches
    /// further, the retention from one clock reading, so their stored difference is exactly the requested retention
    /// minus the requested lease. A retention an earlier write left further out is reported as kept instead.
    /// </summary>
    private async Task<long?> _PrecisionErrorAsync(
        int key,
        IdempotentAdmission admission,
        TimeSpan lease,
        TimeSpan retention,
        CancellationToken cancellationToken
    )
    {
        if (
            !admission.IsAdmitted
            || (await _TryReadAsync(_keys[key], cancellationToken).ConfigureAwait(false)).Row is not { } row
            || row.Generation != admission.Generation
            || row.LeaseExpiresAt is not { } expiresAt
        )
        {
            return null;
        }

        var error = (row.RetentionUntil - expiresAt - (retention - lease)).Ticks;

        // A kept retention is minutes further out than this admission's, never within a second of it.
        return Math.Abs(error) < TimeSpan.TicksPerSecond ? error : null;
    }

    private string _DescribeAdmission(int key, IdempotentAdmission admission)
    {
        var builder = new StringBuilder(admission.Disposition.ToString());

        switch (admission.Disposition)
        {
            case IdempotentDisposition.Admitted:
                _admissions[key].Insert(0, admission);
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"(g{_Ordinal(admission.Generation!.Value)},takeover={admission.IsTakeover},rp={admission.RecoveryPoint?.Name ?? "-"})"
                );
                break;
            case IdempotentDisposition.InFlight:
                builder.Append(CultureInfo.InvariantCulture, $"(holder=g{_Ordinal(admission.Generation!.Value)})");
                break;
            case IdempotentDisposition.Replay:
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"({admission.Result!.Contract},{Encoding.UTF8.GetString(admission.Result.Payload.Span)})"
                );
                break;
            default:
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"(stored-fp={_FingerprintIndex(admission.StoredFingerprint)},stored-contract={admission.StoredContract ?? "-"})"
                );
                break;
        }

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
                : $"{row.Status}:g{(row.Generation is { } g ? _Ordinal(g).ToString(CultureInfo.InvariantCulture) : "-")}:lease{(row.LeaseExpiresAt is null ? "-" : "+")}:fp{_FingerprintIndex(row)}:result={_DescribeResult(row)}:rp={row.RecoveryPoint ?? "-"}"
            );
        }

        return string.Join(' ', rows);
    }

    private static string _DescribeResult(StoredRecord row)
    {
        return row.Result is null ? "-" : $"{row.ResultContract}/{Encoding.UTF8.GetString(row.Result)}";
    }

    // A key the database cannot even look up is an observation (a provider that cannot store the key), not a harness
    // failure, so it is reported in the row snapshot and compared like any other outcome.
    private async Task<(StoredRecord? Row, string? Error)> _TryReadAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken
    )
    {
        // A key call validation refuses never reaches a store, so it has no row to compare, and the fixtures' own raw
        // reads cannot even look it up on every engine (PostgreSQL fails on NUL), so they are not asked.
        if (!_IsPortable(key.TenantId) || !_IsPortable(key.Key))
        {
            return (null, "refused-key");
        }

        try
        {
            return (await _fixture.ReadRecordAsync(key, cancellationToken).ConfigureAwait(false), null);
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

    private static bool _IsPortable(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        try
        {
            Argument.IsPortableKey(value);

            return value.Length <= IdempotencyFieldLimits.KeyMaxLength;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    #endregion

    #region Helpers

    private IdempotentAdmission _Admission(int key, int slot)
    {
        var seen = _admissions[key];

        if (slot < seen.Count)
        {
            return seen[slot];
        }

        // A slot past what the history saw names a generation no store issues, so every provider must call it stale.
        var record = _keys[key];

        return IdempotentAdmission.Admitted(
            record.ToKey(),
            IdempotencyOracleGenerator.Fingerprints[0],
            long.MaxValue - slot,
            DateTimeOffset.UnixEpoch,
            isTakeover: false,
            TimeSpan.FromMinutes(1)
        );
    }

    private static ReadOnlyMemory<byte> _Payload(int key, int contract)
    {
        return Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"k{key}-c{contract}"));
    }

    private static string _FingerprintIndex(IdempotencyFingerprint? fingerprint)
    {
        if (fingerprint is null)
        {
            return "-";
        }

        var index = Array.FindIndex(IdempotencyOracleGenerator.Fingerprints, f => f.Equals(fingerprint));

        return index < 0 ? "?" : index.ToString(CultureInfo.InvariantCulture);
    }

    private static string _FingerprintIndex(StoredRecord row)
    {
        return _FingerprintIndex(new IdempotencyFingerprint(row.FingerprintAlgorithm, row.Fingerprint));
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

    #endregion

    public ValueTask DisposeAsync()
    {
        return _host.DisposeAsync();
    }
}
