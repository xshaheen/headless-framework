// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

/// <summary>
/// Retries the schema initialization of every additional outbox whose database was unreachable at startup, so the
/// host starts without it and picks it up once the database comes back. Each outbox is retried on its own; one
/// that stays down does not hold back another.
/// </summary>
internal sealed class OutboxInitializationProcessor(ILogger<OutboxInitializationProcessor> logger) : IProcessor
{
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);

    // Also the longest a recovered database waits for its relay to resume unless a unit of work initializes it first.
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var secondaries = context.Provider.GetService<MessagingOutboxes>()?.Secondaries ?? [];
        var pending = secondaries.Where(static outbox => !outbox.IsInitialized).ToList();
        var delay = InitialRetryDelay;

        while (pending.Count > 0)
        {
            // Wait first: the bootstrapper has just attempted every outbox this loop starts with.
            await context.WaitAsync(InfiniteRetryProcessor.WithJitter(delay)).ConfigureAwait(false);

            var nextDelay = delay * 2 < MaxRetryDelay ? delay * 2 : MaxRetryDelay;
            await Task.WhenAll(pending.Select(outbox => _InitializeAsync(outbox, nextDelay, context.CancellationToken)))
                .ConfigureAwait(false);

            pending.RemoveAll(static outbox => outbox.IsInitialized);
            delay = nextDelay;
        }

        // Nothing left to initialize. Park until shutdown: returning would make the infinite-retry wrapper call again.
        await context.WaitAsync(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
    }

    private async Task _InitializeAsync(
        MessagingOutbox outbox,
        TimeSpan retryDelay,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Returns at once when a unit of work on the outbox's database already initialized it.
            await outbox.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            logger.OutboxStorageInitialized(outbox.Name);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.OutboxStorageInitRetryFailed(e, outbox.Name, retryDelay);
        }
    }
}
