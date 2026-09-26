// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Chooses the storage of one additional outbox, returned by <see cref="MessagingSetupBuilder.AddOutbox"/>.
/// </summary>
/// <remarks>
/// An additional outbox holds only published rows and their relay for one database, so a unit of work begun on that
/// database can publish atomically through <c>unit.Outbox</c>. The inbox, retry state, and the dashboard stay on the
/// primary storage, which is why this builder accepts nothing but a storage: the storage packages add
/// <c>UseEntityFramework&lt;TContext&gt;()</c>, <c>UsePostgreSql(…)</c>, and <c>UseSqlServer(…)</c> members to it.
/// </remarks>
[PublicAPI]
public sealed class OutboxStorageBuilder
{
    internal OutboxStorageBuilder(MessagingSetupBuilder setup, int ordinal)
    {
        Setup = Argument.IsNotNull(setup);
        OptionsName = string.Create(CultureInfo.InvariantCulture, $"Headless.Messaging.Outbox.{ordinal}");
    }

    internal MessagingSetupBuilder Setup { get; }

    /// <summary>The named-options instance that holds this outbox's provider options, unique within the host.</summary>
    internal string OptionsName { get; }

    internal bool IsConfigured { get; private set; }

    /// <summary>Registers the storage of this outbox. Called once by a storage package's <c>Use…</c> member.</summary>
    /// <param name="name">How the registration reads in configuration errors, such as <c>AddOutbox().UsePostgreSql(…)</c>.</param>
    /// <param name="factory">Builds the outbox from the application's service provider.</param>
    internal MessagingSetupBuilder UseStorage(string name, Func<IServiceProvider, MessagingOutbox> factory)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(factory);

        if (IsConfigured)
        {
            throw new InvalidOperationException(
                "This outbox already has a storage. Call AddOutbox() again to register an outbox for another database."
            );
        }

        IsConfigured = true;
        Setup.Services.AddSingleton(new OutboxStorageRegistration(name, factory));

        return Setup;
    }
}
