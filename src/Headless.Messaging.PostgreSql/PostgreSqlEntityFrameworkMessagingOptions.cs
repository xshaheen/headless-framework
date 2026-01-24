// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Messaging.PostgreSql;

public partial class PostgreSqlEntityFrameworkMessagingOptions
{
    public const string DefaultSchema = "messages";

    /// <summary>
    /// PostgreSQL maximum identifier length for schema names.
    /// </summary>
    public const int MaxSchemaLength = 63;

    /// <summary>
    /// Gets or sets the schema to use when creating database objects.
    /// Default is <see cref="DefaultSchema" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the schema name is empty/whitespace or does not match PostgreSQL identifier rules
    /// (max 63 chars, starts with letter/underscore, alphanumeric/underscore only).
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
                    $"Schema name must start with a letter or underscore and contain only letters, digits, underscores (max {MaxSchemaLength} chars)",
                    nameof(value)
                );
            }

            field = value;
        }
    } = DefaultSchema;

    /// <summary>
    /// PostgreSQL identifier validation regex.
    /// Must start with a letter or underscore, contain only letters, digits, underscores, max 63 chars.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.None, 100)]
    private static partial Regex _ValidIdentifier();

    internal Type? DbContextType { get; set; }

    /// <summary>
    /// Data version
    /// </summary>
    internal string Version { get; set; } = null!;
}
