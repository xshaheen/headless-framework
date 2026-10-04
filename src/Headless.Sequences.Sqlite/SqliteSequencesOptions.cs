// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences.Sqlite;

/// <summary>Connection, command, and table options for the SQLite sequence provider.</summary>
[PublicAPI]
public sealed class SqliteSequencesOptions() : RelationalSequencesOptions(DefaultTableName)
{
    /// <summary>The table used when <see cref="RelationalSequencesOptions.TableName" /> is not set.</summary>
    public const string DefaultTableName = "sequences";
}

// SQLite accepts any quoted name; the dialect's snake_case convention is PostgreSQL's, so its identifier rules apply.
internal sealed class SqliteSequencesOptionsValidator()
    : RelationalSequencesOptionsValidator<SqliteSequencesOptions>(StorageProvider.PostgreSql, "SQLite");
