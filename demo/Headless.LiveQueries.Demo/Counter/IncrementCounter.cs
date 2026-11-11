// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

/// <summary>
/// A command that reads the counter, waits <paramref name="HoldMilliseconds"/>, and saves under a version check.
/// </summary>
/// <param name="HoldMilliseconds">Time between the read and the save, so that requests sent together collide.</param>
/// <param name="Retry">Whether the handler opts its unit of work in to a replay when the save loses the check.</param>
public sealed record IncrementCounter(int HoldMilliseconds = 0, bool Retry = true) : IRequest<ApiResult<CounterView>>;
