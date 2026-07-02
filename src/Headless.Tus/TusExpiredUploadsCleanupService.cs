// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>Options for <see cref="TusExpiredUploadsCleanupService"/>.</summary>
[PublicAPI]
public sealed class TusExpiredUploadsCleanupOptions
{
    /// <summary>How often expired incomplete uploads are removed.</summary>
    /// <remarks>
    /// Defaults to 5 minutes. Each pass calls
    /// <see cref="ITusExpirationStore.RemoveExpiredFilesAsync"/>, which typically scans the
    /// store's uploads (a prefix listing for the Azure store) — prefer a coarser interval on
    /// containers with many uploads; the expiration window itself is configured on
    /// <c>DefaultTusConfiguration.Expiration</c>.
    /// </remarks>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

internal sealed class TusExpiredUploadsCleanupOptionsValidator : AbstractValidator<TusExpiredUploadsCleanupOptions>
{
    public TusExpiredUploadsCleanupOptionsValidator()
    {
        RuleFor(x => x.Interval).GreaterThan(TimeSpan.Zero);
    }
}

/// <summary>
/// Periodically removes expired <em>incomplete</em> uploads via
/// <see cref="ITusExpirationStore.RemoveExpiredFilesAsync"/>. Completed uploads are never touched —
/// conforming stores only reap unfinished uploads whose expiration has passed.
/// </summary>
/// <remarks>
/// tusdotnet only sets the <c>Upload-Expires</c> header; nothing removes expired uploads unless
/// the application runs a job like this one. Register it via
/// <c>services.AddTusExpiredUploadsCleanup()</c> next to a store that implements
/// <see cref="ITusExpirationStore"/>. The first pass runs immediately at startup (reclaiming
/// uploads that expired while the app was down, matching tusdotnet's sample cleanup service),
/// then every <see cref="TusExpiredUploadsCleanupOptions.Interval"/>; failures are logged and
/// the loop continues.
/// </remarks>
[PublicAPI]
public sealed partial class TusExpiredUploadsCleanupService(
    ITusExpirationStore store,
    IOptions<TusExpiredUploadsCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<TusExpiredUploadsCleanupService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Immediate first pass: reclaim uploads that expired while the app was down.
        await _RunPassAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await _RunPassAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task _RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            var removed = await store.RemoveExpiredFilesAsync(stoppingToken).ConfigureAwait(false);

            if (removed > 0)
            {
                LogRemovedExpiredUploads(logger, removed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogCleanupFailed(logger, e);
        }
    }

    [LoggerMessage(
        EventId = 3260,
        Level = LogLevel.Information,
        Message = "Removed {Count} expired incomplete upload(s)"
    )]
    private static partial void LogRemovedExpiredUploads(ILogger logger, int count);

    [LoggerMessage(EventId = 3261, Level = LogLevel.Error, Message = "Expired-upload cleanup pass failed")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
