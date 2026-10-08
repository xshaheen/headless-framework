// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.Idempotency;

internal sealed class IdempotencyLookup(
    IOptionsMonitor<IdempotencyOptions> optionsMonitor,
    IIdempotentOperations operations,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IOptions<MultiTenancyOptions> tenancyOptions
) : IIdempotencyLookup
{
    public async ValueTask<IdempotencyPeekStatus> GetStatusAsync(
        HttpContext context,
        IdempotentRequestTarget target,
        CancellationToken cancellationToken = default
    )
    {
        var options = optionsMonitor.CurrentValue;
        var storeKey = _StoreKey(context, target, options);

        if (storeKey is null)
        {
            return IdempotencyPeekStatus.Absent;
        }

        using (currentTenant.Change(_StoreTenant()))
        {
            return await operations.PeekAsync(storeKey, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> TryReplayAsync(
        HttpContext context,
        IdempotentRequestTarget target,
        CancellationToken cancellationToken = default
    )
    {
        var options = optionsMonitor.CurrentValue;
        var storeKey = _StoreKey(context, target, options);

        if (storeKey is null)
        {
            return false;
        }

        IdempotentResult? result;

        using (currentTenant.Change(_StoreTenant()))
        {
            result = await operations.GetResultAsync(storeKey, cancellationToken).ConfigureAwait(false);
        }

        if (result is null)
        {
            return false;
        }

        await IdempotencyRequestScope
            .ReplayAsync(context, result, options.ReplayHeaderAllowlist, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    private string? _StoreKey(HttpContext context, IdempotentRequestTarget target, IdempotencyOptions options)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(target);
        Argument.IsNotNullOrWhiteSpace(target.Method);
        Argument.IsNotNullOrWhiteSpace(target.Key);

        if (!IdempotencyRequestScope.IsValidKey(target.Key, out var reason))
        {
            throw new ArgumentException($"The idempotency key is malformed ({reason}).", nameof(target));
        }

        var scope = IdempotencyRequestScope.Build(
            context,
            options,
            currentUser,
            target.Method,
            target.Path.Value ?? "",
            target.Query.Value ?? string.Empty,
            target.Key
        );

        // An empty scope is a request the middleware would not have stored, so there is nothing to find.
        return scope.Length == 0 ? null : IdempotencyRequestScope.Hash(scope);
    }

    private string? _StoreTenant()
    {
        return IdempotencyRequestScope.StoreTenant(currentUser, tenancyOptions.Value);
    }
}
