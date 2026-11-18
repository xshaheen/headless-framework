// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A value a test watches, which counts its reads and can hold one read in flight.</summary>
internal sealed class WatchedSource(params string[] dependencies)
{
    private HeldRun? _held;

    public int Value { get; set; } = 1;

    public int Runs { get; private set; }

    /// <summary>Makes the next read stop after it has read <see cref="Value" />, until the test releases it.</summary>
    public HeldRun HoldNextRun() => _held = new HeldRun();

    public async ValueTask<int> ReadAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        Runs++;
        services.GetRequiredService<ILiveQueryDependencies>().DependsOn(dependencies);
        var read = Value;

        if (_held is { } held)
        {
            _held = null;
            await held.WaitAsync(cancellationToken);
        }

        return read;
    }
}
