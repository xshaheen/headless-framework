// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// Restores <see cref="ICurrentTenant"/> on the consume side from the resolved
/// <see cref="ConsumeContext{TMessage}.TenantId"/> for the lifetime of the consume operation, so handler
/// code — including any work it dispatches — runs under the originating tenant.
/// </summary>
/// <remarks>
/// <para>
/// **Trust boundary.** This filter trusts the inbound envelope. The framework assumes the message bus is
/// internal-only; topics exposed to external producers must layer envelope validation or signing in front
/// of this filter. Otherwise an attacker who can publish to the bus can impersonate any tenant.
/// </para>
/// <para>
/// **Canonical resolution.** Whitespace, empty, or oversized header values already map to
/// <see langword="null"/> when the consume pipeline builds <see cref="ConsumeContext{TMessage}.TenantId"/>.
/// This filter consumes that resolved value instead of reparsing raw headers.
/// </para>
/// <para>
/// Register via <c>builder.AddHeadlessTenancy(t =&gt; t.Messaging(m =&gt; m.PropagateTenant()))</c>.
/// </para>
/// </remarks>
public sealed class TenantPropagationConsumeFilter(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationConsumeFilter>? logger = null
) : ConsumeFilter
{
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    // Per-message-scope filter instance; safe to hold the disposable on a field across the
    // executing/executed/exception triad. ConsumeExecutionPipeline creates a fresh DI scope
    // per message, so each message gets its own filter instance.
    private IDisposable? _scope;

    /// <inheritdoc/>
    public override ValueTask OnSubscribeExecutingAsync(
        ExecutingContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);

        if (context.TenantId is { } value)
        {
            logger?.TenantContextSwitched(value);
            // Dispose any prior scope before reassigning. ConsumeExecutionPipeline creates a fresh
            // filter instance per message scope, so this is defensive — but if a pipeline ever
            // reuses the filter and OnSubscribeExecutingAsync runs twice without an intervening
            // OnSubscribeExecutedAsync/OnSubscribeExceptionAsync (e.g., a transport bug), failing to
            // dispose the previous scope would leak the tenant context for the rest of the flow.
            _scope?.Dispose();
            _scope = _currentTenant.Change(value);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask OnSubscribeExecutedAsync(
        ExecutedContext context,
        CancellationToken cancellationToken = default
    )
    {
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        // Dispose deterministically on the exception path so tenant context is restored even
        // when the consumer throws — mirrors the dispose discipline from
        // docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md.
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }
}
