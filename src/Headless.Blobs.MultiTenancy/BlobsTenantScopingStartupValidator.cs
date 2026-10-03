// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

/// <summary>
/// Emits a startup error when the blob seam recorded <c>scope-by-tenant</c> but no store was wrapped, typically
/// because no store was registered through <c>AddHeadlessBlobs</c>. The host would otherwise believe its blobs are
/// tenant-scoped while every call reaches an unscoped store.
/// </summary>
internal sealed class BlobsTenantScopingStartupValidator(TenantBlobScopingState state) : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var recorded =
            context
                .Manifest.GetSeam(HeadlessBlobsTenancyBuilder.Seam)
                ?.Capabilities.Contains(HeadlessBlobsTenancyBuilder.ScopeByTenantCapability, StringComparer.Ordinal)
            == true;

        if (!recorded || state.Decoration.DecoratedRegistrations > 0)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessBlobsTenancyBuilder.Seam,
            "HEADLESS_TENANCY_BLOBS_NO_SCOPED_STORE",
            "Headless blob seam recorded scope-by-tenant but no blob store was scoped. Register the stores through "
                + "AddHeadlessBlobs(...); a store registered directly as IBlobStorage is never scoped."
        );
    }
}
