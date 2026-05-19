// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Headless.Api.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

public sealed class TenantAuthorizationMiddlewareResultHandlerTests
{
    [Fact]
    public async Task should_delegate_to_default_handler_when_authorization_succeeds()
    {
        // given
        var handler = _CreateHandler();
        var context = new DefaultHttpContext();
        var nextCalled = false;

        // when
        await handler.HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            _CreatePolicy(),
            PolicyAuthorizationResult.Success()
        );

        // then
        nextCalled.Should().BeTrue();
        context.Response.HasStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_write_tenant_problem_details_when_tenant_requirement_fails()
    {
        // given
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
            Detail = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            Extensions = { ["error"] = HeadlessProblemDetailsConstants.Errors.TenantContextRequired },
        };
        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        var responseBody = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        problemDetailsCreator
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            )
            .Returns(problemDetails);
        var handler = _CreateHandler(problemDetailsCreator);
        var tenantRequirement = new TenantRequirement();
        var failure = AuthorizationFailure.Failed([tenantRequirement]);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = responseBody },
        };
        var nextCalled = false;

        // when
        await handler.HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            _CreatePolicy(),
            PolicyAuthorizationResult.Forbid(failure)
        );

        // then
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        responseBody.Position = 0;
        using var doc = await JsonDocument.ParseAsync(
            responseBody,
            cancellationToken: TestContext.Current.CancellationToken
        );
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status403Forbidden);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
    }

    [Fact]
    public async Task should_write_tenant_problem_details_when_failure_reason_handler_is_tenant_handler()
    {
        // given - TenantRequirementHandler emits failure via context.Fail(reason), which populates
        // FailureReasons rather than FailedRequirements. The result handler must still detect this.
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
            Detail = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            Extensions = { ["error"] = HeadlessProblemDetailsConstants.Errors.TenantContextRequired },
        };
        var responseBody = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            )
            .Returns(problemDetails);
        var handler = _CreateHandler(problemDetailsCreator);
        var tenantHandler = new TenantRequirementHandler(new Headless.Testing.Helpers.TestCurrentTenant());
        var failure = AuthorizationFailure.Failed([
            new AuthorizationFailureReason(tenantHandler, TenantRequirement.FailureReason),
        ]);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = responseBody },
        };

        // when
        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            _CreatePolicy(),
            PolicyAuthorizationResult.Forbid(failure)
        );

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        responseBody.Position = 0;
        using var doc = await JsonDocument.ParseAsync(
            responseBody,
            cancellationToken: TestContext.Current.CancellationToken
        );
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
    }

    [Fact]
    public async Task should_delegate_to_default_handler_when_only_failure_reason_message_matches_without_tenant_requirement()
    {
        // given - guards against discriminator-string collisions. A foreign handler that emits
        // a reason with the same message must NOT be treated as a tenant failure.
        var handler = _CreateHandler();
        var foreignHandler = new ForeignRequirementHandler();
        var failure = AuthorizationFailure.Failed([
            new AuthorizationFailureReason(foreignHandler, TenantRequirement.FailureReason),
        ]);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { })
                .Services.BuildServiceProvider(),
        };

        // when
        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            _CreatePolicy(),
            PolicyAuthorizationResult.Forbid(failure)
        );

        // then - delegated to default handler (returns 403 with empty body), no tenant body.
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_delegate_to_default_handler_when_failure_is_not_tenant_requirement()
    {
        // given
        var handler = _CreateHandler();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { })
                .Services.BuildServiceProvider(),
        };
        var nextCalled = false;

        // when
        await handler.HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            _CreatePolicy(),
            PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([new DenyAnonymousAuthorizationRequirement()]))
        );

        // then
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static TenantAuthorizationMiddlewareResultHandler _CreateHandler(
        IProblemDetailsCreator? problemDetailsCreator = null
    )
    {
        problemDetailsCreator ??= Substitute.For<IProblemDetailsCreator>();

        return new TenantAuthorizationMiddlewareResultHandler(
            new AuthorizationMiddlewareResultHandler(),
            problemDetailsCreator
        );
    }

    private static AuthorizationPolicy _CreatePolicy()
    {
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    private sealed class ForeignRequirement : IAuthorizationRequirement;

    private sealed class ForeignRequirementHandler : AuthorizationHandler<ForeignRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ForeignRequirement requirement
        )
        {
            return Task.CompletedTask;
        }
    }
}
