// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Headless.Api;
using Headless.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Builds and invokes Minimal API endpoints in memory, with the real Headless problem-details creator.</summary>
internal static class MinimalApiTestHost
{
    public static ServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IBuildInformationAccessor>());
        services.AddHttpContextAccessor();
        services.AddHeadlessProblemDetails();
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    public static DefaultHttpContext CreatePostContext(IServiceProvider services, string json)
    {
        var httpContext = CreateHttpContext(services);
        httpContext.Request.Method = HttpMethods.Post;
        SetJsonBody(httpContext, json);

        return httpContext;
    }

    public static void SetJsonBody(HttpContext httpContext, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        httpContext.Request.Body = new MemoryStream(bytes);
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = bytes.Length;

        // DefaultHttpContext has no server feature saying the request may carry a body, and without one the request
        // delegate factory never reads it. Kestrel reports no body for Content-Length: 0, so mirror that.
        httpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature(bytes.Length > 0));
    }

    private sealed class BodyDetectionFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; } = canHaveBody;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var body = httpContext.Response.Body;
        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        return document.RootElement.Clone();
    }

    public static RouteEndpoint SingleEndpoint(IEndpointRouteBuilder endpoints)
    {
        return endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single();
    }
}

internal sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
{
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    public ICollection<EndpointDataSource> DataSources { get; } = [];

    public IApplicationBuilder CreateApplicationBuilder() => throw new NotSupportedException();
}
