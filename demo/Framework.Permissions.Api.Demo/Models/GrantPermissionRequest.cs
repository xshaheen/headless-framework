using System.ComponentModel.DataAnnotations;

namespace Framework.Permissions.Api.Demo.Models;

public sealed class GrantPermissionRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Permission name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Permission name must be between 1 and 256 characters")]
    [RegularExpression(
        @"^[a-zA-Z0-9._-]+$",
        ErrorMessage = "Permission name can only contain alphanumeric characters, dots, underscores, and hyphens"
    )]
    public required string Name { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Provider name is required")]
    [StringLength(128, MinimumLength = 1, ErrorMessage = "Provider name must be between 1 and 128 characters")]
    [RegularExpression(
        @"^[a-zA-Z0-9_-]+$",
        ErrorMessage = "Provider name can only contain alphanumeric characters, underscores, and hyphens"
    )]
    public required string ProviderName { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Provider key is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Provider key must be between 1 and 256 characters")]
    public required string ProviderKey { get; init; }
}
