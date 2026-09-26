// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application messages, consumers, and variables the messaging guide's examples assume.

global using static MessagingAmbient;
// Provider SDK types the examples name; a consumer's IDE adds these usings.
global using ConfigurationOptions = StackExchange.Redis.ConfigurationOptions;
global using IsolationLevel = Confluent.Kafka.IsolationLevel;
using System.Diagnostics;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed record OrderPlaced(string OrderId)
{
    public Guid CustomerId { get; init; }
}

public sealed record OrderChanged(Guid OrderId);

public sealed record PaymentProcessed(Guid PaymentId);

public sealed record MetricsUpdated(string Name);

public sealed class OrderProjection : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class OrderWorker : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class PaymentHandler : IConsume<PaymentProcessed>
{
    public ValueTask ConsumeAsync(ConsumeContext<PaymentProcessed> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class MetricsHandler : IConsume<MetricsUpdated>
{
    public ValueTask ConsumeAsync(ConsumeContext<MetricsUpdated> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class MyTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context) { }
}

public sealed class MyCustomTransientException : Exception;

public sealed class MyDistributedLock : IDistributedLock
{
    public TimeProvider TimeProvider => TimeProvider.System;

    public ILogger Logger => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(1);

    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    public Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public Task<bool> RenewAsync(
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DistributedLockInfo?> GetLockInfoAsync(
        string resource,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class MessagingAmbient
{
    public static MessagingSetupBuilder setup => null!;

    public static IBus publisher => null!;

    public static IQueue queue => null!;

    public static OrderChanged order => null!;

    public static Message message => null!;

    public static MediumMessage mediumMessage => null!;

    public static IServiceScope scope => null!;

    public static Exception ex => null!;

    public static string connectionString => null!;

    public static string password => null!;

    public static string tenantId => null!;

    public static ICurrentTenant currentTenant => null!;
}
