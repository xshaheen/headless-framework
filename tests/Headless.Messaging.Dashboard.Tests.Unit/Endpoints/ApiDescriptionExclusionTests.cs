// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class ApiDescriptionExclusionTests : TestBase
{
    [Fact]
    public async Task should_exclude_every_dashboard_endpoint_from_application_api_descriptions()
    {
        // The dashboard SPA is the only client of these endpoints, so a host's API document must not list them.
        await using var app = _CreateApp();

        var endpoints = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Should().Contain(["/api/auth/info", "/api/health"]);
        endpoints
            .Should()
            .OnlyContain(endpoint =>
                endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>() != null
                && endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()!.ExcludeFromDescription
            );
    }

    private static WebApplication _CreateApp()
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(config);
        MessagingDashboardHost.AddDashboardAuthentication(builder.Services, config);
        builder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        builder.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        builder.Services.AddSingleton<MessagingDashboardCache>();
        builder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        builder.Services.AddSingleton(new CurrentNodeIdentity("test-node"));
        builder.Services.AddSingleton<GatewayProxyAgent>();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddCors();

        var app = builder.Build();
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }
}
