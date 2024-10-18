// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using StackExchange.Redis;

namespace Framework.Caching;

public sealed class RedisCacheOptions : CacheOptions
{
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    /// <summary>The behaviour required when performing read operations from cache.</summary>
    public CommandFlags ReadMode { get; set; } = CommandFlags.None;
}

public sealed class RedisCacheOptionsValidator : AbstractValidator<RedisCacheOptions>
{
    public RedisCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull();
        RuleFor(x => x.ConnectionMultiplexer).NotNull();
        RuleFor(x => x.ReadMode).IsInEnum();
    }
}
