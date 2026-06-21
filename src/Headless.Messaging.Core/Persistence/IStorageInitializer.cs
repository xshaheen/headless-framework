// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Initializes the persistent storage schema required by the messaging outbox and retry processors.
/// </summary>
/// <remarks>
/// Implemented by each storage provider (EntityFramework, PostgreSQL, SQL Server). The messaging
/// bootstrapper resolves and calls <see cref="InitializeAsync"/> during host startup before any
/// publisher or consumer is activated.
/// </remarks>
[PublicAPI]
public interface IStorageInitializer
{
    /// <summary>
    /// Creates or migrates the published and received message tables to the expected schema.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the initialization operation.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the physical table name used to store outbound (published) messages for this provider.
    /// </summary>
    string GetPublishedTableName();

    /// <summary>
    /// Returns the physical table name used to store inbound (received) messages for this provider.
    /// </summary>
    string GetReceivedTableName();
}
