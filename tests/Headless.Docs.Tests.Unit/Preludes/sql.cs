// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application row types, secrets service, and variables the SQL guide's examples assume.

global using static SqlAmbient;

public sealed record Order(Guid Id);

public sealed record Report(Guid Id, DateTime CreatedAt);

public interface ISecrets
{
    string SqlConnectionString { get; }
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class SqlAmbient
{
    public static string connectionString => "";
}
