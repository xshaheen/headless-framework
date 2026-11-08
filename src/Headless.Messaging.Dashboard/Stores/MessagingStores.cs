// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging stores the module serves: the main store, named <see cref="MessagingSetupBuilder.PrimaryOutboxName"/>,
/// which holds the published and received rows, and each additional outbox registered with <c>AddOutbox(name)</c>,
/// known by that name, which holds published rows only.
/// </summary>
/// <remarks>
/// Every read that covers several stores asks each one apart, with <see cref="Timeout"/> per store, so a store that is
/// slow or down is reported unavailable by name while the others answer.
/// </remarks>
internal sealed class MessagingStores
{
    private readonly ILogger _logger;

    public MessagingStores(
        string nodeName,
        IReadOnlyList<MessagingStore> stores,
        TimeSpan timeout,
        ILogger<MessagingStores>? logger = null
    )
    {
        Argument.IsNotNullOrWhiteSpace(nodeName);
        Argument.IsNotNull(stores);

        // A dashboard-only host reads no store of its own: every answer comes from the nodes it forwards to.
        if (stores.Count > 0 && !stores[0].HoldsReceived)
        {
            throw new ArgumentException("The first store must be the main store.", nameof(stores));
        }

        NodeName = nodeName;
        All = stores;
        Timeout = timeout;
        _logger = logger ?? NullLogger<MessagingStores>.Instance;
    }

    /// <summary>An empty store set, for a dashboard-only host that forwards every read to the nodes it discovers.</summary>
    public static MessagingStores Empty(MessagingDashboardOptions options, ILogger<MessagingStores> logger)
    {
        Argument.IsNotNull(options);

        return new MessagingStores(options.NodeName, [], options.StoreTimeout, logger);
    }

    /// <summary>Builds the store set of a host from the outboxes <c>AddHeadlessMessaging</c> registered.</summary>
    /// <exception cref="InvalidOperationException">The host has no messaging storage.</exception>
    public static MessagingStores Create(
        MessagingOutboxes outboxes,
        MessagingDashboardOptions options,
        ILogger<MessagingStores> logger
    )
    {
        Argument.IsNotNull(outboxes);
        Argument.IsNotNull(options);

        var primary =
            outboxes.Primary
            ?? throw new InvalidOperationException(
                "The Messaging dashboard module (AddMessaging) reads the messaging storage, and this host has none. "
                    + "Configure a storage inside AddHeadlessMessaging(...), such as UsePostgreSql(...)."
            );

        MessagingStore[] stores =
        [
            new(MessagingSetupBuilder.PrimaryOutboxName, primary, HoldsReceived: true),
            .. outboxes.Secondaries.Select(static outbox => new MessagingStore(
                outbox.Name,
                outbox.Storage,
                HoldsReceived: false
            )),
        ];

        return new MessagingStores(options.NodeName, stores, options.StoreTimeout, logger);
    }

    /// <summary>Gets the name this node writes into the ids of the objects it serves.</summary>
    public string NodeName { get; }

    /// <summary>Gets every store, the main store first, then the additional outboxes in registration order.</summary>
    public IReadOnlyList<MessagingStore> All { get; }

    /// <summary>Gets the main store, which holds the received rows.</summary>
    /// <exception cref="InvalidOperationException">The host reads no store of its own.</exception>
    public MessagingStore Primary =>
        All.Count > 0
            ? All[0]
            : throw new InvalidOperationException(
                "This dashboard-only host reads no Messaging store of its own; the request must go to a Messaging node."
            );

    /// <summary>Gets how long a read that covers several stores waits for one of them.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Returns the store named <paramref name="name"/>, or <see langword="null"/>.</summary>
    /// <remarks>Store names are unique compared case-insensitively, so a lookup is too.</remarks>
    public MessagingStore? Find(string? name)
    {
        return All.FirstOrDefault(store => string.Equals(store.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the store whose storage is <paramref name="storage"/>, or the main store when it is null.</summary>
    public MessagingStore? Of(IDataStorage? storage)
    {
        return storage is null ? Primary : All.FirstOrDefault(store => ReferenceEquals(store.Storage, storage));
    }

    /// <summary>
    /// Runs <paramref name="read"/> against every store in <paramref name="stores"/> at once, each bounded by
    /// <see cref="Timeout"/>, and returns each store's answer, or names it unavailable when it threw or did not
    /// answer in time.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<StoreAnswers<T>> ReadEachAsync<T>(
        IEnumerable<MessagingStore> stores,
        Func<MessagingStore, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        var targets = stores.ToArray();
        var reads = targets.Select(store => _ReadOneAsync(store, read, cancellationToken)).ToArray();
        var results = await Task.WhenAll(reads).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var answered = new List<(MessagingStore Store, T Value)>(targets.Length);
        var unavailable = new List<string>();

        for (var i = 0; i < targets.Length; i++)
        {
            if (results[i].Answered)
            {
                answered.Add((targets[i], results[i].Value!));
            }
            else
            {
                unavailable.Add(targets[i].Name);
            }
        }

        return new StoreAnswers<T>(answered, unavailable);
    }

    private async Task<(bool Answered, T? Value)> _ReadOneAsync<T>(
        MessagingStore store,
        Func<MessagingStore, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            // A provider may block before its first await; Task.Run keeps one store from holding the others back.
            var value = await Task.Run(() => read(store, timeout.Token), timeout.Token)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);

            return (true, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The store did not answer within its timeout: it is reported unavailable, the others still answer.
            _logger.LogMessagingStoreTimedOut(store.Name, Timeout);

            return (false, default);
        }
#pragma warning disable CA1031 // A store that fails is reported unavailable by name, so one store cannot fail a read of the others.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            _logger.LogMessagingStoreFailed(exception, store.Name);

            return (false, default);
        }
    }
}

/// <summary>One Messaging store the module serves.</summary>
/// <param name="Name">The store's name: <c>primary</c> for the main store, else its <c>AddOutbox</c> name.</param>
/// <param name="Storage">The store.</param>
/// <param name="HoldsReceived">Whether the store holds received rows: only the main store does.</param>
internal sealed record MessagingStore(string Name, IDataStorage Storage, bool HoldsReceived)
{
    /// <summary>Gets the store's monitoring reads.</summary>
    public IMonitoringApi Monitoring => Storage.GetMonitoringApi();
}

/// <summary>The answers of a read over several stores, and the names of the stores that did not answer.</summary>
internal sealed record StoreAnswers<T>(
    IReadOnlyList<(MessagingStore Store, T Value)> Answered,
    IReadOnlyList<string> Unavailable
);

internal static partial class MessagingStoresLogs
{
    [LoggerMessage(
        EventId = 2131,
        EventName = "MessagingDashboardStoreTimedOut",
        Level = LogLevel.Warning,
        Message = "Messaging dashboard store {Store} did not answer within {Timeout}; it is reported unavailable."
    )]
    internal static partial void LogMessagingStoreTimedOut(this ILogger logger, string store, TimeSpan timeout);

    [LoggerMessage(
        EventId = 2132,
        EventName = "MessagingDashboardStoreFailed",
        Level = LogLevel.Warning,
        Message = "Messaging dashboard store {Store} failed a read; it is reported unavailable."
    )]
    internal static partial void LogMessagingStoreFailed(this ILogger logger, Exception exception, string store);
}
