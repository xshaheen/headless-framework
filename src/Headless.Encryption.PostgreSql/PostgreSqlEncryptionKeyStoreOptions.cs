// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Encryption.PostgreSql;

/// <summary>Connection, command, and table options for the PostgreSQL encryption key store.</summary>
[PublicAPI]
public sealed class PostgreSqlEncryptionKeyStoreOptions()
    : RelationalEncryptionKeyStoreOptions(DefaultTableName, DefaultScopesTableName)
{
    /// <summary>The key table used when <see cref="RelationalEncryptionKeyStoreOptions.TableName" /> is not set.</summary>
    public const string DefaultTableName = "encryption_keys";

    /// <summary>
    /// The scopes table used when <see cref="RelationalEncryptionKeyStoreOptions.ScopesTableName" /> is not set.
    /// </summary>
    public const string DefaultScopesTableName = "encryption_scopes";
}

internal sealed class PostgreSqlEncryptionKeyStoreOptionsValidator()
    : RelationalEncryptionKeyStoreOptionsValidator<PostgreSqlEncryptionKeyStoreOptions>(
        StorageProvider.PostgreSql,
        "PostgreSQL"
    );
