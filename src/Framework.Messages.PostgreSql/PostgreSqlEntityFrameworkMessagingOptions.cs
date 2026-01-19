// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

public class PostgreSqlEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messages";

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;

    internal Type? DbContextType { get; set; }

    /// <summary>
    /// Data version
    /// </summary>
    internal string Version { get; set; } = default!;
}
