// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Configuration;

/// <summary>Fluent handle returned from middleware registration methods.</summary>
[PublicAPI]
public sealed class MiddlewareRegistration
{
    private readonly MessagingBuilder _builder;
    private readonly MiddlewareDescriptor _descriptor;

    internal MiddlewareRegistration(MessagingBuilder builder, MiddlewareDescriptor descriptor)
    {
        _builder = builder;
        _descriptor = descriptor;
    }

    /// <summary>Sets this middleware's numeric priority. Lower values run earlier.</summary>
    public MessagingBuilder WithPriority(int priority)
    {
        _descriptor.SetPriority(priority);
        return _builder;
    }
}
