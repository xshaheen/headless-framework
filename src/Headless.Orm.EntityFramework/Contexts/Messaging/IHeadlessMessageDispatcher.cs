// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.EntityFramework.Messaging;

public interface IHeadlessMessageDispatcher
{
    Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction);

    Task PublishDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    void PublishDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    );
}

internal sealed class ThrowHeadlessMessageDispatcher : IHeadlessMessageDispatcher
{
    public Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw _CreateMissingDispatcherException(emitters.Count, "local");

    public void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction) =>
        throw _CreateMissingDispatcherException(emitters.Count, "local");

    public Task PublishDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw _CreateMissingDispatcherException(emitters.Count, "distributed");

    public void PublishDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    ) => throw _CreateMissingDispatcherException(emitters.Count, "distributed");

    private static InvalidOperationException _CreateMissingDispatcherException(int emitterCount, string messageKind)
    {
        FormattableString template =
            $"Headless EF collected {emitterCount} {messageKind} message emitter(s), but no application {nameof(IHeadlessMessageDispatcher)} is registered. Register an {nameof(IHeadlessMessageDispatcher)} implementation or disable message-emitting save processors.";

        return new(template.ToString(CultureInfo.InvariantCulture));
    }
}
