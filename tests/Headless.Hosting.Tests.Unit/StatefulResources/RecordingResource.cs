// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Tests.StatefulResources;

/// <summary>A resource that records each operation in a shared log and fails or stalls on request.</summary>
internal class RecordingResource(string type, string name, List<string> log) : IStatefulResource
{
    public string Type => type;

    public string Name => name;

    /// <summary>Operations that throw <see cref="InvalidOperationException" /> with <see cref="FailureMessage" />.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations that throw <see cref="StatefulResourceUnsupportedException" />.</summary>
    public HashSet<string> Unsupported { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations that throw a plain <see cref="NotSupportedException" />, as a driver would.</summary>
    public HashSet<string> DriverNotSupported { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations that wait until canceled.</summary>
    public HashSet<string> Stalling { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations that wait until canceled, then fail with a driver's own exception instead of a cancellation.</summary>
    public HashSet<string> StallingThenDriverError { get; } = new(StringComparer.Ordinal);

    public string FailureMessage { get; init; } = "The broker refused the declaration.";

    public IReadOnlyList<KeyValuePair<string, string>> Status { get; init; } = [new("queues", "2")];

    public Task SetupAsync(CancellationToken cancellationToken) => _RunAsync("setup", cancellationToken);

    public Task CheckAsync(CancellationToken cancellationToken) => _RunAsync("check", cancellationToken);

    public Task ClearStateAsync(CancellationToken cancellationToken) => _RunAsync("clear", cancellationToken);

    public Task TeardownAsync(CancellationToken cancellationToken) => _RunAsync("teardown", cancellationToken);

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _RunAsync("statistics", cancellationToken);

        return Status;
    }

    private async Task _RunAsync(string operation, CancellationToken cancellationToken)
    {
        log.Add($"{operation} {name}");

        if (Unsupported.Contains(operation))
        {
            throw new StatefulResourceUnsupportedException("Not here.");
        }

        if (DriverNotSupported.Contains(operation))
        {
            throw new NotSupportedException();
        }

        if (Failing.Contains(operation))
        {
            throw new InvalidOperationException(FailureMessage);
        }

        if (Stalling.Contains(operation))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (StallingThenDriverError.Contains(operation))
        {
            // A driver such as SqlClient reports a canceled command in its own exception type, not as a cancellation.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            throw new InvalidOperationException("Operation cancelled by user.");
        }
    }
}

/// <summary>A recording resource that must follow the resources named in <see cref="DependsOn" />.</summary>
internal sealed class DependentResource(string type, string name, List<string> log, params string[] dependsOn)
    : RecordingResource(type, name, log),
        IStatefulResourceWithDependencies
{
    public IEnumerable<IStatefulResource> GetDependencies(IReadOnlyList<IStatefulResource> resources) =>
        resources.Where(x => dependsOn.Contains(x.Name, StringComparer.Ordinal));

    public IReadOnlyList<string> DependsOn => dependsOn;
}

/// <summary>A source that offers a fixed set of resources.</summary>
internal sealed class FixedResourceSource(IReadOnlyList<IStatefulResource> resources) : IStatefulResourceSource
{
    public Task<IReadOnlyList<IStatefulResource>> GetResourcesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(resources);
}
