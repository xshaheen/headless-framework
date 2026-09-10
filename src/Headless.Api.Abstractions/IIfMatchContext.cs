// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Provides the strong entity tag supplied through <c>If-Match</c> for the current request.</summary>
[PublicAPI]
public interface IIfMatchContext
{
    /// <summary>Gets the supplied entity tag, or <see langword="null"/> when no precondition was required.</summary>
    EntityTag? EntityTag { get; }

    /// <summary>Gets whether the current request supplied a valid strong entity tag.</summary>
    bool HasValue { get; }
}
