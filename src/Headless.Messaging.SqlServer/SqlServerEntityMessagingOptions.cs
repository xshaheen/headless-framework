// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Messaging.SqlServer;

public partial class SqlServerEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messages";

    /// <summary>
    /// SQL Server maximum identifier length for schema names.
    /// </summary>
    public const int MaxSchemaLength = 128;

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the schema name is empty/whitespace or does not match SQL Server identifier rules
    /// (max 128 chars, starts with letter/underscore/@/#, alphanumeric/underscore/$/# only).
    /// </exception>
    public string Schema
    {
        get;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (!_ValidIdentifier().IsMatch(value))
            {
                throw new ArgumentException(
                    $"Schema name must start with a letter, underscore, @ or # and contain only letters, digits, underscores, @ or # (max {MaxSchemaLength} chars)",
                    nameof(value)
                );
            }

            field = value;
        }
    } = DefaultSchema;

    /// <summary>
    /// SQL Server identifier validation regex.
    /// Must start with a letter, underscore, @ or #, contain only letters, digits, underscores, @, # or $.
    /// Max 128 chars per SQL Server identifier rules.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z_@#][a-zA-Z0-9_@#$]{0,127}$", RegexOptions.None, 100)]
    private static partial Regex _ValidIdentifier();

    /// <summary>
    /// EF DbContext
    /// </summary>
    internal Type? DbContextType { get; set; }

    /// <summary>
    /// Data version
    /// </summary>
    internal string Version { get; set; } = null!;
}
