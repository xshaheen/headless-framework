// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Headless.Messaging.SqlServer.Diagnostics;

/// <summary>
/// Observes diagnostic events from SQL Server to track outbox transactions.
/// Subscribes to <c>SqlClientDiagnosticListener</c> for transaction lifecycle events.
/// </summary>
public sealed class DiagnosticProcessorObserver : IObserver<DiagnosticListener>
{
    /// <summary>
    /// Name of the SQL Server diagnostic listener to observe.
    /// </summary>
    public const string DiagnosticListenerName = "SqlClientDiagnosticListener";

    /// <summary>
    /// Thread-safe buffer mapping transaction IDs to their outbox transaction contexts.
    /// Used to correlate SQL transactions with pending outbox messages.
    /// </summary>
    public ConcurrentDictionary<Guid, SqlServerOutboxTransaction> TransBuffer { get; } = new();

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == DiagnosticListenerName)
        {
            listener.Subscribe(new DiagnosticObserver(TransBuffer));
        }
    }
}
