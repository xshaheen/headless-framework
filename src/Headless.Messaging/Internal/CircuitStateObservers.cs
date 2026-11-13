// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>The observers a host registered, read once, on the first circuit change that reports to them.</summary>
internal sealed class CircuitStateObservers(IServiceProvider services)
{
    private readonly IServiceProvider _services = Argument.IsNotNull(services);
    private ICircuitStateObserver[]? _observers;

    /// <summary>Tells every observer that the circuit <paramref name="circuitKey"/> names changed.</summary>
    public void Notify(string circuitKey)
    {
        foreach (var observer in Volatile.Read(ref _observers) ?? _Resolve())
        {
            observer.OnCircuitChanged(circuitKey);
        }
    }

    // Resolved on the first change, not in the constructor: an observer reads the breaker back, so resolving it while
    // the breaker is built would loop.
    private ICircuitStateObserver[] _Resolve()
    {
        try
        {
            ICircuitStateObserver[] observers = [.. _services.GetServices<ICircuitStateObserver>()];
            Volatile.Write(ref _observers, observers);

            return observers;
        }
        catch (ObjectDisposedException)
        {
            // The first change is a circuit removed while the host stops, after its container was disposed: no
            // observer is left to tell, and the breaker's shutdown must not fail for it.
            return [];
        }
    }
}
