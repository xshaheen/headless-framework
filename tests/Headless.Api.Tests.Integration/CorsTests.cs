// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api;
using Headless.Api.Cors;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Pins the Headless CORS policies through the real CORS middleware on a Kestrel host: native clients such as React
/// Native apps send no Origin and pass untouched, configured browser origins (including an Expo web dev server) are
/// echoed, unknown ones are not, named policies apply per endpoint, and an origin source approves origins at request
/// time only after the static lists miss.
/// </summary>
public sealed class CorsTests : TestBase
{
    private const string _App = "https://app.example.com";
    private const string _ExpoWeb = "http://localhost:8081";
    private const string _TenantDomain = "https://shop.acme.com";

    [Fact]
    public async Task should_serve_a_native_client_that_sends_no_origin()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/data", origin: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("data");
    }

    [Theory]
    [InlineData(_App)]
    [InlineData(_ExpoWeb)]
    public async Task should_echo_a_configured_browser_origin_with_credentials(string origin)
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/data", origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _Header(response, "Access-Control-Allow-Origin").Should().Be(origin);
        _Header(response, "Access-Control-Allow-Credentials").Should().Be("true");
        _Header(response, "Access-Control-Expose-Headers").Should().Be("ETag");
    }

    [Fact]
    public async Task should_not_grant_an_unknown_origin()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/data", "https://evil.example.net");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_a_preflight_from_a_configured_origin()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Options, "/data");
        request.Headers.Add("Origin", _ExpoWeb);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _Header(response, "Access-Control-Allow-Origin").Should().Be(_ExpoWeb);
        _Header(response, "Access-Control-Allow-Methods").Should().Be("PUT");
        _Header(response, "Access-Control-Max-Age").Should().Be("600");
    }

    [Fact]
    public async Task should_apply_a_named_any_origin_policy_to_its_endpoint_only()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var open = await _SendAsync(client, HttpMethod.Get, "/public", "https://any.example.org");
        using var restricted = await _SendAsync(client, HttpMethod.Get, "/data", "https://any.example.org");

        _Header(open, "Access-Control-Allow-Origin").Should().Be("*");
        open.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        restricted.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task should_approve_an_origin_from_the_source_after_the_static_lists_miss()
    {
        var source = new TenantDomainRecorder(_TenantDomain);
        await using var app = await _CreateAppAsync(source);
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/data", _TenantDomain);

        _Header(response, "Access-Control-Allow-Origin").Should().Be(_TenantDomain);
        _Header(response, "Access-Control-Allow-Credentials").Should().Be("true");
        response.Headers.Vary.Should().Contain("Origin");
        source.Calls.Should().Equal(_TenantDomain);
    }

    [Fact]
    public async Task should_approve_a_source_origin_on_preflight()
    {
        var source = new TenantDomainRecorder(_TenantDomain);
        await using var app = await _CreateAppAsync(source);
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Options, "/data");
        request.Headers.Add("Origin", _TenantDomain);
        request.Headers.Add("Access-Control-Request-Method", "DELETE");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _Header(response, "Access-Control-Allow-Origin").Should().Be(_TenantDomain);
    }

    [Fact]
    public async Task should_refuse_an_origin_the_source_declines()
    {
        var source = new TenantDomainRecorder(_TenantDomain);
        await using var app = await _CreateAppAsync(source);
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/data", "https://shop.other.com");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        source.Calls.Should().Equal("https://shop.other.com");
    }

    [Theory]
    [InlineData(_App)]
    [InlineData(null)]
    [InlineData("null")]
    public async Task should_not_consult_the_source_for_static_missing_or_opaque_origins(string? origin)
    {
        var source = new TenantDomainRecorder(_TenantDomain);
        await using var app = await _CreateAppAsync(source);
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/data", origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        source.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_let_one_policys_source_approve_origins_for_another_policy()
    {
        var source = new TenantDomainRecorder(_TenantDomain);
        await using var app = await _CreateAppAsync(source);
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/admin", _TenantDomain);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        source.Calls.Should().BeEmpty();
    }

    private async Task<WebApplication> _CreateAppAsync(TenantDomainRecorder? source = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddHeadlessCors(options =>
        {
            options.AllowedOrigins = [_App, _ExpoWeb];
            options.AllowCredentials = true;
            options.ExposedHeaders = ["ETag"];
            options.MaxAge = TimeSpan.FromMinutes(10);
        });
        builder.Services.AddHeadlessCors("admin", options => options.AllowedOrigins = ["https://admin.example.com"]);
        builder.Services.AddHeadlessCors("public", options => options.AllowAnyOrigin = true);

        if (source is not null)
        {
            builder.Services.AddSingleton(source);
            builder.Services.AddHeadlessCorsOriginSource<TenantDomainSource>();
        }

        var app = builder.Build();

        app.UseRouting();
        app.UseCors();

        app.MapMethods("/data", ["GET", "PUT", "DELETE"], () => "data")
            .RequireCors(HeadlessCorsConstants.RestrictedCors);
        app.MapGet("/admin", () => "admin").RequireCors("admin");
        app.MapGet("/public", () => "public").RequireCors("public");

        await app.StartAsync(AbortToken);

        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private static async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? origin
    )
    {
        using var request = new HttpRequestMessage(method, path);

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await client.SendAsync(request, AbortToken);
    }

    private static string? _Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? string.Join(',', values) : null;
    }

    /// <summary>Approves one origin, as a store of tenant custom domains would, and records every consultation.</summary>
    private sealed class TenantDomainRecorder(string allowedOrigin)
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        public bool Record(string origin)
        {
            lock (_calls)
            {
                _calls.Add(origin);
            }

            return string.Equals(origin, allowedOrigin, StringComparison.Ordinal);
        }
    }

    private sealed class TenantDomainSource(TenantDomainRecorder recorder) : ICorsOriginSource
    {
        public ValueTask<bool> IsOriginAllowedAsync(
            string origin,
            HttpContext context,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(recorder.Record(origin));
        }
    }
}
