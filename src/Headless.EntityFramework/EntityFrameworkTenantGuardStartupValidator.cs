// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

/// <summary>
/// Emits a startup error for each guard the EF seam recorded (<c>guard-tenant-writes</c>,
/// <c>guard-tenant-reads</c>) whose <see cref="TenantGuardOptions"/> flag resolves to <see langword="false"/>,
/// typically because a later options registration clobbered the <c>PostConfigure</c> contribution. Surfaces the
/// mismatch at startup so operators are not surprised by silent loss of a guard.
/// </summary>
internal sealed class EntityFrameworkTenantGuardStartupValidator(IOptions<TenantGuardOptions> options)
    : IHeadlessTenancyValidator
{
    private const string _Seam = HeadlessEntityFrameworkTenancyBuilder.Seam;

    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var capabilities = context.Manifest.GetSeam(_Seam)?.Capabilities ?? [];

        if (
            capabilities.Contains(HeadlessEntityFrameworkTenancyBuilder.GuardTenantWritesLabel, StringComparer.Ordinal)
            && !options.Value.GuardWrites
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                _Seam,
                "HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED",
                "Headless EntityFramework seam recorded guard-tenant-writes but TenantGuardOptions.GuardWrites "
                    + "resolved to false at startup. A later override of TenantGuardOptions clobbered the "
                    + "PostConfigure contribution applied by GuardTenantWrites(). Move the override before "
                    + "AddHeadlessTenancy(...) or remove it."
            );
        }

        if (
            capabilities.Contains(HeadlessEntityFrameworkTenancyBuilder.GuardTenantReadsLabel, StringComparer.Ordinal)
            && !options.Value.GuardReads
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                _Seam,
                "HEADLESS_TENANCY_EF_READ_GUARD_DISABLED",
                "Headless EntityFramework seam recorded guard-tenant-reads but TenantGuardOptions.GuardReads "
                    + "resolved to false at startup. A later override of TenantGuardOptions clobbered the "
                    + "PostConfigure contribution applied by GuardTenantReads(). Move the override before "
                    + "AddHeadlessTenancy(...) or remove it."
            );
        }
    }
}
