// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Optional mixin: a type that exposes its <see cref="TimeProvider"/>. Consumers can pattern-match
/// (<c>if (x is IHaveTimeProvider h)</c>) to retrieve it.
/// </summary>
public interface IHaveTimeProvider
{
    TimeProvider TimeProvider { get; }
}
