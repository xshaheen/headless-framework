using Framework.BuildingBlocks.Domains;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> EmittedMessages
);
