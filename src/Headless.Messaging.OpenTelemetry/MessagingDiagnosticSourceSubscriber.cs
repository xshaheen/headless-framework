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

        lock (_listenerSubscriptions)
        {
            foreach (var listenerSubscription in _listenerSubscriptions)
            {
                listenerSubscription?.Dispose();
            }

            _listenerSubscriptions.Clear();
        }

        _allSourcesSubscription?.Dispose();
        _allSourcesSubscription = null;
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
                _listenerSubscriptions.Add(subscription);
            }
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Subscribe()
    {
        _allSourcesSubscription ??= System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(this);
    }
}
