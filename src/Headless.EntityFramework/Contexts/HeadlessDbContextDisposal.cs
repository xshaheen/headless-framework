// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Shared owned-scope teardown for the Headless DbContext bases, exposed as extensions on the internal
/// <see cref="IHeadlessDbContextScopeOwner"/> seam. Both <see cref="HeadlessDbContext"/> and the Identity context
/// call these so the dispose semantics (resolve-logger-first, categorize under the concrete context type,
/// guard against a secondary scope-dispose failure masking the primary exception, prefer async scope
/// disposal) stay identical — the contexts cannot share a base class (the Identity context must derive from
/// <c>IdentityDbContext</c>), so the behavior lives here. The seam receiver makes the owned-scope access
/// compile-time safe (no runtime cast) and binds only on types that actually own a scope.
/// </summary>
/// <remarks>
/// Only the owned-scope step is centralized: <see langword="base"/> disposal, runtime teardown, and
/// <c>GC.SuppressFinalize</c> stay inline in each override (they are trivial, identical, and must remain
/// analyzer-visible).
/// </remarks>
internal static class HeadlessDbContextDisposal
{
    public static void DisposeOwnedScope(this IHeadlessDbContextScopeOwner context)
    {
        var ownedScope = context.OwnedScope;

        if (ownedScope is null)
        {
            return;
        }

        // Resolve the logger before disposing — the scope's provider is gone afterwards. Categorize it under
        // the concrete context type so plain and Identity contexts log under their own name. The guard keeps a
        // secondary scope-dispose failure from masking the primary runtime/base exception, which is the
        // failure operators actually need.
        var logger = ownedScope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(context.GetType());

        try
        {
            ownedScope.Dispose();
        }
        catch (Exception scopeEx)
        {
            logger?.LogOwnedScopeDisposeFailed(scopeEx);
        }
    }

    public static async ValueTask DisposeOwnedScopeAsync(this IHeadlessDbContextScopeOwner context)
    {
        var ownedScope = context.OwnedScope;

        if (ownedScope is null)
        {
            return;
        }

        var logger = ownedScope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(context.GetType());

        try
        {
            // Prefer async scope disposal — MS DI scopes (AsyncServiceScope) may hold
            // async-only-disposable scoped services.
            if (ownedScope is IAsyncDisposable asyncDisposableScope)
            {
                await asyncDisposableScope.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                ownedScope.Dispose();
            }
        }
        catch (Exception scopeEx)
        {
            logger?.LogOwnedScopeDisposeFailed(scopeEx);
        }
    }
}
