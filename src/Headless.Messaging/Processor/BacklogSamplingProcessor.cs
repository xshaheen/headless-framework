// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

/// <summary>
/// Reads the backlog of the primary storage and of each additional outbox once per
/// <see cref="MessagingOptions.BacklogSamplingInterval"/> and keeps the result for the backlog gauges, which only ever
/// report the last sample. Runs only when the interval is set.
/// </summary>
/// <remarks>
/// Each storage is read on its own: a storage whose read fails keeps its last sample and is logged, and the others
/// still refresh. An additional outbox is skipped until its schema is initialized, as its relay is.
/// </remarks>
internal sealed class BacklogSamplingProcessor : IProcessor, IDisposable
{
    private const string _PrimaryName = "the primary storage";

    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _interval;
    private List<StorageSource>? _sources;
    private volatile BacklogMeasurement[] _measurements = [];

    public BacklogSamplingProcessor(
        ILogger<BacklogSamplingProcessor> logger,
        IOptions<MessagingOptions> options,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _interval = options.Value.BacklogSamplingInterval ?? TimeSpan.Zero;
        MessagingMetrics.RegisterBacklogSampler(this);
    }

    /// <summary>The measurements of the last sample of every storage; empty until one succeeds.</summary>
    internal IReadOnlyList<BacklogMeasurement> Measurements => _measurements;

    public async Task ProcessAsync(ProcessingContext context)
    {
        // Wait first: the processing server starts its processors inside the host's start, which must not wait on a
        // storage read.
        await context.WaitAsync(_interval).ConfigureAwait(false);
        await SampleAsync(context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads every storage's backlog once and replaces the measurements of each read that succeeded.</summary>
    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        var sources = _sources ??= _ResolveSources();

        foreach (var source in sources)
        {
            if (!source.IsReady())
            {
                continue;
            }

            try
            {
                var backlog = await source.Read(cancellationToken).ConfigureAwait(false);
                source.Measurements = source.Measure(backlog);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.BacklogSampleFailed(ex, source.Name);
            }
        }

        _measurements = [.. sources.SelectMany(static source => source.Measurements)];
    }

    public void Dispose()
    {
        MessagingMetrics.UnregisterBacklogSampler(this);
    }

    private List<StorageSource> _ResolveSources()
    {
        var sources = new List<StorageSource>();

        if (_serviceProvider.GetService<IDataStorage>() is { } primary)
        {
            var monitoring = primary.GetMonitoringApi();
            sources.Add(
                new StorageSource(
                    _PrimaryName,
                    static () => true,
                    monitoring.GetBacklogAsync,
                    includeReceived: true,
                    primary is IRelationalOutboxStorage relational ? _DatabaseName(relational) : null
                )
            );
        }

        foreach (var outbox in _serviceProvider.GetService<MessagingOutboxes>()?.Secondaries ?? [])
        {
            // An additional outbox holds only published rows, so only its published half is read.
            if (outbox.Storage.GetMonitoringApi() is not RelationalMonitoringApi monitoring)
            {
                continue;
            }

            var databaseName = _DatabaseName(outbox.Relational);
            sources.Add(
                new StorageSource(
                    $"{outbox.Name} ({databaseName})",
                    () => outbox.IsInitialized,
                    monitoring.GetPublishedBacklogAsync,
                    includeReceived: false,
                    databaseName
                )
            );
        }

        return sources;
    }

    // Read from the storage's own connection string, never by connecting: it only names the database for the tag.
    private static string? _DatabaseName(IRelationalOutboxStorage storage)
    {
        using var connection = storage.CreateIdentityConnection();
        return string.IsNullOrEmpty(connection.Database) ? null : connection.Database;
    }

    private sealed class StorageSource(
        string name,
        Func<bool> isReady,
        Func<CancellationToken, ValueTask<BacklogView>> read,
        bool includeReceived,
        string? databaseName
    )
    {
        private readonly KeyValuePair<string, object?>[] _publishTags = _Tags(
            MessagingMetrics.PersistenceTypePublish,
            databaseName
        );

        private readonly KeyValuePair<string, object?>[] _consumeTags = _Tags(
            MessagingMetrics.PersistenceTypeConsume,
            databaseName
        );

        public string Name { get; } = name;

        public Func<bool> IsReady { get; } = isReady;

        public Func<CancellationToken, ValueTask<BacklogView>> Read { get; } = read;

        public BacklogMeasurement[] Measurements { get; set; } = [];

        public BacklogMeasurement[] Measure(BacklogView backlog)
        {
            var published = new BacklogMeasurement(
                backlog.PublishedPending,
                _Seconds(backlog.OldestPublishedPendingAge),
                _publishTags
            );

            return includeReceived
                ?
                [
                    published,
                    new BacklogMeasurement(
                        backlog.ReceivedPending,
                        _Seconds(backlog.OldestReceivedPendingAge),
                        _consumeTags
                    ),
                ]
                : [published];
        }

        // No pending row reads as no wait, so the age series stays continuous instead of vanishing when the
        // backlog drains.
        private static double _Seconds(TimeSpan? age) => age?.TotalSeconds ?? 0;

        private static KeyValuePair<string, object?>[] _Tags(string persistenceType, string? databaseName)
        {
            return databaseName is null
                ? [new(MessagingMetrics.TagPersistenceType, persistenceType)]
                :
                [
                    new(MessagingMetrics.TagPersistenceType, persistenceType),
                    new(MessagingMetrics.TagDbNamespace, databaseName),
                ];
        }
    }
}

/// <summary>One gauge reading of one table of one storage, with the tags that name it.</summary>
internal sealed record BacklogMeasurement(long Pending, double OldestAgeSeconds, KeyValuePair<string, object?>[] Tags);

internal static partial class BacklogSamplingProcessorLog
{
    [LoggerMessage(
        EventId = 4136,
        EventName = "BacklogSampleFailed",
        Level = LogLevel.Warning,
        Message = "Reading the message backlog of {StorageName} failed; its backlog gauges keep the last sample."
    )]
    internal static partial void BacklogSampleFailed(this ILogger logger, Exception exception, string storageName);
}
