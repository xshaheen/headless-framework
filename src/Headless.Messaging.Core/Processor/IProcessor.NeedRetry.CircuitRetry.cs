// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

/// <summary>
/// Circuit-breaker aware disposition of claimed received retries: dispatch of the acquired probe,
/// deferral to the authoritative next-probe boundary, and retention of sibling probe claims.
/// </summary>
internal sealed partial class MessageNeedToRetryProcessor
{
    private async ValueTask<bool> _DispatchReceivedAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        if (_dispatcher is IRetryDispatcher retryDispatcher)
        {
            return await retryDispatcher.DispatchReceivedAsync(message, cancellationToken).ConfigureAwait(false);
        }

        await _dispatcher.EnqueueToExecute(message, descriptor: null, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Hands the acquired probe to the dispatcher. Once the row is transferred, the dispatcher's
    /// attempt owns the probe generation: it releases it if the attempt is abandoned before execution,
    /// and the executor's normal reporting completes it otherwise.
    /// </summary>
    private async ValueTask<bool> _DispatchProbeAsync(HalfOpenProbeHandle probe, CancellationToken cancellationToken)
    {
        var transferred = false;
        try
        {
            transferred = _dispatcher is IRetryDispatcher retryDispatcher
                ? await retryDispatcher
                    .DispatchReceivedAsync(probe.Work.Message, probe.Release, cancellationToken)
                    .ConfigureAwait(false)
                : await _DispatchReceivedAsync(probe.Work.Message, cancellationToken).ConfigureAwait(false);
            if (transferred)
            {
                probe.MarkTransferred();
            }
        }
        catch (Exception ex)
        {
            _logger.CircuitRetryDispositionFailed(
                ex,
                probe.Work.Message.StorageId,
                LogSanitizer.Sanitize(probe.Work.Group)
            );
        }

        if (!transferred)
        {
            probe.Release();
        }

        return transferred;
    }

    private CircuitRetryDecision _GetCircuitRetryDecision(MessageLane lane, string group)
    {
        if (_circuitBreakerStateManager is not null)
        {
            return _circuitBreakerStateManager.GetRetryDecision(lane, group);
        }

        // A read-only monitor cannot reserve the shared probe generation. Conservatively retain
        // an open claim rather than recreating the old clear-without-deferral hot loop.
        return _circuitBreakerMonitor?.IsOpen(CircuitBreakerGroupKeys.For(lane, group)) == true
            ? new CircuitRetryDecision(CircuitRetryDecisionKind.Retain, NextProbeAt: null, ProbeOutcome: null)
            : CircuitRetryDecision.Closed;
    }

    /// <summary>Stable ordering: deferrals before retained claims.</summary>
    private static IEnumerable<CircuitRetryWork> _OrderDispositions(List<CircuitRetryWork> circuitWork)
    {
        return circuitWork
            .Where(static work => work.Decision.Kind is not CircuitRetryDecisionKind.ProbeAcquired)
            .OrderBy(static work =>
                work.Decision.Kind switch
                {
                    CircuitRetryDecisionKind.Defer => 0,
                    CircuitRetryDecisionKind.Retain => 1,
                    _ => 2,
                }
            );
    }

    private async ValueTask _DisposeCircuitClaimAsync(IDataStorage storage, CircuitRetryWork work)
    {
        try
        {
            switch (work.Decision.Kind)
            {
                case CircuitRetryDecisionKind.Defer:
                    var deferred = await _DeferCircuitClaimAsync(
                            storage,
                            work.Message,
                            work.Decision.NextProbeAt!.Value
                        )
                        .ConfigureAwait(false);
                    if (!deferred && Interlocked.Exchange(ref _circuitDeferralRejectedWarned, 1) == 0)
                    {
                        // The provider's deferral write is fenced on row, lane, exact owner, a still-live
                        // lease, and a non-terminal status. A `false` result means one of those no longer
                        // matched, but not which — do not assert a cause here. Either way the row keeps its
                        // stale owner and an unchanged, already-past NextRetryAt, so it is immediately
                        // reclaimable on the next poll: exactly the churn this deferral path exists to prevent.
                        _logger.CircuitRetryDeferralRejected(work.Message.StorageId, LogSanitizer.Sanitize(work.Group));
                    }
                    break;
                case CircuitRetryDecisionKind.Retain:
                    if (Interlocked.Exchange(ref _monitorOnlyRetainWarned, 1) == 0)
                    {
                        _logger.CircuitRetryRetainedWithoutStateManager(
                            work.Message.StorageId,
                            LogSanitizer.Sanitize(work.Group)
                        );
                    }
                    break;
                case CircuitRetryDecisionKind.ProbePending:
                    // This row does not own the shared probe generation. Retain its exact claim for
                    // store-authoritative expiry without blocking healthy pickup in this quadrant.
                    break;
            }
        }
        catch (Exception ex)
        {
            // An exception, cancellation, or unknown provider outcome leaves this exact lease in
            // place. In particular, do not route it through the generic abandoned-claim releaser.
            _logger.CircuitRetryDispositionFailed(ex, work.Message.StorageId, LogSanitizer.Sanitize(work.Group));
        }
    }

    private ValueTask<bool> _DeferCircuitClaimAsync(
        IDataStorage storage,
        MediumMessage message,
        DateTimeOffset nextRetryAt
    )
    {
        if (storage is not ICircuitRetryDeferralStorage deferralStorage)
        {
            if (_unsupportedCircuitDeferralProviders.TryAdd(storage.GetType(), 0))
            {
                _logger.CircuitRetryDeferralUnsupported(storage.GetType().FullName ?? storage.GetType().Name);
            }

            return ValueTask.FromResult(false);
        }

        if (message.LockedUntil is not { } lockedUntil)
        {
            return ValueTask.FromResult(false);
        }

        var identity = new MessageLeaseIdentity(message.StorageId, message.Owner, lockedUntil, message.Lane);
        return deferralStorage.DeferReceivedRetryAsync(
            new CircuitRetryDeferral(identity, nextRetryAt),
            CancellationToken.None
        );
    }

    private readonly record struct CircuitRetryWork(MediumMessage Message, string Group, CircuitRetryDecision Decision);

    /// <summary>
    /// Owns one acquired half-open probe generation for the duration of a pickup cycle. Release is
    /// idempotent so the processor's own cleanup and the dispatcher's pre-execution abandonment hook
    /// can both call it without double-releasing the shared slot.
    /// </summary>
    private sealed class HalfOpenProbeHandle(
        ICircuitBreakerStateManager? stateManager,
        string probeKey,
        CircuitRetryWork work
    )
    {
        private int _released;
        private bool _transferred;

        public CircuitRetryWork Work { get; } = work;

        public void MarkTransferred()
        {
            _transferred = true;
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                stateManager?.ReleaseHalfOpenProbe(probeKey);
            }
        }

        /// <summary>Cycle cleanup: a probe that never reached the dispatcher still owns its slot.</summary>
        public void ReleaseUnlessTransferred()
        {
            if (!_transferred)
            {
                Release();
            }
        }
    }
}
