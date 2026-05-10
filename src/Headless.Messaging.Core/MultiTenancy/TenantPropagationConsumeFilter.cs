// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// Restores <see cref="ICurrentTenant"/> on the consume side from the message envelope's tenant identifier
/// (carried on <see cref="Headers.TenantId"/>) for the lifetime of the consume operation, so handler
/// code — including any work it dispatches — runs under the originating tenant.
/// </summary>
/// <remarks>
/// <para>
/// **Trust boundary.** This filter trusts the inbound envelope. The framework assumes the message bus is
/// internal-only; topics exposed to external producers must layer envelope validation or signing in front
/// of this filter. Otherwise an attacker who can publish to the bus can impersonate any tenant.
/// </para>
/// <para>
/// **Lenient resolution.** Whitespace, empty, or oversized header values map to "no tenant" — the filter
/// performs no <see cref="ICurrentTenant.Change"/> call, mirroring
/// <c>ConsumeExecutionPipeline._ResolveTenantId</c> exactly so the value observable here is identical to
/// what <see cref="ConsumeContext{TMessage}.TenantId"/> would expose.
/// </para>
/// <para>
/// Register via <c>messaging.AddTenantPropagation()</c>.
/// </para>
/// </remarks>
public sealed class TenantPropagationConsumeFilter(ICurrentTenant currentTenant) : ConsumeFilter
{
    private readonly ICurrentTenant _currentTenant =
        Argument.IsNotNull(currentTenant);

    // Per-message-scope filter instance; safe to hold the disposable on a field across the
    // executing/executed/exception triad. ConsumeExecutionPipeline creates a fresh DI scope
    // per message, so each message gets its own filter instance.
    private IDisposable? _scope;

    /// <inheritdoc/>
    public override ValueTask OnSubscribeExecutingAsync(ExecutingContext context)
    {
        Argument.IsNotNull(context);

        var headers = context.MediumMessage.Origin.Headers;
        if (
            headers.TryGetValue(Headers.TenantId, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && value.Length <= PublishOptions.TenantIdMaxLength
        )
        {
            _scope = _currentTenant.Change(value);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask OnSubscribeExecutedAsync(ExecutedContext context)
    {
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask OnSubscribeExceptionAsync(ExceptionContext context)
    {
        // Dispose deterministically on the exception path so tenant context is restored even
        // when the consumer throws — mirrors the dispose discipline from
        // docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md.
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }
}
