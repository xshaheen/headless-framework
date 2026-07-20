// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Tests;

internal static class AzureServiceBusResourceCleanup
{
    public static async ValueTask DeleteTrackedAsync(
        ConcurrentDictionary<string, byte> trackedResources,
        string resourceName,
        Func<CancellationToken, Task> delete,
        Func<Exception, bool> isAlreadyDeleted,
        CancellationToken cancellationToken
    )
    {
        if (!trackedResources.ContainsKey(resourceName))
        {
            return;
        }

        try
        {
            await delete(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (isAlreadyDeleted(exception)) { }

        trackedResources.TryRemove(resourceName, out _);
    }

    public static async ValueTask DeleteAllAsync(IEnumerable<(string Resource, Func<ValueTask> Delete)> operations)
    {
        List<Exception>? failures = null;

        foreach (var (resource, delete) in operations)
        {
            try
            {
                await delete().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException($"Failed to clean up {resource}.", exception));
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more Azure Service Bus resources could not be cleaned up.", failures);
        }
    }
}
