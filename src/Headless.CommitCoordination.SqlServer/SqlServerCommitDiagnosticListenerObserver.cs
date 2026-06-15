// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Subscribes the <see cref="SqlServerCommitDiagnosticObserver" /> to the <c>SqlClientDiagnosticListener</c> as soon
/// as it appears in <see cref="DiagnosticListener.AllListeners" />.
/// </summary>
internal sealed class SqlServerCommitDiagnosticListenerObserver(SqlServerCommitDiagnosticObserver observer)
    : IObserver<DiagnosticListener>,
        IDisposable
{
    private readonly Lock _gate = new();
    private List<IDisposable> _subscriptions = [];

    /// <summary>The name of the SqlClient diagnostic listener.</summary>
    public const string DiagnosticListenerName = "SqlClientDiagnosticListener";

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <inheritdoc />
    public void OnError(Exception error) { }

    /// <inheritdoc />
    public void OnNext(DiagnosticListener listener)
    {
        Argument.IsNotNull(listener);

        if (string.Equals(listener.Name, DiagnosticListenerName, StringComparison.Ordinal))
        {
            var subscription = listener.Subscribe(
                observer,
                static (eventName, _, _) => SqlServerCommitDiagnosticObserver.IsSupportedEvent(eventName)
            );

            lock (_gate)
            {
                _subscriptions.Add(subscription);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<IDisposable> subscriptions;

        lock (_gate)
        {
            subscriptions = _subscriptions;
            _subscriptions = [];
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }
    }
}
