// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Encryption.PostgreSql;

/// <summary>Connection, command, and table options for the PostgreSQL encryption key store.</summary>
[PublicAPI]
public sealed class PostgreSqlEncryptionKeyStoreOptions() : RelationalEncryptionKeyStoreOptions(DefaultTableName)
{
    /// <summary>The table used when <see cref="RelationalEncryptionKeyStoreOptions.TableName" /> is not set.</summary>
    public const string DefaultTableName = "encryption_keys";
}

internal sealed class PostgreSqlEncryptionKeyStoreOptionsValidator()
    : RelationalEncryptionKeyStoreOptionsValidator<PostgreSqlEncryptionKeyStoreOptions>(
        StorageProvider.PostgreSql,
        "PostgreSQL"
    );
