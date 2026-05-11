// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Processors;

public sealed class HeadlessMessageCollectorSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        if (entry.Entity is IDistributedMessageEmitter distributedMessageEmitter)
        {
            var messages = distributedMessageEmitter.GetDistributedMessages();

            if (messages.Count > 0)
            {
                context.DistributedEmitters.Add(new(distributedMessageEmitter, messages));
            }
        }

        if (entry.Entity is ILocalMessageEmitter localMessageEmitter)
        {
            var messages = localMessageEmitter.GetLocalMessages();

            if (messages.Count > 0)
            {
                context.LocalEmitters.Add(new(localMessageEmitter, messages));
            }
        }
    }
}
