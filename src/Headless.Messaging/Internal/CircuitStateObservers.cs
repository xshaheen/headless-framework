// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>The observers a host registered, read once, on the first circuit change that reports to them.</summary>
internal sealed class CircuitStateObservers
{
    private readonly Lazy<ICircuitStateObserver[]> _observers;

    public CircuitStateObservers(IServiceProvider services)
    {
        Argument.IsNotNull(services);

        // Resolved lazily: an observer reads the breaker back, so resolving it while the breaker is built would loop.
        _observers = new Lazy<ICircuitStateObserver[]>(() => [.. services.GetServices<ICircuitStateObserver>()]);
    }

    /// <summary>Tells every observer that the circuit <paramref name="circuitKey"/> names changed.</summary>
    public void Notify(string circuitKey)
    {
        foreach (var observer in _observers.Value)
        {
            observer.OnCircuitChanged(circuitKey);
        }
    }
}
