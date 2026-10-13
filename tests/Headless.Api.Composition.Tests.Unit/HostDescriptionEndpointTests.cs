// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Headless.Api.ServiceDefaults;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class HostDescriptionEndpointTests : TestBase
{
    private const string _Pattern = "/_headless/describe";
    private const string _OperatorPolicy = "operator";
    private const string _RoleHeader = "X-Test-Role";

    [Fact]
    public async Task should_challenge_an_unauthenticated_caller_when_the_app_has_an_authentication_scheme()
    {
        // given
        await using var app = await _StartAppAsync(endpoints => endpoints.MapHeadlessHostDescription(_Pattern));
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync(_Pattern, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotContain("schemaVersion");
    }

    [Fact]
    public async Task should_admit_any_authenticated_caller_when_no_policy_is_named()
    {
        // given
        await using var app = await _StartAppAsync(endpoints => endpoints.MapHeadlessHostDescription(_Pattern));
        using var client = _CreateClient(app, role: "reader");

        // when
        using var response = await client.GetAsync(_Pattern, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_forbid_an_authenticated_caller_who_fails_the_named_policy()
    {
        // given
        await using var app = await _StartAppAsync(endpoints =>
            endpoints.MapHeadlessHostDescription(_Pattern, _OperatorPolicy)
        );
        using var client = _CreateClient(app, role: "reader");

        // when
        using var response = await client.GetAsync(_Pattern, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotContain("schemaVersion");
    }

    [Fact]
    public async Task should_serve_the_description_to_a_caller_who_satisfies_the_named_policy()
    {
        // given
        await using var app = await _StartAppAsync(endpoints =>
            endpoints.MapHeadlessHostDescription(_Pattern, _OperatorPolicy)
        );
        using var client = _CreateClient(app, role: _OperatorPolicy);

        // when
        using var response = await client.GetAsync(_Pattern, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be("""{"schemaVersion":1,"sections":{"jobs":{"name":"jobs"},"messaging":{"name":"messaging"}}}""");
    }

    [Fact]
    public async Task should_serve_the_description_anonymously_only_when_the_caller_opts_out_of_authorization()
    {
        // given
        await using var app = await _StartAppAsync(endpoints =>
            endpoints.MapHeadlessHostDescription(_Pattern).AllowAnonymous()
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync(_Pattern, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be("""{"schemaVersion":1,"sections":{"jobs":{"name":"jobs"},"messaging":{"name":"messaging"}}}""");
    }

    [Fact]
    public async Task should_return_a_fixed_problem_without_the_exception_text_when_a_contributor_throws()
    {
        // given
        const string secret = "Server=db.internal;Password=hunter2";
        await using var app = await _StartAppAsync(
            endpoints => endpoints.MapHeadlessHostDescription(_Pattern).AllowAnonymous(),
            services =>
                services.AddSingleton<IHostDescriptionContributor>(
                    new StubContributor(
                        "outbox",
                        writer =>
                        {
                            writer.WriteStartObject();
                            throw new InvalidOperationException(secret);
                        }
                    )
                )
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync(_Pattern, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain("hunter2").And.NotContain("outbox").And.NotContain("schemaVersion");

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        problem.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private async Task<WebApplication> _StartAppAsync(
        Action<WebApplication> map,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder
            .Services.AddAuthentication(HeaderRoleAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderRoleAuthenticationHandler>(
                HeaderRoleAuthenticationHandler.SchemeName,
                _ => { }
            );
        builder.Services.AddAuthorizationBuilder().AddPolicy(_OperatorPolicy, p => p.RequireRole(_OperatorPolicy));

        builder.Services.AddSingleton<IHostDescriptionContributor>(new StubContributor("messaging"));
        builder.Services.AddSingleton<IHostDescriptionContributor>(new StubContributor("jobs"));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        map(app);

        await app.StartAsync(AbortToken);

        return app;
    }

    private static HttpClient _CreateClient(WebApplication app, string? role = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(_RoleHeader, role);
        }

        return client;
    }

    private sealed class StubContributor(string sectionName, Action<Utf8JsonWriter>? write = null)
        : IHostDescriptionContributor
    {
        public string SectionName => sectionName;

        public void WriteSection(Utf8JsonWriter writer)
        {
            if (write is not null)
            {
                write(writer);

                return;
            }

            writer.WriteStartObject();
            writer.WriteString("name", sectionName);
            writer.WriteEndObject();
        }
    }

    /// <summary>Authenticates a request that carries a role header, so a test picks the caller's role.</summary>
    private sealed class HeaderRoleAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HeaderRole";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(_RoleHeader, out var role) || string.IsNullOrEmpty(role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "caller"), new Claim(ClaimTypes.Role, role.ToString())],
                SchemeName
            );

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName))
            );
        }
    }
}
