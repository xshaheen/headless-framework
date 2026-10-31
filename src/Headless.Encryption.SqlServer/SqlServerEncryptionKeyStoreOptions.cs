// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Encryption.SqlServer;

/// <summary>Connection, command, and table options for the SQL Server encryption key store.</summary>
[PublicAPI]
public sealed class SqlServerEncryptionKeyStoreOptions()
    : RelationalEncryptionKeyStoreOptions(DefaultTableName, DefaultScopesTableName)
{
    /// <summary>The key table used when <see cref="RelationalEncryptionKeyStoreOptions.TableName" /> is not set.</summary>
    public const string DefaultTableName = "EncryptionKeys";

    /// <summary>
    /// The scopes table used when <see cref="RelationalEncryptionKeyStoreOptions.ScopesTableName" /> is not set.
    /// </summary>
    public const string DefaultScopesTableName = "EncryptionScopes";
}

internal sealed class SqlServerEncryptionKeyStoreOptionsValidator()
    : RelationalEncryptionKeyStoreOptionsValidator<SqlServerEncryptionKeyStoreOptions>(
        StorageProvider.SqlServer,
        "SQL Server"
    );
