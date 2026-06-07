// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination.Redis;

[PublicAPI]
public sealed class RedisCoordinationOptions
{
    public TimeSpan RedisCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

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
