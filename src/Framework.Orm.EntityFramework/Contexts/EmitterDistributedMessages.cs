// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> Messages
)
{
    public IDistributedMessageEmitter Emitter { get; } = Emitter;

    public IReadOnlyList<IDistributedMessage> Messages { get; } = Messages.DistinctBy(x => x.UniqueId).ToList();
}
