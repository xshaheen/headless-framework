// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography.X509Certificates;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Manages the lifetime and dynamic reloading of an APNs provider certificate for TLS authentication.
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

    // Retains replaced certificate until subsequent swap or disposal to allow in-flight TLS handshakes to complete.
    private X509Certificate2? _previous;

    // Tracks previously loaded options to avoid redundant reloads on unrelated configuration changes.
    private string? _certificateText;
    private string? _certificatePassword;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ApnsCertificateHolder"/> class and registers option monitors.</summary>
    /// <param name="optionsMonitor">The options monitor for APNs configuration.</param>
    /// <param name="name">The options instance name.</param>
    /// <param name="timeProvider">The time provider for certificate validation.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="InvalidOperationException">The configured options do not specify certificate credentials.</exception>
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
        _current = ApnsCertificateLoader.Load(options.Certificate!, options.CertificatePassword);
        _subscription = optionsMonitor.OnChange(_OnOptionsChanged);
    }

    /// <summary>Gets the current active certificate presented during TLS handshakes.</summary>
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

    /// <summary>Registers the holder in the service collection for instance <paramref name="name"/>.</summary>
    internal static void Register(IServiceCollection services, string? name)
    {
        if (name is null)
        {
            services.AddSingleton(static serviceProvider => _Create(serviceProvider, optionsName: null));

            return;
        }

        services.AddKeyedSingleton(name, (serviceProvider, _) => _Create(serviceProvider, name));
    }

    /// <summary>Resolves the certificate holder for instance <paramref name="name"/>.</summary>
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
                ApnsMetrics.RecordCertificateReload(accepted: false);

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

                ApnsMetrics.RecordCertificateReload(accepted: false);

                return;
            }

            _previous?.Dispose();
            _previous = _current;
            _current = renewed;
            ApnsMetrics.RecordCertificateReload(accepted: true);

            var expiresAt = ApnsCertificateLoader.GetExpiresAt(renewed);
            _logger.LogCertificateReloaded(_instance, expiresAt);
        }
    }
}
