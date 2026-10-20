// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Tests.Provisioning;

/// <summary>A provisioner that records each operation in a shared log and fails or stalls on request.</summary>
internal class RecordingProvisioner(string kind, string name, List<string> log) : IResourceProvisioner
{
    public string Kind => kind;

    public string Name => name;

    /// <summary>Operations that throw <see cref="InvalidOperationException" /> with <see cref="FailureMessage" />.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations the provisioner reports it cannot run: <c>purge</c> or <c>delete</c>.</summary>
    public HashSet<string> Unsupported { get; } = new(StringComparer.Ordinal);

    public bool CanPurge => !Unsupported.Contains("purge");

    public bool CanDelete => !Unsupported.Contains("delete");

    /// <summary>Operations that throw a plain <see cref="NotSupportedException" />, as a driver would.</summary>
    public HashSet<string> DriverNotSupported { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations that wait until canceled.</summary>
    public HashSet<string> Stalling { get; } = new(StringComparer.Ordinal);

    /// <summary>Operations that wait until canceled, then fail with a driver's own exception instead of a cancellation.</summary>
    public HashSet<string> StallingThenDriverError { get; } = new(StringComparer.Ordinal);

    public string FailureMessage { get; init; } = "The broker refused the declaration.";

    public IReadOnlyList<KeyValuePair<string, string>> Status { get; init; } = [new("queues", "2")];

    public Task ProvisionAsync(CancellationToken cancellationToken) => _RunAsync("provision", cancellationToken);

    public Task VerifyAsync(CancellationToken cancellationToken) => _RunAsync("verify", cancellationToken);

    public Task PurgeAsync(CancellationToken cancellationToken) => _RunAsync("purge", cancellationToken);

    public Task DeleteAsync(CancellationToken cancellationToken) => _RunAsync("delete", cancellationToken);

    public virtual IEnumerable<IResourceProvisioner> DependsOn(IReadOnlyList<IResourceProvisioner> provisioners) => [];

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _RunAsync("status", cancellationToken);

        return Status;
    }

    private async Task _RunAsync(string operation, CancellationToken cancellationToken)
    {
        log.Add($"{operation} {name}");

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

/// <summary>A recording provisioner that must follow the provisioners named in its constructor.</summary>
internal sealed class DependentProvisioner(string kind, string name, List<string> log, params string[] dependsOn)
    : RecordingProvisioner(kind, name, log)
{
    public override IEnumerable<IResourceProvisioner> DependsOn(IReadOnlyList<IResourceProvisioner> provisioners) =>
        provisioners.Where(x => dependsOn.Contains(x.Name, StringComparer.Ordinal));
}

/// <summary>A source that offers a fixed set of provisioners.</summary>
internal sealed class FixedProvisionerSource(IReadOnlyList<IResourceProvisioner> provisioners)
    : IResourceProvisionerSource
{
    public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(provisioners);
}
