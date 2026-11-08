// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging module's commands: Re-execute, Purge, Hold, and Release on a received row; Re-send, Delete, Dispatch
/// now, and Revoke on a published row; Reset and Force open on a circuit; and Reset back-pressure on a node.
/// Re-execute, Purge, Re-send, and Delete also run over a filter as Operations.
/// </summary>
/// <remarks>
/// <para>
/// The shell enforces each command's permission, confirmation, and reason before this class runs, so this class
/// checks the command's rule against the stored row, applies it through the store's audited operations, and maps the
/// store's outcome to a row result. A call on one object that the rule refuses changes nothing and answers with the
/// refusal, which the shell returns as <c>409</c>.
/// </para>
/// <para>
/// Every storage write runs as the signed-in operator, whom the store records on its receipt. A user with no stable
/// name cannot be recorded, so the storage commands refuse them. A circuit and a node's back-pressure live in this
/// node's memory: their commands act here and write no receipt, and the shell's operator log records them.
/// </para>
/// </remarks>
internal sealed class MessagingCommands(MessagingStores stores, MessagingSources sources, IServiceProvider services)
{
    internal const string NeedsNamedOperator = "Sign in with a named account";
    internal const string Gone = "Gone";
    internal const string OtherNode = "Served by another node";
    internal const string StillProcessing = "Still processing";
    internal const string ReleaseFirst = "Release first";
    internal const string HeldReleaseFirst = "Held — release first";
    internal const string AlreadyHeld = "Already held";
    internal const string NotHeld = "Not held";
    internal const string NotFailed = "Not failed";
    internal const string Changed = "Changed since it was read";
    internal const string OperationIdUsed = "Operation id already used";
    internal const string NotSupportedForOutboxes = "Not supported for additional outboxes";
    internal const string NotSupportedByStore = "Not supported by this store";
    internal const string NotScheduled = "Not a pending scheduled delivery";
    internal const string Leased = "Leased by a node — try again shortly";
    internal const string OnePodOnly = "Acts on one pod; Kubernetes discovery addresses a Service";
    internal const string UnknownCircuit = "Unknown circuit";
    internal const string AlreadyClosed = "Already closed";
    internal const string AlreadyOpen = "Already open";
    internal const string NoRetryProcessor = "No retry processor on this node";
    internal const string NotBackedOff = "Not backed off";

    private const string _Prefix = MessagingDashboardOptions.ModuleId + ".";

    public const string ReExecuteId = _Prefix + "re-execute";
    public const string PurgeId = _Prefix + "purge";
    public const string HoldId = _Prefix + "hold";
    public const string ReleaseId = _Prefix + "release";
    public const string ResendId = _Prefix + "re-send";
    public const string DeleteId = _Prefix + "delete";
    public const string DispatchNowId = _Prefix + "dispatch-now";
    public const string RevokeId = _Prefix + "revoke";
    public const string ResetCircuitId = _Prefix + "reset-circuit";
    public const string ForceOpenCircuitId = _Prefix + "force-open";
    public const string ResetBackpressureId = _Prefix + "reset-backpressure";

    private static readonly ClaimsPrincipal _Nobody = new(new ClaimsIdentity());

    private readonly MessagingStores _stores = Argument.IsNotNull(stores);
    private readonly MessagingSources _sources = Argument.IsNotNull(sources);
    private readonly IServiceProvider _services = Argument.IsNotNull(services);

    // Kubernetes discovery is the one provider that registers no current-node identity, because it addresses each
    // node by its Service; a Service spreads requests over its pods, so a call cannot choose the pod it acts on.
    private readonly Lazy<bool> _onKubernetes = new(() =>
        services.GetService<INodeDiscoveryProvider>() is not null && services.GetService<CurrentNodeIdentity>() is null
    );

    public static IReadOnlyList<DashboardCommand> All { get; } =
    [
        new()
        {
            Id = ReExecuteId,
            Label = "Re-execute",
            ObjectKind = MessagingViews.ReceivedKind,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = PurgeId,
            Label = "Purge",
            ObjectKind = MessagingViews.ReceivedKind,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = HoldId,
            Label = "Hold",
            ObjectKind = MessagingViews.ReceivedKind,
            InverseCommandId = ReleaseId,
        },
        new()
        {
            Id = ReleaseId,
            Label = "Release",
            ObjectKind = MessagingViews.ReceivedKind,
            InverseCommandId = HoldId,
        },
        new()
        {
            Id = ResendId,
            Label = "Re-send",
            ObjectKind = MessagingViews.PublishedKind,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = DeleteId,
            Label = "Delete",
            ObjectKind = MessagingViews.PublishedKind,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = DispatchNowId,
            Label = "Dispatch now",
            ObjectKind = MessagingViews.PublishedKind,
        },
        new()
        {
            Id = RevokeId,
            Label = "Revoke",
            ObjectKind = MessagingViews.PublishedKind,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = ResetCircuitId,
            Label = "Reset circuit",
            ObjectKind = MessagingViews.CircuitKind,
        },
        new()
        {
            Id = ForceOpenCircuitId,
            Label = "Force open",
            ObjectKind = MessagingViews.CircuitKind,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = ResetBackpressureId,
            Label = "Reset back-pressure",
            ObjectKind = MessagingViews.NodeKind,
        },
    ];

    public async Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedAsync(
        IReadOnlyList<ObjectRef> targets,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var answers = new List<DashboardCommandAvailability>(targets.Count * 4);
        var authority = DashboardOperatorAuthority.Resolve(user ?? _Nobody);

        foreach (var target in targets)
        {
            if (!string.Equals(target.Module, MessagingDashboardOptions.ModuleId, StringComparison.Ordinal))
            {
                continue;
            }

            switch (target.Kind)
            {
                case MessagingViews.ReceivedKind:
                {
                    var (reExecute, purge, hold, release) = authority.IsSuccess
                        ? await _ReceivedRefusalsAsync(target, cancellationToken).ConfigureAwait(false)
                        : (NeedsNamedOperator, NeedsNamedOperator, NeedsNamedOperator, NeedsNamedOperator);
                    answers.Add(_Answer(target, ReExecuteId, reExecute));
                    answers.Add(_Answer(target, PurgeId, purge));
                    answers.Add(_Answer(target, HoldId, hold));
                    answers.Add(_Answer(target, ReleaseId, release));

                    break;
                }
                case MessagingViews.PublishedKind:
                {
                    var (resend, delete, dispatchNow, revoke) = authority.IsSuccess
                        ? await _PublishedRefusalsAsync(target, user!, cancellationToken).ConfigureAwait(false)
                        : (NeedsNamedOperator, NeedsNamedOperator, NeedsNamedOperator, NeedsNamedOperator);
                    answers.Add(_Answer(target, ResendId, resend));
                    answers.Add(_Answer(target, DeleteId, delete));
                    answers.Add(_Answer(target, DispatchNowId, dispatchNow));
                    answers.Add(_Answer(target, RevokeId, revoke));

                    break;
                }
                case MessagingViews.CircuitKind:
                {
                    var (reset, forceOpen) = _CircuitRefusals(target.Id);
                    answers.Add(_Answer(target, ResetCircuitId, reset));
                    answers.Add(_Answer(target, ForceOpenCircuitId, forceOpen));

                    break;
                }
                case MessagingViews.NodeKind:
                {
                    answers.Add(_Answer(target, ResetBackpressureId, _BackpressureRefusal(target.Id)));

                    break;
                }
            }
        }

        return answers;
    }

    public async Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<DashboardRowResult>(request.Ids.Count);
        var authority = DashboardOperatorAuthority.Resolve(request.User);

        foreach (var id in request.Ids)
        {
            var (row, refused) = await _ApplyAsync(request, authority, id, cancellationToken).ConfigureAwait(false);

            // A call on one object the rule refuses answers as a refusal: nothing changed, and the caller learns why.
            if (refused && request.Ids.Count == 1)
            {
                return new DashboardCommandResult { Rows = [row], Refusal = row.Reason };
            }

            rows.Add(row);
        }

        return new DashboardCommandResult { Rows = rows };
    }

    /// <summary>
    /// Freezes Re-execute, Purge, Re-send, or Delete over a filter: the exact count up to one past the limit, a sample,
    /// and a mark holding the store's latest change marker, past which no row joins the scope.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The command does not run over a filter, the filter is malformed or names an unknown store, or a received-row
    /// command names an additional outbox, which holds no received rows.
    /// </exception>
    public async Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        var (type, kind) = _OperationKind(request.CommandId);
        var (store, filter) = _ParseFilter(type, request.Filter);
        var highWater = await store.Storage.GetLatestMessageChangeMarkerAsync(cancellationToken).ConfigureAwait(false);
        var scope = _Scope(type, kind, filter, highWater ?? DateTimeOffset.MinValue);

        var count =
            type == MessageType.Subscribe
                ? await store
                    .Storage.GetInboxOperationsApi()
                    .CountOperationScopeAsync(scope, request.CountLimit, cancellationToken)
                    .ConfigureAwait(false)
                : await store
                    .Storage.GetPublishedOperationsApi()
                    .CountOperationScopeAsync(scope, request.CountLimit, cancellationToken)
                    .ConfigureAwait(false);
        var first = await _ReadScopeAsync(
                store,
                type,
                scope,
                after: null,
                Math.Clamp(request.SampleSize, 1, MessageOperationChunk.MaxChunkSize),
                cancellationToken
            )
            .ConfigureAwait(false);
        var sample = new List<DashboardObject>(first.Targets.Count);

        foreach (var target in first.Targets)
        {
            var id = new MessagingObjectId(_stores.NodeName, store.Name, target.StorageId);

            if (await _sources.FindAsync(type, id, cancellationToken).ConfigureAwait(false) is { } found)
            {
                sample.Add(MessagingViews.ToObject(type, found.Id, found.Message));
            }
        }

        return new DashboardOperationScope
        {
            Count = count.Capped ? (long)request.CountLimit + 1 : count.Count,
            Sample = sample,
            HighWaterMark = scope.HighWaterMark.UtcTicks.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Runs the next chunk of a frozen scope. Each write is conditional on the state the scope read returned: a
    /// received row's incarnation and status, a published row's change marker. So a row an earlier chunk, a concurrent
    /// chunk on another node, or an earlier run of the Operation changed is skipped, never written twice.
    /// </summary>
    /// <exception cref="ArgumentException">The mark, the cursor, or the filter is not one this module returned.</exception>
    public async Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        var (type, kind) = _OperationKind(request.CommandId);
        var (store, filter) = _ParseFilter(type, request.Filter);
        var scope = _Scope(type, kind, filter, _ParseMark(request.HighWaterMark));
        Guid? after = null;

        if (request.Cursor is { } value)
        {
            after = Guid.TryParseExact(value, "D", out var parsed)
                ? parsed
                : throw new ArgumentException($"'{value}' is not a Messaging Operation cursor.", nameof(request));
        }

        var chunk = await _ReadScopeAsync(
                store,
                type,
                scope,
                after,
                Math.Clamp(request.ChunkSize, 1, MessageOperationChunk.MaxChunkSize),
                cancellationToken
            )
            .ConfigureAwait(false);
        var rows = new List<DashboardRowResult>(chunk.Targets.Count);
        var authority = DashboardOperatorAuthority.Resolve(request.User);

        if (!authority.IsSuccess)
        {
            // Every row would be refused the same way, so the Operation stops here instead of walking its whole scope.
            foreach (var target in chunk.Targets)
            {
                rows.Add(_Skipped(_RowId(store, target.StorageId), NeedsNamedOperator));
            }

            return new DashboardOperationChunk { Rows = rows, NextCursor = null };
        }

        foreach (var target in chunk.Targets)
        {
            rows.Add(
                await _ApplyOperationAsync(
                        store,
                        kind,
                        type,
                        target,
                        request.Reason,
                        authority.Authorization,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            );
        }

        return new DashboardOperationChunk { Rows = rows, NextCursor = chunk.Next?.ToString("D") };
    }

    // An Operation's rows reach the streams through the change reader, which every node runs over the shared stores;
    // a read-back per row here would double the chunk's round trips for a change the reader delivers anyway.
    private async Task<DashboardRowResult> _ApplyOperationAsync(
        MessagingStore store,
        MessageOperationKind kind,
        MessageType type,
        MessageOperationTarget target,
        string? reason,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken
    )
    {
        var id = _RowId(store, target.StorageId);

        try
        {
            InboxOperationOutcome outcome;

            if (type == MessageType.Subscribe)
            {
                var inbox = store.Storage.GetInboxOperationsApi();
                var write = new InboxOperationRequest(
                    Guid.CreateVersion7(),
                    target.IncarnationId,
                    target.Status,
                    reason,
                    authorization
                );
                var result =
                    kind == MessageOperationKind.ReExecute
                        ? await inbox.ForceReprocessAsync(write, cancellationToken).ConfigureAwait(false)
                        : await inbox.PurgeAsync(write, cancellationToken).ConfigureAwait(false);
                outcome = result.Outcome;
            }
            else
            {
                var write = new PublishedOperationRequest(
                    Guid.CreateVersion7(),
                    target.StorageId,
                    target.ChangedAt,
                    reason,
                    authorization
                );
                var (published, receiptFailure) = await _WritePublishedAsync(
                        store,
                        kind == MessageOperationKind.Resend,
                        write,
                        id,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                return _PublishedRow(id, published, receiptFailure);
            }

            return outcome == InboxOperationOutcome.Applied ? _Done(id) : _Skipped(id, _OutcomeReason(outcome));
        }
        catch (NotSupportedException)
        {
            return _Skipped(id, NotSupportedForOutboxes);
        }
#pragma warning disable CA1031 // One row's failure, such as a failed receipt write, is reported on that row; the Operation runs on.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return _Failure(id, exception);
        }
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyAsync(
        DashboardCommandRequest request,
        OperatorAuthorityResult authority,
        string id,
        CancellationToken cancellationToken
    )
    {
        switch (request.CommandId)
        {
            case ResetCircuitId:
            case ForceOpenCircuitId:
                return await _ApplyToCircuitAsync(request.CommandId, id, cancellationToken).ConfigureAwait(false);
            case ResetBackpressureId:
                return await _ApplyToNodeAsync(id, cancellationToken).ConfigureAwait(false);
        }

        if (!authority.IsSuccess)
        {
            return (_Skipped(id, NeedsNamedOperator), true);
        }

        try
        {
            return request.CommandId switch
            {
                ReExecuteId or PurgeId or HoldId or ReleaseId => await _ApplyToReceivedAsync(
                        request,
                        authority.Authorization,
                        id,
                        cancellationToken
                    )
                    .ConfigureAwait(false),
                ResendId or DeleteId => await _ApplyToPublishedAsync(
                        request,
                        authority.Authorization,
                        id,
                        cancellationToken
                    )
                    .ConfigureAwait(false),
                DispatchNowId or RevokeId => await _ApplyToScheduledAsync(
                        request,
                        authority.Authorization,
                        id,
                        cancellationToken
                    )
                    .ConfigureAwait(false),
                _ => (_Failed(id, $"Unknown command '{request.CommandId}'"), true),
            };
        }
        catch (NotSupportedException)
        {
            return (_Skipped(id, NotSupportedForOutboxes), true);
        }
#pragma warning disable CA1031 // A failed write is reported on its row so the shell answers and logs it, instead of a 500.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Not a refusal: the store may have written part of the change, such as the row without its receipt, so
            // the call answers with the failure and the shell's operator log records the attempt.
            return (_Failure(id, exception), false);
        }
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToReceivedAsync(
        DashboardCommandRequest request,
        OperatorAuthorizationContext authorization,
        string id,
        CancellationToken cancellationToken
    )
    {
        var (found, view, refusal) = await _ReadReceivedAsync(id, cancellationToken).ConfigureAwait(false);
        var command = request.CommandId;

        if (refusal is null)
        {
            var (reExecute, purge, hold, release) = _ReceivedRules(view!);
            refusal = command switch
            {
                ReExecuteId => reExecute,
                PurgeId => purge,
                HoldId => hold,
                _ => release,
            };
        }

        if (refusal is not null)
        {
            return (_Skipped(id, refusal), true);
        }

        var (_, objectId, store, _) = found!.Value;
        var inbox = store.Storage.GetInboxOperationsApi();
        var write = new InboxOperationRequest(
            Guid.CreateVersion7(),
            view!.IncarnationId!.Value,
            view.StatusName,
            request.Reason,
            authorization
        );
        var result = command switch
        {
            ReExecuteId => await inbox.ForceReprocessAsync(write, cancellationToken).ConfigureAwait(false),
            PurgeId => await inbox.PurgeAsync(write, cancellationToken).ConfigureAwait(false),
            HoldId => await inbox.HoldAsync(write, cancellationToken).ConfigureAwait(false),
            _ => await inbox.ReleaseHoldAsync(write, cancellationToken).ConfigureAwait(false),
        };

        if (result.Outcome != InboxOperationOutcome.Applied)
        {
            return (_Skipped(id, _OutcomeReason(result.Outcome)), true);
        }

        // A purged row is gone; the change reader tells the views so.
        if (!string.Equals(command, PurgeId, StringComparison.Ordinal))
        {
            _Publish(MessageType.Subscribe, objectId.StorageId, view.MessageId, store);
        }

        if (result.ChildStorageId is { } child)
        {
            _Publish(MessageType.Subscribe, child, view.MessageId, store);
        }

        return (_Done(id), false);
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToPublishedAsync(
        DashboardCommandRequest request,
        OperatorAuthorizationContext authorization,
        string id,
        CancellationToken cancellationToken
    )
    {
        var (found, view, refusal) = await _ReadPublishedAsync(id, cancellationToken).ConfigureAwait(false);
        var command = request.CommandId;

        if (refusal is null)
        {
            var (resend, delete) = _PublishedRules(view!);
            refusal = string.Equals(command, ResendId, StringComparison.Ordinal) ? resend : delete;
        }

        if (refusal is not null)
        {
            return (_Skipped(id, refusal), true);
        }

        var (_, objectId, store, _) = found!.Value;
        var isResend = string.Equals(command, ResendId, StringComparison.Ordinal);
        var write = new PublishedOperationRequest(
            Guid.CreateVersion7(),
            objectId.StorageId,
            view!.ChangedAt,
            request.Reason,
            authorization
        );
        var (result, receiptFailure) = await _WritePublishedAsync(store, isResend, write, id, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == InboxOperationOutcome.Applied && isResend)
        {
            _Publish(MessageType.Publish, objectId.StorageId, view.MessageId, store);
        }

        return (_PublishedRow(id, result, receiptFailure), result.Outcome != InboxOperationOutcome.Applied);
    }

    /// <summary>
    /// Re-sends or deletes a published row. The main store writes the row and its receipt in one transaction. An
    /// additional outbox holds no ledger: the row changes there, then the receipt goes to the main store tagged with
    /// the outbox's name. The two databases are not written atomically, so a receipt that fails to write leaves the
    /// change in place and is returned beside the outcome instead of thrown.
    /// </summary>
    /// <exception cref="NotSupportedException">The outbox's storage cannot apply a write without its receipt.</exception>
    private async Task<(PublishedOperationResult Result, string? ReceiptFailure)> _WritePublishedAsync(
        MessagingStore store,
        bool isResend,
        PublishedOperationRequest write,
        string id,
        CancellationToken cancellationToken
    )
    {
        if (store.HoldsReceived)
        {
            var published = store.Storage.GetPublishedOperationsApi();
            var result = isResend
                ? await published.ResendAsync(write, cancellationToken).ConfigureAwait(false)
                : await published.PurgeAsync(write, cancellationToken).ConfigureAwait(false);

            return (result, null);
        }

        if (
            store.Storage is not IAdditionalOutboxOperations outbox
            || _stores.Primary.Storage is not IAdditionalOutboxOperations ledger
        )
        {
            throw new NotSupportedException("The storage cannot apply an additional outbox's published operation.");
        }

        var applied = await outbox
            .ApplyPublishedWithoutReceiptAsync(
                isResend ? MessagingOperationType.Resend : MessagingOperationType.Purge,
                write,
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            await ledger.RecordOutboxReceiptAsync(store.Name, applied, cancellationToken).ConfigureAwait(false);

            return (applied, null);
        }
#pragma warning disable CA1031 // The row already changed on the outbox; any receipt failure is reported beside that outcome, not as a failed write.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _services
                .GetService<ILogger<MessagingCommands>>()
                ?.LogMessagingOutboxReceiptFailed(exception, id, store.Name);

            return (applied, exception.Message);
        }
    }

    private static DashboardRowResult _PublishedRow(string id, PublishedOperationResult result, string? receiptFailure)
    {
        var row =
            result.Outcome == InboxOperationOutcome.Applied ? _Done(id) : _Skipped(id, _OutcomeReason(result.Outcome));

        if (receiptFailure is null)
        {
            return row;
        }

        var note = $"The receipt was not recorded: {receiptFailure}";

        return row with
        {
            Reason = row.Reason is null ? note : $"{row.Reason}. {note}",
        };
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToScheduledAsync(
        DashboardCommandRequest request,
        OperatorAuthorizationContext authorization,
        string id,
        CancellationToken cancellationToken
    )
    {
        var (found, scheduled, refusal) = await _ReadScheduledAsync(id, request.User, cancellationToken)
            .ConfigureAwait(false);
        var command = request.CommandId;

        if (refusal is null)
        {
            var (dispatchNow, revoke) = _ScheduledRules(scheduled!);
            refusal = string.Equals(command, DispatchNowId, StringComparison.Ordinal) ? dispatchNow : revoke;
        }

        if (refusal is not null)
        {
            return (_Skipped(id, refusal), true);
        }

        var store = found!.Value.Store;
        var operations = store.Storage.GetScheduledDeliveryOperationsApi();
        var write = new ScheduledDeliveryOperationRequest(
            Guid.CreateVersion7(),
            scheduled!.StorageId,
            scheduled.ExpectedDueAt,
            request.Reason,
            authorization
        );
        var result = string.Equals(command, DispatchNowId, StringComparison.Ordinal)
            ? await operations.DispatchNowAsync(write, cancellationToken).ConfigureAwait(false)
            : await operations.RevokeAsync(write, cancellationToken).ConfigureAwait(false);

        if (result.Outcome != InboxOperationOutcome.Applied)
        {
            return (_Skipped(id, _OutcomeReason(result.Outcome)), true);
        }

        _Publish(MessageType.Publish, scheduled.StorageId, scheduled.MessageId, store);

        return (_Done(id), false);
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToCircuitAsync(
        string command,
        string id,
        CancellationToken cancellationToken
    )
    {
        var isReset = string.Equals(command, ResetCircuitId, StringComparison.Ordinal);
        var (reset, forceOpen) = _CircuitRefusals(id);
        var refusal = isReset ? reset : forceOpen;

        if (refusal is not null || !MessagingViews.TryParseCircuitId(id, out _, out var key))
        {
            return (_Skipped(id, refusal ?? UnknownCircuit), true);
        }

        var monitor = _services.GetRequiredService<ICircuitBreakerMonitor>();
        var changed = isReset
            ? await monitor.ResetAsync(key, cancellationToken).ConfigureAwait(false)
            : await monitor.ForceOpenAsync(key, cancellationToken).ConfigureAwait(false);

        // The circuit moved between the rule check and the call, such as a half-open probe that closed it.
        return changed ? (_Done(id), false) : (_Skipped(id, isReset ? AlreadyClosed : AlreadyOpen), true);
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToNodeAsync(
        string id,
        CancellationToken cancellationToken
    )
    {
        if (_BackpressureRefusal(id) is { } refusal)
        {
            return (_Skipped(id, refusal), true);
        }

        await _services
            .GetRequiredService<IRetryProcessorMonitor>()
            .ResetBackpressureAsync(cancellationToken)
            .ConfigureAwait(false);

        return (_Done(id), false);
    }

    private async Task<(string? ReExecute, string? Purge, string? Hold, string? Release)> _ReceivedRefusalsAsync(
        ObjectRef target,
        CancellationToken cancellationToken
    )
    {
        var (_, view, refusal) = await _ReadReceivedAsync(target.Id, cancellationToken).ConfigureAwait(false);

        return refusal is null ? _ReceivedRules(view!) : (refusal, refusal, refusal, refusal);
    }

    private async Task<(string? Resend, string? Delete, string? DispatchNow, string? Revoke)> _PublishedRefusalsAsync(
        ObjectRef target,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var (_, view, refusal) = await _ReadPublishedAsync(target.Id, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return (refusal, refusal, refusal, refusal);
        }

        var (resend, delete) = _PublishedRules(view!);

        // Only a pending row can be a scheduled delivery, so a terminal row costs no scheduled read.
        if (_IsTerminal(view!))
        {
            return (resend, delete, NotScheduled, NotScheduled);
        }

        var (_, scheduled, scheduledRefusal) = await _ReadScheduledAsync(target.Id, user, cancellationToken)
            .ConfigureAwait(false);

        if (scheduledRefusal is not null)
        {
            return (resend, delete, scheduledRefusal, scheduledRefusal);
        }

        var (dispatchNow, revoke) = _ScheduledRules(scheduled!);

        return (resend, delete, dispatchNow, revoke);
    }

    private async Task<(Found? Found, MessageView? View, string? Refusal)> _ReadReceivedAsync(
        string id,
        CancellationToken cancellationToken
    )
    {
        var (found, refusal) = await _FindAsync(MessageType.Subscribe, id, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, null, refusal);
        }

        var view = await _ReadViewAsync(found!.Value, cancellationToken).ConfigureAwait(false);

        if (view is null)
        {
            return (null, null, Gone);
        }

        // The audited writes fence on the generation's incarnation; a store that records none cannot run them.
        return view.IncarnationId is null ? (null, null, NotSupportedByStore) : (found, view, null);
    }

    private async Task<(Found? Found, MessageView? View, string? Refusal)> _ReadPublishedAsync(
        string id,
        CancellationToken cancellationToken
    )
    {
        var (found, refusal) = await _FindAsync(MessageType.Publish, id, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, null, refusal);
        }

        // An outbox row's write runs on the outbox and its receipt on the main store; without both halves of that
        // seam no audited write can run, so the row is refused before it is read.
        if (!found!.Value.Store.HoldsReceived && !_CanWriteOutbox(found.Value.Store))
        {
            return (null, null, NotSupportedForOutboxes);
        }

        var view = await _ReadViewAsync(found.Value, cancellationToken).ConfigureAwait(false);

        return view is null ? (null, null, Gone) : (found, view, null);
    }

    private bool _CanWriteOutbox(MessagingStore store) =>
        store.Storage is IAdditionalOutboxOperations && _stores.Primary.Storage is IAdditionalOutboxOperations;

    private async Task<(Found? Found, ScheduledDeliveryView? View, string? Refusal)> _ReadScheduledAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var (found, refusal) = await _FindAsync(MessageType.Publish, id, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, null, refusal);
        }

        var store = found!.Value.Store;

        if (!store.HoldsReceived)
        {
            return (null, null, NotSupportedForOutboxes);
        }

        IScheduledDeliveryOperationsApi operations;

        try
        {
            operations = store.Storage.GetScheduledDeliveryOperationsApi();
        }
        catch (NotSupportedException)
        {
            return (null, null, NotSupportedByStore);
        }

        // A read: it needs an authenticated principal, not a named operator.
        var page = await operations
            .QueryAsync(
                new ScheduledDeliveryQuery { StorageIds = [found.Value.Id.StorageId], PageSize = 1 },
                new OperatorAuthorizationContext(MessagingModuleApi.Reader(user)),
                cancellationToken
            )
            .ConfigureAwait(false);

        return page.Items.Count == 0 ? (null, null, NotScheduled) : (found, page.Items[0], null);
    }

    private async Task<(Found? Found, string? Refusal)> _FindAsync(
        MessageType type,
        string id,
        CancellationToken cancellationToken
    )
    {
        if (!MessagingObjectId.TryParse(id, out var objectId))
        {
            return (null, Gone);
        }

        if (!string.Equals(objectId.Node, _stores.NodeName, StringComparison.Ordinal))
        {
            return (null, OtherNode);
        }

        if (type == MessageType.Subscribe && _stores.Find(objectId.Store) is { HoldsReceived: false })
        {
            return (null, NotSupportedForOutboxes);
        }

        var found = await _sources.FindAsync(type, objectId, cancellationToken).ConfigureAwait(false);

        return found is { } row ? (new Found(type, row.Id, row.Store, row.Message), null) : (null, Gone);
    }

    private static Task<MessageView?> _ReadViewAsync(Found found, CancellationToken cancellationToken) =>
        MessagingViews.ReadViewAsync(
            found.Store,
            found.Type,
            found.Id.StorageId,
            MessagingViews.MessageIdOf(found.Message),
            cancellationToken
        );

    private (string? Reset, string? ForceOpen) _CircuitRefusals(string id)
    {
        if (_onKubernetes.Value)
        {
            return (OnePodOnly, OnePodOnly);
        }

        if (!MessagingViews.TryParseCircuitId(id, out var node, out var key))
        {
            return (UnknownCircuit, UnknownCircuit);
        }

        if (!string.Equals(node, _stores.NodeName, StringComparison.Ordinal))
        {
            return (OtherNode, OtherNode);
        }

        if (_services.GetService<ICircuitBreakerMonitor>()?.GetSnapshot(key) is not { } snapshot)
        {
            return (UnknownCircuit, UnknownCircuit);
        }

        return (
            snapshot.State == CircuitBreakerState.Closed ? AlreadyClosed : null,
            snapshot.State == CircuitBreakerState.Open ? AlreadyOpen : null
        );
    }

    private string? _BackpressureRefusal(string id)
    {
        if (_onKubernetes.Value)
        {
            return OnePodOnly;
        }

        if (!string.Equals(id, _stores.NodeName, StringComparison.Ordinal))
        {
            return OtherNode;
        }

        if (_services.GetService<IRetryProcessorMonitor>() is not { } monitor)
        {
            return NoRetryProcessor;
        }

        return monitor.IsBackedOff ? null : NotBackedOff;
    }

    private void _Publish(MessageType type, Guid storageId, string messageId, MessagingStore store)
    {
        _services.GetService<MessagingChangePublisher>()?.Enqueue(type, storageId, messageId, store);
    }

    private DashboardRowResult _Failure(string id, Exception exception)
    {
        _services.GetService<ILogger<MessagingCommands>>()?.LogMessagingCommandFailed(exception, id);

        return _Failed(id, exception.Message);
    }

    // A row is terminal once it succeeded, or failed with no retry left; only then may an operator act on it.
    private static bool _IsTerminal(MessageView view) =>
        view.StatusName is StatusName.Succeeded or StatusName.Failed && view.NextRetryAt is null;

    private static (string? ReExecute, string? Purge, string? Hold, string? Release) _ReceivedRules(MessageView view)
    {
        if (!_IsTerminal(view))
        {
            return (StillProcessing, StillProcessing, StillProcessing, view.IsHeld ? null : NotHeld);
        }

        return view.IsHeld ? (ReleaseFirst, HeldReleaseFirst, AlreadyHeld, null) : (null, null, null, NotHeld);
    }

    private static (string? Resend, string? Delete) _PublishedRules(MessageView view)
    {
        if (!_IsTerminal(view))
        {
            return (StillProcessing, StillProcessing);
        }

        return (view.StatusName == StatusName.Failed ? null : NotFailed, null);
    }

    private static (string? DispatchNow, string? Revoke) _ScheduledRules(ScheduledDeliveryView view)
    {
        // A delivery attempt already ran inline, so the row is a retry in progress, not a waiting delivery.
        if (view.InlineAttempts > 0)
        {
            return (StillProcessing, StillProcessing);
        }

        // Revoking a leased row is safe: the dispatcher re-checks the row before it sends. Dispatching it again is not.
        return (view.IsLeased ? Leased : null, null);
    }

    private static (MessageType Type, MessageOperationKind Kind) _OperationKind(string commandId) =>
        commandId switch
        {
            ReExecuteId => (MessageType.Subscribe, MessageOperationKind.ReExecute),
            PurgeId => (MessageType.Subscribe, MessageOperationKind.Delete),
            ResendId => (MessageType.Publish, MessageOperationKind.Resend),
            DeleteId => (MessageType.Publish, MessageOperationKind.Delete),
            _ => throw new ArgumentException($"Command '{commandId}' does not run over a filter.", nameof(commandId)),
        };

    private (MessagingStore Store, MessagingListScope Filter) _ParseFilter(MessageType type, JsonElement filter)
    {
        MessagingListScope scope;

        try
        {
            scope =
                filter.Deserialize(MessagingDashboardJsonContext.Default.MessagingListScope)
                ?? new MessagingListScope();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The filter is not a Messaging list filter.", nameof(filter), exception);
        }

        var store =
            (string.IsNullOrEmpty(scope.Store) ? _stores.Primary : _stores.Find(scope.Store))
            ?? throw new ArgumentException($"The filter names an unknown store '{scope.Store}'.", nameof(filter));

        // Received rows live in the main store only, so a received-row Operation over an additional outbox has nothing
        // to change. A published-row Operation runs over the outbox and records its receipts in the main store.
        if (type == MessageType.Subscribe && !store.HoldsReceived)
        {
            throw new ArgumentException(
                $"Store '{store.Name}' is an additional outbox; {NotSupportedForOutboxes.ToLowerInvariant()}.",
                nameof(filter)
            );
        }

        return (store, scope);
    }

    private static MessageOperationScope _Scope(
        MessageType type,
        MessageOperationKind kind,
        MessagingListScope filter,
        DateTimeOffset highWater
    )
    {
        var query = filter.ToQuery(type);

        return new MessageOperationScope
        {
            Kind = kind,
            // A held row is refused by Re-execute, so the scope leaves it out instead of counting a row it will skip.
            Query = kind == MessageOperationKind.ReExecute ? query with { IsHeld = false } : query,
            HighWaterMark = highWater,
        };
    }

    private static ValueTask<MessageOperationChunk> _ReadScopeAsync(
        MessagingStore store,
        MessageType type,
        MessageOperationScope scope,
        Guid? after,
        int size,
        CancellationToken cancellationToken
    ) =>
        type == MessageType.Subscribe
            ? store.Storage.GetInboxOperationsApi().ReadOperationScopeAsync(scope, after, size, cancellationToken)
            : store.Storage.GetPublishedOperationsApi().ReadOperationScopeAsync(scope, after, size, cancellationToken);

    private static DateTimeOffset _ParseMark(string mark)
    {
        if (
            long.TryParse(mark, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks <= DateTimeOffset.MaxValue.UtcTicks
        )
        {
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        throw new ArgumentException($"'{mark}' is not a Messaging Operation mark.", nameof(mark));
    }

    private string _RowId(MessagingStore store, Guid storageId) =>
        new MessagingObjectId(_stores.NodeName, store.Name, storageId).ToString();

    private static string _OutcomeReason(InboxOperationOutcome outcome) =>
        outcome switch
        {
            InboxOperationOutcome.NotFound => Gone,
            InboxOperationOutcome.Active => StillProcessing,
            InboxOperationOutcome.Held => HeldReleaseFirst,
            InboxOperationOutcome.OperationConflict => OperationIdUsed,
            _ => Changed,
        };

    private static DashboardRowResult _Done(string id) => new() { Id = id, Outcome = DashboardRowOutcome.Done };

    private static DashboardRowResult _Skipped(string id, string reason) =>
        new()
        {
            Id = id,
            Outcome = DashboardRowOutcome.Skipped,
            Reason = reason,
        };

    private static DashboardRowResult _Failed(string id, string reason) =>
        new()
        {
            Id = id,
            Outcome = DashboardRowOutcome.Failed,
            Reason = reason,
        };

    private static DashboardCommandAvailability _Answer(ObjectRef target, string commandId, string? refusal) =>
        new()
        {
            Target = target,
            CommandId = commandId,
            Allowed = refusal is null,
            DisabledReason = refusal,
        };

    private readonly record struct Found(
        MessageType Type,
        MessagingObjectId Id,
        MessagingStore Store,
        MediumMessage Message
    );
}

internal static partial class MessagingCommandsLogs
{
    [LoggerMessage(
        EventId = 2140,
        EventName = "MessagingDashboardCommandFailed",
        Level = LogLevel.Warning,
        Message = "Messaging dashboard command failed on row {RowId}; the row is reported failed."
    )]
    internal static partial void LogMessagingCommandFailed(this ILogger logger, Exception exception, string rowId);

    [LoggerMessage(
        EventId = 2141,
        EventName = "MessagingDashboardOutboxReceiptFailed",
        Level = LogLevel.Warning,
        Message = "Messaging dashboard changed row {RowId} on outbox {Outbox} but did not record its receipt in the main store."
    )]
    internal static partial void LogMessagingOutboxReceiptFailed(
        this ILogger logger,
        Exception exception,
        string rowId,
        string outbox
    );
}
