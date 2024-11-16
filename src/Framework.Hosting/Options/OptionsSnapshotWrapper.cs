// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Options;

/// <summary><see cref="IOptionsSnapshot{TOptions}"/> wrapper that returns the options instance.</summary>
/// <typeparam name="TOptions">Options type.</typeparam>
public sealed class OptionsSnapshotWrapper<TOptions>(TOptions options) : IOptionsSnapshot<TOptions>
    where TOptions : class
{
    public TOptions Value { get; } = options;

    public TOptions Get(string? name)
    {
        return Value;
    }
}
