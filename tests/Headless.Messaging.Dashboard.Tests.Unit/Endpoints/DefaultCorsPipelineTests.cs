// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;

namespace Tests.Endpoints;

/// <summary>
/// The dashboard names its CORS policy on its endpoints only when one was configured. An endpoint with CORS metadata
/// and no CORS middleware throws at request time, and the host adds <c>UseCors</c> only when it wants it, so a host
/// that configured no policy must work without <c>UseCors</c>.
/// </summary>
public sealed class DefaultCorsPipelineTests : TestBase
{
    private const string _AllowedOrigin = "https://ops.example";

    [Fact]
    public async Task should_carry_no_cors_metadata_and_serve_a_host_without_use_cors_when_no_policy_is_configured()
    {
        // given - no SetCorsOrigins / SetCorsPolicy call (the shipped default) and no UseCors in the host
        await using var app = await MessagingDashboardHost.StartAsync(
            dashboard => dashboard.WithNoAuth(),
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // then - no dashboard endpoint names a CORS policy
        _DashboardEndpoints(app)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(e => e.Metadata.GetMetadata<ICorsMetadata>() == null);

        // when - a cross-origin request reaches the API
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/messaging/api/health", UriKind.Relative));
        request.Headers.Add("Origin", _AllowedOrigin);
        using var response = await client.SendAsync(request, AbortToken);

        // then - it is served, without a CORS grant
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("OK");
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task should_grant_only_the_configured_origin_when_the_host_adds_use_cors()
    {
        // given
        await using var app = await MessagingDashboardHost.StartAsync(
            dashboard => dashboard.WithNoAuth().SetCorsOrigins(_AllowedOrigin),
            configurePipeline: host => host.UseCors(),
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        _DashboardEndpoints(app).Should().Contain(e => e.Metadata.GetMetadata<ICorsMetadata>() != null);

        // when
        using var allowed = await _GetHealthFromAsync(client, _AllowedOrigin);
        using var other = await _GetHealthFromAsync(client, "https://other.example");

        // then
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(_AllowedOrigin);
        other.StatusCode.Should().Be(HttpStatusCode.OK);
        other.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private static RouteEndpoint[] _DashboardEndpoints(WebApplication app)
    {
        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(endpoint =>
                    endpoint.RoutePattern.RawText?.StartsWith("/messaging", StringComparison.Ordinal) == true
                ),
        ];
    }

    private static async Task<HttpResponseMessage> _GetHealthFromAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/messaging/api/health", UriKind.Relative));
        request.Headers.Add("Origin", origin);

        return await client.SendAsync(request, AbortToken);
    }
}
