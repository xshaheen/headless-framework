// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

internal sealed class CollectorProcessor : IProcessor
{
    private const int _ItemBatch = 1000;
    private static readonly CleanupCategory[] _Categories =
    [
        CleanupCategory.Published,
        CleanupCategory.Received,
        CleanupCategory.InboxAudits,
        CleanupCategory.InboxReceipts,
    ];

    private readonly TimeSpan _delay = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;

    private readonly string _publishedTableName;
    private readonly string _receivedTableName;
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

        _publishedTableName = initializer.GetPublishedTableName();
        _receivedTableName = initializer.GetReceivedTableName();
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
            foreach (var category in _Categories)
            {
                context.ThrowIfStopping();
                var name = category.ToString();
                _logger.CollectingExpiredData(name);
                try
                {
                    var deletedCount = category switch
                    {
                        CleanupCategory.Published => await storage
                            .DeleteExpiresAsync(_publishedTableName, time, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                        CleanupCategory.Received => await storage
                            .DeleteExpiresAsync(_receivedTableName, time, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                        CleanupCategory.InboxAudits => await storage
                            .DeleteExpiredInboxAuditsAsync(cutoffs, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                        CleanupCategory.InboxReceipts => await storage
                            .DeleteExpiredInboxReceiptsAsync(cutoffs, _ItemBatch, context.CancellationToken)
                            .ConfigureAwait(false),
                        _ => throw new UnreachableException(),
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

    private enum CleanupCategory
    {
        Published,
        Received,
        InboxAudits,
        InboxReceipts,
    }
}

internal static partial class CollectorProcessorLog
{
    [LoggerMessage(
        EventId = 3104,
        Level = LogLevel.Debug,
        Message = "Collecting expired data for category {Category}."
    )]
    public static partial void CollectingExpiredData(this ILogger logger, string category);

    [LoggerMessage(
        EventId = 3105,
        Level = LogLevel.Debug,
        Message = "Successfully deleted {DeletedCount} expired items for category {Category}."
    )]
    public static partial void ExpiredItemsDeleted(this ILogger logger, int deletedCount, string category);

    [LoggerMessage(
        EventId = 3106,
        Level = LogLevel.Error,
        Message = "An error occurred while deleting expired data for category {Category}: {ExMessage}"
    )]
    public static partial void ExpiredDataDeleteFailed(
        this ILogger logger,
        Exception ex,
        string category,
        string exMessage
    );
}
