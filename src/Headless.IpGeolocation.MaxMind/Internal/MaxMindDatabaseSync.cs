// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MaxMind.Db;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation.Internal;

/// <summary>
/// Brings the database files in line with MaxMind's releases, for the updater, the startup warm-up, and the
/// <c>resources</c> verb alike.
/// </summary>
/// <remarks>
/// One operation runs at a time: the updater, the warm-up, and a resource operation share each edition's download
/// path, and two downloads of one edition would also cost two of the account's daily downloads.
/// <para>
/// A download that fails is not tried again for that edition until <see cref="MaxMindOptions.RetryDelay" /> has
/// passed: MaxMind counts each download against the account's daily limit, and a download that keeps failing after it
/// starts, retried at the warm-up's pace, would spend that limit in minutes. A failed release check downloads nothing,
/// so it is not delayed.
/// </para>
/// </remarks>
internal sealed partial class MaxMindDatabaseSync : IDisposable
{
    private readonly MaxMindDatabases _databases;
    private readonly MaxMindDownloader _downloader;
    private readonly MaxMindOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaxMindDatabaseSync> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Read and written only while holding _gate.
    private readonly Dictionary<string, (DateTimeOffset FailedAt, Exception Error)> _failedDownloads = new(
        StringComparer.Ordinal
    );

    public MaxMindDatabaseSync(
        MaxMindDatabases databases,
        MaxMindDownloader downloader,
        IOptions<MaxMindOptions> options,
        TimeProvider timeProvider,
        ILogger<MaxMindDatabaseSync> logger
    )
    {
        _databases = databases;
        _downloader = downloader;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        // Fixed when the host builds the provider, so the warm-up's readiness gate and the updater's first check
        // agree on who downloads first even after a download lands.
        NeedsInitialDownload = _options.HasCredentials && databases.Editions.Any(e => databases.Get(e) is null);
    }

    /// <summary>
    /// Gets whether downloads are enabled and an edition had no readable database when the provider was built, so
    /// lookups return nothing for it until the first download lands.
    /// </summary>
    public bool NeedsInitialDownload { get; }

    /// <summary>
    /// Checks every edition once, downloading any that is missing or older than MaxMind's release. An edition whose
    /// download failed less than <see cref="MaxMindOptions.RetryDelay" /> ago fails again at once with that error and
    /// sends no request.
    /// </summary>
    /// <returns>Each edition whose check failed, with the error; empty when every check succeeded.</returns>
    public Task<IReadOnlyList<(string EditionId, Exception Error)>> UpdateAllAsync(
        CancellationToken cancellationToken
    ) => _UpdateAllAsync(honorDownloadDelay: true, cancellationToken);

    /// <summary>
    /// Checks every edition once like <see cref="UpdateAllAsync" />, but tries a download that failed recently again:
    /// an operator who runs <c>resources provision</c> asked for it explicitly.
    /// </summary>
    /// <returns>Each edition whose check failed, with the error; empty when every check succeeded.</returns>
    public Task<IReadOnlyList<(string EditionId, Exception Error)>> UpdateAllNowAsync(
        CancellationToken cancellationToken
    ) => _UpdateAllAsync(honorDownloadDelay: false, cancellationToken);

    private async Task<IReadOnlyList<(string EditionId, Exception Error)>> _UpdateAllAsync(
        bool honorDownloadDelay,
        CancellationToken cancellationToken
    )
    {
        var failures = new List<(string, Exception)>();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var editionId in _databases.Editions)
            {
                try
                {
                    await _UpdateAsync(editionId, honorDownloadDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                    when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // One edition's failure must not keep the others from updating.
                    failures.Add((editionId, e));
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        return failures;
    }

    /// <summary>Deletes every edition's file and stops serving lookups from it.</summary>
    public async Task DeleteAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var editionId in _databases.Editions)
            {
                var path = _databases.PathOf(editionId);
                File.Delete(path);
                File.Delete(path + ".download");
                _databases.Remove(editionId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Turns the failures <see cref="UpdateAllAsync" /> returned into one exception that names each edition.</summary>
    public static Exception ToException(IReadOnlyList<(string EditionId, Exception Error)> failures)
    {
        var errors = failures
            .Select(static f => new InvalidOperationException(
                $"Updating the MaxMind {f.EditionId} database failed: {f.Error.Message}",
                f.Error
            ))
            .ToList();

        return errors.Count == 1 ? errors[0] : new AggregateException("Updating the MaxMind databases failed.", errors);
    }

    public void Dispose() => _gate.Dispose();

    private async Task _UpdateAsync(string editionId, bool honorDownloadDelay, CancellationToken cancellationToken)
    {
        if (honorDownloadDelay && _failedDownloads.TryGetValue(editionId, out var failed))
        {
            var retryAt = failed.FailedAt + _options.RetryDelay;

            if (_timeProvider.GetUtcNow() < retryAt)
            {
                throw new InvalidOperationException(
                    $"The last download failed ({failed.Error.Message}); it is tried again from {retryAt:u}.",
                    failed.Error
                );
            }
        }

        var path = _databases.PathOf(editionId);
        var current = _databases.Get(editionId);
        var published = await _downloader.GetLastModifiedAsync(editionId, cancellationToken).ConfigureAwait(false);

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
            await _downloader.DownloadAsync(editionId, downloadPath, cancellationToken).ConfigureAwait(false);

            // Opening the file proves it is a database before it replaces the one in use.
            var database = _databases.Open(downloadPath);
            File.Move(downloadPath, path, overwrite: true);

            if (published is { } releaseDate)
            {
                File.SetLastWriteTimeUtc(path, releaseDate.UtcDateTime);
            }

            _databases.Replace(editionId, database);
            _failedDownloads.Remove(editionId);
            LogDatabaseUpdated(_logger, editionId, database.BuildDate);
            _WarnIfStale(editionId, database);
        }
        catch (InvalidDatabaseException e)
        {
            var error = new InvalidDataException($"The downloaded {editionId} file is not a MaxMind database.", e);
            _failedDownloads[editionId] = (_timeProvider.GetUtcNow(), error);

            throw error;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failedDownloads[editionId] = (_timeProvider.GetUtcNow(), e);

            throw;
        }
        finally
        {
            File.Delete(downloadPath);
        }
    }

    private void _WarnIfStale(string editionId, MaxMindDatabase database)
    {
        if (_databases.IsStale(database))
        {
            MaxMindDatabases.LogDatabaseStale(_logger, editionId, database.BuildDate);
        }
    }

    [LoggerMessage(
        EventId = 10,
        EventName = "MaxMindDatabaseUpdated",
        Level = LogLevel.Information,
        Message = "Loaded the MaxMind {EditionId} database built {BuildDate:yyyy-MM-dd}"
    )]
    private static partial void LogDatabaseUpdated(ILogger logger, string editionId, DateTime buildDate);
}
