// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// HF2003 (distributed locks), HF2005 (fenced leases), and HF2006 (idempotency), plus the members of those receivers and
/// of sequences that have no enlisted counterpart and are never reported.
/// </summary>
public sealed class HF2003HF2005HF2006ReceiverTests : TestBase
{
    private const string _Prelude = """
        using System;
        using System.Text.Json.Serialization.Metadata;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.DistributedLocks;
        using Headless.Fencing;
        using Headless.Idempotency;
        using Headless.Sequences;
        using Headless.UnitOfWork;
        using Microsoft.EntityFrameworkCore;

        """;

    [Fact]
    public async Task should_report_lock_acquisition_and_name_the_transaction_locks_without_a_fix()
    {
        const string source =
            _Prelude
            + """
                public sealed class Handler(IDistributedLock locks, IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await using var lease = await locks.AcquireAsync("orders:1", cancellationToken: token);
                            await using var maybe = await locks.TryAcquireAsync("orders:2", cancellationToken: token);
                        }, cancellationToken: ct);
                }
                """;

        var diagnostics = await AnalyzerHarness.AnalyzeAsync(source, AbortToken);

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().AllSatisfy(diagnostic => diagnostic.Id.Should().Be("HF2003"));
        diagnostics
            .Should()
            .AllSatisfy(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("'unit.TransactionLocks'")
            );
        (await AnalyzerHarness.GetFixesAsync(source, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_fenced_lease_changes_and_fix_them_onto_the_unit_leases()
    {
        var fixedSource = await AnalyzerHarness.FixAllAsync(
            _Prelude
                + """
                public sealed class Handler(IFencedLeases leases, IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, FencedLease lease, LeaseProgress progress, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await leases.GrantAsync("import", "file-1", TimeSpan.FromMinutes(1), token);
                            await leases.RenewAsync(lease, TimeSpan.FromMinutes(1), token);
                            await leases.RenewAsync(lease, TimeSpan.FromMinutes(1), progress, token);
                            await leases.SettleAsync(lease, token);
                            await leases.ReleaseAsync(lease, token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        fixedSource
            .Should()
            .Contain("await unit.Leases.GrantAsync(\"import\", \"file-1\", TimeSpan.FromMinutes(1), token);");
        fixedSource.Should().Contain("await unit.Leases.RenewAsync(lease, TimeSpan.FromMinutes(1), token);");
        fixedSource.Should().Contain("await unit.Leases.RenewAsync(lease, TimeSpan.FromMinutes(1), progress, token);");
        fixedSource.Should().Contain("await unit.Leases.SettleAsync(lease, token);");
        fixedSource.Should().Contain("await unit.Leases.ReleaseAsync(lease, token);");
    }

    [Fact]
    public async Task should_report_idempotency_writes_and_fix_them_onto_the_unit_idempotency()
    {
        var fixedSource = await AnalyzerHarness.FixAllAsync(
            _Prelude
                + """
                public sealed record Receipt(int Id);

                public sealed class Handler(IIdempotentOperations idempotency, IUnitOfWorkFactory factory)
                {
                    public Task Handle(
                        DbContext db,
                        IdempotencyFingerprint fingerprint,
                        IdempotentAdmission admission,
                        JsonTypeInfo<Receipt> typeInfo,
                        CancellationToken ct
                    ) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await idempotency.AdmitAsync("order-1", fingerprint, cancellationToken: token);
                            await idempotency.CompleteAsync(admission, ReadOnlyMemory<byte>.Empty, "v1", cancellationToken: token);
                            await idempotency.CompleteAsync(admission, new Receipt(1), typeInfo, "v1", cancellationToken: token);
                            await idempotency.SetRecoveryPointAsync(admission, "charged", ReadOnlyMemory<byte>.Empty, "v1", token);
                            await idempotency.ReleaseAsync(admission, token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        fixedSource
            .Should()
            .Contain("await unit.Idempotency.AdmitAsync(\"order-1\", fingerprint, cancellationToken: token);");
        fixedSource
            .Should()
            .Contain(
                "await unit.Idempotency.CompleteAsync(admission, ReadOnlyMemory<byte>.Empty, \"v1\", cancellationToken: token);"
            );
        fixedSource
            .Should()
            .Contain(
                "await unit.Idempotency.CompleteAsync(admission, new Receipt(1), typeInfo, \"v1\", cancellationToken: token);"
            );
        fixedSource
            .Should()
            .Contain(
                "await unit.Idempotency.SetRecoveryPointAsync(admission, \"charged\", ReadOnlyMemory<byte>.Empty, \"v1\", token);"
            );
        fixedSource.Should().Contain("await unit.Idempotency.ReleaseAsync(admission, token);");
    }

    [Fact]
    public async Task should_not_report_members_without_an_enlisted_counterpart()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            _Prelude
                + """
                public sealed class Handler(
                    IDistributedLock locks,
                    IFencedLeases leases,
                    IIdempotentOperations idempotency,
                    ISequenceGenerator sequences,
                    IUnitOfWorkFactory factory
                )
                {
                    public Task Handle(DbContext db, IdempotentAdmission admission, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await locks.TryUsingAsync("orders:1", () => Task.CompletedTask, cancellationToken: token);
                            await leases.SweepExpiredAsync("import", (lease, sweepUnit, sweepToken) => ValueTask.CompletedTask, 10, token);
                            await leases.PurgeAsync("import", TimeSpan.FromDays(1), token);
                            await idempotency.RenewAsync(admission, TimeSpan.FromMinutes(1), token);
                            await idempotency.PeekAsync("order-1", token);
                            await sequences.NextAsync("invoice", cancellationToken: token);
                            await sequences.ReserveAsync("invoice", 10, cancellationToken: token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        diagnostics.Should().BeEmpty();
    }
}
