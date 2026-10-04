// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Provides a correlation ID for grouping related operations across services.
/// </summary>
/// <remarks>
/// Used by audit logging, distributed tracing, and structured logging.
/// </remarks>
public interface ICorrelationIdProvider
{
    /// <summary>Gets the current correlation identifier, or <see langword="null"/> if none is active.</summary>
    string? CorrelationId { get; }
}
