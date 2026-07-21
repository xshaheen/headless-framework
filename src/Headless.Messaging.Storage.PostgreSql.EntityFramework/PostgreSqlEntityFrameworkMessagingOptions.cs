// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Constants;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.PostgreSql.EntityFramework;

[PublicAPI]
public class PostgreSqlEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messaging";

    /// <summary>
    /// PostgreSQL maximum identifier length for schema names.
    /// </summary>
    public const int MaxSchemaLength = StorageIdentifier.PostgreSql.IdentifierMaxLength;

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the schema name is empty/whitespace or does not match PostgreSQL identifier rules
    /// (max 63 chars, starts with letter/underscore, alphanumeric/underscore only).
    /// </exception>
    public string Schema
    {
        get;
        set
        {
            Argument.IsNotNullOrWhiteSpace(value);
            Argument.IsLessThanOrEqualTo(
                value.Length,
                MaxSchemaLength,
                $"Schema name must not exceed {MaxSchemaLength} chars"
            );
            Argument.Matches(
                value,
                StorageIdentifier.PostgreSql.IdentifierPattern,
                $"Schema name must start with a letter or underscore and contain only letters, digits, underscores (max {MaxSchemaLength} chars)"
            );

            field = value;
        }
    } = DefaultSchema;

    /// <summary>
    /// Gets or sets the maximum length for the Owner column. Default is <see cref="DataStorageConstants.OwnerColumnMaxLength"/>.
    /// </summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// Gets or sets whether the transactional (atomic) outbox is enabled for this EF-context storage path.
    /// Default <see langword="true" />: a publish issued inside a coordinated transaction writes its outbox row in
    /// the same DB transaction and is discarded on rollback (commit coordination, the interceptor attach, and the
    /// startup self-probe are auto-wired). Set to <see langword="false" /> to opt out and restore non-transactional
    /// immediate dispatch. No effect on the raw-ADO storage paths (connection-string overloads), which are never
    /// transactional by default.
    /// </summary>
    public bool EnableTransactionalOutbox { get; set; } = true;
}
