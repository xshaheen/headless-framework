// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Simulation;

/// <summary>A storage failure the simulation raised on purpose. It is transient, as a dropped connection is.</summary>
[PublicAPI]
public sealed class SimulatedStoreException(string message) : DbException(message)
{
    public override bool IsTransient => true;
}

/// <summary>One storage fault the decorator applied, with what the store returned when the reply was the part lost.</summary>
/// <param name="Fault">The fault as it was armed.</param>
/// <param name="WithheldResult">
/// For <see cref="StoreFaultKind.LoseReply"/>, the result the store committed and the caller never saw; otherwise null.
/// </param>
[PublicAPI]
public sealed record AppliedStoreFault(StoreFault Fault, object? WithheldResult);

/// <summary>
/// Wraps the in-memory messaging store and fails it on cue: a call throws before it writes, commits and then loses its
/// reply, or commits only after the simulation clock moves on, and a granted lease can be lost while its attempt still
/// runs. Every other call passes through unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The messaging pipeline picks its path by testing the store for capability interfaces, so the decorator implements
/// every one the in-memory store implements and forwards each to it. A plain <see cref="IDataStorage"/> wrapper would
/// send the pipeline down fallback branches production never takes. The constructor refuses a store that lacks one,
/// rather than claim a capability it cannot forward.
/// </para>
/// <para>
/// <see cref="Install"/> also registers the decorator as the host's <see cref="IDeliveryCoordinationResolver"/>, which
/// the in-memory setup points straight at its own store, so coordinated publishes go through the decorator too.
/// </para>
/// </remarks>
/// <param name="inner">The store to forward to.</param>
/// <param name="faults">The fault plan to follow.</param>
/// <param name="clock">The simulated time delayed commits and lease lifetimes run on.</param>
/// <exception cref="ArgumentException"><paramref name="inner"/> lacks a capability interface the decorator forwards.</exception>
[PublicAPI]
public sealed class FaultInjectingDataStorage(IDataStorage inner, FaultScript faults, SimulationClock clock)
    : IDataStorage,
        IDelayedMessageClaimStorage,
        IGracefulLeaseReleaseStorage,
        ICircuitRetryDeferralStorage,
        IDeliveryCoordinationResolver,
        ICoordinatedMessageStore,
        IMessageRevocationStorage
{
    // Initialized in declaration order: the null checks run before any capability is read from the wrapped store.
    private readonly FaultScript _faults = Argument.IsNotNull(faults);
    private readonly SimulationClock _clock = Argument.IsNotNull(clock);
    private readonly IDelayedMessageClaimStorage _delayedClaims = _Require<IDelayedMessageClaimStorage>(inner);
    private readonly IGracefulLeaseReleaseStorage _leaseRelease = _Require<IGracefulLeaseReleaseStorage>(inner);
    private readonly ICircuitRetryDeferralStorage _retryDeferral = _Require<ICircuitRetryDeferralStorage>(inner);
    private readonly IDeliveryCoordinationResolver _coordination = _Require<IDeliveryCoordinationResolver>(inner);
    private readonly ICoordinatedMessageStore _coordinatedStore = _Require<ICoordinatedMessageStore>(inner);
    private readonly IMessageRevocationStorage _revocation = _Require<IMessageRevocationStorage>(inner);
    private readonly ConcurrentQueue<AppliedStoreFault> _applied = new();
    private readonly ConcurrentQueue<Guid> _lostLeases = new();

    // Leases granted while ExpireLeasesAfter is armed: when each was first granted, and the identity it holds now.
    private readonly ConcurrentDictionary<(Guid StorageId, bool Published), LeaseGrant> _leases = new();

    /// <summary>Gets the store the decorator forwards to.</summary>
    public IDataStorage Inner { get; } = inner;

    /// <summary>Gets every armed storage fault that fired, in order.</summary>
    public IReadOnlyList<AppliedStoreFault> AppliedFaults => [.. _applied];

    /// <summary>Gets the storage ids of the rows whose lease the decorator ended, in order.</summary>
    public IReadOnlyList<Guid> LostLeases => [.. _lostLeases];

    /// <summary>
    /// Replaces the messaging store a host registered with a decorator over it, as both its <see cref="IDataStorage"/>
    /// and its <see cref="IDeliveryCoordinationResolver"/>. Call it after <c>AddHeadlessMessaging</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No messaging store is registered.</exception>
    public static void Install(IServiceCollection services, FaultScript faults, SimulationClock clock)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(faults);
        Argument.IsNotNull(clock);

        var storage =
            services.LastOrDefault(static d => d.ServiceType == typeof(IDataStorage) && !d.IsKeyedService)
            ?? throw new InvalidOperationException(
                "No messaging store is registered to decorate. Call it after AddHeadlessMessaging with a storage."
            );

        services.Remove(storage);
        foreach (
            var resolver in services.Where(static d => d.ServiceType == typeof(IDeliveryCoordinationResolver)).ToList()
        )
        {
            services.Remove(resolver);
        }

        services.AddSingleton(sp => new FaultInjectingDataStorage((IDataStorage)_Create(sp, storage), faults, clock));
        services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<FaultInjectingDataStorage>());
        services.AddSingleton<IDeliveryCoordinationResolver>(sp => sp.GetRequiredService<FaultInjectingDataStorage>());
    }

    public IMonitoringApi GetMonitoringApi() => Inner.GetMonitoringApi();

    public IInboxOperationsApi GetInboxOperationsApi() => Inner.GetInboxOperationsApi();

    public IScheduledDeliveryOperationsApi GetScheduledDeliveryOperationsApi() =>
        Inner.GetScheduledDeliveryOperationsApi();

    public ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.GetInboxHistoryRetentionCutoffs,
            () => Inner.GetInboxHistoryRetentionCutoffsAsync(cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.DeleteExpiredInboxAudits,
            () => Inner.DeleteExpiredInboxAuditsAsync(cutoffs, batchSize, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.DeleteExpiredInboxReceipts,
            () => Inner.DeleteExpiredInboxReceiptsAsync(cutoffs, batchSize, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<InboxAdmissionResult> AdmitReceivedMessageAsync(
        string name,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation = 0,
        TimeSpan? inboxRetention = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.AdmitReceivedMessage,
            () =>
                Inner.AdmitReceivedMessageAsync(
                    name,
                    consumerIdentity,
                    contractVersion,
                    message,
                    generation,
                    inboxRetention,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    public ValueTask<bool> IsInboxMessageAdmittedAsync(
        string? tenantId,
        string messageId,
        string messageName,
        string consumerIdentity,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.IsInboxMessageAdmitted,
            () =>
                Inner.IsInboxMessageAdmittedAsync(
                    tenantId,
                    messageId,
                    messageName,
                    consumerIdentity,
                    lane,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    public ValueTask<bool> DeferReceivedInboxOrphanAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.DeferReceivedInboxOrphan,
            () => Inner.DeferReceivedInboxOrphanAsync(message, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<bool> ConfirmReceivedInboxRoutableAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ConfirmReceivedInboxRoutable,
            () => Inner.ConfirmReceivedInboxRoutableAsync(message, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedInboxOrphansOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.GetReceivedInboxOrphansOfNeedRetry,
            () => Inner.GetReceivedInboxOrphansOfNeedRetryAsync(lane, consumerIdentities, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask ChangePublishStateToDelayedAsync(
        IReadOnlyCollection<MediumMessage> messages,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ChangePublishStateToDelayed,
            () => Inner.ChangePublishStateToDelayedAsync(messages, cancellationToken),
            cancellationToken
        );
    }

    public async ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DbTransaction? transaction = null,
        RetryDelay? retryDelay = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        await _ExpireLeaseIfDueAsync(message.StorageId, published: true, cancellationToken).ConfigureAwait(false);
        return await _RunAsync(
                StorageOperation.ChangePublishState,
                () =>
                    Inner.ChangePublishStateAsync(
                        message,
                        state,
                        contentWrite,
                        transaction,
                        retryDelay,
                        lockedUntil,
                        originalRetries,
                        cancellationToken
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        await _ExpireLeaseIfDueAsync(message.StorageId, published: true, cancellationToken).ConfigureAwait(false);
        return await _RunAsync(
                StorageOperation.ChangePublishRetryState,
                () =>
                    Inner.ChangePublishRetryStateAsync(
                        message,
                        state,
                        contentWrite,
                        retryDelay,
                        lockedUntil,
                        originalRetries,
                        originalInlineAttempts,
                        cancellationToken
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReservePublishAttempt,
            () => Inner.ReservePublishAttemptAsync(message, originalInlineAttempts, cancellationToken),
            cancellationToken
        );
    }

    public async ValueTask<bool> RenewPublishLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        await _ExpireLeaseIfDueAsync(message.StorageId, published: true, cancellationToken).ConfigureAwait(false);
        var renewed = await _RunAsync(
                StorageOperation.RenewPublishLease,
                () => Inner.RenewPublishLeaseAsync(message, leaseDuration, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _TrackLease(renewed, message, published: true, renewal: true);
    }

    public async ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        var leased = await _RunAsync(
                StorageOperation.LeasePublish,
                () => Inner.LeasePublishAsync(message, leaseDuration, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _TrackLease(leased, message, published: true, renewal: false);
    }

    public async ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        var leased = await _RunAsync(
                StorageOperation.LeasePublishAndReserveAttempt,
                () =>
                    Inner.LeasePublishAndReserveAttemptAsync(
                        message,
                        leaseDuration,
                        originalInlineAttempts,
                        cancellationToken
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _TrackLease(leased, message, published: true, renewal: false);
    }

    public async ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        RetryDelay? retryDelay = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        await _ExpireLeaseIfDueAsync(message.StorageId, published: false, cancellationToken).ConfigureAwait(false);
        return await _RunAsync(
                StorageOperation.ChangeReceiveState,
                () =>
                    Inner.ChangeReceiveStateAsync(
                        message,
                        state,
                        contentWrite,
                        retryDelay,
                        lockedUntil,
                        originalRetries,
                        cancellationToken
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        await _ExpireLeaseIfDueAsync(message.StorageId, published: false, cancellationToken).ConfigureAwait(false);
        return await _RunAsync(
                StorageOperation.ChangeReceiveRetryState,
                () =>
                    Inner.ChangeReceiveRetryStateAsync(
                        message,
                        state,
                        contentWrite,
                        retryDelay,
                        lockedUntil,
                        originalRetries,
                        originalInlineAttempts,
                        cancellationToken
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReserveReceiveAttempt,
            () => Inner.ReserveReceiveAttemptAsync(message, originalInlineAttempts, cancellationToken),
            cancellationToken
        );
    }

    public async ValueTask<bool> RenewReceiveLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        await _ExpireLeaseIfDueAsync(message.StorageId, published: false, cancellationToken).ConfigureAwait(false);
        var renewed = await _RunAsync(
                StorageOperation.RenewReceiveLease,
                () => Inner.RenewReceiveLeaseAsync(message, leaseDuration, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _TrackLease(renewed, message, published: false, renewal: true);
    }

    public async ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        var leased = await _RunAsync(
                StorageOperation.LeaseReceive,
                () => Inner.LeaseReceiveAsync(message, leaseDuration, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _TrackLease(leased, message, published: false, renewal: false);
    }

    public async ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        var leased = await _RunAsync(
                StorageOperation.LeaseReceiveAndReserveAttempt,
                () =>
                    Inner.LeaseReceiveAndReserveAttemptAsync(
                        message,
                        leaseDuration,
                        originalInlineAttempts,
                        cancellationToken
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _TrackLease(leased, message, published: false, renewal: false);
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreMessage,
            () => Inner.StoreMessageAsync(name, message, transaction, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreMessage,
            () => Inner.StoreMessageAsync(name, content, transaction, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<MediumMessage> StoreScheduledMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset publishAt,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreScheduledMessage,
            () => Inner.StoreScheduledMessageAsync(name, message, publishAt, transaction, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreReceivedExceptionMessage,
            () =>
                Inner.StoreReceivedExceptionMessageAsync(
                    name,
                    consumerIdentity,
                    message,
                    exceptionInfo,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    public ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string consumerIdentity,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreReceivedExceptionMessage,
            () =>
                Inner.StoreReceivedExceptionMessageAsync(
                    name,
                    consumerIdentity,
                    content,
                    exceptionInfo,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreReceivedMessage,
            () => Inner.StoreReceivedMessageAsync(name, consumerIdentity, message, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        Message content,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.StoreReceivedMessage,
            () => Inner.StoreReceivedMessageAsync(name, consumerIdentity, content, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTimeOffset timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.DeleteExpires,
            () => Inner.DeleteExpiresAsync(table, timeout, batchCount, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.GetPublishedMessagesOfNeedRetry,
            () => Inner.GetPublishedMessagesOfNeedRetryAsync(lane, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> ReclaimDeadPublishedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReclaimDeadPublishedOwners,
            () => Inner.ReclaimDeadPublishedOwnersAsync(deadOwners, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ScheduleMessagesOfDelayed,
            () => Inner.ScheduleMessagesOfDelayedAsync(scheduleTask, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.GetReceivedMessagesOfNeedRetry,
            () => Inner.GetReceivedMessagesOfNeedRetryAsync(lane, consumerIdentities, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> ReclaimDeadReceivedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReclaimDeadReceivedOwners,
            () => Inner.ReclaimDeadReceivedOwnersAsync(deadOwners, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _RunAsync(
            StorageOperation.DeleteReceivedMessage,
            () => Inner.DeleteReceivedMessageAsync(id, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.DeleteReceivedMessages,
            () => Inner.DeleteReceivedMessagesAsync(ids, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _RunAsync(
            StorageOperation.DeletePublishedMessage,
            () => Inner.DeletePublishedMessageAsync(id, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> DeletePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.DeletePublishedMessages,
            () => Inner.DeletePublishedMessagesAsync(ids, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<IReadOnlyList<MediumMessage>> ClaimDelayedMessagesAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ClaimDelayedMessages,
            () => _delayedClaims.ClaimDelayedMessagesAsync(cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<bool> ReleasePublishedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReleasePublishedLease,
            () => _leaseRelease.ReleasePublishedLeaseAsync(identity, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<bool> ReleaseReceivedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReleaseReceivedLease,
            () => _leaseRelease.ReleaseReceivedLeaseAsync(identity, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> ReleasePublishedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReleasePublishedLeases,
            () => _leaseRelease.ReleasePublishedLeasesAsync(identities, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<int> ReleaseReceivedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAsync(
            StorageOperation.ReleaseReceivedLeases,
            () => _leaseRelease.ReleaseReceivedLeasesAsync(identities, cancellationToken),
            cancellationToken
        );
    }

    public ValueTask<MessageRevocationResult> RevokeAsync(Guid storageId, CancellationToken cancellationToken = default)
    {
        return _RunAsync(
            StorageOperation.Revoke,
            () => _revocation.RevokeAsync(storageId, cancellationToken),
            cancellationToken
        );
    }

    async ValueTask<bool> ICircuitRetryDeferralStorage.DeferReceivedRetryAsync(
        CircuitRetryDeferral deferral,
        CancellationToken cancellationToken
    )
    {
        await _ExpireLeaseIfDueAsync(deferral.Identity.StorageId, published: false, cancellationToken)
            .ConfigureAwait(false);
        return await _RunAsync(
                StorageOperation.DeferReceivedRetry,
                () => _retryDeferral.DeferReceivedRetryAsync(deferral, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    DeliveryCoordination IDeliveryCoordinationResolver.Resolve(IUnitOfWork unitOfWork)
    {
        return _coordination.Resolve(unitOfWork);
    }

    ValueTask<MediumMessage> ICoordinatedMessageStore.StoreCoordinatedMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        return _RunAsync(
            StorageOperation.StoreCoordinatedMessage,
            () =>
                _coordinatedStore.StoreCoordinatedMessageAsync(name, message, publishAt, unitOfWork, cancellationToken),
            cancellationToken
        );
    }

    private async ValueTask<T> _RunAsync<T>(
        StorageOperation operation,
        Func<ValueTask<T>> call,
        CancellationToken cancellationToken
    )
    {
        var fault = _faults.TakeStoreFault(operation);
        if (fault is null)
        {
            return await call().ConfigureAwait(false);
        }

        switch (fault.Kind)
        {
            case StoreFaultKind.Fail:
                _applied.Enqueue(new AppliedStoreFault(fault, WithheldResult: null));
                throw fault.Exception ?? _Failure(fault, "failed before it reached the store");
            case StoreFaultKind.LoseReply:
            {
                var result = await call().ConfigureAwait(false);
                _applied.Enqueue(new AppliedStoreFault(fault, result));
                throw _Failure(fault, "committed, but its reply was lost");
            }
            default:
                _applied.Enqueue(new AppliedStoreFault(fault, WithheldResult: null));
                await _clock.DelayAsync(fault.Delay, cancellationToken).ConfigureAwait(false);
                return await call().ConfigureAwait(false);
        }
    }

    private async ValueTask _RunAsync(
        StorageOperation operation,
        Func<ValueTask> call,
        CancellationToken cancellationToken
    )
    {
        await _RunAsync(
                operation,
                async () =>
                {
                    await call().ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private bool _TrackLease(bool granted, MediumMessage message, bool published, bool renewal)
    {
        if (!granted || message.LockedUntil is not { } lockedUntil || _faults.LeaseLifetime is null)
        {
            return granted;
        }

        var identity = new MessageLeaseIdentity(
            message.StorageId,
            message.Owner,
            lockedUntil,
            message.Lane,
            message.InboxAttemptFence
        );

        // A renewal moves the identity the row holds, but not when the lease was first granted: the scripted lifetime
        // runs from the grant, so renewing never saves a lease the store is going to lose.
        _leases.AddOrUpdate(
            (message.StorageId, published),
            static (_, grant) => new LeaseGrant(grant.Now, grant.Identity),
            static (_, existing, grant) =>
                grant.Renewal ? existing with { Identity = grant.Identity } : new LeaseGrant(grant.Now, grant.Identity),
            (Now: _clock.GetUtcNow(), Identity: identity, Renewal: renewal)
        );

        return granted;
    }

    /// <summary>
    /// Ends the row's lease in the wrapped store once its scripted lifetime has passed, so the call that follows sees
    /// the lease gone, exactly as the store reports a lease it lost.
    /// </summary>
    private async ValueTask _ExpireLeaseIfDueAsync(Guid storageId, bool published, CancellationToken cancellationToken)
    {
        if (
            _faults.LeaseLifetime is not { } lifetime
            || !_leases.TryGetValue((storageId, published), out var grant)
            || _clock.GetUtcNow() < grant.GrantedAt + lifetime
            || !_leases.TryRemove(new((storageId, published), grant))
        )
        {
            return;
        }

        _ = published
            ? await _leaseRelease.ReleasePublishedLeaseAsync(grant.Identity, cancellationToken).ConfigureAwait(false)
            : await _leaseRelease.ReleaseReceivedLeaseAsync(grant.Identity, cancellationToken).ConfigureAwait(false);

        _lostLeases.Enqueue(storageId);
    }

    private static SimulatedStoreException _Failure(StoreFault fault, string what)
    {
        return new SimulatedStoreException($"Simulated store fault: {fault.Operation} call {fault.CallNumber} {what}.");
    }

    private static T _Require<T>(IDataStorage inner)
        where T : class
    {
        Argument.IsNotNull(inner);

        return inner as T
            ?? throw new ArgumentException(
                $"{inner.GetType().Name} does not implement {typeof(T).Name}, which the decorator forwards; decorating "
                    + "it would claim a capability the store does not have.",
                nameof(inner)
            );
    }

    private static object _Create(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        return descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }

    private sealed record LeaseGrant(DateTimeOffset GrantedAt, MessageLeaseIdentity Identity);
}
