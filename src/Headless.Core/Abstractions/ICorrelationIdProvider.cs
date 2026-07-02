// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Provides a correlation ID for grouping related operations across services.
/// Used by audit logging, distributed tracing, and structured logging.
/// </summary>
public interface ICorrelationIdProvider
{
    /// <summary>Gets the current correlation ID, or <see langword="null"/> if none is active.</summary>
    string? CorrelationId { get; }
}
