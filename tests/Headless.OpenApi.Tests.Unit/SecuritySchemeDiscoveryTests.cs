// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Tests;

/// <summary>Pins the security schemes published from authentication schemes that describe their own credentials.</summary>
public sealed class SecuritySchemeDiscoveryTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync(
        Action<CredentialOptions> configureKey,
        Action<IServiceCollection>? configure = null
    )
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddAuthentication()
                    .AddScheme<CredentialOptions, CredentialHandler>("Key", configureKey)
                    .AddScheme<CredentialOptions, CredentialHandler>(
                        "Password",
                        options =>
                            options.Credentials = [
                                HttpCredentialDescription.Authorization("Basic", "The user name and password."),
                            ]
                    )
                    // A scheme whose options describe nothing publishes nothing.
                    .AddScheme<CredentialOptions, CredentialHandler>("Silent", _ => { })
                    // Neither do options that do not implement the contract, nor a handler without options.
                    .AddScheme<AuthenticationSchemeOptions, PlainHandler>("Plain", _ => { });
                builder.Services.Configure<AuthenticationOptions>(options =>
                    options.AddScheme("Raw", scheme => scheme.HandlerType = typeof(RawHandler))
                );
                builder.Services.AddHeadlessOpenApi();
                configure?.Invoke(builder.Services);
            },
            app => app.MapGet("/secured", () => TypedResults.Ok("secured")).RequireAuthorization(),
            "v1",
            AbortToken
        );
    }

    [Fact]
    public async Task should_publish_a_scheme_per_credential_of_each_authentication_scheme()
    {
        var document = await _GetDocumentAsync(options =>
            options.Credentials = [
                HttpCredentialDescription.Header("X-Api-Key", "The API key in the header."),
                HttpCredentialDescription.Query("api_key", "The API key in the query."),
            ]
        );

        document["components"]!["securitySchemes"]!.ShouldBeJson(
            """
            {
              "Bearer": {"type":"http","description":"JWT Authorization header using the Bearer scheme.\nEnter Token without any prefix.","scheme":"bearer","bearerFormat":"JWT"},
              "Key": {"type":"apiKey","description":"The API key in the header.","name":"X-Api-Key","in":"header"},
              "KeyQuery": {"type":"apiKey","description":"The API key in the query.","name":"api_key","in":"query"},
              "Password": {"type":"http","description":"The user name and password.","scheme":"basic"}
            }
            """
        );
        document.Operation("/secured", "get")["security"]!.ShouldBeJson(
            """[{"Bearer":[]},{"Key":[]},{"KeyQuery":[]},{"Password":[]}]"""
        );
        document.ShouldBeValidOpenApi("the discovered schemes");
    }

    [Fact]
    public async Task should_let_a_configured_scheme_win_over_a_discovered_scheme_with_the_same_name()
    {
        var document = await _GetDocumentAsync(
            options => options.Credentials = [HttpCredentialDescription.Header("X-Api-Key", "Discovered.")],
            services =>
                services.ConfigureHeadlessOpenApi(options =>
                    options.SecuritySchemes["Key"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = "X-Custom-Key",
                    }
                )
        );

        document["components"]!["securitySchemes"]!
            ["Key"]
            .ShouldBeJson("""{"type":"apiKey","name":"X-Custom-Key","in":"header"}""");
    }

    public sealed class PlainHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    public sealed class RawHandler : IAuthenticationHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;

        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    public sealed class CredentialOptions : AuthenticationSchemeOptions, IHttpCredentialSchemeOptions
    {
        public IReadOnlyList<HttpCredentialDescription> Credentials { get; set; } = [];
    }

    public sealed class CredentialHandler(
        IOptionsMonitor<CredentialOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<CredentialOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
