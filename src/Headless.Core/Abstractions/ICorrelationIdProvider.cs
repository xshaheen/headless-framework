// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Provides a correlation identifier for grouping related operations across services.
/// </summary>
public interface ICorrelationIdProvider
{
    /// <summary>Gets the current correlation identifier, or <see langword="null"/> if none is active.</summary>
    string? CorrelationId { get; }
}
