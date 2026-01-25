// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class HealthEndpointTests : TestBase
{
    [Fact]
    public async Task Health_should_return_OK()
    {
        // given
        var context = _CreateHttpContext();
        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.Health(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Be("OK");
    }

    [Fact]
    public async Task Health_should_not_require_authentication()
    {
        // given
        var context = _CreateHttpContext();
        // No user/identity set - simulating anonymous request

        var options = new DashboardOptions { AllowAnonymousExplicit = false, AuthorizationPolicy = "Admin" };
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when - health endpoint works without auth
        await provider.Health(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Be("OK");
    }

    private static DefaultHttpContext _CreateHttpContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        return context;
    }
}
