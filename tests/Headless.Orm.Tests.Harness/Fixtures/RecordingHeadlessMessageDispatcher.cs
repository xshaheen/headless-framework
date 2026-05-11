// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests.Fixtures;

public sealed class RecordingHeadlessMessageDispatcher : IHeadlessMessageDispatcher
{
    public List<EmitterDistributedMessages> EmittedDistributedMessages { get; } = [];

    public List<EmitterLocalMessages> EmittedLocalMessages { get; } = [];

    public Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedLocalMessages.AddRange(emitters);
        return Task.CompletedTask;
    }

    public void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction)
    {
        EmittedLocalMessages.AddRange(emitters);
    }

    public Task PublishDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
        return Task.CompletedTask;
    }

    public void PublishDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
    }
}
