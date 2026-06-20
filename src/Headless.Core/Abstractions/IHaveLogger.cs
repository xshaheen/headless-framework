// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Abstractions;

/// <summary>
/// Optional mixin: a type that exposes its <see cref="ILogger"/>. Consumers can pattern-match
/// (<c>if (x is IHaveLogger h)</c>) to retrieve it.
/// </summary>
public interface IHaveLogger
{
    ILogger Logger { get; }
}
