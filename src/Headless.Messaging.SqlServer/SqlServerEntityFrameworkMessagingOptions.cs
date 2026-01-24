// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.SqlServer;

public class SqlServerEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messages";

    /// <summary>
    /// SQL Server maximum identifier length for schema names.
    /// </summary>
    public const int MaxSchemaLength = 128;

    // ReSharper disable once ConvertToAutoProperty - need backing field for validation
#pragma warning disable IDE0032 // Use auto property
    private string _schema = DefaultSchema;
#pragma warning restore IDE0032

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the schema name is empty/whitespace or exceeds 128 characters.
    /// </exception>
    public string Schema
    {
        get => _schema;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (value.Length > MaxSchemaLength)
            {
                throw new ArgumentException($"Schema name cannot exceed {MaxSchemaLength} characters", nameof(value));
            }

            _schema = value;
        }
    }

    /// <summary>
    /// EF DbContext
    /// </summary>
    internal Type? DbContextType { get; set; }

    /// <summary>
    /// Data version
    /// </summary>
    internal string Version { get; set; } = null!;
}
