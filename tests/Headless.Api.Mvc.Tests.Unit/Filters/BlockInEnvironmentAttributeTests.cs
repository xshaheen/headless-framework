// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Filters;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Filters;

public sealed class BlockInEnvironmentAttributeTests : TestBase
{
    [Fact]
    public async Task should_block_in_matching_environment()
    {
        // given
        var attribute = new BlockInEnvironmentAttribute("Production");
        var env = _CreateEnvironment("Production");
        var context = _CreateResourceExecutingContext("/api/users", env);
        var nextCalled = false;

        // when
        await attribute.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        // then
        nextCalled.Should().BeFalse();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task should_allow_in_non_matching_environment()
    {
        // given
        var attribute = new BlockInEnvironmentAttribute("Production");
        var env = _CreateEnvironment("Development");
        var context = _CreateResourceExecutingContext("/api/users", env);
        var nextCalled = false;

        // when
        await attribute.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        // then
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_be_case_insensitive_for_environment_name()
    {
        // given - IWebHostEnvironment.IsEnvironment is case-insensitive
        var attribute = new BlockInEnvironmentAttribute("PRODUCTION");
        var env = _CreateEnvironment("production");
        var context = _CreateResourceExecutingContext("/api/users", env);
        var nextCalled = false;

        // when
        await attribute.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        // then - should match due to case-insensitivity
        nextCalled.Should().BeFalse();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void should_expose_Environment_property()
    {
        // given
        var attribute = new BlockInEnvironmentAttribute("Production");

        // when
        var environment = attribute.Environment;

        // then
        environment.Should().Be("Production");
    }

    #region Helper Methods

    private static ResourceExecutingContext _CreateResourceExecutingContext(
        string path,
        IWebHostEnvironment? env = null,
        IProblemDetailsCreator? problemDetailsCreator = null
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        var services = new ServiceCollection();
        services.AddSingleton(env ?? _CreateEnvironment("Development"));
        services.AddSingleton(problemDetailsCreator ?? _CreateProblemDetailsCreator());
        services.AddLogging();
        services.AddProblemDetails();
        httpContext.RequestServices = services.BuildServiceProvider();
        httpContext.Response.Body = new MemoryStream();

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(actionContext, [], []);
    }

    private static IWebHostEnvironment _CreateEnvironment(string environmentName)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);

        var buildInformationAccessor = Substitute.For<IBuildInformationAccessor>();
        buildInformationAccessor.GetBuildNumber().Returns("test-build");
        buildInformationAccessor.GetCommitNumber().Returns("test-commit");

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var apiBehaviorOptions = Options.Create(new ApiBehaviorOptions());

        return new ProblemDetailsCreator(
            timeProvider,
            buildInformationAccessor,
            httpContextAccessor,
            apiBehaviorOptions
        );
    }

    #endregion
}
