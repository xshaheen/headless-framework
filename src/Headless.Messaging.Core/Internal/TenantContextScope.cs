// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Messages;
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

        if (value.Length > PublishOptions.TenantIdMaxLength)
        {
            logger?.TenantIdHeaderRejected(value.Length);
            return null;
        }

        return value;
    }

    public static IDisposable? ChangeFromEnvelope(IServiceProvider serviceProvider, Message message, ILogger? logger)
    {
        var tenantId = ResolveTenantId(message.Headers, logger);
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
