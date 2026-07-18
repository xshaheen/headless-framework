// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using FluentValidation;
using Headless.Checks;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server-specific configuration for the raw ADO.NET messaging storage backend.
/// </summary>
[PublicAPI]
public sealed partial class SqlServerOptions
{
    public const string DefaultSchema = "messaging";

    /// <summary>SQL Server maximum identifier length for schema names.</summary>
    public const int MaxSchemaLength = 128;

    /// <summary>Gets or sets the schema used when creating messaging database objects.</summary>
    public string Schema
    {
        get;
        set
        {
            Argument.IsNotNullOrWhiteSpace(value);
            Argument.Matches(
                value,
                ValidIdentifier,
                $"Schema name must start with a letter, underscore, @ or # and contain only letters, digits, underscores, @ or # (max {MaxSchemaLength} chars)"
            );

            field = value;
        }
    } = DefaultSchema;

    [GeneratedRegex("^[a-zA-Z_@#][a-zA-Z0-9_@#$]{0,127}$", RegexOptions.None, 100)]
    private static partial Regex ValidIdentifier { get; }

    /// <summary>Gets or sets the maximum length for the Owner column.</summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// Gets or sets the database's connection string that will be used to store database entities.
    /// </summary>
    public string? ConnectionString { get; set; }

    internal string Version { get; set; } = null!;
}

internal sealed class SqlServerOptionsValidator : AbstractValidator<SqlServerOptions>
{
    public SqlServerOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                "SQL Server messaging storage requires a ConnectionString. "
                    + "Configure via UseSqlServer(connectionString) or UseSqlServer(options => options.ConnectionString = ...)"
            );

        RuleFor(x => x.OwnerColumnMaxLength).GreaterThanOrEqualTo(DataStorageConstants.MinimumOwnerColumnMaxLength);
    }
}
