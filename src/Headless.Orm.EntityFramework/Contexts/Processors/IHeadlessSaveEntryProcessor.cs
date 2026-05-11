// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Processors;

public interface IHeadlessSaveEntryProcessor
{
    void Process(EntityEntry entry, HeadlessSaveEntryContext context);
}

public sealed class HeadlessSaveEntryContext(DbContext dbContext)
{
    public DbContext DbContext { get; } = dbContext;

    public List<EmitterLocalMessages> LocalEmitters { get; } = [];

    public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

    public void ClearEmitterMessages()
    {
        foreach (var emitter in DistributedEmitters)
        {
            emitter.Emitter.ClearDistributedMessages();
        }

        foreach (var emitter in LocalEmitters)
        {
            emitter.Emitter.ClearLocalMessages();
        }
    }
}
