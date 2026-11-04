// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Scopes work to an explicitly named tenant through <see cref="ICurrentTenant"/>.</summary>
[PublicAPI]
public static class CurrentTenantExtensions
{
    /// <summary>
    /// Runs the returned scope as <paramref name="tenantId"/>: enters it when no tenant is set, keeps the ambient
    /// tenant when it is the same one, and refuses a different one. Use it where an operation names its tenant
    /// (a console command, a job, a message for one tenant) and the tenant query filter and write guard must agree
    /// with that name.
    /// </summary>
    /// <remarks>
    /// It checks consistency, never access: it does not decide whether the caller may act for
    /// <paramref name="tenantId"/>. Authorize that first. A mismatch throws rather than switching tenant, because
    /// silently replacing the caller's tenant would run the rest of the scope against data the caller did not ask for.
    /// </remarks>
    /// <param name="currentTenant">The current tenant.</param>
    /// <param name="tenantId">The tenant the operation is for.</param>
    /// <returns>
    /// A scope to dispose: it restores the previous tenant when this call entered one, and does nothing when the
    /// tenant was already set.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="currentTenant"/> or <paramref name="tenantId"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is empty or white space.</exception>
    /// <exception cref="TenantMismatchException">The ambient tenant is a different tenant.</exception>
    [MustDisposeResource]
    public static IDisposable EnterOrVerify(this ICurrentTenant currentTenant, string tenantId)
    {
        Argument.IsNotNull(currentTenant);
        Argument.IsNotNullOrWhiteSpace(tenantId);

        if (currentTenant.Id is not { } currentId)
        {
            return currentTenant.Change(tenantId);
        }

        // Tenant ids are opaque keys compared ordinally everywhere else (the query filter, the write guard).
        return string.Equals(currentId, tenantId, StringComparison.Ordinal)
            ? NoOpScope.Instance
            : throw new TenantMismatchException(tenantId, currentId);
    }

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();

        public void Dispose() { }
    }
}
