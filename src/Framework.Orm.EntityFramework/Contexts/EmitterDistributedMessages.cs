// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Domains;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> EmittedMessages
);
