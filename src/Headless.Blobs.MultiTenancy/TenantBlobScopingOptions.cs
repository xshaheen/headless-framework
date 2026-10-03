// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs;

/// <summary>Options for tenant-scoped blob storage, configured through <c>tenancy.Blobs(b => b.ScopeByTenant(...))</c>.</summary>
[PublicAPI]
public sealed class TenantBlobScopingOptions
{
    /// <summary>Gets or sets where the tenant goes. Defaults to <see cref="TenantBlobScopingStrategy.PathPrefix"/>.</summary>
    public TenantBlobScopingStrategy Strategy { get; set; } = TenantBlobScopingStrategy.PathPrefix;

    /// <summary>
    /// Gets or sets the text prepended to the tenant id to name a tenant's container under
    /// <see cref="TenantBlobScopingStrategy.ContainerPerTenant"/>, for example <c>myapp-</c> so that S3 bucket names,
    /// which are global across accounts, stay unique. Lowercase letters, digits, and hyphens only. Ignored by
    /// <see cref="TenantBlobScopingStrategy.PathPrefix"/>. Defaults to empty.
    /// </summary>
    public string ContainerPrefix { get; set; } = "";

    /// <summary>
    /// Gets the names of named stores (<c>AddNamed</c>) that are left unscoped, for blobs shared by every tenant. The
    /// default store is always scoped.
    /// </summary>
    public ISet<string> UnscopedStores { get; } = new HashSet<string>(StringComparer.Ordinal);
}

internal sealed class TenantBlobScopingOptionsValidator : AbstractValidator<TenantBlobScopingOptions>
{
    public TenantBlobScopingOptionsValidator()
    {
        RuleFor(x => x.Strategy).IsInEnum();

        RuleFor(x => x.ContainerPrefix)
            .NotNull()
            .MaximumLength(TenantBlobScope.MaxContainerNameLength - 1)
            .Matches("^[a-z0-9-]*$")
            .WithMessage("ContainerPrefix may contain only lowercase letters, digits, and hyphens.");

        RuleForEach(x => x.UnscopedStores).NotEmpty();
    }
}
