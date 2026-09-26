// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Idempotency;
using Headless.Core;
using Headless.MultiTenancy;
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
/// Minimal test harness: wires the idempotency middleware against a real durable store (PostgreSQL or SQL Server,
/// chosen by the caller's fixture) and exposes endpoints that exercise specific response shapes the integration
/// tests assert on.
/// </summary>
internal static class IdempotencyTestApp
{
    public static async Task<WebApplication> CreateAsync(
        Action<IServiceCollection> configureStore,
        Action<IdempotencyOptions>? configure = null,
        Action<WebApplication>? mapAdditionalEndpoints = null,
        string? tenantHeaderName = null,
        TestHandlerGate? handlerGate = null
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddRouting();
        builder.Services.AddProblemDetails();

        // Framework primitives required by IdempotencyMiddleware constructor
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();
        builder.Services.TryAddSingleton<IBuildInformationAccessor, NullBuildInformationAccessor>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();

        // Tenant: a context-driven singleton so tests can switch tenants per request
        var tenantState = new TestTenantState(tenantHeaderName);
        builder.Services.AddSingleton(tenantState);
        builder.Services.AddScoped<ICurrentTenant>(_ => tenantState.CurrentForRequest());

        // Current user: a context-driven singleton so tests can swap user identity
        // per request. Defaults to an authenticated test user — the default key
        // composition refuses idempotency when both tenant and user are absent.
        var userState = new TestCurrentUserState();
        builder.Services.AddSingleton(userState);
        builder.Services.AddScoped<ICurrentUser>(_ => userState.CurrentForRequest());

        // Durable store: fencing + idempotency against the fixture's database.
        configureStore(builder.Services);

        // Optional handler gate for concurrency tests. When the gate is supplied
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

        var app = builder.Build();

        // Tenant resolution from a custom header BEFORE idempotency (so the store key sees the tenant)
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

        // Surfaces the admission a handler reaches through IIdempotencyContext, so tests can assert on it.
        app.MapPost(
            "/context",
            ctx =>
            {
                var idempotency = ctx.GetIdempotencyContext();
                if (idempotency is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    return Task.CompletedTask;
                }

                ctx.Response.Headers["X-Idempotency-Key"] = idempotency.Key;
                ctx.Response.Headers["X-Idempotency-Lease-Resource"] = idempotency.Lease.Resource;
                ctx.Response.Headers["X-Idempotency-Takeover"] = idempotency.IsTakeover.ToString();
                ctx.Response.StatusCode = StatusCodes.Status201Created;
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

        public void SetForCurrentRequest(string? id)
        {
            _current.Value = id;
        }

        public ICurrentTenant CurrentForRequest()
        {
            return new TestCurrentTenant(_current.Value);
        }
    }

    private sealed class TestCurrentTenant(string? id) : ICurrentTenant
    {
        public bool IsAvailable => Id is not null;

        public string? Id { get; } = id;

        public string? Name => null;

        public IDisposable Change(string? id, string? name = null)
        {
            return DisposableFactory.Empty;
        }
    }

    /// <summary>
    /// User identity state for the harness. Defaults to an authenticated test user so existing
    /// tests without explicit tenant context still satisfy the middleware's "tenant OR user
    /// must be set" precondition. Tests that need to exercise the anonymous pass-through
    /// branch call <see cref="SetAnonymous"/> BEFORE issuing the request — the change
    /// is observed by the next scoped <see cref="ICurrentUser"/> resolution. The flag is
    /// app-level (a singleton field), not request-local, because AsyncLocal does not flow
    /// across the HTTP request boundary in Kestrel's pipeline.
    /// </summary>
    public sealed class TestCurrentUserState
    {
        private static readonly UserId _DefaultUserId = new("test-user");
        private bool _anonymous;

        public void SetAnonymous()
        {
            _anonymous = true;
        }

        public void SetAuthenticated()
        {
            _anonymous = false;
        }

        public ICurrentUser CurrentForRequest()
        {
            return _anonymous ? new NullCurrentUser() : new TestCurrentUser(_DefaultUserId);
        }
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
        public string? GetVersion()
        {
            return null;
        }

        public string? GetCommitNumber()
        {
            return null;
        }

        public string? GetTitle()
        {
            return null;
        }

        public string? GetProduct()
        {
            return null;
        }

        public string? GetDescription()
        {
            return null;
        }

        public string? GetCompany()
        {
            return null;
        }
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

        public void OnHandlerEntered()
        {
            Interlocked.Increment(ref _entered);
        }

        public async Task WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            await using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                _release
            );

            await _release.Task.ConfigureAwait(false);
        }

        public void Release()
        {
            _release.TrySetResult();
        }

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
}
