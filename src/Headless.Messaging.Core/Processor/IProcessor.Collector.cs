// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

internal sealed class CollectorProcessor : IProcessor
{
    private const int _ItemBatch = 1000;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;

    private readonly string[] _categories;
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

        _categories =
        [
            initializer.GetPublishedTableName(),
            initializer.GetReceivedTableName(),
            "inbox-audits",
            "inbox-receipts",
        ];
    }

    public async Task ProcessAsync(ProcessingContext context)
    {
        context.ThrowIfStopping();
        var storage = _serviceProvider.GetRequiredService<IDataStorage>();
        var cutoffs = await storage
            .GetInboxHistoryRetentionCutoffsAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var time = _timeProvider.GetUtcNow();
        bool deletedInRound;

        // Freeze eligibility for this sweep so new history cannot keep extending it.
        do
        {
            deletedInRound = false;
            for (var category = 0; category < _categories.Length; category++)
            {
                context.ThrowIfStopping();
                var name = _categories[category];
                _logger.CollectingExpiredData(name);
                try
                {
                    var deletedCount = category switch
                    {
                        0 or 1 => await storage
                            .DeleteExpiresAsync(name, time, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                        2 => await storage
                            .DeleteExpiredInboxAuditsAsync(cutoffs, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                        _ => await storage
                            .DeleteExpiredInboxReceiptsAsync(cutoffs, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                    };

                    if (deletedCount != 0)
                    {
                        deletedInRound = true;
                        _logger.ExpiredItemsDeleted(deletedCount, name);

                        await context.WaitAsync(_delay).ConfigureAwait(false);
                        context.ThrowIfStopping();
                    }
                }
                catch (Exception ex)
                {
                    _logger.ExpiredDataDeleteFailed(ex, name, ex.Message);
                    throw;
                }
            }
        } while (deletedInRound);

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
