// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Nito.Disposables;

namespace Headless.Hosting.Options;

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> that always returns a single fixed value.</summary>
/// <remarks>
/// This is a constant adapter, not a live monitor: <see cref="Get"/> ignores the requested name and
/// always returns <see cref="CurrentValue"/>, and <see cref="OnChange"/> never fires — the listener is
/// dropped and a no-op disposable is returned. Use it only when the value is immutable for the
/// consumer's lifetime; do not use it to back code that relies on reload notifications.
/// </remarks>
/// <typeparam name="TOptions">Options type.</typeparam>
[PublicAPI]
public sealed class OptionsMonitorWrapper<TOptions>(TOptions options) : IOptionsMonitor<TOptions>
    where TOptions : class
{
    /// <summary>Gets the fixed options value. Always returns the value provided at construction.</summary>
    public TOptions CurrentValue { get; } = options;

    /// <summary>Returns <see cref="CurrentValue"/> regardless of <paramref name="name"/>.</summary>
    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    /// <summary>No-op: change notifications are not supported and <paramref name="listener"/> is never invoked.</summary>
    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        return NoopDisposable.Instance;
    }
}
