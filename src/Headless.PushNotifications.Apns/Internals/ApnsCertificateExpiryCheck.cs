// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Checks a certificate-mode instance's provider certificate at host start and then once a day. At start, an expired
/// certificate fails the start and one that expires within 30 days logs a warning. Each later check reads the
/// current, possibly renewed, certificate: within 30 days of expiry it logs a warning, and once expired it logs an
/// error without stopping the host. A token-mode instance is skipped without loading anything.
/// </summary>
/// <remarks>
/// Apple provider certificates last one year and are renewed by hand, so the warning gives operators time to renew
/// before every push starts failing the TLS handshake. The daily check matters because a host can run for longer
/// than the 30-day window.
/// </remarks>
internal sealed class ApnsCertificateExpiryCheck(
    IOptionsMonitor<ApnsOptions> optionsMonitor,
    string? name,
    Func<ApnsCertificateHolder> getHolder,
    TimeProvider timeProvider,
    ILogger<ApnsCertificateExpiryCheck> logger
) : IHostedService, IDisposable
{
    /// <summary>How often the certificate is checked again after host start.</summary>
    internal static readonly TimeSpan RecheckPeriod = TimeSpan.FromDays(1);

    private static readonly TimeSpan _WarningWindow = TimeSpan.FromDays(30);

    private readonly string _instance = name ?? "default";
    private ITimer? _timer;

    /// <summary>Fails when the certificate has expired, warns when it expires within 30 days, and starts the daily check.</summary>
    /// <exception cref="InvalidOperationException">The instance's certificate has expired.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!optionsMonitor.Get(name).UsesCertificate)
        {
            return Task.CompletedTask;
        }

        var holder = getHolder();
        var expiresAt = ApnsCertificateLoader.GetExpiresAt(holder.Certificate);
        var remaining = expiresAt - timeProvider.GetUtcNow();

        if (remaining <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"The APNs certificate of the '{_instance}' instance expired at {expiresAt.ToString("u", CultureInfo.InvariantCulture)}. Renew it in the Apple Developer account."
            );
        }

        if (remaining <= _WarningWindow)
        {
            logger.LogCertificateExpiringSoon(_instance, expiresAt, (int)Math.Ceiling(remaining.TotalDays));
        }

        _timer?.Dispose();
        _timer = timeProvider.CreateTimer(_ => _Recheck(holder), state: null, RecheckPeriod, RecheckPeriod);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void _Recheck(ApnsCertificateHolder holder)
    {
#pragma warning disable CA1031 // A timer callback is a background loop boundary: an escaping exception would crash the process, so every failure is logged and the next tick tries again.
        try
        {
            var expiresAt = ApnsCertificateLoader.GetExpiresAt(holder.Certificate);
            var remaining = expiresAt - timeProvider.GetUtcNow();

            if (remaining <= TimeSpan.Zero)
            {
                logger.LogCertificateExpired(_instance, expiresAt);
            }
            else if (remaining <= _WarningWindow)
            {
                logger.LogCertificateExpiringSoon(_instance, expiresAt, (int)Math.Ceiling(remaining.TotalDays));
            }
        }
        catch (Exception e)
        {
            logger.LogCertificateCheckFailed(e, _instance);
        }
#pragma warning restore CA1031
    }
}
