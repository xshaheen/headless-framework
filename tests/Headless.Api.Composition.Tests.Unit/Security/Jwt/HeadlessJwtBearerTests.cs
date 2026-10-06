// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Headless.Api;
using Headless.Api.Security;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Security.Jwt;

public sealed class HeadlessJwtBearerTests : TestBase
{
    private const string _SigningKey = "this-is-a-test-signing-key-must-be-32-bytes-or-more";
    private const string _EncryptingKey = "this-is-test-encrypt-key-must-be-64-bytes-for-aes256-cbc-hs512!!";
    private const string _Issuer = "test-issuer";
    private const string _Audience = "test-audience";

    [Fact]
    public async Task should_authenticate_signed_and_encrypted_token_issued_by_factory()
    {
        // given
        await using var app = await _CreateAppAsync(_Configure);
        using var client = _CreateClient(app);
        var token = _CreateToken(TimeProvider.System, TimeSpan.FromMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // when
        using var response = await client.SendAsync(request, AbortToken);
        var body = await response.Content.ReadAsStringAsync(AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("testuser|admin");
    }

    [Fact]
    public async Task should_reject_token_expired_within_default_clock_skew()
    {
        // given - expired one minute ago: ASP.NET Core's default five-minute skew would still accept it
        await using var app = await _CreateAppAsync(_Configure);
        using var client = _CreateClient(app);
        var issuedAt = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-2));
        var token = _CreateToken(issuedAt, TimeSpan.FromMinutes(1));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_reject_token_when_issuer_differs()
    {
        // given
        await using var app = await _CreateAppAsync(options =>
        {
            _Configure(options);
            options.Issuer = "another-issuer";
        });
        using var client = _CreateClient(app);
        var token = _CreateToken(TimeProvider.System, TimeSpan.FromMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_apply_bearer_callback_after_headless_rules()
    {
        // given - the callback reads the token from the query string, as a WebSocket client must send it
        await using var app = await _CreateAppAsync(
            _Configure,
            bearer =>
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Query["access_token"];
                        return Task.CompletedTask;
                    },
                }
        );
        using var client = _CreateClient(app);
        var token = _CreateToken(TimeProvider.System, TimeSpan.FromMinutes(5));

        // when
        using var response = await client.GetAsync($"/me?access_token={token}", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_fail_start_when_signing_key_is_shorter_than_32_bytes()
    {
        // given
        var builder = _CreateBuilder(options =>
        {
            _Configure(options);
            options.SigningKey = "too-short";
        });
        await using var app = builder.Build();

        // when
        var act = () => app.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*SigningKey*");
    }

    private static void _Configure(HeadlessJwtBearerOptions options)
    {
        options.SigningKey = _SigningKey;
        options.EncryptingKey = _EncryptingKey;
        options.Issuer = _Issuer;
        options.Audience = _Audience;
    }

    private static WebApplicationBuilder _CreateBuilder(
        Action<HeadlessJwtBearerOptions> configure,
        Action<JwtBearerOptions>? configureBearer = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder
            .Services.AddAuthentication(AuthenticationConstants.Schemas.Bearer)
            .AddHeadlessJwtBearer(configure, configureBearer: configureBearer);
        builder.Services.AddAuthorization();

        return builder;
    }

    private async Task<WebApplication> _CreateAppAsync(
        Action<HeadlessJwtBearerOptions> configure,
        Action<JwtBearerOptions>? configureBearer = null
    )
    {
        var app = _CreateBuilder(configure, configureBearer).Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(
                "/me",
                (ClaimsPrincipal user) =>
                    $"{user.Identity!.Name}|{string.Join(',', user.FindAll(UserClaimTypes.Roles).Select(x => x.Value))}"
            )
            .RequireAuthorization();

        await app.StartAsync(AbortToken);

        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private static string _CreateToken(TimeProvider timeProvider, TimeSpan ttl)
    {
        var identityOptions = Options.Create(
            new IdentityOptions
            {
                ClaimsIdentity = new ClaimsIdentityOptions
                {
                    UserNameClaimType = UserClaimTypes.UserName,
                    RoleClaimType = UserClaimTypes.Roles,
                },
            }
        );
        var factory = new JwtTokenFactory(new ClaimsPrincipalFactory(identityOptions), timeProvider);

        return factory.CreateJwtToken(
            [
                new Claim(UserClaimTypes.UserId, "user-123"),
                new Claim(UserClaimTypes.UserName, "testuser"),
                new Claim(UserClaimTypes.Roles, "admin"),
            ],
            new JwtTokenRequest
            {
                Ttl = ttl,
                SigningKey = _SigningKey,
                EncryptingKey = _EncryptingKey,
                Issuer = _Issuer,
                Audience = _Audience,
            }
        );
    }
}
