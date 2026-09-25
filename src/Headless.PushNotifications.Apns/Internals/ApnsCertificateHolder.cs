// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography.X509Certificates;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Owns one certificate-mode instance's provider certificate, loaded once, for the primary handler and the startup
/// expiry check. Registered per instance, so the container disposes the certificate with it.
/// </summary>
internal sealed class ApnsCertificateHolder : IDisposable
{
    /// <summary>Loads the certificate the options configure.</summary>
    /// <exception cref="InvalidOperationException">The options do not select certificate mode.</exception>
    public ApnsCertificateHolder(ApnsOptions options)
    {
        Argument.IsNotNull(options);

        if (!options.UsesCertificate)
        {
            throw new InvalidOperationException("The APNs options do not configure a Certificate.");
        }

        Certificate = ApnsCertificateLoader.Load(options.Certificate!, options.CertificatePassword);
    }

    public X509Certificate2 Certificate { get; }

    public void Dispose()
    {
        Certificate.Dispose();
    }

    /// <summary>Registers the holder for the instance <paramref name="name"/>; <see langword="null"/> is the default instance.</summary>
    internal static void Register(IServiceCollection services, string? name)
    {
        if (name is null)
        {
            services.AddSingleton(static serviceProvider => _Create(serviceProvider, optionsName: null));

            return;
        }

        services.AddKeyedSingleton(name, (serviceProvider, _) => _Create(serviceProvider, name));
    }

    /// <summary>Resolves the holder for the instance <paramref name="name"/>, loading the certificate on first use.</summary>
    internal static ApnsCertificateHolder Get(IServiceProvider serviceProvider, string? name)
    {
        return name is null
            ? serviceProvider.GetRequiredService<ApnsCertificateHolder>()
            : serviceProvider.GetRequiredKeyedService<ApnsCertificateHolder>(name);
    }

    private static ApnsCertificateHolder _Create(IServiceProvider serviceProvider, string? optionsName)
    {
        return new ApnsCertificateHolder(
            serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>().Get(optionsName)
        );
    }
}
