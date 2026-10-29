// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api;
using Headless.Api.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using HeaderSameSiteMode = Microsoft.Net.Http.Headers.SameSiteMode;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace Tests.Security.Jwt;

public sealed class RefreshTokenCookieTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(_Now);
    private readonly HttpContextAccessor _accessor = new() { HttpContext = new DefaultHttpContext() };

    [Fact]
    public void should_write_the_cookie_with_the_configured_and_enforced_attributes()
    {
        var sut = _Create(options =>
        {
            options.Name = "refresh";
            options.Path = "/auth";
            options.Domain = "example.com";
            options.SameSite = SameSiteMode.Lax;
        });

        sut.Write("token-value", _Now.AddDays(14));

        var cookie = _SetCookie();
        cookie.Name.ToString().Should().Be("refresh");
        cookie.Value.ToString().Should().Be("token-value");
        cookie.Path.ToString().Should().Be("/auth");
        cookie.Domain.ToString().Should().Be("example.com");
        cookie.SameSite.Should().Be(HeaderSameSiteMode.Lax);
        cookie.Secure.Should().BeTrue();
        cookie.HttpOnly.Should().BeTrue();
        cookie.Expires.Should().Be(_Now.AddDays(14));
        cookie.MaxAge.Should().BeNull();
    }

    [Fact]
    public void should_write_the_default_host_prefixed_strict_cookie()
    {
        var sut = _Create();

        sut.Write("token-value", _Now.AddHours(1));

        var cookie = _SetCookie();
        cookie.Name.ToString().Should().Be("__Host-refresh_token");
        cookie.Path.ToString().Should().Be("/");
        cookie.Domain.HasValue.Should().BeFalse();
        cookie.SameSite.Should().Be(HeaderSameSiteMode.Strict);
        cookie.Secure.Should().BeTrue();
        cookie.HttpOnly.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_an_expiry_that_is_not_in_the_future(int offsetSeconds)
    {
        var sut = _Create();

        var act = () => sut.Write("token-value", _Now.AddSeconds(offsetSeconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
        _accessor.HttpContext!.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_an_empty_token()
    {
        var sut = _Create();

        var act = () => sut.Write("", _Now.AddHours(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_read_the_cookie_value()
    {
        var sut = _Create();
        _accessor.HttpContext!.Request.Headers.Cookie = "__Host-refresh_token=token-value; other=x";

        sut.Read().Should().Be("token-value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("__Host-refresh_token=")]
    [InlineData("other=x")]
    public void should_read_null_when_the_cookie_is_absent_or_empty(string? cookieHeader)
    {
        var sut = _Create();

        if (cookieHeader is not null)
        {
            _accessor.HttpContext!.Request.Headers.Cookie = cookieHeader;
        }

        sut.Read().Should().BeNull();
    }

    [Fact]
    public void should_delete_with_the_same_path_and_domain()
    {
        var sut = _Create(options =>
        {
            options.Name = "refresh";
            options.Path = "/auth";
            options.Domain = "example.com";
        });

        sut.Delete();

        var cookie = _SetCookie();
        cookie.Name.ToString().Should().Be("refresh");
        cookie.Value.ToString().Should().BeEmpty();
        cookie.Path.ToString().Should().Be("/auth");
        cookie.Domain.ToString().Should().Be("example.com");
        cookie.Secure.Should().BeTrue();
        cookie.Expires.Should().BeBefore(_Now);
    }

    [Fact]
    public void should_throw_outside_a_request()
    {
        var sut = _Create();
        _accessor.HttpContext = null;

        var act = () => sut.Read();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_resolve_from_the_registration()
    {
        var services = new ServiceCollection();
        services.AddHeadlessRefreshTokenCookie(options => options.Name = "refresh");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRefreshTokenCookie>().Should().BeOfType<RefreshTokenCookie>();
        provider.GetRequiredService<IOptions<RefreshTokenCookieOptions>>().Value.Name.Should().Be("refresh");
    }

    [Fact]
    public void should_bind_options_from_configuration_when_registered_with_a_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Name"] = "refresh",
                    ["Path"] = "/auth",
                    ["SameSite"] = "Lax",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddHeadlessRefreshTokenCookie(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RefreshTokenCookieOptions>>().Value;

        options.Name.Should().Be("refresh");
        options.Path.Should().Be("/auth");
        options.SameSite.Should().Be(SameSiteMode.Lax);
        provider.GetRequiredService<IRefreshTokenCookie>().Should().BeOfType<RefreshTokenCookie>();
    }

    [Fact]
    public void should_pass_the_service_provider_when_registered_with_a_provider_callback()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ServiceMarker("/session"));
        services.AddHeadlessRefreshTokenCookie(
            (options, provider) =>
            {
                options.Name = "refresh";
                options.Path = provider.GetRequiredService<ServiceMarker>().Path;
            }
        );
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RefreshTokenCookieOptions>>().Value.Path.Should().Be("/session");
        provider.GetRequiredService<IRefreshTokenCookie>().Should().BeOfType<RefreshTokenCookie>();
    }

    private sealed record ServiceMarker(string Path);

    [Fact]
    public void should_accept_the_default_options()
    {
        new RefreshTokenCookieOptionsValidator()
            .TestValidate(new RefreshTokenCookieOptions())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("refresh token", "/", null, SameSiteMode.Strict, "Name")]
    [InlineData("", "/", null, SameSiteMode.Strict, "Name")]
    [InlineData("refresh", "auth", null, SameSiteMode.Strict, "Path")]
    [InlineData("__Host-refresh", "/auth", null, SameSiteMode.Strict, "Path")]
    [InlineData("__Host-refresh", "/", "example.com", SameSiteMode.Strict, "Domain")]
    [InlineData("refresh", "/", "", SameSiteMode.Strict, "Domain")]
    [InlineData("refresh", "/", null, SameSiteMode.None, "SameSite")]
    [InlineData("refresh", "/", null, SameSiteMode.Unspecified, "SameSite")]
    public void should_reject_invalid_options(
        string name,
        string path,
        string? domain,
        SameSiteMode sameSite,
        string property
    )
    {
        var options = new RefreshTokenCookieOptions
        {
            Name = name,
            Path = path,
            Domain = domain,
            SameSite = sameSite,
        };

        var result = new RefreshTokenCookieOptionsValidator().TestValidate(options);

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(property);
    }

    [Fact]
    public void should_accept_a_narrow_path_and_domain_without_the_host_prefix()
    {
        var options = new RefreshTokenCookieOptions
        {
            Name = "__Secure-refresh",
            Path = "/auth",
            Domain = "example.com",
            SameSite = SameSiteMode.Lax,
        };

        new RefreshTokenCookieOptionsValidator().TestValidate(options).ShouldNotHaveAnyValidationErrors();
    }

    private RefreshTokenCookie _Create(Action<RefreshTokenCookieOptions>? configure = null)
    {
        var options = new RefreshTokenCookieOptions();
        configure?.Invoke(options);
        var monitor = Substitute.For<IOptionsMonitor<RefreshTokenCookieOptions>>();
        monitor.CurrentValue.Returns(options);

        return new RefreshTokenCookie(_accessor, monitor, _time);
    }

    private SetCookieHeaderValue _SetCookie()
    {
        var header = _accessor.HttpContext!.Response.Headers.SetCookie;
        header.Should().ContainSingle();

        return SetCookieHeaderValue.Parse(header[0]);
    }
}
