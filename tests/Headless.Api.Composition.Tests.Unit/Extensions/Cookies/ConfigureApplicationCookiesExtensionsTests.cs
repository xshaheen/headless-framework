// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Extensions.Cookies;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Extensions.Cookies;

public sealed class ConfigureApplicationCookiesExtensionsTests
{
    [Theory]
    [InlineData(true, StatusCodes.Status401Unauthorized, "/account/login")]
    [InlineData(false, StatusCodes.Status403Forbidden, "/account/denied")]
    public async Task should_return_api_status_and_preserve_redirect_location(
        bool loginRedirect,
        int expectedStatus,
        string redirectUri
    )
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddCookie(IdentityConstants.ApplicationScheme);
        services.ConfigureApiApplicationCookie();
        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var context = new DefaultHttpContext { RequestServices = provider };
        var redirectContext = new RedirectContext<CookieAuthenticationOptions>(
            context,
            new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            options,
            new AuthenticationProperties(),
            redirectUri
        );

        // when
        if (loginRedirect)
        {
            await options.Events.OnRedirectToLogin(redirectContext);
        }
        else
        {
            await options.Events.OnRedirectToAccessDenied(redirectContext);
        }

        // then
        context.Response.StatusCode.Should().Be(expectedStatus);
        context.Response.Headers[HttpHeaderNames.Location].Should().ContainSingle().Which.Should().Be(redirectUri);
    }
}
