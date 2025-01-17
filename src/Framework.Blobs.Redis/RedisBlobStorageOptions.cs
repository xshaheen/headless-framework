// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.Serializer;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Framework.Blobs.Redis;

public sealed class RedisBlobStorageOptions
{
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }

    public ISerializer? Serializer { get; set; }
}

internal sealed class RedisBlobStorageOptionsValidator : AbstractValidator<RedisBlobStorageOptions>
{
    public RedisBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ConnectionMultiplexer).NotNull();
    }
}
