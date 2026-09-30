// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// Resolves the fully-qualified PostgreSQL names of the published and received message tables in the configured
/// messaging schema.
/// </summary>
internal sealed class PostgreSqlStorageTableNames(IOptions<MessagingStorageOptions> storageOptions) : IStorageTableNames
{
    /// <summary>
    /// Returns the fully-qualified PostgreSQL table name for published outbox messages,
    /// in the form <c>"schema"."messaging_published"</c>.
    /// </summary>
    public string GetPublishedTableName()
    {
        return Published(storageOptions.Value.Schema);
    }

    /// <summary>
    /// Returns the fully-qualified PostgreSQL table name for received outbox messages,
    /// in the form <c>"schema"."messaging_received"</c>.
    /// </summary>
    public string GetReceivedTableName()
    {
        return Received(storageOptions.Value.Schema);
    }

    public static string Published(string schema)
    {
        return $"\"{schema}\".\"messaging_published\"";
    }

    public static string Received(string schema)
    {
        return $"\"{schema}\".\"messaging_received\"";
    }
}
