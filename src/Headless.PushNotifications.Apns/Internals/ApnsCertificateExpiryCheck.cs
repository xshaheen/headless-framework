// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Checks a certificate-mode instance's provider certificate at host start: an expired certificate fails the start,
/// and one that expires within 30 days logs a warning. A token-mode instance is skipped without loading anything.
/// </summary>
/// <remarks>
/// Apple provider certificates last one year and are renewed by hand, so the warning gives operators time to renew
/// before every push starts failing the TLS handshake.
/// </remarks>
internal sealed class ApnsCertificateExpiryCheck(
    IOptionsMonitor<ApnsOptions> optionsMonitor,
    string? name,
    Func<ApnsCertificateHolder> getHolder,
    TimeProvider timeProvider,
    ILogger<ApnsCertificateExpiryCheck> logger
) : IHostedService
{
    private static readonly TimeSpan _WarningWindow = TimeSpan.FromDays(30);

    /// <summary>Fails when the certificate has expired and warns when it expires within 30 days.</summary>
    /// <exception cref="InvalidOperationException">The instance's certificate has expired.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!optionsMonitor.Get(name).UsesCertificate)
        {
            return Task.CompletedTask;
        }

        var expiresAt = ApnsCertificateLoader.GetExpiresAt(getHolder().Certificate);
        var remaining = expiresAt - timeProvider.GetUtcNow();
        var instance = name ?? "default";

        if (remaining <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"The APNs certificate of the '{instance}' instance expired at {expiresAt.ToString("u", CultureInfo.InvariantCulture)}. Renew it in the Apple Developer account."
            );
        }

        if (remaining <= _WarningWindow)
        {
            logger.LogCertificateExpiringSoon(instance, expiresAt, (int)Math.Ceiling(remaining.TotalDays));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
