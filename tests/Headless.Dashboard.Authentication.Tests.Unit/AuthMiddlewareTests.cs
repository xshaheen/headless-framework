// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class AuthMiddlewareTests : TestBase
{
    private const string _Name = "dashboard-a";
    private const string _OtherName = "dashboard-b";

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
        var middleware = new AuthMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            _Name,
            _logger
        );
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task skips_non_api_paths()
    {
        var nextCalled = false;
        var middleware = new AuthMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            _Name,
            _logger
        );
        var context = new DefaultHttpContext();
        context.Request.Path = "/some-page";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task returns_401_for_unauthenticated_api_request()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDashboardAuthentication(
            _Name,
            config =>
            {
                config.Mode = AuthMode.ApiKey;
                config.ApiKey = "secret";
            }
        );
        var sp = services.BuildServiceProvider();

        var middleware = new AuthMiddleware(_ => Task.CompletedTask, _Name, _logger);
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/api/data";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task passes_through_for_authenticated_api_request()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDashboardAuthentication(
            _Name,
            config =>
            {
                config.Mode = AuthMode.ApiKey;
                config.ApiKey = "secret";
            }
        );
        var sp = services.BuildServiceProvider();

        var nextCalled = false;
        var middleware = new AuthMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            _Name,
            _logger
        );
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/api/data";
        context.Request.Headers.Authorization = "Bearer secret";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items[AuthMiddleware.AuthenticatedKey].Should().Be(true);
        context.Items[AuthMiddleware.UsernameKey].Should().Be("api-user");
        context.User.Identity.Should().NotBeNull();
        context.User.Identity.IsAuthenticated.Should().BeTrue();
        context.User.Identity.Name.Should().Be("api-user");
    }

    [Fact]
    public async Task preserves_existing_authenticated_host_principal()
    {
        var hostPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "host-operator")], authenticationType: "host")
        );
        var authService = Substitute.For<IAuthService>();
        authService
            .AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Success("host-operator"));
        var services = new ServiceCollection();
        services.AddKeyedSingleton(_Name, authService);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = hostPrincipal,
        };
        context.Request.Path = "/api/data";

        var middleware = new AuthMiddleware(_ => Task.CompletedTask, _Name, _logger);
        await middleware.InvokeAsync(context);

        context.User.Should().BeSameAs(hostPrincipal);
    }

    [Fact]
    public async Task authenticates_with_the_named_dashboard_config_when_another_dashboard_registers_its_own()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDashboardAuthentication(
            _Name,
            config =>
            {
                config.Mode = AuthMode.ApiKey;
                config.ApiKey = "key-a";
            }
        );
        services.AddDashboardAuthentication(
            _OtherName,
            config =>
            {
                config.Mode = AuthMode.ApiKey;
                config.ApiKey = "key-b";
            }
        );
        await using var sp = services.BuildServiceProvider();

        var own = await _InvokeAsync(sp, _Name, "Bearer key-a");
        var other = await _InvokeAsync(sp, _Name, "Bearer key-b");

        own.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        other.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private async Task<HttpContext> _InvokeAsync(IServiceProvider sp, string name, string authorization)
    {
        await using var scope = sp.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Path = "/api/data";
        context.Request.Headers.Authorization = authorization;

        await new AuthMiddleware(_ => Task.CompletedTask, name, _logger).InvokeAsync(context);

        return context;
    }
}
