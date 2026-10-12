// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using Headless.Checks;

namespace Headless.Messaging;

internal sealed class ProviderConfigBag
{
    private static readonly IReadOnlyDictionary<Type, object> _EmptyConfigs = ReadOnlyDictionary<Type, object>.Empty;

    private readonly Dictionary<Type, object> _configs = [];

    public void Set(object config)
    {
        Argument.IsNotNull(config);

        _configs[config.GetType()] = config;
    }

    public IReadOnlyDictionary<Type, object> Build()
    {
        return _configs.Count == 0
            ? _EmptyConfigs
            : new ReadOnlyDictionary<Type, object>(new Dictionary<Type, object>(_configs));
    }
}

internal interface IMessageProviderConfigBuilder<out TMessage>
    where TMessage : class
{
    void SetMessageProviderConfig(object config);
}

internal interface IConsumerProviderConfigBuilder
{
    void SetConsumerProviderConfig(object config);
}

internal interface IProviderHeaderContributions
{
    IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; }
}

/// <summary>
/// A provider config that must refuse a publish without a payload, because a value it needs is computed from the
/// payload and the message would otherwise be stored with no way to send it.
/// </summary>
internal interface IProviderMissingPayloadValidation
{
    /// <summary>Called when a message of the config's contract is published with a <see langword="null"/> payload.</summary>
    /// <exception cref="InvalidOperationException">The contract lane cannot be published without a payload.</exception>
    void ValidateMissingPayload(string messageName, MessageLane lane);
}

internal readonly record struct ProviderHeaderContribution(string HeaderName, Func<object, string?> Selector);
