// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Messages;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal static class TenantContextScope
{
    public static string? ResolveTenantId(IDictionary<string, string?> headers, ILogger? logger)
    {
        if (!headers.TryGetValue(Headers.TenantId, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > MessageOptions.TenantIdMaxLength)
        {
            logger?.TenantIdHeaderRejected(value.Length);
            return null;
        }

        return value;
    }

    public static IDisposable? ChangeFromEnvelope(IServiceProvider serviceProvider, Message message, ILogger? logger) =>
        _ChangeFromEnvelope(serviceProvider, message, logger, out _);

    /// <summary>
    /// Runs <paramref name="next"/> under the envelope's tenant with that tenant's data placement preloaded, so
    /// code that resolves a tenant-routed context from <paramref name="serviceProvider"/> can build it. For the
    /// paths that set the tenant outside the consume middleware, such as the exhausted callbacks.
    /// </summary>
    public static async Task RunInEnvelopeTenantAsync(
        IServiceProvider serviceProvider,
        Message message,
        ILogger? logger,
        Func<Task> next,
        CancellationToken cancellationToken
    )
    {
        using var tenantScope = _ChangeFromEnvelope(serviceProvider, message, logger, out var tenantId);

        if (tenantScope is not null && serviceProvider.GetService<TenantDataPlacementPreloader>() is { } preloader)
        {
            await preloader.RunAsync(serviceProvider, tenantId, next, cancellationToken).ConfigureAwait(false);

            return;
        }

        await next().ConfigureAwait(false);
    }

    private static IDisposable? _ChangeFromEnvelope(
        IServiceProvider serviceProvider,
        Message message,
        ILogger? logger,
        out string? tenantId
    )
    {
        tenantId = ResolveTenantId(message.Headers, logger);
        if (tenantId is null)
        {
            return null;
        }

        var currentTenant = serviceProvider.GetService<ICurrentTenant>();
        if (currentTenant is null)
        {
            return null;
        }

        logger?.TenantContextSwitched(tenantId);
        return currentTenant.Change(tenantId);
    }
}
