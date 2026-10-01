// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Context for the examples in docs/llms/messaging.md: the namespaces of the Headless packages the guide documents, the
// setup builder an example inside AddHeadlessMessaging(setup => ...) calls, the services an example calls, and
// placeholder application types the examples name but do not declare. An example that declares a type of the same
// name uses its own.

global using Headless.Api;
global using Headless.DistributedLocks;
global using Headless.Messaging;
global using Headless.Messaging.CircuitBreaker;
global using Headless.Messaging.Configuration;
global using Headless.Messaging.Dashboard;
global using Headless.Messaging.Dashboard.K8s;
global using Headless.Messaging.Messages;
global using Headless.Messaging.Testing;
global using Headless.MultiTenancy;
global using Headless.UnitOfWork;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace DocsPrelude
{
    public abstract partial class Ambient
    {
        protected MessagingSetupBuilder setup = null!;
        protected IBus publisher = null!;
        protected IQueue queue = null!;
        protected ICurrentTenant currentTenant = null!;
        protected MediumMessage mediumMessage = null!;
        protected OrderPlaced message = null!;
        protected OrderChanged order = null!;
        protected string password = "";
        protected Exception ex = null!;
    }

    public sealed record OrderPlaced(Guid OrderId)
    {
        public Guid CustomerId { get; init; }
    }

    public sealed record OrderChanged(Guid OrderId);

    public sealed record PlaceOrder(Guid OrderId)
    {
        public Guid CustomerId { get; init; }
    }

    public sealed record FulfilOrderCommand(Guid OrderId);

    public sealed record PriceChanged(string Sku);

    public interface IOrderReadModel
    {
        // Takes object so an example that declares its own OrderPlaced can still pass it.
        ValueTask ApplyAsync(object message, CancellationToken cancellationToken);
    }

    public interface IPriceCacheStore
    {
        ValueTask EvictAsync(string sku, CancellationToken cancellationToken);

        ValueTask ClearAsync(CancellationToken cancellationToken);
    }

    public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options);

    public sealed class BillingDb(DbContextOptions<BillingDb> options) : DbContext(options);

    public sealed class PaymentGatewayUnavailableException : Exception;

    public sealed class MyCustomTransientException : Exception;

    public sealed class MyTagEnricher : IActivityTagEnricher
    {
        public void Enrich(Activity activity, in MessagingEnrichmentContext context) { }
    }

    public sealed class MyDistributedLock : IDistributedLock
    {
        public TimeProvider TimeProvider => throw new NotSupportedException();

        public ILogger Logger => throw new NotSupportedException();

        public TimeSpan DefaultTimeUntilExpires => throw new NotSupportedException();

        public TimeSpan DefaultAcquireTimeout => throw new NotSupportedException();

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
}
