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
    /// entities during EF saves are written to the messaging outbox within the save transaction (see
    /// <see cref="IOutboxBus"/>) and dispatched to the broker after commit. Chain after
    /// <c>AddHeadlessDbContextServices(...)</c>, alongside <c>AddDomainEvents()</c>.
    /// </summary>
    /// <remarks>
    /// Requires a messaging setup (<c>AddHeadlessMessaging</c>) configured with an outbox storage provider
    /// (PostgreSQL / SQL Server / in-memory). The dispatcher itself has no options; broker, storage, and
    /// retry behavior are configured on the messaging setup.
    /// </remarks>
    public static IHeadlessDbContextBuilder AddIntegrationEventOutbox(this IHeadlessDbContextBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.AddCommitCoordination();
        builder.Services.TryAddSingleton<IntegrationEventPublishInvokerCache>();
        builder.Services.TryAddScoped<IHeadlessOutboxDispatcher, OutboxIntegrationEventDispatcher>();

        return builder;
    }
}
