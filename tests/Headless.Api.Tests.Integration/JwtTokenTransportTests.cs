// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless;
using Headless.Api;
using Headless.Api.Security;
using Headless.Checks;
using Headless.Context;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;

namespace Tests;

/// <summary>
/// Pins the JWT token sources and the refresh-token cookie on a Kestrel host: a token read from the cookie
/// authenticates only a same-origin request, a token read from the query string authenticates only on a listed path,
/// the header keeps working beside both, and a guarded refresh endpoint writes, reads, and deletes the refresh cookie.
/// </summary>
public sealed class JwtTokenTransportTests : TestBase
{
    private const string _SigningKey = "this-is-an-integration-signing-key-of-32-bytes-or-more";
    private const string _Issuer = "integration-issuer";
    private const string _Audience = "integration-audience";
    private const string _CookieName = "access";

    [Fact]
    public async Task should_authenticate_a_same_origin_request_with_the_token_from_the_cookie()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add(HeaderNames.Cookie, $"{_CookieName}={_CreateToken(app)}");
        request.Headers.Add(HeaderNames.Origin, app.Urls.Single());

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("testuser");
    }

    [Fact]
    public async Task should_not_authenticate_a_cross_site_request_with_the_token_from_the_cookie()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add(HeaderNames.Cookie, $"{_CookieName}={_CreateToken(app)}");
        request.Headers.Add(HeaderNames.Origin, "https://evil.example.net");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_authenticate_with_the_token_from_the_query_string_on_a_listed_path()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);
        var token = _CreateToken(app);

        using var hub = await client.GetAsync($"/hubs/me?access_token={token}", AbortToken);
        using var api = await client.GetAsync($"/me?access_token={token}", AbortToken);

        hub.StatusCode.Should().Be(HttpStatusCode.OK);
        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_keep_authenticating_with_the_authorization_header()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new("Bearer", _CreateToken(app));

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_write_read_and_delete_the_refresh_cookie_on_a_guarded_endpoint()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var login = new HttpRequestMessage(HttpMethod.Post, "/session/login");
        login.Headers.Add(HeaderNames.Origin, app.Urls.Single());
        using var loginResponse = await client.SendAsync(login, AbortToken);

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/session/refresh");
        refresh.Headers.Add(HeaderNames.Origin, app.Urls.Single());
        refresh.Headers.Add(HeaderNames.Cookie, "__Host-refresh_token=refresh-value");
        using var refreshResponse = await client.SendAsync(refresh, AbortToken);

        using var crossSite = new HttpRequestMessage(HttpMethod.Post, "/session/refresh");
        crossSite.Headers.Add(HeaderNames.Origin, "https://evil.example.net");
        crossSite.Headers.Add(HeaderNames.Cookie, "__Host-refresh_token=refresh-value");
        using var crossSiteResponse = await client.SendAsync(crossSite, AbortToken);

        var written = SetCookieHeaderValue.Parse(loginResponse.Headers.GetValues(HeaderNames.SetCookie).Single());
        written.Name.ToString().Should().Be("__Host-refresh_token");
        written.Secure.Should().BeTrue();
        written.HttpOnly.Should().BeTrue();
        written.SameSite.Should().Be(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        written.Path.ToString().Should().Be("/");

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await refreshResponse.Content.ReadAsStringAsync(AbortToken)).Should().Be("refresh-value");
        var deleted = SetCookieHeaderValue.Parse(refreshResponse.Headers.GetValues(HeaderNames.SetCookie).Single());
        deleted.Name.ToString().Should().Be("__Host-refresh_token");
        deleted.Expires.Should().BeBefore(DateTimeOffset.UtcNow);

        crossSiteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private static string _CreateToken(WebApplication app)
    {
        return app
            .Services.GetRequiredService<IJwtTokenFactory>()
            .CreateJwtToken(
                [new Claim(UserClaimTypes.UserId, "user-1"), new Claim(UserClaimTypes.UserName, "testuser")],
                new JwtTokenRequest
                {
                    Ttl = TimeSpan.FromMinutes(5),
                    SigningKey = _SigningKey,
                    Issuer = _Issuer,
                    Audience = _Audience,
                }
            );
    }

    private async Task<WebApplication> _CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.TryAddSingleton<IClaimsPrincipalFactory, ClaimsPrincipalFactory>();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddHeadlessRefreshTokenCookie(_ => { });
        builder
            .Services.AddAuthentication(AuthenticationConstants.Schemas.Bearer)
            .AddHeadlessJwtBearer(options =>
            {
                options.SigningKey = _SigningKey;
                options.Issuer = _Issuer;
                options.Audience = _Audience;
                options.TokenSources = JwtTokenSources.Header | JwtTokenSources.Cookie | JwtTokenSources.QueryString;
                options.CookieName = _CookieName;
                options.QueryPathPrefixes = ["/hubs"];
            });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ClaimsPrincipal user) => user.Identity!.Name).RequireAuthorization();
        app.MapGet("/hubs/me", (ClaimsPrincipal user) => user.Identity!.Name).RequireAuthorization();

        var session = app.MapGroup("/session").RequireSameOrigin();
        session.MapPost(
            "/login",
            (IRefreshTokenCookie cookie, TimeProvider time) =>
            {
                cookie.Write("refresh-value", time.GetUtcNow().AddDays(14));
                return Results.NoContent();
            }
        );
        session.MapPost(
            "/refresh",
            (IRefreshTokenCookie cookie) =>
            {
                var value = cookie.Read();
                cookie.Delete();
                return value;
            }
        );

        await app.StartAsync(AbortToken);

        return app;
    }
}
