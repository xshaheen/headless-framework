// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Configuration;
using Framework.Messages.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Processor;

public sealed class CollectorProcessor : IProcessor
{
    private const int _ItemBatch = 1000;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly string[] _tableNames;
    private readonly TimeSpan _waitingInterval;

    public CollectorProcessor(
        ILogger<CollectorProcessor> logger,
        IOptions<CapOptions> options,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _waitingInterval = TimeSpan.FromSeconds(options.Value.CollectorCleaningInterval);

        var initializer = _serviceProvider.GetRequiredService<IStorageInitializer>();

        _tableNames = [initializer.GetPublishedTableName(), initializer.GetReceivedTableName()];
    }

    public async Task ProcessAsync(ProcessingContext context)
    {
        foreach (var table in _tableNames)
        {
            _logger.LogDebug($"Collecting expired data from table: {table}");

            int deletedCount;
            var time = DateTime.Now;
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
                        _logger.LogDebug($"Successfully deleted {deletedCount} expired items from table '{table}'.");

                        await context.WaitAsync(_delay).ConfigureAwait(false);
                        context.ThrowIfStopping();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        $"An error occurred while attempting to delete expired data from table '{table}':{ex.Message}"
                    );
                    throw;
                }
            } while (deletedCount != 0);
        }

        await context.WaitAsync(_waitingInterval).ConfigureAwait(false);
    }
}
