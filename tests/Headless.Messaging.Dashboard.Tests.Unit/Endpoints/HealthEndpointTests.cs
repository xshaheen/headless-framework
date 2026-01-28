// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
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
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        // when
        await RouteActionProvider.Health(context);

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

        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        // when - health endpoint works without auth
        await RouteActionProvider.Health(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Be("OK");
    }

    private static DefaultHttpContext _CreateHttpContext()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };

        return context;
    }
}
