// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;

namespace Tests;

internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
{
    public bool StopApplicationCalled { get; private set; }

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
        StopApplicationCalled = true;
    }
}
