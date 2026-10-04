// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Settings;

/// <summary>Connection and command options every relational settings storage provider shares.</summary>
[PublicAPI]
public abstract class RelationalSettingsOptions
{
    /// <summary>Gets or sets the connection string used to open connections for DDL and DML operations. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout applied to every command this provider runs. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal int CommandTimeoutSeconds => (int)CommandTimeout.TotalSeconds;
}

internal abstract class RelationalSettingsOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalSettingsOptions
{
    protected RelationalSettingsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}

/// <summary>Validates <see cref="SettingsStorageOptions"/> for a relational provider's identifier rules.</summary>
internal sealed class RelationalSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
{
    public RelationalSettingsStorageOptionsValidator(StorageProvider provider)
    {
        // Every provider measures derived names against PostgreSQL's limit, so a table name valid on one database
        // stays valid on the others.
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.SettingValuesTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(SettingsStorageNames.ValuesIndexes)
            .When(x => x.SettingValuesTableName is not null);
        RuleFor(x => x.SettingDefinitionsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(SettingsStorageNames.DefinitionsIndexes)
            .When(x => x.SettingDefinitionsTableName is not null);
    }
}
