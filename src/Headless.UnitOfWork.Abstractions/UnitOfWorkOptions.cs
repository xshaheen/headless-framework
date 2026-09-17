// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Options for opening a unit of work. Currently empty by design: propagation knobs
/// (<c>RequiresNew</c> / <c>Suppress</c>) are deferred and will land here additively.
/// </summary>
[PublicAPI]
public sealed record UnitOfWorkOptions;
