// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Diagnostics;

namespace Headless.Messaging.OpenTelemetry;

internal sealed class MessagingDiagnosticSourceSubscriber(
    Func<string, DiagnosticListener> handlerFactory,
    Func<System.Diagnostics.DiagnosticListener, bool> diagnosticSourceFilter,
    Func<string, object?, object?, bool>? isEnabledFilter
) : IDisposable, IObserver<System.Diagnostics.DiagnosticListener>
{
    private readonly Func<string, DiagnosticListener> _handlerFactory = Argument.IsNotNull(handlerFactory);
    private readonly List<IDisposable> _listenerSubscriptions = [];
    private IDisposable? _allSourcesSubscription;
    private long _disposed;

    public MessagingDiagnosticSourceSubscriber(
        DiagnosticListener handler,
        Func<string, object?, object?, bool>? isEnabledFilter
    )
        : this(
            _ => handler,
            value =>
                string.Equals(
                    MessageDiagnosticListenerNames.DiagnosticListenerName,
                    value.Name,
                    StringComparison.Ordinal
                ),
            isEnabledFilter
        ) { }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        // Extract the subscriptions under the lock so writes to _allSourcesSubscription and
        // _listenerSubscriptions happen under the same lock that Subscribe()/OnNext() take.
        // Dispose() of the captured subscriptions is performed outside the lock to avoid any
        // re-entrancy risk if the subscription's Dispose() runs OnNext synchronously.
        IDisposable? allSources;
        IDisposable[] listeners;

        lock (_listenerSubscriptions)
        {
            allSources = _allSourcesSubscription;
            _allSourcesSubscription = null;
            listeners = [.. _listenerSubscriptions];
            _listenerSubscriptions.Clear();
        }

        foreach (var listenerSubscription in listeners)
        {
            listenerSubscription?.Dispose();
        }

        allSources?.Dispose();
    }

    public void OnNext(System.Diagnostics.DiagnosticListener value)
    {
        if (Interlocked.Read(ref _disposed) == 0 && diagnosticSourceFilter(value))
        {
            var handler = _handlerFactory(value.Name);
            var subscription =
                isEnabledFilter == null ? value.Subscribe(handler) : value.Subscribe(handler, isEnabledFilter);

            lock (_listenerSubscriptions)
            {
                if (Interlocked.Read(ref _disposed) == 1)
                {
                    subscription.Dispose();
                    return;
                }

                _listenerSubscriptions.Add(subscription);
            }
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Subscribe()
    {
        lock (_listenerSubscriptions)
        {
            if (Interlocked.Read(ref _disposed) == 1)
            {
                return;
            }

            _allSourcesSubscription ??= System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(this);
        }
    }
}
