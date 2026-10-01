// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Sql;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Persistence;

/// <summary>
/// The quoted, schema-qualified names of the published and received message tables in the configured messaging
/// schema, in the dialect's naming convention.
/// </summary>
internal sealed class RelationalStorageTableNames(ISqlDialect dialect, IOptions<MessagingStorageOptions> storageOptions)
    : IStorageTableNames
{
    private readonly MessagingTables _tables = MessagingTables.For(dialect, storageOptions);

    public string GetPublishedTableName() => _tables.Published;

    public string GetReceivedTableName() => _tables.Received;
}
