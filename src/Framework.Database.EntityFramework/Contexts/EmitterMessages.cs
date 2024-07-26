using Framework.BuildingBlocks.Domains;

namespace Framework.Database.EntityFramework.Contexts;

public sealed record EmitterMessages(IMessageEmitter Emitter, IReadOnlyList<IIntegrationMessage> EmittedMessages);
