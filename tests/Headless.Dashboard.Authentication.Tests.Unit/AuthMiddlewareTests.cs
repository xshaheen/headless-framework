// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
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

    [Theory]
    [InlineData("/api/published/x.js")]
    [InlineData("/api/received/assets/")]
    [InlineData("/api/jobs/report.css")]
    [InlineData("/api/data/negotiate")]
    [InlineData("/api/auth/validate/extra")]
    public async Task returns_401_for_unauthenticated_api_path_that_looks_static(string path)
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
        await using var sp = services.BuildServiceProvider();

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
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
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

    [Theory]
    [InlineData("/job-notification-hub")]
    [InlineData("/job-notification-hub/negotiate")]
    public async Task returns_401_before_an_unauthenticated_hub_request_reaches_signalr(string path)
    {
        // SignalR adds a connection to Clients.All before the hub's OnConnectedAsync can reject it, so a connection
        // left to the hub to refuse would hear every broadcast sent while its credential was being checked.
        await using var sp = _ApiKeyServices();
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
        var context = _HubContext(sp, path, accessToken: null);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task passes_an_authenticated_hub_request_through()
    {
        await using var sp = _ApiKeyServices();
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
        var context = _HubContext(sp, "/job-notification-hub", accessToken: "secret");

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

    private static ServiceProvider _ApiKeyServices()
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

        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext _HubContext(IServiceProvider services, string path, string? accessToken)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        if (accessToken is not null)
        {
            context.Request.QueryString = QueryString.Create("access_token", accessToken);
        }

        context.SetEndpoint(
            new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new HubMetadata(typeof(Hub))), "hub")
        );

        return context;
    }
}
