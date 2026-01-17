// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

public class EfOptions
{
    public const string DefaultSchema = "cap";

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;

    /// <summary>
    /// EF DbContext
    /// </summary>
    internal Type? DbContextType { get; set; }

    internal bool IsSqlServer2008 { get; set; }

    /// <summary>
    /// Data version
    /// </summary>
    internal string Version { get; set; } = default!;

    public EfOptions UseSqlServer2008()
    {
        IsSqlServer2008 = true;
        return this;
    }
}
