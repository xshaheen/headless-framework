// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

/// <summary>
/// What a relational provider package supplies to the one relational messaging storage: its dialect, how it opens a
/// connection to its database, and the names it reports under.
/// </summary>
internal sealed class RelationalMessagingStorage
{
    /// <summary>Describes a provider's database to the relational storage.</summary>
    /// <param name="dialect">The engine's dialect, which renders every statement the storage runs.</param>
    /// <param name="providerName">The provider's name in metrics and capability reports, such as <c>PostgreSql</c>.</param>
    /// <param name="createConnection">Creates an unopened connection to the database that holds the messaging tables.</param>
    /// <param name="ownerColumnMaxLength">The length of the owner column the provider's schema created.</param>
    public RelationalMessagingStorage(
        ISqlDialect dialect,
        string providerName,
        Func<DbConnection> createConnection,
        int ownerColumnMaxLength
    )
    {
        Dialect = Argument.IsNotNull(dialect);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        CreateConnection = Argument.IsNotNull(createConnection);
        OwnerColumnMaxLength = Argument.IsPositive(ownerColumnMaxLength);
    }

    public ISqlDialect Dialect { get; }

    public string ProviderName { get; }

    public Func<DbConnection> CreateConnection { get; }

    public int OwnerColumnMaxLength { get; }
}
