// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class AuthServiceTests : TestBase
{
    private readonly ILogger<AuthService> _logger = Substitute.For<ILogger<AuthService>>();

    [Fact]
    public async Task returns_success_for_none_mode()
    {
        var config = new AuthConfig { Mode = AuthMode.None };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("anonymous");
    }

    [Fact]
    public async Task basic_auth_succeeds_with_valid_credentials()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:password"));
        var config = new AuthConfig { Mode = AuthMode.Basic, BasicCredentials = credentials };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Basic {credentials}";

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("admin");
    }

    [Fact]
    public async Task basic_auth_fails_with_invalid_credentials()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:password")),
        };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization =
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong"));

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task apikey_auth_succeeds_with_valid_key()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "my-secret-key" };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer my-secret-key";

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("api-user");
    }

    [Fact]
    public async Task apikey_auth_fails_with_invalid_key()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "my-secret-key" };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer wrong-key";

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task host_auth_succeeds_with_authenticated_user()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test-scheme")),
        };

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("test-user");
    }

    [Fact]
    public async Task host_auth_fails_with_unauthenticated_user()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task custom_auth_succeeds_with_valid_validator()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = (token, _) => string.Equals(token, "valid-token", StringComparison.Ordinal),
        };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "valid-token";

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("custom-user");
    }

    [Fact]
    public async Task custom_auth_fails_with_invalid_token()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = (token, _) => string.Equals(token, "valid-token", StringComparison.Ordinal),
        };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "invalid-token";

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task fails_when_no_authorization_header()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:pass")),
        };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Be("No authorization provided");
    }

    [Theory]
    [InlineData(AuthMode.ApiKey, "Bearer:my-key")]
    [InlineData(AuthMode.ApiKey, "my-key")]
    [InlineData(AuthMode.Custom, "Token raw value")]
    public async Task reads_access_token_on_a_hub_endpoint_whatever_its_path(AuthMode mode, string accessToken)
    {
        // The Jobs hub is mapped at /job-notification-hub: no "/hub" segment, so routing metadata, not the path,
        // decides whether the query credential counts.
        var config = new AuthConfig
        {
            Mode = mode,
            ApiKey = "my-key",
            CustomValidator = (credential, _) => string.Equals(credential, "Token raw value", StringComparison.Ordinal),
        };
        var service = new AuthService(config, _logger);
        var context = _HubContext("/job-notification-hub", accessToken);

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task reads_the_basic_credential_from_access_token_on_a_hub_endpoint()
    {
        var credentials = "admin:pass".ToBase64();
        var config = new AuthConfig { Mode = AuthMode.Basic, BasicCredentials = credentials };
        var service = new AuthService(config, _logger);
        var context = _HubContext("/job-notification-hub", credentials);

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("admin");
    }

    [Theory]
    [InlineData("/api/data")]
    [InlineData("/dashboard/hub")]
    [InlineData("/dashboard/hub/negotiate")]
    public async Task ignores_access_token_query_parameter_off_hub_endpoints(string path)
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "my-key" };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?access_token=my-key");

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Be("No authorization provided");
    }

    [Fact]
    public async Task prefers_the_authorization_header_over_the_hub_access_token()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "my-key" };
        var service = new AuthService(config, _logger);
        var context = _HubContext("/job-notification-hub", "my-key");
        context.Request.Headers.Authorization = "Bearer wrong-key";

        var result = await service.AuthenticateAsync(context, AbortToken);

        result.IsAuthenticated.Should().BeFalse();
    }

    private static DefaultHttpContext _HubContext(string path, string accessToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = QueryString.Create("access_token", accessToken);
        context.SetEndpoint(
            new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new HubMetadata(typeof(Hub))), "hub")
        );

        return context;
    }

    [Fact]
    public void get_auth_info_returns_config()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("a:b")),
            SessionTimeoutMinutes = 30,
        };
        var service = new AuthService(config, _logger);

        var info = service.GetAuthInfo();

        info.Mode.Should().Be(AuthMode.Basic);
        info.IsEnabled.Should().BeTrue();
        info.SessionTimeoutMinutes.Should().Be(30);
    }
}
