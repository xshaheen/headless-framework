// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination.Redis;

/// <summary>Options for the Redis coordination backing store.</summary>
[PublicAPI]
public sealed class RedisCoordinationOptions
{
    /// <summary>
    /// How often the background cleanup service scans for and deletes Redis keys belonging to node
    /// incarnations that have expired beyond <see cref="RedisKnownNodeRetention"/>. Must be positive.
    /// </summary>
    public TimeSpan RedisCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a node's Redis keys are retained after the node stops heartbeating. Keys older than this
    /// window are eligible for deletion by the cleanup service. Must be positive.
    /// </summary>
    public TimeSpan RedisKnownNodeRetention { get; set; } = TimeSpan.FromDays(7);
}

internal sealed class RedisCoordinationOptionsValidator : AbstractValidator<RedisCoordinationOptions>
{
    public RedisCoordinationOptionsValidator()
    {
        RuleFor(x => x.RedisCleanupInterval).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.RedisKnownNodeRetention).GreaterThan(TimeSpan.Zero);
    }
}
