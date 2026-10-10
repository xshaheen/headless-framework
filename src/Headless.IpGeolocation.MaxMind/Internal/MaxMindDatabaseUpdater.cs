// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MaxMind.Db;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation.Internal;

/// <summary>Keeps the databases current: checks MaxMind at startup and on an interval, and swaps in new releases.</summary>
internal sealed partial class MaxMindDatabaseUpdater(
    MaxMindDatabases databases,
    MaxMindDownloader downloader,
    IOptions<MaxMindOptions> options,
    TimeProvider timeProvider,
    ILogger<MaxMindDatabaseUpdater> logger
) : BackgroundService
{
    private readonly MaxMindOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.HasCredentials)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var succeeded = await CheckAllAsync(stoppingToken).ConfigureAwait(false);
                var delay = succeeded ? _options.UpdateCheckInterval : _options.RetryDelay;

                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>Checks every edition once.</summary>
    /// <returns><see langword="true" /> when every check succeeded.</returns>
    internal async Task<bool> CheckAllAsync(CancellationToken cancellationToken)
    {
        var succeeded = true;

        foreach (var editionId in databases.Editions)
        {
            try
            {
                await _CheckAsync(editionId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A failed check keeps the current database and must not end the loop and with it the host.
                LogUpdateFailed(logger, e, editionId, _options.RetryDelay);
                succeeded = false;
            }
        }

        return succeeded;
    }

    private async Task _CheckAsync(string editionId, CancellationToken cancellationToken)
    {
        var path = databases.PathOf(editionId);
        var current = databases.Get(editionId);
        var published = await downloader.GetLastModifiedAsync(editionId, cancellationToken).ConfigureAwait(false);

        // The file's write time holds the Last-Modified of the release it came from, so an unchanged release is
        // skipped without downloading it again.
        if (current is not null && published is { } release && File.GetLastWriteTimeUtc(path) >= release.UtcDateTime)
        {
            _WarnIfStale(editionId, current);

            return;
        }

        Directory.CreateDirectory(_options.DatabaseDirectory);
        var downloadPath = path + ".download";

        try
        {
            await downloader.DownloadAsync(editionId, downloadPath, cancellationToken).ConfigureAwait(false);

            // Opening the file proves it is a database before it replaces the one in use.
            var database = databases.Open(downloadPath);
            File.Move(downloadPath, path, overwrite: true);

            if (published is { } releaseDate)
            {
                File.SetLastWriteTimeUtc(path, releaseDate.UtcDateTime);
            }

            databases.Replace(editionId, database);
            LogDatabaseUpdated(logger, editionId, database.BuildDate);
            _WarnIfStale(editionId, database);
        }
        catch (InvalidDatabaseException e)
        {
            throw new InvalidDataException($"The downloaded {editionId} file is not a MaxMind database.", e);
        }
        finally
        {
            File.Delete(downloadPath);
        }
    }

    private void _WarnIfStale(string editionId, MaxMindDatabase database)
    {
        if (databases.IsStale(database))
        {
            MaxMindDatabases.LogDatabaseStale(logger, editionId, database.BuildDate);
        }
    }

    [LoggerMessage(
        EventId = 10,
        EventName = "MaxMindDatabaseUpdated",
        Level = LogLevel.Information,
        Message = "Loaded the MaxMind {EditionId} database built {BuildDate:yyyy-MM-dd}"
    )]
    private static partial void LogDatabaseUpdated(ILogger logger, string editionId, DateTime buildDate);

    [LoggerMessage(
        EventId = 11,
        EventName = "MaxMindDatabaseUpdateFailed",
        Level = LogLevel.Warning,
        Message = "Updating the MaxMind {EditionId} database failed; keeping the current one and retrying in {RetryDelay}"
    )]
    private static partial void LogUpdateFailed(
        ILogger logger,
        Exception exception,
        string editionId,
        TimeSpan retryDelay
    );
}
