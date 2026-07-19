// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Hosting.Options;

/// <summary>An <see cref="IOptionsSnapshot{TOptions}"/> that always returns a single fixed value.</summary>
/// <remarks>
/// This is a constant adapter: <see cref="Get"/> ignores the requested name and always returns
/// <see cref="Value"/>. Use it only when a single, name-independent options value is intended.
/// </remarks>
/// <typeparam name="TOptions">Options type.</typeparam>
[PublicAPI]
public sealed class OptionsSnapshotWrapper<TOptions>(TOptions options) : IOptionsSnapshot<TOptions>
    where TOptions : class
{
    /// <summary>Gets the fixed options value. Always returns the value provided at construction.</summary>
    public TOptions Value { get; } = options;

    /// <summary>Returns <see cref="Value"/> regardless of <paramref name="name"/>.</summary>
    public TOptions Get(string? name)
    {
        return Value;
    }
}
