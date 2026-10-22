// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Work a feature does once after the host starts so its first requests do not pay for it: loading a cache, building
/// a model, reading a key ring, downloading data, or checking that broker objects exist.
/// </summary>
/// <remarks>
/// <para>
/// Register one with <c>IServiceCollection.AddWarmup</c>. The host runs every warm-up concurrently in the background
/// once it has started, so none of them delays the start. Until each warm-up that <see cref="GatesReadiness" /> has
/// finished, the <c>warmup</c> readiness health check reports <c>Unhealthy</c>, so an orchestrator sends no traffic to a
/// host that is not warm; liveness is not affected.
/// </para>
/// <para>
/// A failed warm-up is retried with a jittered back-off (one second, doubling to 30 seconds) until it succeeds or the
/// host stops, and the readiness check reports its last error. Make <see cref="WarmUpAsync" /> safe to run again. It
/// never fails the host: a correctness gate that must stop the start belongs in an <c>IHeadlessStartupValidator</c>.
/// </para>
/// <para>
/// A warm-up is resolved from its own dependency injection scope, which lives for the whole run, retries included.
/// </para>
/// </remarks>
[PublicAPI]
public interface IWarmup
{
    /// <summary>Gets the name the readiness check and the logs report, such as <c>FeatureDefinitions</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the host is not ready until this warm-up has finished. Default: <see langword="true" />. Return
    /// <see langword="false" /> for work whose absence only costs time, such as a cache that loads on first use anyway.
    /// </summary>
    bool GatesReadiness => true;

    /// <summary>Does the warm-up work.</summary>
    /// <param name="cancellationToken">Canceled when the host stops.</param>
    /// <returns>A task that completes when the work is done.</returns>
    Task WarmUpAsync(CancellationToken cancellationToken);
}
