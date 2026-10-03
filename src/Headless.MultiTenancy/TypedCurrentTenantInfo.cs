// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Default <see cref="ICurrentTenantInfo{T}"/>: downcasts when the base accessor already returned
/// <typeparamref name="T"/> (the fast path — happens when this call's resolution was a cache miss and
/// the store returned the subtype directly), otherwise invokes the app-supplied projection delegate,
/// which may re-hydrate from the store itself since the cache only ever holds the base shape.
/// </summary>
internal sealed class TypedCurrentTenantInfo<T>(
    ICurrentTenantInfo baseAccessor,
    Func<TenantInfo, CancellationToken, Task<T>> projection
) : ICurrentTenantInfo<T>
    where T : TenantInfo
{
    public async Task<T?> GetAsync(CancellationToken cancellationToken = default)
    {
        var baseInfo = await baseAccessor.GetAsync(cancellationToken).ConfigureAwait(false);

        if (baseInfo is null)
        {
            return null;
        }

        if (baseInfo is T typed)
        {
            return typed;
        }

        return await projection(baseInfo, cancellationToken).ConfigureAwait(false);
    }
}
