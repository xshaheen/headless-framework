// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting;

namespace Headless.Search.PostgreSql;

/// <summary>Connection and schema options for the PostgreSQL search provider.</summary>
[PublicAPI]
public sealed class PostgreSqlSearchOptions
{
    /// <summary>
    /// Gets or sets the connection string of the database that receives the search functions. Required; the
    /// parameterless <c>UsePostgreSql()</c> takes the one <c>AddPostgreSqlSql</c> registered.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schema that holds the search functions. The default is
    /// <see cref="HeadlessStorageDefaults.Schema" /> (<c>headless</c>). EF Core models that map the function must name
    /// the same schema.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;
}

internal sealed class PostgreSqlSearchOptionsValidator : AbstractValidator<PostgreSqlSearchOptions>
{
    public PostgreSqlSearchOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A PostgreSQL connection string is required.");
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
