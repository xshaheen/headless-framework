// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Transport;
using StackExchange.Redis;

namespace Headless.Messaging.RedisPubSub;

public sealed class RedisPubSubOptions
{
    /// <summary>
    /// Gets or sets the native StackExchange.Redis connection options.
    /// </summary>
    public ConfigurationOptions? Configuration { get; set; }

    internal string DisplayEndpoint =>
        Configuration?.EndPoints.Count > 0
            ? string.Join(',', Configuration.EndPoints.Select(BrokerAddressDisplay.Format))
            : string.Empty;
}

internal sealed class RedisPubSubOptionsValidator : AbstractValidator<RedisPubSubOptions>;
