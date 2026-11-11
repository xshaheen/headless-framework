// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Context;
using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveEndpointAuthorizationTests : TestBase
{
    private const string _Permission = "permission";

    public enum Protection
    {
        NamedPolicy,
        PolicyObject,
        RequirementData,
        FallbackPolicy,
    }

    [Theory]
    [InlineData(Protection.NamedPolicy)]
    [InlineData(Protection.PolicyObject)]
    [InlineData(Protection.RequirementData)]
    [InlineData(Protection.FallbackPolicy)]
    public async Task should_end_the_stream_with_a_failure_when_the_permission_is_revoked_between_runs(
        Protection protection
    )
    {
        // given - an open stream of a user who holds the permission the endpoint asks for
        var value = new Shared<int>(1);
        await using var app = await LiveTestApp.StartAsync(
            application =>
            {
                var endpoint = _MapValue(application, "/reports", value);

                _ = protection switch
                {
                    Protection.NamedPolicy => endpoint.RequireAuthorization("CanRead"),
                    Protection.PolicyObject => endpoint.RequireAuthorization(_CanRead()),
                    Protection.RequirementData => endpoint.WithMetadata(new RequiresPermissionAttribute("read")),
                    _ => endpoint,
                };
            },
            AbortToken,
            configureBuilder: builder =>
            {
                var authorization = builder.Services.AddAuthorizationBuilder().AddPolicy("CanRead", _CanRead());

                if (protection is Protection.FallbackPolicy)
                {
                    authorization.SetFallbackPolicy(_CanRead());
                }
            }
        );
        app.Users.Set("alice", (_Permission, "read"));
        using var stream = await app.OpenAsync("/reports", AbortToken, user: "alice");
        await stream.NextResultAsync(AbortToken);

        // when
        app.Users.Set("alice");
        value.Value = 2;
        await app.InvalidateAsync("value", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);
        var afterFailure = await stream.NextEventAsync(AbortToken);

        // then
        failure.GetProperty("status").GetInt32().Should().Be(403);
        failure.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.Unauthorized);
        afterFailure.Should().BeNull("a failure ends the stream");
    }

    [Fact]
    public async Task should_end_the_stream_on_the_next_run_after_the_credential_expired()
    {
        // given - a policy that names no scheme, so only authenticating the default scheme again can notice
        var value = new Shared<int>(1);
        await using var app = await LiveTestApp.StartAsync(
            application => _MapValue(application, "/reports", value).RequireAuthorization(),
            AbortToken
        );
        app.Users.Set("alice");
        using var stream = await app.OpenAsync("/reports", AbortToken, user: "alice");
        await stream.NextResultAsync(AbortToken);

        // when
        app.Users.Expire("alice");
        value.Value = 2;
        await app.InvalidateAsync("value", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);

        // then
        failure.GetProperty("status").GetInt32().Should().Be(401);
        failure.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.Unauthorized);
        (await stream.NextEventAsync(AbortToken)).Should().BeNull("a failure ends the stream");
    }

    [Fact]
    public async Task should_show_the_handler_the_principals_new_claims_through_the_current_user_accessor()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/profile",
                        (ICurrentUser user, ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("profile");

                            return new { nickname = user.Principal?.FindFirst("nickname")?.Value };
                        }
                    )
                    .RequireAuthorization()
                    .WithLiveUpdates(),
            AbortToken
        );
        app.Users.Set("alice", ("nickname", "Al"));
        using var stream = await app.OpenAsync("/profile", AbortToken, user: "alice");
        var first = await stream.NextResultAsync(AbortToken);

        // when
        app.Users.Set("alice", ("nickname", "Alice the Second"));
        await app.InvalidateAsync("profile", AbortToken);
        var second = await stream.NextResultAsync(AbortToken);

        // then
        first.GetProperty("nickname").GetString().Should().Be("Al");
        second.GetProperty("nickname").GetString().Should().Be("Alice the Second");
    }

    [Fact]
    public async Task should_keep_running_an_anonymous_endpoint_for_an_anonymous_caller_under_a_fallback_policy()
    {
        // given
        var value = new Shared<int>(1);
        await using var app = await LiveTestApp.StartAsync(
            application => _MapValue(application, "/public", value).AllowAnonymous(),
            AbortToken,
            configureBuilder: builder => builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(_CanRead())
        );
        using var stream = await app.OpenAsync("/public", AbortToken);
        await stream.NextResultAsync(AbortToken);

        // when
        value.Value = 2;
        await app.InvalidateAsync("value", AbortToken);
        var second = await stream.NextResultAsync(AbortToken);

        // then
        second.GetProperty("value").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task should_show_a_policy_handler_the_request_feature_a_middleware_set_on_every_run()
    {
        // given - a policy that passes only for a request that came through the gateway middleware
        var value = new Shared<int>(1);
        var sightings = new ConcurrentQueue<string?>();
        await using var app = await LiveTestApp.StartAsync(
            application =>
                _MapValue(application, "/behind-gateway", value)
                    .RequireAuthorization(policy => policy.AddRequirements(new GatewayRequirement())),
            AbortToken,
            configureBuilder: builder =>
                builder.Services.AddSingleton<IAuthorizationHandler>(new GatewayHandler(sightings)),
            beforeAuthentication: application =>
                application.Use(
                    (context, next) =>
                    {
                        context.Features.Set(new GatewayFeature("edge-1"));

                        return next(context);
                    }
                )
        );
        app.Users.Set("alice");
        using var stream = await app.OpenAsync("/behind-gateway", AbortToken, user: "alice");
        await stream.NextResultAsync(AbortToken);

        // when
        value.Value = 2;
        await app.InvalidateAsync("value", AbortToken);
        var second = await stream.NextResultAsync(AbortToken);

        // then - the middleware's own check, the first run and the re-run each saw the feature
        second.GetProperty("value").GetInt32().Should().Be(2);
        sightings.Should().Equal("edge-1", "edge-1", "edge-1");
    }

    private static RouteHandlerBuilder _MapValue(WebApplication application, string pattern, Shared<int> value)
    {
        return application
            .MapGet(
                pattern,
                (ILiveQueryDependencies dependencies) =>
                {
                    dependencies.DependsOn("value");

                    return new { value = value.Value };
                }
            )
            .WithLiveUpdates();
    }

    private static AuthorizationPolicy _CanRead()
    {
        return new AuthorizationPolicyBuilder().RequireClaim(_Permission, "read").Build();
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class RequiresPermissionAttribute(string permission) : Attribute, IAuthorizationRequirementData
    {
        public string Permission => permission;

        public IEnumerable<IAuthorizationRequirement> GetRequirements()
        {
            return [new ClaimsAuthorizationRequirement(_Permission, [permission])];
        }
    }

    private sealed record GatewayFeature(string Edge);

    private sealed class GatewayRequirement : IAuthorizationRequirement;

    private sealed class GatewayHandler(ConcurrentQueue<string?> sightings) : AuthorizationHandler<GatewayRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            GatewayRequirement requirement
        )
        {
            var feature = (context.Resource as HttpContext)?.Features.Get<GatewayFeature>();
            sightings.Enqueue(feature?.Edge);

            if (feature is not null)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
