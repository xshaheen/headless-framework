// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HeadlessShop.Api;

public sealed class FakeTourAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    IHostEnvironment environment
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "FakeTour";
    public const string UserHeader = "X-User-Id";
    public const string TenantHeader = "X-Tenant-Id";
    public const string PermissionHeader = "X-Permission";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_AllowFakeTourAuthentication(configuration, environment))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers[UserHeader].FirstOrDefault();
        var tenantId = Request.Headers[TenantHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(UserClaimTypes.TenantId, tenantId),
        };

        foreach (var permission in Request.Headers[PermissionHeader])
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            claims.Add(new("permission", permission));
        }

        var identity = new ClaimsIdentity(claims, AuthenticationScheme, ClaimTypes.NameIdentifier, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool _AllowFakeTourAuthentication(IConfiguration configuration, IHostEnvironment environment)
    {
        return environment.IsDevelopment()
            || environment.IsEnvironment("Test")
            || configuration.GetValue<bool>("HeadlessShop:AllowFakeTourAuth");
    }
}
