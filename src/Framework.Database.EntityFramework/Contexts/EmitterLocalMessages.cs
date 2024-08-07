using Framework.BuildingBlocks.Domains;

namespace Framework.Database.EntityFramework.Contexts;

public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> EmittedMessages);
