// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Headless.Messaging.SqlServer.Diagnostics;

public class DiagnosticProcessorObserver : IObserver<DiagnosticListener>
{
    public const string DiagnosticListenerName = "SqlClientDiagnosticListener";

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
