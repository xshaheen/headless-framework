// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Api;
using Headless.Caching;
using Headless.Context;
using Headless.Features;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Authorization;

public sealed class FeatureAuthorizationHttpTests : TestBase
{
    private const string _Reports = "Reports";
    private const string _Exports = "Exports";
    private const string _ReportsView = "Reports.View";
    private const string _FeatureUnavailableReason = "This feature is currently unavailable.";
    private const string _LockedReason = "The account is locked.";

    #region MVC

    [Fact]
    public async Task should_return_403_with_the_reason_when_the_controller_feature_is_disabled()
    {
        // given
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, "/reports", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    [Fact]
    public async Task should_run_the_controller_action_when_the_controller_feature_is_enabled()
    {
        // given
        await using var app = await _StartAsync(new HostOptions { EnabledFeatures = [_Reports] });

        // when
        using var response = await _GetAsync(app, "/reports", authenticated: true);

        // then
        await _AssertOkAsync(response, "reports");
    }

    [Fact]
    public async Task should_apply_the_action_requirement_on_top_of_the_controller_requirement()
    {
        // given - the controller feature is on, the action's own feature is off
        await using var app = await _StartAsync(new HostOptions { EnabledFeatures = [_Reports] });

        // when
        using var response = await _GetAsync(app, "/reports/export", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    [Fact]
    public async Task should_run_an_action_that_disables_the_feature_check_when_the_controller_feature_is_disabled()
    {
        // given
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, "/reports/status", authenticated: true);

        // then
        await _AssertOkAsync(response, "status");
    }

    [Fact]
    public async Task should_enforce_a_requirement_added_through_the_controller_route_convention()
    {
        // given - the controller's own feature is on, the convention's feature is off
        await using var app = await _StartAsync(
            new HostOptions { EnabledFeatures = [_Reports], GateControllersOn = _Exports }
        );

        // when
        using var response = await _GetAsync(app, "/reports", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    #endregion

    #region Minimal API

    [Fact]
    public async Task should_return_403_with_the_reason_when_a_minimal_api_feature_is_disabled()
    {
        // given
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, "/minimal", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    [Fact]
    public async Task should_run_a_minimal_api_endpoint_when_its_feature_is_enabled()
    {
        // given
        await using var app = await _StartAsync(new HostOptions { EnabledFeatures = [_Reports] });

        // when
        using var response = await _GetAsync(app, "/minimal", authenticated: true);

        // then
        await _AssertOkAsync(response, "minimal");
    }

    [Fact]
    public async Task should_gate_a_minimal_api_handler_carrying_the_attribute()
    {
        // given
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, "/minimal/attribute", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    [Fact]
    public async Task should_require_every_feature_when_requires_all_is_set()
    {
        // given - only one of the two required features is on
        await using var app = await _StartAsync(new HostOptions { EnabledFeatures = [_Reports] });

        // when
        using var response = await _GetAsync(app, "/minimal/all", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    [Theory]
    [InlineData("/group/gated")]
    [InlineData("/metadata-group/gated")]
    public async Task should_gate_every_endpoint_of_a_route_group(string path)
    {
        // given
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, path, authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    [Theory]
    [InlineData("/group/open-attribute")]
    [InlineData("/group/open-metadata")]
    [InlineData("/metadata-group/open")]
    public async Task should_let_a_group_endpoint_opt_out_of_the_feature_check(string path)
    {
        // given
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, path, authenticated: true);

        // then
        await _AssertOkAsync(response, "open");
    }

    [Fact]
    public void should_reject_an_empty_feature_list()
    {
        // given
        var builder = new AuthorizationPolicyBuilder();

        // when
        var act = () => builder.RequireFeatures();

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Anonymous callers

    [Theory]
    [InlineData("/minimal")]
    [InlineData("/minimal/metadata")]
    [InlineData("/minimal/attribute")]
    public async Task should_challenge_an_anonymous_caller_on_a_disabled_feature(string path)
    {
        // given - like any failed authorization, an anonymous caller is challenged: signing in is its next step
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, path, authenticated: false);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/minimal", "minimal")]
    [InlineData("/minimal/metadata", "metadata")]
    [InlineData("/minimal/attribute", "attribute")]
    public async Task should_let_an_anonymous_caller_through_an_enabled_feature_gate(string path, string body)
    {
        // given - a feature gate is about the tenant's state, so it adds no authenticated-user requirement
        await using var app = await _StartAsync(new HostOptions { EnabledFeatures = [_Reports] });

        // when
        using var response = await _GetAsync(app, path, authenticated: false);

        // then
        await _AssertOkAsync(response, body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_keep_the_fallback_policy_on_a_feature_gated_endpoint(bool featureEnabled)
    {
        // given - the fallback policy requires a user; the feature gate must not replace it
        await using var app = await _StartAsync(
            new HostOptions { EnabledFeatures = featureEnabled ? [_Reports] : [], RequireUserByDefault = true }
        );

        // when
        using var response = await _GetAsync(app, "/minimal/metadata", authenticated: false);

        // then - the challenge wins: an anonymous caller learns nothing about the feature
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_apply_both_the_fallback_policy_and_the_feature_gate_to_an_authenticated_caller()
    {
        // given
        await using var app = await _StartAsync(new HostOptions { RequireUserByDefault = true });

        // when
        using var response = await _GetAsync(app, "/minimal/metadata", authenticated: true);

        // then
        await _AssertFeatureForbiddenAsync(response);
    }

    #endregion

    #region Composition with a permission policy

    [Theory]
    [InlineData(true, true, HttpStatusCode.OK)]
    [InlineData(true, false, HttpStatusCode.Forbidden)]
    [InlineData(false, true, HttpStatusCode.Forbidden)]
    [InlineData(false, false, HttpStatusCode.Forbidden)]
    public async Task should_compose_the_feature_gate_with_a_permission_policy(
        bool granted,
        bool featureEnabled,
        HttpStatusCode expected
    )
    {
        // given - either a missing grant or a disabled feature forbids the request
        await using var app = await _StartAsync(
            new HostOptions { EnabledFeatures = featureEnabled ? [_Reports] : [], Granted = granted }
        );

        // when
        using var response = await _GetAsync(app, "/permission-and-feature", authenticated: true);

        // then
        response.StatusCode.Should().Be(expected);
    }

    #endregion

    #region Failure reasons

    [Fact]
    public async Task should_include_the_feature_reason_when_both_the_grant_and_the_feature_are_missing()
    {
        // given
        await using var app = await _StartAsync(new HostOptions { Granted = false });

        // when
        using var response = await _GetAsync(app, "/permission-and-feature", authenticated: true);

        // then - the permission handler gives no reason; the feature handler's reason is the detail
        await _AssertFeatureForbiddenAsync(response);
    }

    [Fact]
    public async Task should_show_an_application_handler_reason_in_the_detail()
    {
        // given - the generic path: any handler that fails with a reason, not only the feature gate
        await using var app = await _StartAsync(new HostOptions());

        // when
        using var response = await _GetAsync(app, "/locked", authenticated: true);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be(_LockedReason);
    }

    [Fact]
    public async Task should_localize_the_feature_reason_to_the_request_culture()
    {
        // given
        await using var app = await _StartAsync(new HostOptions());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/minimal", UriKind.Relative));
        request.Headers.Add(TestAuthenticationHandler.UserHeader, "alice");
        request.Headers.AcceptLanguage.ParseAdd("ar");

        // when
        using var response = await app.GetTestClient().SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be("هذه الميزة غير متوفرة حاليا.");
    }

    #endregion

    #region Without Headless.Api

    [Theory]
    [InlineData(true, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    public async Task should_fall_back_to_the_default_authorization_failure_without_the_status_codes_rewriter(
        bool authenticated,
        HttpStatusCode expected
    )
    {
        // given
        await using var app = await _StartAsync(new HostOptions { UseHeadlessApi = false });

        // when
        using var response = await _GetAsync(app, "/minimal", authenticated);

        // then
        response.StatusCode.Should().Be(expected);
    }

    #endregion

    #region Fail closed

    [Fact]
    public async Task should_enforce_the_gate_when_the_host_never_calls_use_authorization()
    {
        // given - WebApplication adds the authorization middleware itself once AddHeadlessFeatures registered the
        // authorization services, so no HTTP-specific feature call exists to forget. That implicit middleware runs
        // ahead of every user middleware, the status-codes rewriter included, so the failure stays a bare 403.
        await using var app = await _StartAsync(
            new HostOptions { CallUseAuthorization = false, UseHeadlessApi = false }
        );

        // when
        using var response = await _GetAsync(app, "/reports", authenticated: true);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_refuse_to_start_when_authorization_services_are_missing()
    {
        // given - a Minimal API host that never registers the authorization middleware's services. MVC would add them
        // itself, so the host has no controllers. AddHeadlessFeatures registered the handler services, so WebApplication
        // still adds the authorization middleware, which refuses to run without them instead of skipping the gate.
        var act = async () =>
        {
            await using var app = await _StartAsync(
                new HostOptions
                {
                    AddAuthorization = false,
                    AddControllers = false,
                    CallUseAuthorization = false,
                    UseHeadlessApi = false,
                }
            );
        };

        // when / then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddAuthorization*");
    }

    #endregion

    #region Helpers

    private sealed class HostOptions
    {
        public string[] EnabledFeatures { get; init; } = [];

        public string? GateControllersOn { get; init; }

        public bool Granted { get; init; } = true;

        public bool UseHeadlessApi { get; init; } = true;

        public bool RequireUserByDefault { get; init; }

        public bool AddAuthorization { get; init; } = true;

        public bool CallUseAuthorization { get; init; } = true;

        public bool AddControllers { get; init; } = true;
    }

    private static async Task<WebApplication> _StartAsync(HostOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        // The real registration, so the handler under test is the one AddHeadlessFeatures installs. Only the value
        // source is replaced, keeping the host free of a database and the definition initializer.
        builder.Services.AddHeadlessFeatures(setup =>
            setup.UseEntityFramework<FeaturesDbContext>().DisableStartupInitialization()
        );
        builder.Services.AddDbContextFactory<FeaturesDbContext>(db => db.UseSqlite("Data Source=:memory:"));
        builder.Services.AddSingleton(Substitute.For<ICache>());
        builder.Services.Replace(ServiceDescriptor.Singleton(_CreateFeatureManager(options.EnabledFeatures)));

        builder.Services.AddSingleton(_CreatePermissionManager(options.Granted));
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, LockedRequirementHandler>();

        builder
            .Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { }
            );

        if (options.AddAuthorization)
        {
            builder.Services.AddAuthorization(authorization =>
            {
                authorization.AddPolicy(
                    _ReportsView,
                    policy => policy.AddRequirements(new PermissionRequirement(_ReportsView))
                );

                if (options.RequireUserByDefault)
                {
                    authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                }
            });
        }

        if (options.UseHeadlessApi)
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddHeadlessProblemDetails();
            builder.Services.AddStatusCodesRewriterMiddleware();
        }

        if (options.AddControllers)
        {
            builder.Services.AddControllers().AddApplicationPart(typeof(FeatureAuthorizationHttpTests).Assembly);
        }

        var app = builder.Build();

        if (options.UseHeadlessApi)
        {
            app.UseStatusCodesRewriter();
        }

        // The failure reason is resolved during authorization, so the request culture must be set before it.
        app.UseRequestLocalization(localization =>
            localization.AddSupportedUICultures("en", "ar").SetDefaultCulture("en")
        );

        if (options.CallUseAuthorization)
        {
            // An explicit UseRouting would place routing after the authorization middleware WebApplication adds on
            // its own, so the implicit case leaves routing implicit too.
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
        }

        if (options.AddControllers)
        {
            var controllers = app.MapControllers();

            if (options.GateControllersOn is not null)
            {
                controllers.RequireAuthorization(policy => policy.RequireFeatures(options.GateControllersOn));
            }
        }

        // RequireAuthorization with a policy adds no user requirement, but it replaces the fallback policy; the
        // metadata and attribute shapes below add the feature requirement alone and keep the fallback in force.
        app.MapGet("/minimal", () => "minimal").RequireAuthorization(policy => policy.RequireFeatures(_Reports));
        app.MapGet("/minimal/metadata", () => "metadata").WithMetadata(new RequiresFeatureAttribute(_Reports));
        app.MapGet("/minimal/attribute", [RequiresFeature(_Reports)] () => "attribute");
        app.MapGet("/minimal/all", () => "all")
            .RequireAuthorization(policy => policy.RequireFeatures(requiresAll: true, _Reports, _Exports));
        app.MapGet("/permission-and-feature", () => "both")
            .RequireAuthorization(_ReportsView)
            .RequireAuthorization(policy => policy.RequireFeatures(_Reports));

        var group = app.MapGroup("/group").RequireAuthorization(policy => policy.RequireFeatures(_Reports));
        group.MapGet("/gated", () => "gated");
        group.MapGet("/open-attribute", [DisableFeatureCheck] () => "open");
        group.MapGet("/open-metadata", () => "open").WithMetadata(new DisableFeatureCheckAttribute());

        app.MapGet("/locked", () => "locked")
            .RequireAuthorization(policy => policy.AddRequirements(new LockedRequirement()));

        var metadataGroup = app.MapGroup("/metadata-group").WithMetadata(new RequiresFeatureAttribute(_Reports));
        metadataGroup.MapGet("/gated", () => "gated");
        metadataGroup.MapGet("/open", () => "open").WithMetadata(new DisableFeatureCheckAttribute());

        try
        {
            await app.StartAsync(AbortToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static IFeatureManager _CreateFeatureManager(string[] enabledFeatures)
    {
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager
            .GetAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                var value = enabledFeatures.Contains(name, StringComparer.Ordinal) ? "true" : "false";

                return new FeatureValue(name, value, Provider: null);
            });

        return featureManager;
    }

    private static IPermissionManager _CreatePermissionManager(bool granted)
    {
        var permissionManager = Substitute.For<IPermissionManager>();
        permissionManager
            .GetAsync(Arg.Any<string>(), Arg.Any<ICurrentUser>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => new GrantedPermissionResult(call.ArgAt<string>(0), granted));

        return permissionManager;
    }

    private static async Task<HttpResponseMessage> _GetAsync(WebApplication app, string path, bool authenticated)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (authenticated)
        {
            request.Headers.Add(TestAuthenticationHandler.UserHeader, "alice");
        }

        return await app.GetTestClient().SendAsync(request, AbortToken);
    }

    private static async Task _AssertOkAsync(HttpResponseMessage response, string expectedBody)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be(expectedBody);
    }

    private static async Task _AssertFeatureForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be(_FeatureUnavailableReason);
    }

    /// <summary>A gate whose handler fails with a reason the caller should see.</summary>
    private sealed class LockedRequirement : IAuthorizationRequirement;

    private sealed class LockedRequirementHandler : AuthorizationHandler<LockedRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            LockedRequirement requirement
        )
        {
            context.Fail(new AuthorizationFailureReason(this, _LockedReason));

            return Task.CompletedTask;
        }
    }

    private sealed class FeaturesDbContext(DbContextOptions<FeaturesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(this);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], SchemeName);

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName))
            );
        }
    }

    #endregion
}

[ApiController]
[Route("reports")]
[RequiresFeature("Reports")]
public sealed class FeatureGatedReportsController : ControllerBase
{
    [HttpGet]
    public string Get() => "reports";

    [HttpGet("export")]
    [RequiresFeature("Exports")]
    public string Export() => "export";

    [HttpGet("status")]
    [DisableFeatureCheck]
    public string Status() => "status";
}
