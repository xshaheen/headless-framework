// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Contexts.Processors;

[PublicAPI]
public sealed class HeadlessMessageCollectorSaveEntryProcessor : IHeadlessSaveEntryProcessor
{
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        if (entry.State == EntityState.Detached)
        {
            return;
        }

        if (entry.Entity is IIntegrationEventEmitter integrationEmitter)
        {
            var events = integrationEmitter.GetIntegrationEvents();

            if (events.Count > 0)
            {
                context.IntegrationEventEmitters.Add(new(integrationEmitter, events));
            }
        }

        if (entry.Entity is IDomainEventEmitter domainEmitter)
        {
            var events = domainEmitter.GetDomainEvents();

            if (events.Count > 0)
            {
                context.DomainEventEmitters.Add(new(domainEmitter, events));
            }
        }
    }
}
