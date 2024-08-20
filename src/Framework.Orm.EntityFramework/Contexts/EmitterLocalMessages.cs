using Framework.BuildingBlocks.Domains;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> EmittedMessages);
