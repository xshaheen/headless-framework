// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MaxMind.Db;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation.Internal;

/// <summary>The databases the provider reads, keyed by edition, and replaced in place when an update lands.</summary>
/// <remarks>
/// Readers load the whole file into memory, so the file can be replaced while a reader is in use, and a replaced
/// reader is left to the garbage collector instead of disposed: a lookup that already holds it finishes safely.
/// </remarks>
internal sealed partial class MaxMindDatabases
{
    private readonly MaxMindOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaxMindDatabases> _logger;
    private readonly Dictionary<string, MaxMindDatabase> _databases = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public MaxMindDatabases(
        IOptions<MaxMindOptions> options,
        TimeProvider timeProvider,
        ILogger<MaxMindDatabases> logger
    )
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        foreach (var editionId in Editions)
        {
            _LoadExisting(editionId);
        }
    }

    public IEnumerable<string> Editions
    {
        get
        {
            if (_options.LocationEditionId is { Length: > 0 } location)
            {
                yield return location;
            }

            if (_options.AsnEditionId is { Length: > 0 } asn)
            {
                yield return asn;
            }
        }
    }

    public MaxMindDatabase? Location => _options.LocationEditionId is { Length: > 0 } id ? Get(id) : null;

    public MaxMindDatabase? Asn => _options.AsnEditionId is { Length: > 0 } id ? Get(id) : null;

    public MaxMindDatabase? Get(string editionId)
    {
        lock (_lock)
        {
            return _databases.GetValueOrDefault(editionId);
        }
    }

    public string PathOf(string editionId) => Path.Combine(_options.DatabaseDirectory, editionId + ".mmdb");

    /// <summary>Loads <paramref name="path" /> into memory, proving the file is a valid database.</summary>
    /// <exception cref="InvalidDatabaseException">The file is not a MaxMind database.</exception>
    public MaxMindDatabase Open(string path)
    {
#pragma warning disable CA2000 // False positive: MaxMindDatabase owns the reader, and a replaced one is left to the GC because a running lookup may still hold it (an in-memory reader holds no file handle).
        return new(new DatabaseReader(path, _options.Locales, FileAccessMode.Memory));
#pragma warning restore CA2000
    }

    public void Replace(string editionId, MaxMindDatabase database)
    {
        lock (_lock)
        {
            _databases[editionId] = database;
        }
    }

    private void _LoadExisting(string editionId)
    {
        var path = PathOf(editionId);

        if (!File.Exists(path))
        {
            LogDatabaseMissing(_logger, editionId, path, _options.HasCredentials);

            return;
        }

        try
        {
            Replace(editionId, Open(path));
        }
        catch (Exception e) when (e is InvalidDatabaseException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the host; the updater replaces it when it can.
            LogDatabaseUnreadable(_logger, e, editionId, path);

            return;
        }

        // Without a license key nothing refreshes the file, so its age is reported once here.
        var database = Get(editionId)!;

        if (!_options.HasCredentials && IsStale(database))
        {
            LogDatabaseStale(_logger, editionId, database.BuildDate);
        }
    }

    /// <summary>The GeoLite EULA requires using a new release within 30 days of it.</summary>
    public bool IsStale(MaxMindDatabase database) =>
        _timeProvider.GetUtcNow().UtcDateTime - database.BuildDate > TimeSpan.FromDays(30);

    [LoggerMessage(
        EventId = 1,
        EventName = "MaxMindDatabaseMissing",
        Level = LogLevel.Warning,
        Message = "The MaxMind {EditionId} database is missing at {Path}; lookups leave its fields empty until it "
            + "exists (downloading: {DownloadsEnabled})"
    )]
    private static partial void LogDatabaseMissing(
        ILogger logger,
        string editionId,
        string path,
        bool downloadsEnabled
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "MaxMindDatabaseUnreadable",
        Level = LogLevel.Error,
        Message = "The MaxMind {EditionId} database at {Path} could not be read; lookups leave its fields empty"
    )]
    private static partial void LogDatabaseUnreadable(
        ILogger logger,
        Exception exception,
        string editionId,
        string path
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "MaxMindDatabaseStale",
        Level = LogLevel.Warning,
        Message = "The MaxMind {EditionId} database was built {BuildDate:yyyy-MM-dd}, more than 30 days ago; the "
            + "GeoLite license requires using a newer release within 30 days of it"
    )]
    internal static partial void LogDatabaseStale(ILogger logger, string editionId, DateTime buildDate);
}

/// <summary>One loaded database and what lookups need to know about it.</summary>
internal sealed class MaxMindDatabase(DatabaseReader reader)
{
    public DatabaseReader Reader { get; } = reader;

    public DateTime BuildDate { get; } = reader.Metadata.BuildDate;

    /// <summary>City-level editions answer city lookups; Country editions refuse them.</summary>
    public bool HasCityData { get; } =
        reader.Metadata.DatabaseType.Contains("City", StringComparison.Ordinal)
        || reader.Metadata.DatabaseType.Contains("Enterprise", StringComparison.Ordinal);
}
