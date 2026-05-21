// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Permissions.Storage.EntityFramework;

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
        RuleFor(x => x.Schema).NotEmpty();
        RuleFor(x => x.PermissionGrantsTableName).NotEmpty();
        RuleFor(x => x.PermissionDefinitionsTableName).NotEmpty();
        RuleFor(x => x.PermissionGroupDefinitionsTableName).NotEmpty();
    }
}
