// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.EntityFramework.Messaging;

public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> Messages)
{
    // Snapshot the caller's list so subsequent mutation on the emitter doesn't leak into the pipeline.
    public IReadOnlyList<ILocalMessage> Messages { get; } = _Snapshot(Messages);

    private static IReadOnlyList<ILocalMessage> _Snapshot(IReadOnlyList<ILocalMessage> messages)
    {
        return messages.Count switch
        {
            0 => [],
            1 => [messages[0]],
            _ => messages.DistinctBy(x => x.UniqueId).ToArray(),
        };
    }
}

public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> Messages
)
{
    public IReadOnlyList<IDistributedMessage> Messages { get; } = _Snapshot(Messages);

    private static IReadOnlyList<IDistributedMessage> _Snapshot(IReadOnlyList<IDistributedMessage> messages)
    {
        return messages.Count switch
        {
            0 => [],
            1 => [messages[0]],
            _ => messages.DistinctBy(x => x.UniqueId).ToArray(),
        };
    }
}
