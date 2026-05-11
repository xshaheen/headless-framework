// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.EntityFramework.Contexts;

public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> Messages)
{
    // Clone to avoid issues with the original list being modified after this record is created.
    public IReadOnlyList<ILocalMessage> Messages { get; } = Messages.DistinctBy(x => x.UniqueId).ToArray();
}
