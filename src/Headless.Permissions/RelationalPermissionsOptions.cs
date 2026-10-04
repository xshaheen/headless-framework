// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Permissions;

/// <summary>Connection and command options every relational permissions storage provider shares.</summary>
[PublicAPI]
public abstract class RelationalPermissionsOptions
{
    /// <summary>Gets or sets the connection string used to open connections for DDL and DML operations. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout applied to every command this provider runs. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal int CommandTimeoutSeconds => (int)CommandTimeout.TotalSeconds;
}

internal abstract class RelationalPermissionsOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalPermissionsOptions
{
    protected RelationalPermissionsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}

/// <summary>Validates <see cref="PermissionsStorageOptions"/> for a relational provider's identifier rules.</summary>
internal sealed class RelationalPermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
{
    public RelationalPermissionsStorageOptionsValidator(StorageProvider provider)
    {
        // Every provider measures derived names against PostgreSQL's limit, so a table name valid on one database
        // stays valid on the others.
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.PermissionGrantsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(PermissionsStorageNames.GrantsIndexes)
            .When(x => x.PermissionGrantsTableName is not null);
        RuleFor(x => x.PermissionDefinitionsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(PermissionsStorageNames.DefinitionsIndexes)
            .When(x => x.PermissionDefinitionsTableName is not null);
        RuleFor(x => x.PermissionGroupDefinitionsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(PermissionsStorageNames.GroupsIndexes)
            .When(x => x.PermissionGroupDefinitionsTableName is not null);
    }
}
