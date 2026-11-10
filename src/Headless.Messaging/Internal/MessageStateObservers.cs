// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>The observers a host registered, read once, on the first write that reports to them.</summary>
internal sealed class MessageStateObservers
{
    private readonly Lazy<IMessageStateObserver[]> _observers;

    public MessageStateObservers(IServiceProvider services)
    {
        Argument.IsNotNull(services);

        // Resolved lazily: the pipelines are built while the container is still resolving the host's services.
        _observers = new Lazy<IMessageStateObserver[]>(() => [.. services.GetServices<IMessageStateObserver>()]);
    }

    /// <summary>Tells every observer that <paramref name="message"/>'s state write landed.</summary>
    public void Notify(MessageType messageType, MediumMessage message)
    {
        foreach (var observer in _observers.Value)
        {
            observer.OnStateWritten(messageType, message);
        }
    }
}
