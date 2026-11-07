// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>
/// Chooses the storage of one additional outbox, returned by <see cref="MessagingSetupBuilder.AddOutbox"/>.
/// </summary>
/// <remarks>
/// An additional outbox holds only published rows and their relay for one database, so a unit of work begun on that
/// database can publish atomically through <c>unit.Outbox</c>. The inbox, retry state, and the dashboard stay on the
/// primary storage, which is why this builder accepts nothing but a storage: the storage packages add
/// <c>UsePostgreSqlEntityFramework&lt;TContext&gt;()</c>, <c>UseSqlServerEntityFramework&lt;TContext&gt;()</c>, <c>UsePostgreSql(…)</c>, and <c>UseSqlServer(…)</c> members to it.
/// </remarks>
[PublicAPI]
public sealed class OutboxStorageBuilder
{
    internal OutboxStorageBuilder(MessagingSetupBuilder setup, string name, int ordinal)
    {
        Setup = Argument.IsNotNull(setup);
        Name = Argument.IsNotNullOrWhiteSpace(name);
        OptionsName = string.Create(CultureInfo.InvariantCulture, $"Headless.Messaging.Outbox.{ordinal}");
    }

    /// <summary>
    /// The outbox's name, as passed to <see cref="MessagingSetupBuilder.AddOutbox"/>: unique within the host and
    /// stable across redeploys and reordered registrations.
    /// </summary>
    public string Name { get; }

    internal MessagingSetupBuilder Setup { get; }

    /// <summary>
    /// The named-options instance that holds this outbox's provider options, unique within the host. It is keyed on
    /// the registration's position and never leaves the process, so it is not the outbox's identity.
    /// </summary>
    internal string OptionsName { get; }

    internal bool IsConfigured { get; private set; }

    /// <summary>Registers the storage of this outbox. Called once by a storage package's <c>Use…</c> member.</summary>
    /// <param name="factory">Builds the outbox from the application's service provider.</param>
    internal MessagingSetupBuilder UseStorage(Func<IServiceProvider, MessagingOutbox> factory)
    {
        Argument.IsNotNull(factory);

        if (IsConfigured)
        {
            throw new InvalidOperationException(
                $"Outbox '{Name}' already has a storage. Call AddOutbox(name) again to register an outbox for another database."
            );
        }

        IsConfigured = true;
        Setup.Services.AddSingleton(new OutboxStorageRegistration(Name, factory));

        return Setup;
    }
}
