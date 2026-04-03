// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Testing.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TestHttpContextExtensionsTests : IDisposable
{
    private readonly ServiceProvider _sp;

    public TestHttpContextExtensionsTests()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        _sp = services.BuildServiceProvider();
    }

    public void Dispose() => _sp.Dispose();

    [Fact]
    public void should_set_principal()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _sp.SetHttpContext(principal: principal);

        var accessor = _sp.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.User.Should().BeSameAs(principal);
        accessor.HttpContext.User.Identity!.IsAuthenticated.Should().BeTrue();
        accessor.HttpContext.User.Identity.Name.Should().Be("test-user");
    }

    [Fact]
    public void should_set_remote_ip()
    {
        _sp.SetHttpContext(remoteIp: IPAddress.Loopback);

        var accessor = _sp.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.Connection.RemoteIpAddress.Should().Be(IPAddress.Loopback);
    }

    [Fact]
    public void should_set_remote_ip_as_string()
    {
        _sp.SetHttpContext(remoteIp: "127.0.0.1");

        var accessor = _sp.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.Connection.RemoteIpAddress.Should().Be(IPAddress.Loopback);
    }

    [Fact]
    public void should_set_user_agent()
    {
        _sp.SetHttpContext(userAgent: "TestBot/1.0");

        var accessor = _sp.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.Request.Headers.UserAgent.ToString().Should().Be("TestBot/1.0");
    }

    [Fact]
    public void should_set_all_parameters_combined()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var ip = IPAddress.Parse("192.168.1.1");

        var context = _sp.SetHttpContext(principal: principal, remoteIp: ip, userAgent: "MyApp/2.0");

        context.User.Should().BeSameAs(principal);
        context.Connection.RemoteIpAddress.Should().Be(ip);
        context.Request.Headers.UserAgent.ToString().Should().Be("MyApp/2.0");
    }

    [Fact]
    public void should_create_anonymous_context_with_no_args()
    {
        var context = _sp.SetHttpContext();

        context.Should().NotBeNull();
        context.User.Should().NotBeNull();
        context.User.Identity!.IsAuthenticated.Should().BeFalse();
        context.Connection.RemoteIpAddress.Should().BeNull();
    }

    [Fact]
    public void should_wire_request_services()
    {
        var context = _sp.SetHttpContext();

        context.RequestServices.Should().BeSameAs(_sp);
    }

    [Fact]
    public void should_throw_when_http_context_accessor_not_registered()
    {
        var services = new ServiceCollection();
        using var sp = services.BuildServiceProvider();

        var act = () => sp.SetHttpContext();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_return_the_created_context()
    {
        var context = _sp.SetHttpContext();

        var accessor = _sp.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext.Should().BeSameAs(context);
    }
}
