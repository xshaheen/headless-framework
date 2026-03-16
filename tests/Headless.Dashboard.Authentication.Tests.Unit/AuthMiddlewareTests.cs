using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class AuthMiddlewareTests : TestBase
{
    private readonly ILogger<AuthMiddleware> _logger = Substitute.For<ILogger<AuthMiddleware>>();

    [Theory]
    [InlineData("/assets/main.js")]
    [InlineData("/styles.css")]
    [InlineData("/favicon.ico")]
    [InlineData("/logo.png")]
    [InlineData("/image.jpg")]
    [InlineData("/icon.svg")]
    [InlineData("/hub/negotiate")]
    [InlineData("/api/auth/validate")]
    [InlineData("/api/auth/info")]
    public async Task skips_excluded_paths(string path)
    {
        var nextCalled = false;
        var middleware = new AuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task skips_non_api_paths()
    {
        var nextCalled = false;
        var middleware = new AuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/some-page";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task returns_401_for_unauthenticated_api_request()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "secret",
        };

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        services.AddScoped<IAuthService, AuthService>();
        var sp = services.BuildServiceProvider();

        var middleware = new AuthMiddleware(_ => Task.CompletedTask, _logger);
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/api/data";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task passes_through_for_authenticated_api_request()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "secret",
        };

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        services.AddScoped<IAuthService, AuthService>();
        var sp = services.BuildServiceProvider();

        var nextCalled = false;
        var middleware = new AuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, _logger);
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/api/data";
        context.Request.Headers.Authorization = "Bearer secret";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items["auth.authenticated"].Should().Be(true);
        context.Items["auth.username"].Should().Be("api-user");
    }
}
