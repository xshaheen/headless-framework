// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Queueing;

public static class QueueExtensions
{
    public static Task StartAsync<T>(
        this IQueue<T> queue,
        Func<IQueueEntry<T>, Task> handler,
        bool autoComplete = false,
        CancellationToken cancellationToken = default
    )
        where T : class => queue.StartAsync((entry, _) => handler(entry), autoComplete, cancellationToken);
}
