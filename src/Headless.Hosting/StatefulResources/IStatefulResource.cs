// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// External state a feature needs before it can run, such as database tables, broker queues, or a storage container,
/// that a deployment creates with <c>resources setup</c> instead of the running host creating it.
/// </summary>
/// <remarks>
/// <para>
/// Register a resource with <c>AddHeadlessStatefulResource</c>, or a set derived from registrations with
/// <c>AddHeadlessStatefulResourceSource</c>. The <c>resources</c> verb of <c>RunHeadlessAsync</c>, the startup action of
/// <c>AddHeadlessResourceSetupOnStartup</c>, and the test helpers on <see cref="IServiceProvider" /> all run every
/// resource through the same operations.
/// </para>
/// <para>
/// An operation that does not apply to a resource throws <see cref="StatefulResourceUnsupportedException" />, which callers
/// report as unsupported instead of failed; any other exception, a driver's <see cref="NotSupportedException" /> included,
/// is a failure. Every operation must be idempotent: a deployment re-runs <c>setup</c> on every release.
/// </para>
/// </remarks>
[PublicAPI]
public interface IStatefulResource
{
    /// <summary>
    /// Gets the kind of resource, such as <c>Schema</c> or <c>RabbitMQ</c>. The <c>--type</c> filter of the
    /// <c>resources</c> verb matches it.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Gets the resource's name within its type. The <c>--name</c> filter of the <c>resources</c> verb matches it. It
    /// is printed to deploy logs, so it never carries a host name, connection string, or credential.
    /// </summary>
    string Name { get; }

    /// <summary>Creates whatever the resource lacks, and changes nothing that already exists as expected.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the resource is set up.</returns>
    Task SetupAsync(CancellationToken cancellationToken);

    /// <summary>Checks, without writing, that the resource is set up and reachable.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the resource is ready.</returns>
    /// <exception cref="StatefulResourceException">The resource is reachable but not set up as expected.</exception>
    Task CheckAsync(CancellationToken cancellationToken);

    /// <summary>Removes the resource's data, such as queued messages or stored rows, and keeps its structure.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the data is removed.</returns>
    /// <exception cref="StatefulResourceUnsupportedException">The resource cannot clear its data.</exception>
    Task ClearStateAsync(CancellationToken cancellationToken);

    /// <summary>Deletes what <see cref="SetupAsync" /> created.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the resource is deleted.</returns>
    /// <exception cref="StatefulResourceUnsupportedException">The resource cannot delete itself.</exception>
    Task TeardownAsync(CancellationToken cancellationToken);

    /// <summary>Reads the resource's current state as ordered name and value pairs, for an operator to read.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state, in display order.</returns>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>A resource that must be set up after other resources, for example queues stored in a database.</summary>
[PublicAPI]
public interface IStatefulResourceWithDependencies : IStatefulResource
{
    /// <summary>Selects, from every registered resource, the ones this resource needs set up first.</summary>
    /// <param name="resources">Every registered resource, this one included.</param>
    /// <returns>The resources to set up before this one. Teardown deletes them after it.</returns>
    IEnumerable<IStatefulResource> GetDependencies(IReadOnlyList<IStatefulResource> resources);
}

/// <summary>
/// Provides resources whose set depends on other registrations, such as one queue per registered consumer. It is
/// asked only when an operation runs, after the container is built.
/// </summary>
[PublicAPI]
public interface IStatefulResourceSource
{
    /// <summary>Returns the resources this source provides.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The resources; empty when there is nothing to provision.</returns>
    Task<IReadOnlyList<IStatefulResource>> GetResourcesAsync(CancellationToken cancellationToken);
}
