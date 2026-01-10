// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> Messages)
{
    // Clone to avoid issues with the original list being modified after this record is created.
    public IReadOnlyList<ILocalMessage> Messages { get; } = Messages.ToArray(); // Clone
}
