// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.SqlServer;

public class SqlServerEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messages";

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
    internal string Version { get; set; } = null!;

    public SqlServerEntityFrameworkMessagingOptions UseSqlServer2008()
    {
        IsSqlServer2008 = true;
        return this;
    }
}
