// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Permissions;

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
