// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class AntiforgeryServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core antiforgery with Headless-standard defaults: the <c>X-XSRF-TOKEN</c>
    /// header name, an <c>HttpOnly</c> cookie with <c>Lax</c> same-site and request-matched secure
    /// policy, and an application-discriminator-derived cookie name so multi-app deployments on the
    /// same host do not collide.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddHeadlessAntiforgery(this IServiceCollection services)
    {
        services.AddTransient<IConfigureOptions<AntiforgeryOptions>, AntiforgeryOptionsConfiguration>();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = HttpHeaderNames.Antiforgery;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }

    [UsedImplicitly]
    private sealed class AntiforgeryOptionsConfiguration(IOptions<DataProtectionOptions> optionsAccessor)
        : IConfigureOptions<AntiforgeryOptions>
    {
        private readonly DataProtectionOptions _dataProtectionOptions = optionsAccessor.Value;

        public void Configure(AntiforgeryOptions options)
        {
            Argument.IsNotNull(options);

            if (options.Cookie.Name is null)
            {
                var applicationId = _dataProtectionOptions.ApplicationDiscriminator ?? string.Empty;
                options.Cookie.Name = HttpHeaderNames.Antiforgery + "_" + _ComputeCookieName(applicationId);
            }
        }

        private static string _ComputeCookieName(string applicationId)
        {
            var fullHash = SHA256.HashData(Encoding.UTF8.GetBytes(applicationId));

            return AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(fullHash, 0, 8);
        }
    }
}
