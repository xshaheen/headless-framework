// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.SqlServer;

[PublicAPI]
public partial class SqlServerEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messaging";

    /// <summary>
    /// SQL Server maximum identifier length for schema names.
    /// </summary>
    public const int MaxSchemaLength = 128;

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the schema name is empty/whitespace or does not match SQL Server identifier rules
    /// (max 128 chars, starts with letter/underscore/@/#, alphanumeric/underscore/$/# only).
    /// </exception>
    public string Schema
    {
        get;
        set
        {
            Argument.IsNotNullOrWhiteSpace(value);
            Argument.Matches(
                value,
                _ValidIdentifier(),
                $"Schema name must start with a letter, underscore, @ or # and contain only letters, digits, underscores, @ or # (max {MaxSchemaLength} chars)"
            );

            field = value;
        }
    } = DefaultSchema;

    /// <summary>
    /// SQL Server identifier validation regex.
    /// Must start with a letter, underscore, @ or #, contain only letters, digits, underscores, @, # or $.
    /// Max 128 chars per SQL Server identifier rules.
    /// </summary>
    [GeneratedRegex("^[a-zA-Z_@#][a-zA-Z0-9_@#$]{0,127}$", RegexOptions.None, 100)]
    private static partial Regex _ValidIdentifier();

    /// <summary>
    /// Gets or sets the maximum length for the Owner column. Default is <see cref="DataStorageConstants.OwnerColumnMaxLength"/>.
    /// </summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// EF DbContext
    /// </summary>
    internal Type? DbContextType { get; set; }

    /// <summary>
    /// Gets or sets whether the transactional (atomic) outbox is enabled for this EF-context storage path.
    /// Default <see langword="true" />: a publish issued inside a coordinated transaction writes its outbox row in
    /// the same DB transaction and is discarded on rollback (commit coordination, the interceptor attach, and the
    /// startup self-probe are auto-wired). Set to <see langword="false" /> to opt out and restore non-transactional
    /// immediate dispatch. No effect on the raw-ADO storage paths (connection-string overloads), which are never
    /// transactional by default.
    /// </summary>
    public bool EnableTransactionalOutbox { get; set; } = true;

    /// <summary>
    /// Data version
    /// </summary>
    internal string Version { get; set; } = null!;
}
