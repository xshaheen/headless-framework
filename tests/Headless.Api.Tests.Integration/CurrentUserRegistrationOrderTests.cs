// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.AuditLog;
using Headless.Context;
using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// EF and relational audit-log storage register a <see cref="NullCurrentUser"/> fallback for hosts without HTTP.
/// A web host must still read the request's user whichever of them was registered first.
/// </summary>
public sealed class CurrentUserRegistrationOrderTests : TestBase
{
    private const string _Scheme = "CurrentUserTest";
    private const string _UserHeader = "X-Test-User-Id";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_read_request_user_when_ef_registered_before_or_after_add_headless(bool efFirst)
    {
        // given
        await using var app = await _StartAppAsync(efFirst);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add(_UserHeader, "user-42");

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("user-42");
        app.Services.GetRequiredService<ICurrentUser>().Should().BeOfType<HttpCurrentUser>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_register_http_current_user_when_relational_audit_log_registered_before_or_after_add_headless(
        bool auditLogFirst
    )
    {
        // given
        var builder = _CreateBuilder();

        if (auditLogFirst)
        {
            _AddRelationalAuditLog(builder.Services);
        }

        _AddHeadless(builder);

        if (!auditLogFirst)
        {
            _AddRelationalAuditLog(builder.Services);
        }

        // when
        // Resolved without starting the host: the audit-log storage would verify its table against a real database.
        await using var app = builder.Build();

        // then
        app.Services.GetRequiredService<ICurrentUser>().Should().BeOfType<HttpCurrentUser>();
        builder
            .Services.Count(descriptor => descriptor.ServiceType == typeof(ICurrentUser) && !descriptor.IsKeyedService)
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_keep_consumer_current_user_when_ef_registered_before_add_headless()
    {
        // given
        var builder = _CreateBuilder();
        var custom = new FixedCurrentUser();
        builder.Services.AddSingleton<ICurrentUser>(custom);
        builder.Services.AddHeadlessDbContextServices();

        // when
        _AddHeadless(builder);
        await using var app = builder.Build();

        // then
        app.Services.GetRequiredService<ICurrentUser>().Should().BeSameAs(custom);
    }

    private static async Task<WebApplication> _StartAppAsync(bool efFirst)
    {
        var builder = _CreateBuilder();

        if (efFirst)
        {
            builder.Services.AddHeadlessDbContextServices();
        }

        _AddHeadless(builder);

        if (!efFirst)
        {
            builder.Services.AddHeadlessDbContextServices();
        }

        builder
            .Services.AddAuthentication(_Scheme)
            .AddScheme<AuthenticationSchemeOptions, UserIdAuthenticationHandler>(_Scheme, _ => { });

        var app = builder.Build();
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
        });
        app.UseAuthentication();
        app.MapHeadlessEndpoints();
        app.MapGet("/me", (ICurrentUser user) => user.UserId?.ToString() ?? "<none>");

        await app.StartAsync(AbortToken);

        return app;
    }

    private static WebApplicationBuilder _CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        return builder;
    }

    private static void _AddHeadless(WebApplicationBuilder builder)
    {
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
    }

    private static void _AddRelationalAuditLog(IServiceCollection services)
    {
        services.AddHeadlessAuditLog(setup => setup.UsePostgreSql("Host=localhost;Database=registration_only"));
    }

    private sealed class UserIdAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(_UserHeader, out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(UserClaimTypes.UserId, userId.ToString())], Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), Scheme.Name)));
        }
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public ClaimsPrincipal? Principal => null;

        public bool IsAuthenticated => false;

        public UserId? UserId => null;

        public string? AccountType => null;

        public AccountId? AccountId => null;

        public IReadOnlySet<string> Roles => new HashSet<string>(StringComparer.Ordinal);
    }
}
