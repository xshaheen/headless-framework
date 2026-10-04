// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting;

namespace Headless.Sequences;

/// <summary>Configures common relational database settings for sequence providers.</summary>
/// <param name="defaultTableName">The default table name for the specific database dialect.</param>
[PublicAPI]
public abstract class RelationalSequencesOptions(string defaultTableName)
{
    /// <summary>
    /// Gets or sets the database connection string. Fast-mode operations open dedicated connections with this string,
    /// and gap-free allocations require units of work connected to the same database.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the database command timeout. The default is 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the database schema name containing the sequence table.
    /// The default is <see cref="HeadlessStorageDefaults.Schema" /> (<c>headless</c>).
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>Gets or sets the name of the sequence table.</summary>
    public string TableName { get; set; } = defaultTableName;

    /// <summary>
    /// Gets or sets a value indicating whether to create the database schema and table on host startup if missing.
    /// The default is <see langword="true" />.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal abstract class RelationalSequencesOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalSequencesOptions
{
    protected RelationalSequencesOptionsValidator(StorageProvider provider, string displayName)
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage($"A {displayName} connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.TableName).IsValidIdentifierFor(provider);
    }
}
