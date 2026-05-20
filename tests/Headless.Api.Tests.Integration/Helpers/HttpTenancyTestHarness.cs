// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Helpers;

/// <summary>
/// Shared scaffolding for HTTP tenancy integration tests:
/// <see cref="TestAuthenticationHandler"/>, <see cref="CapturingLoggerProvider"/>, default in-memory security config,
/// and request helpers used by SkipTenantResolutionTests, TenantResolutionMiddlewareTests, and TenantRequirementTests.
/// </summary>
internal static class HttpTenancyTestHarness
{
    public const string Scheme = "Test";
    public const string UserHeader = "X-Test-User";
    public const string TenantHeader = "X-Test-Tenant";
    public const string CustomTenantHeader = "X-Test-Custom-Tenant";
    public const string UnauthenticatedHeader = "X-Test-Unauthenticated";
    public const string CustomTenantClaimType = "custom_tenant_id";

    public static void AddDefaultHeadlessSecurityConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
            new KeyValuePair<string, string?>("Headless:StringHash:DefaultSalt", "TestSalt"),
        ]);
    }

    public static AuthenticationBuilder AddTestAuthentication(
        this IServiceCollection services,
        bool registerForbidScheme = false
    )
    {
        return services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = Scheme;
                options.DefaultChallengeScheme = Scheme;

                if (registerForbidScheme)
                {
                    options.DefaultForbidScheme = Scheme;
                }
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(Scheme, _ => { });
    }

    public static HttpClient CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    public static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string? user = null,
        string? tenantId = null,
        string? customTenantId = null,
        bool unauthenticated = false
    )
    {
        var request = new HttpRequestMessage(method, path);

        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(TenantHeader, tenantId);
        }

        if (customTenantId is not null)
        {
            request.Headers.Add(CustomTenantHeader, customTenantId);
        }

        if (unauthenticated)
        {
            request.Headers.Add(UnauthenticatedHeader, "true");
        }

        return request;
    }
}

internal sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, _entries);
    }

    public void Dispose() { }
}

internal sealed class CapturingLogger(string categoryName, ConcurrentQueue<LogEntry> entries) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        entries.Enqueue(new LogEntry(categoryName, logLevel, eventId, formatter(state, exception)));
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HttpTenancyTestHarness.UserHeader, out var userValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var isUnauthenticated = Request.Headers.ContainsKey(HttpTenancyTestHarness.UnauthenticatedHeader);
        var claims = new List<Claim> { new(UserClaimTypes.Name, userValues.ToString()) };

        if (Request.Headers.TryGetValue(HttpTenancyTestHarness.TenantHeader, out var tenantValues))
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantValues.ToString()));
        }

        if (Request.Headers.TryGetValue(HttpTenancyTestHarness.CustomTenantHeader, out var customTenantValues))
        {
            claims.Add(new Claim(HttpTenancyTestHarness.CustomTenantClaimType, customTenantValues.ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: isUnauthenticated ? null : Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
