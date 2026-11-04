// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Thrown by <see cref="CurrentTenantExtensions.EnterOrVerify"/> when the ambient tenant is a different tenant from
/// the one the operation names, so the operation would otherwise read or write the wrong tenant's data. Maps to HTTP
/// 403 with <c>g:tenant_mismatch</c> in the API exception handler.
/// </summary>
/// <remarks>
/// It is non-transient and must not be retried: the ambient tenant is the caller's, and no later attempt can make
/// the two agree. Neither tenant id reaches the response.
/// </remarks>
/// <param name="requestedTenantId">The tenant the operation names.</param>
/// <param name="currentTenantId">The ambient tenant.</param>
[PublicAPI]
public sealed class TenantMismatchException(string requestedTenantId, string currentTenantId)
    : Exception($"The operation is for tenant '{requestedTenantId}', but the current tenant is '{currentTenantId}'.")
{
    /// <summary>The tenant the operation names.</summary>
    public string RequestedTenantId { get; } = requestedTenantId;

    /// <summary>The ambient tenant.</summary>
    public string CurrentTenantId { get; } = currentTenantId;
}
