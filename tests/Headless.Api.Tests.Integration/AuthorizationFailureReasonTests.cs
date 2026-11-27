// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

public sealed class AuthorizationFailureReasonTests : TestBase
{
    private const string _LockedReason = "The account is locked.";

    [Fact]
    public async Task should_show_the_failure_reason_when_a_custom_result_handler_is_registered_after_add_headless()
    {
        // given: the last IAuthorizationMiddlewareResultHandler registration wins, so a handler the application
        // registers after AddHeadless used to replace the framework's wrapper and drop the reason.
        var handler = new CountingResultHandler();
        await using var app = await _CreateAppAsync(configureAfter: services =>
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(handler)
        );

        // when
        using var response = await _GetLockedAsync(app);

        // then
        await _AssertLockedForbiddenAsync(response);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task should_show_the_failure_reason_when_a_custom_result_handler_is_registered_before_add_headless()
    {
        // given
        var handler = new CountingResultHandler();
        await using var app = await _CreateAppAsync(configureBefore: services =>
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(handler)
        );

        // when
        using var response = await _GetLockedAsync(app);

        // then
        await _AssertLockedForbiddenAsync(response);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task should_register_one_policy_evaluator_wrapped_once_when_the_setup_runs_again()
    {
        // given
        await using var app = await _CreateAppAsync(configureAfter: services =>
            services.AddStatusCodesRewriterMiddleware()
        );

        // when
        using var response = await _GetLockedAsync(app);

        // then: one evaluator registration, decorated directly over the framework default.
        var evaluator = app.Services.GetRequiredService<IPolicyEvaluator>();
        evaluator.GetType().Name.Should().Be("AuthorizationFailureReasonPolicyEvaluator");
        _InnerOf(evaluator).Should().BeOfType<PolicyEvaluator>();
        await _AssertLockedForbiddenAsync(response);
    }

    private static async Task<WebApplication> _CreateAppAsync(
        Action<IServiceCollection>? configureBefore = null,
        Action<IServiceCollection>? configureAfter = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        configureBefore?.Invoke(builder.Services);
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });
        builder.Services.AddTestAuthentication(registerForbidScheme: true);
        builder.Services.AddSingleton<IAuthorizationHandler, LockedRequirementHandler>();
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("Locked", policy => policy.RequireAuthenticatedUser().AddRequirements(new LockedRequirement()));
        configureAfter?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseStatusCodesRewriter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/locked", () => Results.Ok()).RequireAuthorization("Locked");

        await app.StartAsync(AbortToken);

        return app;
    }

    private static async Task<HttpResponseMessage> _GetLockedAsync(WebApplication app)
    {
        using var client = HttpTenancyTestHarness.CreateClient(app);
        using var request = HttpTenancyTestHarness.CreateRequest(HttpMethod.Get, "/locked", user: "alice");

        return await client.SendAsync(request, AbortToken);
    }

    private static async Task _AssertLockedForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be(_LockedReason);
    }

    private static object? _InnerOf(IPolicyEvaluator evaluator)
    {
        // A second wrap is invisible in the response, because the first rejection set wins, so the shape is read
        // directly: the decorator must wrap the default evaluator, not another copy of itself.
        // The type is internal and named as a string so this file also compiles against a base without it.
#pragma warning disable REFL003 // False positive: the analyzer reads the static IPolicyEvaluator type, the runtime type is the decorator that declares _inner.
        return evaluator
            .GetType()
            .GetField("_inner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(evaluator);
#pragma warning restore REFL003
    }

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

    private sealed class CountingResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult
        )
        {
            Interlocked.Increment(ref _calls);

            return _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
