// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domains;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> EmittedMessages);
