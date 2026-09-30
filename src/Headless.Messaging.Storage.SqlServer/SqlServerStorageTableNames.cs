// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// Resolves the fully-qualified SQL Server names of the published and received message tables in the configured
/// messaging schema.
/// </summary>
internal sealed class SqlServerStorageTableNames(IOptions<MessagingStorageOptions> storageOptions) : IStorageTableNames
{
    /// <summary>
    /// Returns the fully-qualified SQL Server table name for published outbox messages,
    /// in the form <c>schema.MessagingPublished</c>.
    /// </summary>
    public string GetPublishedTableName()
    {
        return Published(storageOptions.Value.Schema);
    }

    /// <summary>
    /// Returns the fully-qualified SQL Server table name for received outbox messages,
    /// in the form <c>schema.MessagingReceived</c>.
    /// </summary>
    public string GetReceivedTableName()
    {
        return Received(storageOptions.Value.Schema);
    }

    public static string Published(string schema)
    {
        return $"{schema}.MessagingPublished";
    }

    public static string Received(string schema)
    {
        return $"{schema}.MessagingReceived";
    }
}
