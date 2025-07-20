// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record ProcessBeforeSaveReport
{
    public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

    public List<EmitterLocalMessages> LocalEmitters { get; } = [];
}
