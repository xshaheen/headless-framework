// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace Headless.Api.Diagnostics;

[PublicAPI]
public sealed class BadRequestEventListener : IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private static readonly Predicate<string> _IsEnabled = provider =>
        provider switch
        {
            DiagnosticSources.KestrelOnBadRequest => true,
            _ => false,
        };

    private readonly IDisposable _subscription;
    private readonly Action<IBadRequestExceptionFeature> _callback;

    public BadRequestEventListener(DiagnosticListener diagnosticListener, Action<IBadRequestExceptionFeature> callback)
    {
        _subscription = diagnosticListener.Subscribe(this, _IsEnabled);
        _callback = callback;
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is not IFeatureCollection featureCollection)
        {
            return;
        }

        var badRequestFeature = featureCollection.Get<IBadRequestExceptionFeature>();

        if (badRequestFeature is not null)
        {
            _callback(badRequestFeature);
        }
    }

    public void OnError(Exception error)
    {
        // no-op
    }

    public void OnCompleted()
    {
        // no-op
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
