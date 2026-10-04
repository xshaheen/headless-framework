// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Testing;

/// <summary>
/// Starts a fully recorded <see cref="System.Diagnostics.Activity"/> on a private <see cref="ActivitySource"/> and
/// makes it <see cref="Activity.Current"/>, so a test can assert the tags code under test writes to the current span
/// without an OpenTelemetry pipeline. The listener only samples this instance's source, so parallel tests never see
/// each other's spans.
/// </summary>
[PublicAPI]
public sealed class RecordedTestActivity : IDisposable
{
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;

    private RecordedTestActivity(string operationName)
    {
        _source = new ActivitySource($"Headless.Testing.{Guid.NewGuid():N}");
        _listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, _source),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
        Activity = _source.StartActivity(operationName)!;
    }

    /// <summary>The started activity; it is <see cref="Activity.Current"/> until disposed.</summary>
    public Activity Activity { get; }

    /// <summary>Starts a recorded activity and makes it current.</summary>
    /// <param name="operationName">The activity's operation name.</param>
    /// <returns>The recorded activity; dispose it to stop the activity and its listener.</returns>
    public static RecordedTestActivity Start(string operationName = "test")
    {
        return new RecordedTestActivity(operationName);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Activity.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }
}
