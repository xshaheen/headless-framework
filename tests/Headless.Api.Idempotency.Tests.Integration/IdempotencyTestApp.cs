// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Caching;
using Headless.Core;
using Headless.DistributedLocks;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Minimal test harness: wires the idempotency middleware against an in-memory cache,
/// exposes endpoints that exercise specific response shapes the integration tests assert on.
/// </summary>
internal static class IdempotencyTestApp
{
    public static async Task<WebApplication> CreateAsync(
        Action<IdempotencyOptions>? configure = null,
        Action<WebApplication>? mapAdditionalEndpoints = null,
        string? tenantHeaderName = null,
        bool withLockProvider = false,
        TestHandlerGate? handlerGate = null,
        InMemoryDistributedLockProvider? lockProvider = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddRouting();
        builder.Services.AddProblemDetails();

        // Framework primitives required by IdempotencyMiddleware constructor
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IClock, Clock>();
        builder.Services.TryAddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();
        builder.Services.TryAddSingleton<IBuildInformationAccessor, NullBuildInformationAccessor>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();

        // Tenant: a context-driven singleton so tests can switch tenants per request
        var tenantState = new TestTenantState(tenantHeaderName);
        builder.Services.AddSingleton(tenantState);
        builder.Services.AddScoped<ICurrentTenant>(_ => tenantState.CurrentForRequest());

        // Current user: a context-driven singleton so tests can swap user identity
        // per request. Defaults to an authenticated test user — the default cache-key
        // composition refuses idempotency when both tenant and user are absent.
        var userState = new TestCurrentUserState();
        builder.Services.AddSingleton(userState);
        builder.Services.AddScoped<ICurrentUser>(_ => userState.CurrentForRequest());

        // In-memory cache (no Redis dependency for v1)
        builder.Services.AddInMemoryCache();

        // Optional in-memory distributed-lock provider for WaitAndReplay tests. Built on a
        // SemaphoreSlim-per-resource map; the production wiring (DistributedLockProvider in
        // Headless.DistributedLocks.Core) depends on IOutboxPublisher which would force the
        // tests to spin up the messaging infrastructure. The middleware only exercises
        // TryAcquireAsync + IDistributedLock.DisposeAsync, so the test double covers exactly
        // the surface under test.
        if (lockProvider is not null)
        {
            builder.Services.AddSingleton<IDistributedLockProvider>(lockProvider);
        }
        else if (withLockProvider)
        {
            builder.Services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();
        }

        // Optional handler gate for concurrency tests (AE3, AE4). When the gate is supplied
        // the default /echo endpoint awaits the gate before completing, so tests can hold the
        // winner mid-request while spawning the loser.
        if (handlerGate is not null)
        {
            builder.Services.AddSingleton(handlerGate);
        }

        // Idempotency
        builder.Services.AddIdempotency(o =>
        {
            o.InFlightStrategy = InFlightStrategy.Reject;
            configure?.Invoke(o);
        });

        // Tests can override or replace any of the above registrations (e.g., swap ICache for a
        // throwing decorator). Runs after the default wiring so Replace<TService>() works.
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        // Tenant resolution from a custom header BEFORE idempotency (so the cache-key sees the tenant)
        if (tenantHeaderName is not null)
        {
            app.Use(
                async (ctx, next) =>
                {
                    if (ctx.Request.Headers.TryGetValue(tenantHeaderName, out var t) && !string.IsNullOrWhiteSpace(t))
                    {
                        tenantState.SetForCurrentRequest(t.ToString());
                    }
                    else
                    {
                        tenantState.SetForCurrentRequest(null);
                    }

                    await next();
                }
            );
        }

        app.UseIdempotency();

        // Default endpoints used by most tests
        app.MapPost(
            "/echo",
            async (HttpContext ctx, [FromServices] TestHandlerGate? gate) =>
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();

                // When a gate is registered, count this invocation and wait for the test to
                // release. Tests use this to hold the winner mid-handler so a concurrent
                // request observes the InFlight state.
                if (gate is not null)
                {
                    gate.OnHandlerEntered();
                    await gate.WaitForReleaseAsync(ctx.RequestAborted).ConfigureAwait(false);
                }

                // Per-invocation GUID embedded in the body — replay returns the cached GUID,
                // so byte-equality across two retries proves the handler ran exactly once.
                var invocationId = Guid.NewGuid();
                ctx.Response.StatusCode = StatusCodes.Status201Created;
                ctx.Response.Headers.Location = "/echo/1";
                ctx.Response.Headers.ContentType = "application/json";
                ctx.Response.Headers.Append("Set-Cookie", "session=abc; HttpOnly");
                ctx.Response.Headers.Append("traceparent", "00-deadbeef-1-00");
                await ctx.Response.WriteAsync($"{{\"invocation\":\"{invocationId}\",\"echo\":\"{body}\"}}");
            }
        );

        app.MapPost(
            "/status",
            (HttpContext ctx, [FromQuery] int code) =>
            {
                ctx.Response.StatusCode = code;
                return Task.CompletedTask;
            }
        );

        mapAdditionalEndpoints?.Invoke(app);

        await app.StartAsync();
        return app;
    }

    public static HttpClient CreateClient(WebApplication app)
    {
        return new() { BaseAddress = new Uri(app.Urls.Single()) };
    }

    public sealed class TestTenantState(string? tenantHeaderName)
    {
        private readonly AsyncLocal<string?> _current = new();

        public string? HeaderName { get; } = tenantHeaderName;

        public void SetForCurrentRequest(string? id) => _current.Value = id;

        public ICurrentTenant CurrentForRequest() => new TestCurrentTenant(_current.Value);
    }

    private sealed class TestCurrentTenant(string? id) : ICurrentTenant
    {
        public bool IsAvailable => Id is not null;

        public string? Id { get; } = id;

        public string? Name => null;

        public IDisposable Change(string? id, string? name = null) => DisposableFactory.Empty;
    }

    /// <summary>
    /// User identity state for the harness. Defaults to an authenticated test user so existing
    /// tests without explicit tenant context still satisfy the middleware's "tenant OR user
    /// must be set" precondition. Tests that need to exercise the anonymous pass-through
    /// branch (AE8) call <see cref="SetAnonymous"/> BEFORE issuing the request — the change
    /// is observed by the next scoped <see cref="ICurrentUser"/> resolution. The flag is
    /// app-level (a singleton field), not request-local, because AsyncLocal does not flow
    /// across the HTTP request boundary in Kestrel's pipeline.
    /// </summary>
    public sealed class TestCurrentUserState
    {
        private static readonly UserId _DefaultUserId = new("test-user");
        private bool _anonymous;

        public void SetAnonymous() => _anonymous = true;

        public void SetAuthenticated() => _anonymous = false;

        public ICurrentUser CurrentForRequest() =>
            _anonymous ? new NullCurrentUser() : new TestCurrentUser(_DefaultUserId);
    }

    private sealed class TestCurrentUser(UserId? userId) : ICurrentUser
    {
        public System.Security.Claims.ClaimsPrincipal? Principal => null;

        public bool IsAuthenticated => UserId is not null;

        public UserId? UserId { get; } = userId;

        public string? AccountType => null;

        public AccountId? AccountId => null;

        public IReadOnlySet<string> Roles => ImmutableHashSet<string>.Empty;
    }

    private sealed class NullBuildInformationAccessor : IBuildInformationAccessor
    {
        public string? GetBuildNumber() => null;

        public string? GetCommitNumber() => null;

        public string? GetTitle() => null;

        public string? GetProduct() => null;

        public string? GetDescription() => null;

        public string? GetCompany() => null;
    }

    /// <summary>
    /// Gate the test uses to hold the winner mid-handler while the loser observes the InFlight
    /// state. Tests increment a counter as handler invocations enter and await a TCS until the
    /// test signals release.
    /// </summary>
    public sealed class TestHandlerGate
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        /// <summary>Number of handler invocations that have entered. Read by tests after Task.WhenAll.</summary>
        public int InvocationCount => Volatile.Read(ref _entered);

        public void OnHandlerEntered() => Interlocked.Increment(ref _entered);

        public async Task WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                _release
            );
            await _release.Task.ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult();

        /// <summary>
        /// Polls until at least <paramref name="count"/> handler invocations have entered, or the
        /// timeout elapses. Used by tests to ensure the loser has reached the InFlight check
        /// before releasing the winner.
        /// </summary>
        public async Task WaitForInvocationsAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (InvocationCount < count && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(20).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// In-memory <see cref="IDistributedLockProvider"/> that models lease expiry. Each resource
    /// tracks an owner lock-id and an absolute expiration timestamp; <c>TryAcquireAsync</c>
    /// considers the slot free either when no owner is set OR when the current owner's lease has
    /// elapsed (lock stealing). Releasing a lock whose lease already expired is a silent no-op so
    /// the original holder cannot disturb a successor.
    ///
    /// Why model expiry: production lock providers (Redis SET NX PX, ZooKeeper ephemeral nodes)
    /// always allow another caller to acquire a previously-leased resource once the lease elapses,
    /// even if the original holder has not released. A pure semaphore-per-resource double hides
    /// this behavior — handler code that depends on lease semantics (e.g., short
    /// <c>WinnerLockLease</c> values, lease-shorter-than-handler-runtime bugs) passes integration
    /// tests against a semaphore double and fails in production against Redis.
    ///
    /// Implements only TryAcquireAsync + IDistributedLock.DisposeAsync — the surface the
    /// idempotency middleware actually uses. Other interface methods throw NotSupportedException.
    /// </summary>
    internal sealed class InMemoryDistributedLockProvider(TimeProvider timeProvider) : IDistributedLockProvider
    {
        internal sealed class LockSlot(TimeProvider timeProvider)
        {
            private readonly Lock _sync = new();
            private string _ownerLockId = string.Empty;
            private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

            public string? TryAcquireOrSteal(TimeSpan lease)
            {
                lock (_sync)
                {
                    var now = timeProvider.GetUtcNow();
                    if (_ownerLockId.Length != 0 && now < _expiresAt)
                    {
                        return null;
                    }

                    var newLockId = Guid.NewGuid().ToString("N");
                    _ownerLockId = newLockId;
                    _expiresAt = now + lease;
                    return newLockId;
                }
            }

            public void ReleaseIfOwner(string lockId)
            {
                lock (_sync)
                {
                    if (_ownerLockId == lockId)
                    {
                        _ownerLockId = string.Empty;
                        _expiresAt = DateTimeOffset.MinValue;
                    }
                }
            }
        }

        private readonly ConcurrentDictionary<string, LockSlot> _slots = new(StringComparer.Ordinal);

        /// <summary>
        /// Test hook fired before the lock state is evaluated. Receives the resource name, the
        /// lease (<c>timeUntilExpires</c>), the acquire timeout, and the cancellation token.
        /// Tests use this to widen specific race windows (e.g., the WaitAndReplay
        /// TryInsert→TryAcquire race), capture argument values for assertions, or throw to
        /// simulate provider outages. No mutual exclusion is held during the hook.
        /// </summary>
        public Func<string, TimeSpan?, TimeSpan?, CancellationToken, Task>? BeforeAcquireAsync { get; init; }

        public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

        public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

        public async Task<IDistributedLock> AcquireAsync(
            string resource,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            bool releaseOnDispose = true,
            CancellationToken cancellationToken = default
        )
        {
            return await TryAcquireAsync(
                        resource,
                        timeUntilExpires,
                        acquireTimeout,
                        releaseOnDispose,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
                ?? throw new LockAcquisitionTimeoutException(resource);
        }

        public async Task<IDistributedLock?> TryAcquireAsync(
            string resource,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            bool releaseOnDispose = true,
            CancellationToken cancellationToken = default
        )
        {
            if (BeforeAcquireAsync is not null)
            {
                await BeforeAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            var lease = timeUntilExpires ?? DefaultTimeUntilExpires;
            var acquireTimeoutEffective = acquireTimeout ?? DefaultAcquireTimeout;
            var slot = _slots.GetOrAdd(resource, _ => new LockSlot(timeProvider));
            var deadline = timeProvider.GetUtcNow() + acquireTimeoutEffective;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                var acquiredLockId = slot.TryAcquireOrSteal(lease);
                if (acquiredLockId is not null)
                {
                    return new InMemoryDistributedLock(resource, acquiredLockId, slot, timeProvider);
                }

                if (timeProvider.GetUtcNow() >= deadline)
                {
                    return null;
                }

                try
                {
                    await timeProvider.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        public Task<IDistributedLock?> TryAcquireAsync(
            string resource,
            TimeSpan? timeUntilExpires,
            TimeSpan? acquireTimeout,
            CancellationToken cancellationToken
        )
        {
            return TryAcquireAsync(
                resource,
                timeUntilExpires,
                acquireTimeout,
                releaseOnDispose: true,
                cancellationToken: cancellationToken
            );
        }

        public Task<bool> RenewAsync(
            string resource,
            string lockId,
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// <see cref="ICache"/> stand-in that throws <see cref="InvalidOperationException"/> on every
    /// call. Used by the cache-failure integration tests to simulate a hard cache outage. The
    /// idempotency middleware should either fail open (default) or rethrow depending on
    /// <see cref="IdempotencyOptions.OnCacheError"/>.
    /// </summary>
    internal sealed class ThrowingCache : ICache
    {
        private static InvalidOperationException _Boom() => new("simulated cache outage");

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T?>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<bool> UpsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<int> UpsertAllAsync<T>(
            IDictionary<string, T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<bool> TryInsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<bool> TryReplaceAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(
            string key,
            T? expected,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<double> IncrementAsync(
            string key,
            double amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<long> IncrementAsync(
            string key,
            long amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<double> SetIfHigherAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<long> SetIfHigherAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<double> SetIfLowerAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<long> SetIfLowerAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<long> SetAddAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
            string prefix,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
            string prefix,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            throw _Boom();

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
            throw _Boom();

        public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => throw _Boom();

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
            throw _Boom();

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
            string key,
            int? pageIndex = null,
            int pageSize = 100,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => throw _Boom();

        public ValueTask<bool> RemoveIfEqualAsync<T>(
            string key,
            T? expected,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<int> RemoveAllAsync(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw _Boom();

        public ValueTask<long> SetRemoveAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw _Boom();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => throw _Boom();
    }

    internal sealed class InMemoryDistributedLock(
        string resource,
        string lockId,
        InMemoryDistributedLockProvider.LockSlot slot,
        TimeProvider timeProvider
    ) : IDistributedLock
    {
        private int _released;

        public string LockId { get; } = lockId;

        public string Resource { get; } = resource;

        public int RenewalCount => 0;

        public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public Task ReleaseAsync()
        {
            _ReleaseIfStillOwner();
            return Task.CompletedTask;
        }

        public Task<bool> RenewAsync(
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            _ReleaseIfStillOwner();
            return ValueTask.CompletedTask;
        }

        private void _ReleaseIfStillOwner()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            // Silent no-op if our lease already expired and another acquirer took over.
            // This mirrors Redis-style lock providers, where ReleaseAsync uses a Lua script
            // that checks the stored token before deleting — preventing a lapsed holder from
            // releasing a successor's lock.
            slot.ReleaseIfOwner(LockId);
        }
    }
}
