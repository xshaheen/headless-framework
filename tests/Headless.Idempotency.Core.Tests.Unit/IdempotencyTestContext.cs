// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Builds the Core idempotency services over a substituted record store, lease feature, and tenant.</summary>
internal sealed class IdempotencyTestContext
{
    public const string Key = "order-1";

    public static readonly IdempotencyRecordKey RecordKey = new("t1", Key);

    public static readonly IdempotencyFingerprint Fingerprint = IdempotencyFingerprint.Compute("request-a");

    public static readonly IdempotencyFingerprint OtherFingerprint = IdempotencyFingerprint.Compute("request-b");

    public static readonly FencedLease Lease = new("t1", IdempotentAdmission.LeaseKind, Key, 7);

    public static readonly DateTimeOffset ExpiresAt = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    public IdempotencyTestContext()
    {
        var monitor = Substitute.For<IOptionsMonitor<IdempotentOperationsOptions>>();
        monitor.CurrentValue.Returns(_ => Options);
        Tenant.Id = "t1";

        Resolver = new IdempotencyRequestResolver(Tenant, monitor);
        Feature = new UnitOfWorkIdempotencyFeature(Resolver, Store, Leases);
        Operations = new IdempotentOperations(Feature, Store, FencedLeases, Resolver);
    }

    public IdempotentOperationsOptions Options { get; } = new();

    public MutableCurrentTenant Tenant { get; } = new();

    public IIdempotencyRecordStore Store { get; } = Substitute.For<IIdempotencyRecordStore>();

    public IUnitOfWorkLeases Leases { get; } = Substitute.For<IUnitOfWorkLeases>();

    public IFencedLeases FencedLeases { get; } = Substitute.For<IFencedLeases>();

    public IdempotencyRequestResolver Resolver { get; }

    public UnitOfWorkIdempotencyFeature Feature { get; }

    public IdempotentOperations Operations { get; }

    public TimeSpan Retention => Options.DefaultRetention;

    public TimeSpan LeaseDuration => Options.DefaultLeaseDuration;

    /// <summary>An active unit over a live relational resource: every refusal gate passes.</summary>
    public static (IUnitOfWork Unit, IRelationalUnitOfWorkResource Resource) ActiveUnit(bool isOwned = true)
    {
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsOwned.Returns(isOwned);
        resource.IsTransactionCompleted.Returns(false);
        resource.Transaction.Returns(Substitute.For<DbTransaction>());

        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        return (unit, resource);
    }

    public static IdempotencyRecordState Inserted()
    {
        return new(
            Inserted: true,
            IdempotencyRecordStatus.Pending,
            Fingerprint,
            LeaseGeneration: null,
            Result: null,
            ExpiresAt.AddDays(1),
            IsRetentionElapsed: false
        );
    }

    public static IdempotencyRecordState Pending(
        long? generation,
        IdempotencyFingerprint? fingerprint = null,
        bool isRetentionElapsed = false
    )
    {
        return new(
            Inserted: false,
            IdempotencyRecordStatus.Pending,
            fingerprint ?? Fingerprint,
            generation,
            Result: null,
            ExpiresAt.AddDays(1),
            isRetentionElapsed
        );
    }

    public static IdempotencyRecordState Completed(
        IdempotentResult result,
        IdempotencyFingerprint? fingerprint = null,
        bool isRetentionElapsed = false
    )
    {
        return new(
            Inserted: false,
            IdempotencyRecordStatus.Completed,
            fingerprint ?? Fingerprint,
            LeaseGeneration: 7,
            result,
            ExpiresAt.AddDays(1),
            isRetentionElapsed
        );
    }

    public static IdempotentAdmission Admitted(bool isTakeover = false)
    {
        return IdempotentAdmission.Admitted(
            new IdempotencyKey("t1", Key),
            Fingerprint,
            Lease,
            ExpiresAt,
            isTakeover,
            TimeSpan.FromDays(7)
        );
    }
}

internal sealed class MutableCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => Id is not null;

    public string? Id { get; set; }

    public string? Name => null;

    public IDisposable Change(string? id, string? name = null)
    {
        var previous = Id;
        Id = id;

        return new Restore(this, previous);
    }

    private sealed class Restore(MutableCurrentTenant tenant, string? previous) : IDisposable
    {
        public void Dispose() => tenant.Id = previous;
    }
}
