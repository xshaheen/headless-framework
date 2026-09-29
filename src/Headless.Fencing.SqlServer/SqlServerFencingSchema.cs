// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing.SqlServer;

/// <summary>Object and column names of the lease storage, shared by the initializer's DDL and the store's statements.</summary>
internal static class SqlServerFencingSchema
{
    public const string TableName = "FencingLeases";
    public const string SequenceName = "FencingLeaseGenerations";

    public const string TenantId = "[TenantId]";
    public const string Kind = "[Kind]";
    public const string Resource = "[Resource]";
    public const string Generation = "[Generation]";
    public const string State = "[State]";
    public const string GrantedAt = "[GrantedAt]";
    public const string ExpiresAt = "[ExpiresAt]";
    public const string EndedAt = "[EndedAt]";
    public const string TakeoverCount = "[TakeoverCount]";
    public const string Progress = "[Progress]";
    public const string ProgressContract = "[ProgressContract]";

    // Stored as smallint. Expired is never stored: it is Active with an expiry at or before the database clock.
    public const short Active = 0;
    public const short Settled = 1;
    public const short Released = 2;
    public const short Abandoned = 3;

    // Binary code-point order, so kinds, resources, and tenant ids match case- and accent-sensitively whatever the
    // database's default collation is. It does not stop SQL Server padding trailing spaces before comparing, so 'a'
    // and 'a ' would still collide; key parts are refused when they start or end with whitespace, which is what makes
    // matching ordinal, the same as the PostgreSQL provider.
    public const string KeyCollation = "Latin1_General_100_BIN2";

    public static string QualifiedTable(string schema)
    {
        return $"[{schema}].[{TableName}]";
    }

    public static string QualifiedSequence(string schema)
    {
        return $"[{schema}].[{SequenceName}]";
    }
}
