// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

public sealed class CollectorProcessor : IProcessor
{
    private const int _ItemBatch = 1000;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;

    private readonly string[] _tableNames;
    private readonly TimeSpan _waitingInterval;

    public CollectorProcessor(
        ILogger<CollectorProcessor> logger,
        IOptions<MessagingOptions> options,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        _waitingInterval = TimeSpan.FromSeconds(options.Value.CollectorCleaningInterval);

        var initializer = _serviceProvider.GetRequiredService<IStorageInitializer>();

        _tableNames = [initializer.GetPublishedTableName(), initializer.GetReceivedTableName()];
    }

    public async Task ProcessAsync(ProcessingContext context)
    {
        foreach (var table in _tableNames)
        {
            _logger.CollectingExpiredData(table);

            int deletedCount;
            var time = _timeProvider.GetUtcNow().UtcDateTime;
            do
            {
                try
                {
                    deletedCount = await _serviceProvider
                        .GetRequiredService<IDataStorage>()
                        .DeleteExpiresAsync(table, time, _ItemBatch, context.CancellationToken)
                        .ConfigureAwait(false);

                    if (deletedCount != 0)
                    {
                        _logger.ExpiredItemsDeleted(deletedCount, table);

                        await context.WaitAsync(_delay).ConfigureAwait(false);
                        context.ThrowIfStopping();
                    }
                }
                catch (Exception ex)
                {
                    _logger.ExpiredDataDeleteFailed(ex, table, ex.Message);
                    throw;
                }
            } while (deletedCount != 0);
        }

        await context.WaitAsync(_waitingInterval).ConfigureAwait(false);
    }
}

internal static partial class CollectorProcessorLog
{
    [LoggerMessage(EventId = 3104, Level = LogLevel.Debug, Message = "Collecting expired data from table: {Table}")]
    public static partial void CollectingExpiredData(this ILogger logger, string table);

    [LoggerMessage(
        EventId = 3105,
        Level = LogLevel.Debug,
        Message = "Successfully deleted {DeletedCount} expired items from table '{Table}'."
    )]
    public static partial void ExpiredItemsDeleted(this ILogger logger, int deletedCount, string table);

    [LoggerMessage(
        EventId = 3106,
        Level = LogLevel.Error,
        Message = "An error occurred while attempting to delete expired data from table '{Table}':{ExMessage}"
    )]
    public static partial void ExpiredDataDeleteFailed(
        this ILogger logger,
        Exception ex,
        string table,
        string exMessage
    );
}
