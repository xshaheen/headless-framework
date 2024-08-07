using Framework.BuildingBlocks.Domains;

namespace Framework.Database.EntityFramework.Contexts;

public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> EmittedMessages
);
