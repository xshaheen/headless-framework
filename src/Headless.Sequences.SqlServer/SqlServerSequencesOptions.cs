// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences.SqlServer;

/// <summary>Connection, command, and table options for the SQL Server sequence provider.</summary>
[PublicAPI]
public sealed class SqlServerSequencesOptions() : RelationalSequencesOptions(DefaultTableName)
{
    /// <summary>The table used when <see cref="RelationalSequencesOptions.TableName" /> is not set.</summary>
    public const string DefaultTableName = "Sequences";
}

internal sealed class SqlServerSequencesOptionsValidator()
    : RelationalSequencesOptionsValidator<SqlServerSequencesOptions>(StorageProvider.SqlServer, "SQL Server");
