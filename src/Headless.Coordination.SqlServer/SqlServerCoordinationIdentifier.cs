// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination.SqlServer;

internal static class SqlServerCoordinationIdentifier
{
    public static string Quote(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string Qualified(string schema, string table)
    {
        return $"{Quote(schema)}.{Quote(table)}";
    }

    public static string ObjectName(string schema, string table)
    {
        return $"{schema}.{table}";
    }
}
