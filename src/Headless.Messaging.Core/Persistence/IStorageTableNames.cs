// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Resolves the physical names of the tables a messaging storage provider keeps published and received messages in.
/// </summary>
/// <remarks>
/// Implemented by each storage provider (PostgreSQL, SQL Server, in-memory). Creating those tables is not part of
/// this contract: the relational providers contribute their DDL to the Headless schema runner, which applies it in
/// <c>IHostedLifecycleService.StartingAsync</c>, before the messaging bootstrapper starts, and the in-memory provider
/// has nothing to create.
/// </remarks>
[PublicAPI]
public interface IStorageTableNames
{
    /// <summary>
    /// Returns the physical table name used to store outbound (published) messages for this provider.
    /// </summary>
    string GetPublishedTableName();

    /// <summary>
    /// Returns the physical table name used to store inbound (received) messages for this provider.
    /// </summary>
    string GetReceivedTableName();
}
