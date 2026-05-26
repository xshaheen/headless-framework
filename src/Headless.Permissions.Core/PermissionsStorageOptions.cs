// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Storage;

namespace Headless.Permissions;

[PublicAPI]
public sealed class PermissionsStorageOptions
{
    public string Schema { get; set; } = "permissions";

    public string PermissionGrantsTableName { get; set; } = "PermissionGrants";

    public string PermissionDefinitionsTableName { get; set; } = "PermissionDefinitions";

    public string PermissionGroupDefinitionsTableName { get; set; } = "PermissionGroupDefinitions";
}

internal sealed class PermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
{
    public PermissionsStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.PermissionGrantsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.PermissionDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.PermissionGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
    }
}
