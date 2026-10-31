// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting;

namespace Headless.Encryption;

/// <summary>Configures common relational database settings for data-key stores.</summary>
/// <param name="defaultTableName">The default key table name for the specific database dialect.</param>
/// <param name="defaultScopesTableName">The default scopes table name for the specific database dialect.</param>
[PublicAPI]
public abstract class RelationalEncryptionKeyStoreOptions(string defaultTableName, string defaultScopesTableName)
{
    /// <summary>
    /// Gets or sets the connection string of the database that holds the wrapped data keys. By default this is the
    /// application's own database, so tenant creation and backups stay consistent with the data; a separate database
    /// is allowed. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the database command timeout. The default is 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the schema that holds the key table. The default is
    /// <see cref="HeadlessStorageDefaults.Schema" /> (<c>headless</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Gets or sets the name of the key table. The default is <c>encryption_keys</c> on PostgreSQL,
    /// <c>EncryptionKeys</c> on SQL Server.
    /// </summary>
    public string TableName { get; set; } = defaultTableName;

    /// <summary>
    /// Gets or sets the name of the scopes table, one row per scope that ever had a key, which records when a scope
    /// was shredded. The default is <c>encryption_scopes</c> on PostgreSQL, <c>EncryptionScopes</c> on SQL Server.
    /// </summary>
    public string ScopesTableName { get; set; } = defaultScopesTableName;

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal abstract class RelationalEncryptionKeyStoreOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalEncryptionKeyStoreOptions
{
    protected RelationalEncryptionKeyStoreOptionsValidator(StorageProvider provider, string displayName)
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage($"A {displayName} connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.TableName).IsValidIdentifierFor(provider);
        RuleFor(x => x.ScopesTableName).IsValidIdentifierFor(provider);
        RuleFor(x => x.ScopesTableName)
            .Must((options, name) => !string.Equals(name, options.TableName, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The scopes table must differ from the key table.");
    }
}
