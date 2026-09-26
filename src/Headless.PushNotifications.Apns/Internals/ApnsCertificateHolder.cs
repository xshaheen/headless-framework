// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography.X509Certificates;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Owns one certificate-mode instance's provider certificate for the primary handler and the expiry check. It loads
/// the certificate once, then reloads it whenever the instance's <see cref="ApnsOptions.Certificate"/> or
/// <see cref="ApnsOptions.CertificatePassword"/> changes, so a renewed certificate takes effect without a restart.
/// Registered per instance, so the container disposes it.
/// </summary>
internal sealed class ApnsCertificateHolder : IDisposable
{
    private readonly Lock _gate = new();
    private readonly string _optionsName;
    private readonly string _instance;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly IDisposable? _subscription;
    private volatile X509Certificate2 _current;

    // The certificate the last swap replaced. A TLS handshake that read it just before the swap may still be using
    // it, so it is disposed only on the next swap or with the holder, never at the swap that replaced it.
    private X509Certificate2? _previous;

    // The certificate fields last seen, loaded or rejected, so a change to any other option never reloads.
    private string? _certificateText;
    private string? _certificatePassword;
    private bool _disposed;

    /// <summary>Loads the certificate the instance's options configure and starts observing their changes.</summary>
    /// <exception cref="InvalidOperationException">The options do not select certificate mode.</exception>
    public ApnsCertificateHolder(
        IOptionsMonitor<ApnsOptions> optionsMonitor,
        string? name,
        TimeProvider timeProvider,
        ILogger<ApnsCertificateHolder> logger
    )
    {
        Argument.IsNotNull(optionsMonitor);
        Argument.IsNotNull(timeProvider);
        Argument.IsNotNull(logger);

        var options = optionsMonitor.Get(name);

        if (!options.UsesCertificate)
        {
            throw new InvalidOperationException("The APNs options do not configure a Certificate.");
        }

        _optionsName = name ?? Options.DefaultName;
        _instance = name ?? "default";
        _timeProvider = timeProvider;
        _logger = logger;
        _certificateText = options.Certificate;
        _certificatePassword = options.CertificatePassword;
        // The options validator has already checked this certificate when the monitor built the options.
        _current = ApnsCertificateLoader.Load(options.Certificate!, options.CertificatePassword);
        _subscription = optionsMonitor.OnChange(_OnOptionsChanged);
    }

    /// <summary>The certificate a new TLS connection presents; replaced when a renewed certificate loads.</summary>
    public X509Certificate2 Certificate => _current;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _subscription?.Dispose();
        _previous?.Dispose();
        _current.Dispose();
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
            serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>(),
            optionsName,
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<ApnsCertificateHolder>>()
        );
    }

    private void _OnOptionsChanged(ApnsOptions options, string? name)
    {
        if (!string.Equals(name ?? Options.DefaultName, _optionsName, StringComparison.Ordinal))
        {
            return;
        }

        lock (_gate)
        {
            if (
                _disposed
                || (
                    string.Equals(options.Certificate, _certificateText, StringComparison.Ordinal)
                    && string.Equals(options.CertificatePassword, _certificatePassword, StringComparison.Ordinal)
                )
            )
            {
                return;
            }

            _certificateText = options.Certificate;
            _certificatePassword = options.CertificatePassword;

            if (!options.UsesCertificate)
            {
                // The service fixed its authentication mode when it was built, so only a restart can switch it.
                _logger.LogCertificateReloadFailed(
                    _instance,
                    "The options no longer configure a Certificate; switching to token mode needs a restart."
                );

                return;
            }

            var renewed = ApnsCertificateLoader.LoadValid(
                options.Certificate!,
                options.CertificatePassword,
                _timeProvider.GetUtcNow(),
                out var errors
            );

            if (renewed is null)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogCertificateReloadFailed(_instance, string.Join(' ', errors));
                }

                return;
            }

            _previous?.Dispose();
            _previous = _current;
            _current = renewed;

            var expiresAt = ApnsCertificateLoader.GetExpiresAt(renewed);
            _logger.LogCertificateReloaded(_instance, expiresAt);
        }
    }
}
