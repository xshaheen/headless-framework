// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reactive.Disposables;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Options;

/// <summary><see cref="IOptionsMonitor{TOptions}"/> wrapper that returns the options instance.</summary>
/// <typeparam name="TOptions">Options type.</typeparam>
public sealed class OptionsMonitorWrapper<TOptions>(TOptions options) : IOptionsMonitor<TOptions>
    where TOptions : class
{
    public TOptions CurrentValue { get; set; } = options;

    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable OnChange(Action<TOptions, string> listener)
    {
        return Disposable.Empty;
    }
}
