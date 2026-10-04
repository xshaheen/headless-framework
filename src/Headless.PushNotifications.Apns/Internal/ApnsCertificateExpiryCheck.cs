// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Monitors APNs provider certificate expiration on host startup and recurring daily intervals.
/// </summary>
internal sealed class ApnsCertificateExpiryCheck(
    IOptionsMonitor<ApnsOptions> optionsMonitor,
    string? name,
    Func<ApnsCertificateHolder> getHolder,
    TimeProvider timeProvider,
    ILogger<ApnsCertificateExpiryCheck> logger
) : IHostedService, IDisposable
{
    /// <summary>Gets the interval between certificate expiration checks.</summary>
    internal static readonly TimeSpan RecheckPeriod = TimeSpan.FromDays(1);

    private static readonly TimeSpan _WarningWindow = TimeSpan.FromDays(30);

    private readonly string _instance = name ?? "default";
    private ITimer? _timer;

    /// <summary>Starts monitoring and validates the certificate expiration status.</summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A completed task when initialization succeeds.</returns>
    /// <exception cref="InvalidOperationException">The APNs certificate has expired.</exception>
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
    }
}
