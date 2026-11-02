// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.Encryption;

internal static class DataKeyScope
{
    /// <summary>Checks that <paramref name="scope" /> fits the scope column and compares the same on every store.</summary>
    public static string Validate(string? scope, string paramName)
    {
        Argument.IsNotNull(scope, paramName: paramName);
        Argument.HasMaxLength(scope, DataKeyFieldLimits.ScopeMaxLength, paramName: paramName);
        Argument.IsPortableKey(scope, paramName: paramName);

        return scope;
    }

    /// <summary>
    /// Returns the explicit scope, else the current tenant; never the host scope by default, because a value written
    /// under the host key would survive the tenant's shredding.
    /// </summary>
    /// <exception cref="MissingTenantContextException">No scope is given and no tenant is current.</exception>
    public static string Resolve(
        string? scope,
        ICurrentTenant currentTenant,
        string operation,
        string purpose,
        string hostScopeFactory,
        string paramName
    )
    {
        // The message is built only when it is thrown: the scope resolves on every encrypt, decrypt, and blind index.
        var resolved =
            scope
            ?? currentTenant.Id
            ?? throw new MissingTenantContextException(
                $"{operation} '{purpose}' needs a tenant: no scope was given and no tenant is current. Wrap the call in "
                    + $"ICurrentTenant.Change(tenantId), set the scope, or use {hostScopeFactory} for host-level data."
            );

        return Validate(resolved, paramName);
    }
}
