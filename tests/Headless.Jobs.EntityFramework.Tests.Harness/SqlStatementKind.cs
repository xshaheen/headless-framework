// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Classifies a captured command by the statement it runs, across the providers the harness covers.</summary>
internal static class SqlStatementKind
{
    // EF Core 11 sends a SQL Server ExecuteUpdate as "SET NOCOUNT OFF; UPDATE ..." so the affected-row count survives an
    // open connection (dotnet/efcore#37827); the statement itself still starts after that prefix.
    private const string _SqlServerNoCountOff = "SET NOCOUNT OFF;";

    /// <summary>Whether <paramref name="commandText"/> is an <c>UPDATE</c> statement.</summary>
    public static bool IsUpdate(string commandText)
    {
        var statement = commandText.StartsWith(_SqlServerNoCountOff, StringComparison.Ordinal)
            ? commandText.AsSpan(_SqlServerNoCountOff.Length).TrimStart()
            : commandText.AsSpan();

        return statement.StartsWith("UPDATE", StringComparison.Ordinal);
    }
}
