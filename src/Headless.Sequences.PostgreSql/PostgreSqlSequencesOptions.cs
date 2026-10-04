// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences.PostgreSql;

/// <summary>Connection, command, and table options for the PostgreSQL sequence provider.</summary>
[PublicAPI]
public sealed class PostgreSqlSequencesOptions() : RelationalSequencesOptions(DefaultTableName)
{
    /// <summary>The table used when <see cref="RelationalSequencesOptions.TableName" /> is not set.</summary>
    public const string DefaultTableName = "sequences";
}

internal sealed class PostgreSqlSequencesOptionsValidator()
    : RelationalSequencesOptionsValidator<PostgreSqlSequencesOptions>(StorageProvider.PostgreSql, "PostgreSQL");
