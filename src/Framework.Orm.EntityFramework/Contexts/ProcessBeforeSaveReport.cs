// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Orm.EntityFramework.Contexts;

public sealed record ProcessBeforeSaveReport
{
    public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

    public List<EmitterLocalMessages> LocalEmitters { get; } = [];
}
