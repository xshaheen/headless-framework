// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

[PublicAPI]
public static class SetupEntityFrameworkMessaging
{
    /// <summary>
    /// Registers the outbox-backed <see cref="IHeadlessOutboxDispatcher"/> so integration events emitted by
    /// entities during EF saves are written to the messaging outbox within the save's unit of work (through that
    /// unit's <see cref="UnitOfWorkOutbox"/>) and dispatched to the broker after commit. Chain after
    /// <c>AddHeadlessDbContextServices(...)</c>, alongside <c>AddDomainEvents()</c>.
    /// </summary>
    /// <remarks>
    /// Requires a messaging setup (<c>AddHeadlessMessaging</c>) configured with an outbox storage provider
    /// (PostgreSQL / SQL Server / in-memory). The dispatcher itself has no options; broker, storage, and
    /// retry behavior are configured on the messaging setup. The scoped unit-of-work manager the dispatcher
    /// consults is registered by <c>AddHeadlessDbContextServices</c>.
    /// </remarks>
    public static IHeadlessDbContextBuilder AddIntegrationEventOutbox(this IHeadlessDbContextBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.Services.TryAddSingleton<IntegrationEventPublishInvokerCache>();
        builder.Services.TryAddScoped<IHeadlessOutboxDispatcher, OutboxIntegrationEventDispatcher>();

        return builder;
    }
}
