// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Idempotency;
using Headless.Caching;
using Headless.Core;
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
        string? tenantHeaderName = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Test" }
        );

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddRouting();
        builder.Services.AddProblemDetails();

        // Framework primitives required by IdempotencyMiddleware constructor
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddSingleton<IClock, Clock>();
        builder.Services.TryAddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();
        builder.Services.TryAddSingleton<IBuildInformationAccessor, NullBuildInformationAccessor>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();

        // Tenant: a context-driven singleton so tests can switch tenants per request
        var tenantState = new TestTenantState(tenantHeaderName);
        builder.Services.AddSingleton(tenantState);
        builder.Services.AddScoped<ICurrentTenant>(_ => tenantState.CurrentForRequest());

        // In-memory cache (no Redis dependency for v1)
        builder.Services.AddInMemoryCache();

        // Idempotency
        builder.Services.AddIdempotency(o =>
        {
            o.InFlightStrategy = InFlightStrategy.Reject;
            configure?.Invoke(o);
        });

        var app = builder.Build();

        // Tenant resolution from a custom header BEFORE idempotency (so the cache-key sees the tenant)
        if (tenantHeaderName is not null)
        {
            app.Use(async (ctx, next) =>
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
            });
        }

        app.UseIdempotency();

        // Default endpoints used by most tests
        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            // Per-invocation GUID embedded in the body — replay returns the cached GUID,
            // so byte-equality across two retries proves the handler ran exactly once.
            var invocationId = Guid.NewGuid();
            ctx.Response.StatusCode = StatusCodes.Status201Created;
            ctx.Response.Headers.Location = "/echo/1";
            ctx.Response.Headers.ContentType = "application/json";
            ctx.Response.Headers.Append("Set-Cookie", "session=abc; HttpOnly");
            ctx.Response.Headers.Append("traceparent", "00-deadbeef-1-00");
            await ctx.Response.WriteAsync($"{{\"invocation\":\"{invocationId}\",\"echo\":\"{body}\"}}");
        });

        app.MapPost("/status", (HttpContext ctx, [FromQuery] int code) =>
        {
            ctx.Response.StatusCode = code;
            return Task.CompletedTask;
        });

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

    private sealed class NullBuildInformationAccessor : IBuildInformationAccessor
    {
        public string? GetBuildNumber() => null;

        public string? GetCommitNumber() => null;

        public string? GetTitle() => null;

        public string? GetProduct() => null;

        public string? GetDescription() => null;

        public string? GetCompany() => null;
    }
}
