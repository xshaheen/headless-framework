// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.LiveQueries;

/// <summary>One result of a live query watched from .NET code.</summary>
/// <typeparam name="T">The result type.</typeparam>
[PublicAPI]
public sealed record LiveSnapshot<T>
{
    /// <summary>Gets the value the query returned.</summary>
    public required T Value { get; init; }

    /// <summary>
    /// Gets an opaque token that identifies this result within its subscription. Compare tokens for equality only;
    /// see <see cref="LiveUpdate.Version" />.
    /// </summary>
    public required string Version
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    }

    /// <summary>Gets a value indicating whether this is the first result of the subscription.</summary>
    public required bool IsInitial { get; init; }
}
