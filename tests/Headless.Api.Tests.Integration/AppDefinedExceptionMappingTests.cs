// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json.Nodes;
using Headless;
using Headless.Api;
using Headless.Context;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

/// <summary>
/// An application derives its own typed failures from the framework's status-mapped exceptions, so its code can catch
/// them by name, and the API exception handler answers them exactly as it answers the framework type, with no
/// exception handler of the application's own.
/// </summary>
public sealed class AppDefinedExceptionMappingTests : TestBase
{
    private const string _LogOnlyMessage = "LOG-ONLY-MESSAGE-XYZ";

    [Fact]
    public async Task should_map_an_app_defined_conflict_to_the_same_409_body_as_conflict_exception()
    {
        // given
        var error = new ErrorDescriptor("order_already_shipped", "The order has already shipped.");

        // when
        var (derivedStatus, derivedBody, derivedRaw) = await _SendAsync(() =>
            throw new OrderAlreadyShippedException(error)
        );
        var (baseStatus, baseBody, _) = await _SendAsync(() => throw new ConflictException(error));

        // then
        derivedStatus.Should().Be(HttpStatusCode.Conflict);
        baseStatus.Should().Be(HttpStatusCode.Conflict);
        JsonNode.DeepEquals(derivedBody, baseBody).Should().BeTrue($"{derivedBody} should equal {baseBody}");
        derivedBody["errors"]!.ToJsonString().Should().Contain("order_already_shipped");
        derivedRaw.Should().NotContain(_LogOnlyMessage);
    }

    [Theory]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized)]
    [InlineData("too_many_requests", HttpStatusCode.TooManyRequests)]
    [InlineData("not_found", HttpStatusCode.NotFound)]
    public async Task should_map_an_app_defined_failure_to_the_same_body_as_its_framework_exception(
        string kind,
        HttpStatusCode expectedStatus
    )
    {
        // given
        var error = new ErrorDescriptor($"app_{kind}", "Refused by the application.");
        (Action Derived, Action Framework) endpoints = kind switch
        {
            "forbidden" => (() => throw new AppForbiddenException(error), () => throw new ForbiddenException(error)),
            "unauthorized" => (
                () => throw new AppUnauthorizedException(error),
                () => throw new UnauthorizedException(error)
            ),
            "too_many_requests" => (
                () => throw new AppTooManyRequestsException(error),
                () => throw new TooManyRequestsException(TimeSpan.FromSeconds(30), error)
            ),
            "not_found" => (
                () => throw new AppEntityNotFoundException(),
                () => throw new EntityNotFoundException("Account", "42")
            ),
            _ => throw new InvalidOperationException($"unknown kind {kind}"),
        };

        // when
        var (derivedStatus, derivedBody, derivedRaw) = await _SendAsync(endpoints.Derived);
        var (frameworkStatus, frameworkBody, _) = await _SendAsync(endpoints.Framework);

        // then
        derivedStatus.Should().Be(expectedStatus);
        frameworkStatus.Should().Be(expectedStatus);
        JsonNode.DeepEquals(derivedBody, frameworkBody).Should().BeTrue($"{derivedBody} should equal {frameworkBody}");
        derivedRaw.Should().NotContain(_LogOnlyMessage);
    }

    private async Task<(HttpStatusCode Status, JsonNode Body, string Raw)> _SendAsync(Action endpoint)
    {
        await using var app = await _CreateAppAsync(endpoint);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await client.GetAsync("/throw", AbortToken);

        var raw = await response.Content.ReadAsStringAsync(AbortToken);
        var body = JsonNode.Parse(raw)!;
        // The trace id and timestamp differ per request, so they are the fields the two bodies cannot share.
        body.AsObject().Remove("traceId").Should().BeTrue();
        body.AsObject().Remove("timestamp").Should().BeTrue();
        return (response.StatusCode, body, raw);
    }

    private async Task<WebApplication> _CreateAppAsync(Action endpoint)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.AddHeadlessProblemDetails();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.MapGet(
            "/throw",
            () =>
            {
                endpoint();
                return Results.Ok();
            }
        );

        await app.StartAsync(AbortToken);
        return app;
    }

    // An application's own conflict family, caught by type in its code and answered as a 409 by the framework.
    private abstract class OrderRefusedException(ErrorDescriptor error, string message)
        : ConflictException(message, [error]);

    private sealed class OrderAlreadyShippedException(ErrorDescriptor error)
        : OrderRefusedException(error, _LogOnlyMessage);

    private sealed class AppForbiddenException(ErrorDescriptor error) : ForbiddenException(_LogOnlyMessage, error);

    private sealed class AppUnauthorizedException(ErrorDescriptor error)
        : UnauthorizedException(_LogOnlyMessage, error);

    private sealed class AppTooManyRequestsException(ErrorDescriptor error)
        : TooManyRequestsException(_LogOnlyMessage, TimeSpan.FromSeconds(30), error);

    private sealed class AppEntityNotFoundException() : EntityNotFoundException(_LogOnlyMessage, "Account", "42");
}
