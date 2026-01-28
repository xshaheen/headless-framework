// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.Orm.EntityFramework.Contexts;

public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> Messages
)
{
    public IDistributedMessageEmitter Emitter { get; } = Emitter;

    public IReadOnlyList<IDistributedMessage> Messages { get; } = Messages.DistinctBy(x => x.UniqueId).ToList();
}
