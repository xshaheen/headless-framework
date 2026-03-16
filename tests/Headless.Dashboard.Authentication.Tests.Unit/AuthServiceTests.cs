using System.Security.Claims;
using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
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

        var result = await service.AuthenticateAsync(context);

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

        var result = await service.AuthenticateAsync(context);

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
        context.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong"));

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task apikey_auth_succeeds_with_valid_key()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "my-secret-key" };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer my-secret-key";

        var result = await service.AuthenticateAsync(context);

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

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task host_auth_succeeds_with_authenticated_user()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "test-user")],
                    "test-scheme"
                )
            ),
        };

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("test-user");
    }

    [Fact]
    public async Task host_auth_fails_with_unauthenticated_user()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task custom_auth_succeeds_with_valid_validator()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = token => token == "valid-token",
        };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "valid-token";

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("custom-user");
    }

    [Fact]
    public async Task custom_auth_fails_with_invalid_token()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = token => token == "valid-token",
        };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "invalid-token";

        var result = await service.AuthenticateAsync(context);

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

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Be("No authorization provided");
    }

    [Fact]
    public async Task reads_access_token_from_query_parameter()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "my-key" };
        var service = new AuthService(config, _logger);
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?access_token=my-key");

        var result = await service.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
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
